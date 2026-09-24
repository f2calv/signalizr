using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Diagnostics;
using CasCap.Grpc;
using CasCap.Models;
using CasCap.Models.Dtos;
using CasCap.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Xunit;

namespace CasCap.Tests;

/// <summary>Service-level tests for the bidirectional inbound gRPC contract.</summary>
public class InboundGrpcServiceTests
{
    private const string SecondMessage = "second";

    [Fact]
    public async Task Acknowledgement_timeout_returns_deadline_exceeded()
    {
        using var fixture = new RegistryFixture(maxOutstanding: 1);
        var service = CreateService(fixture, ackTimeoutMs: 100);
        await using var session = StartSession(service, "non-acknowledging");
        await WaitForSubscribersAsync(fixture.Registry, 1);

        await fixture.AddMessageAsync("first");
        _ = await ReadResponseAsync(session.Response);
        await fixture.AddMessageAsync(SecondMessage);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => session.Call.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.DeadlineExceeded, exception.StatusCode);
        Assert.Equal(0, fixture.Registry.Count);
    }

    [Fact]
    public async Task Two_subscribers_acknowledge_independently()
    {
        using var fixture = new RegistryFixture(maxOutstanding: 1);
        var service = CreateService(fixture, ackTimeoutMs: 250);
        await using var acknowledging = StartSession(service, "acknowledging");
        await using var stalled = StartSession(service, "stalled");
        await WaitForSubscribersAsync(fixture.Registry, 2);

        await fixture.AddMessageAsync("first");
        var acknowledgedFirst = await ReadResponseAsync(acknowledging.Response);
        var stalledFirst = await ReadResponseAsync(stalled.Response);
        Assert.Equal(acknowledgedFirst.DeliveryId, stalledFirst.DeliveryId);

        acknowledging.Request.Write(new SubscribeRequest
        {
            Ack = new Ack { DeliveryId = acknowledgedFirst.DeliveryId }
        });
        await acknowledging.Request.WaitForReadAsync(
            request => request.PayloadCase is SubscribeRequest.PayloadOneofCase.Ack,
            TestContext.Current.CancellationToken);

        await fixture.AddMessageAsync(SecondMessage);
        var acknowledgedSecond = await ReadResponseAsync(acknowledging.Response);
        Assert.Equal(SecondMessage, acknowledgedSecond.Message);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => stalled.Call.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.DeadlineExceeded, exception.StatusCode);
        Assert.False(acknowledging.Call.IsCompleted);
    }

    [Fact]
    public async Task Duplicate_live_subscriber_identity_returns_already_exists()
    {
        using var fixture = new RegistryFixture(maxOutstanding: 1);
        var service = CreateService(fixture, ackTimeoutMs: 2_000);
        await using var first = StartSession(service, "duplicate");
        await WaitForSubscribersAsync(fixture.Registry, 1);
        await using var second = StartSession(service, "duplicate");

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => second.Call.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.AlreadyExists, exception.StatusCode);
        Assert.Equal(1, fixture.Registry.Count);
    }

    private static InboundGrpcService CreateService(
        RegistryFixture fixture, int ackTimeoutMs) =>
        new(NullLogger<InboundGrpcService>.Instance, fixture.Registry, fixture.Metrics,
            Options.Create(new SubscriberConfig
            {
                ReplayBatchSize = 10,
                MaxOutstanding = 1,
                AckTimeoutMs = ackTimeoutMs,
            }));

    private static async Task<InboundMessage> ReadResponseAsync(TestServerStreamWriter<InboundMessage> response)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await response.ReadAsync(timeout.Token);
    }

    private static TestSession StartSession(
        InboundGrpcService service, string name, bool pauseWrites = false)
    {
        var cancellation = new CancellationTokenSource();
        var request = new TestAsyncStreamReader<SubscribeRequest>();
        request.Write(new SubscribeRequest { Hello = new Hello { SubscriberName = name } });
        var response = new TestServerStreamWriter<InboundMessage>(pauseWrites);
        var context = new TestServerCallContext(cancellation.Token);
        var call = service.Subscribe(request, response, context);
        return new TestSession(cancellation, request, response, call);
    }

    private static async Task WaitForSubscribersAsync(
        InboundSubscriberRegistry registry, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (registry.Count != expected)
            await Task.Delay(10, timeout.Token);
    }

    private sealed class RegistryFixture : IDisposable
    {
        private readonly TestDbContextFactory _dbContextFactory = new();

        public RegistryFixture(int maxOutstanding)
        {
            Metrics = new SignalizrMetrics();
            Registry = new InboundSubscriberRegistry(
                NullLogger<InboundSubscriberRegistry>.Instance,
                Options.Create(new SubscriberConfig
                {
                    ReplayBatchSize = 10,
                    MaxOutstanding = maxOutstanding
                }),
                TimeProvider.System,
                Metrics,
                _dbContextFactory);
        }

        public SignalizrMetrics Metrics { get; }

        public InboundSubscriberRegistry Registry { get; }

        public async Task AddMessageAsync(string message)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            dbContext.InboundMessages.Add(new InboundMessageEntity
            {
                Message = message,
                PersistedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            await dbContext.SaveChangesAsync();
            Registry.NotifyMessageAvailable();
        }

        public void Dispose() => Metrics.Dispose();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<SignalizrDbContext>
    {
        private readonly DbContextOptions<SignalizrDbContext> _options =
            new DbContextOptionsBuilder<SignalizrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
                .Options;

        public SignalizrDbContext CreateDbContext() => new(_options);

        public ValueTask<SignalizrDbContext> CreateDbContextAsync(
            CancellationToken _ = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed class TestSession(
        CancellationTokenSource cancellation,
        TestAsyncStreamReader<SubscribeRequest> request,
        TestServerStreamWriter<InboundMessage> response,
        Task call) : IAsyncDisposable
    {
        public TestAsyncStreamReader<SubscribeRequest> Request { get; } = request;

        public TestServerStreamWriter<InboundMessage> Response { get; } = response;

        public Task Call { get; } = call;

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            Request.Complete();
            try
            {
                await Call.ConfigureAwait(false);
            }
            catch (RpcException)
            {
                // Expected when the scenario deliberately exhausts the acknowledgement budget.
            }
            cancellation.Dispose();
        }
    }

    private sealed class TestAsyncStreamReader<T> : IAsyncStreamReader<T> where T : class
    {
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
        private readonly Channel<T> _observed = Channel.CreateUnbounded<T>();
        private T? _current;

        public T Current => _current
            ?? throw new InvalidOperationException("MoveNext must succeed before Current is read.");

        public void Write(T item) => _channel.Writer.TryWrite(item);

        public void Complete() => _channel.Writer.TryComplete();

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_channel.Reader.TryRead(out var item))
                {
                    _current = item;
                    _observed.Writer.TryWrite(item);
                    return true;
                }
            }

            return false;
        }

        public async Task WaitForReadAsync(Func<T, bool> predicate, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            while (await _observed.Reader.WaitToReadAsync(timeout.Token))
            {
                if (_observed.Reader.TryRead(out var item) && predicate(item))
                    return;
            }
        }
    }

    private sealed class TestServerStreamWriter<T> : IServerStreamWriter<T>
    {
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
        private readonly TaskCompletionSource _writeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestServerStreamWriter(bool pauseWrites = false)
        {
            if (!pauseWrites)
                _writeGate.TrySetResult();
        }

        public WriteOptions? WriteOptions { get; set; }

        public async Task WriteAsync(T message)
        {
            _writeStarted.TrySetResult();
            await _writeGate.Task;
            _channel.Writer.TryWrite(message);
        }

        public void ReleaseWrites() => _writeGate.TrySetResult();

        public Task WaitForWriteAsync(CancellationToken cancellationToken) =>
            _writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        public ValueTask<T> ReadAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAsync(cancellationToken);
    }

    private sealed class TestServerCallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        private readonly Metadata _responseTrailers = [];

        protected override string MethodCore => "signalizr.v1.Inbound/Subscribe";

        protected override string HostCore => "localhost";

        protected override string PeerCore => "test";

        protected override DateTime DeadlineCore => DateTime.MaxValue;

        protected override Metadata RequestHeadersCore => [];

        protected override CancellationToken CancellationTokenCore => cancellationToken;

        protected override Metadata ResponseTrailersCore => _responseTrailers;

        protected override Status StatusCore { get; set; }

        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore => new("anonymous", []);

        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }
}
