using CasCap.Grpc;
using CasCap.Models;
using CasCap.Models.Dtos;
using CasCap.Services;
using Grpc.Core;
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
        var registry = CreateRegistry(maxOutstanding: 1);
        var service = CreateService(registry, ackTimeoutMs: 100);
        await using var session = StartSession(service, "non-acknowledging");
        await WaitForSubscribersAsync(registry, 1);

        Assert.Empty(registry.Broadcast(CreateDelivery("first")));
        _ = await ReadResponseAsync(session.Response);
        Assert.Empty(registry.Broadcast(CreateDelivery(SecondMessage)));

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => session.Call.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.DeadlineExceeded, exception.StatusCode);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Two_subscribers_acknowledge_independently()
    {
        var registry = CreateRegistry(maxOutstanding: 1);
        var service = CreateService(registry, ackTimeoutMs: 250);
        await using var acknowledging = StartSession(service, "acknowledging");
        await using var stalled = StartSession(service, "stalled");
        await WaitForSubscribersAsync(registry, 2);

        Assert.Empty(registry.Broadcast(CreateDelivery("first")));
        var acknowledgedFirst = await ReadResponseAsync(acknowledging.Response);
        var stalledFirst = await ReadResponseAsync(stalled.Response);
        Assert.NotEqual(acknowledgedFirst.DeliveryId, stalledFirst.DeliveryId);

        acknowledging.Request.Write(new SubscribeRequest
        {
            Ack = new Ack { DeliveryId = acknowledgedFirst.DeliveryId }
        });
        await acknowledging.Request.WaitForReadAsync(
            request => request.PayloadCase is SubscribeRequest.PayloadOneofCase.Ack,
            TestContext.Current.CancellationToken);

        Assert.Empty(registry.Broadcast(CreateDelivery(SecondMessage)));
        var acknowledgedSecond = await ReadResponseAsync(acknowledging.Response);
        Assert.Equal(SecondMessage, acknowledgedSecond.Message);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => stalled.Call.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.DeadlineExceeded, exception.StatusCode);
        Assert.False(acknowledging.Call.IsCompleted);
    }

    [Fact]
    public async Task Subscriber_queue_overrun_returns_resource_exhausted()
    {
        var registry = CreateRegistry(maxOutstanding: 10, queueCapacity: 1);
        var service = CreateService(registry, ackTimeoutMs: 2_000);
        await using var session = StartSession(service, "slow", pauseWrites: true);
        await WaitForSubscribersAsync(registry, 1);

        Assert.Empty(registry.Broadcast(CreateDelivery("first")));
        await session.Response.WaitForWriteAsync(TestContext.Current.CancellationToken);
        Assert.Empty(registry.Broadcast(CreateDelivery(SecondMessage)));
        var failed = Assert.Single(registry.Broadcast(CreateDelivery("third")));

        registry.Unsubscribe(failed, new SubscriberFellBehindException(failed.Name));
        session.Response.ReleaseWrites();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => session.Call.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.ResourceExhausted, exception.StatusCode);
        Assert.Equal(0, registry.Count);
    }

    private static InboundSubscriberRegistry CreateRegistry(int maxOutstanding, int queueCapacity = 10) =>
        new(NullLogger<InboundSubscriberRegistry>.Instance,
            Options.Create(new SubscriberConfig
            {
                QueueCapacity = queueCapacity,
                MaxOutstanding = maxOutstanding,
            }));

    private static InboundGrpcService CreateService(
        InboundSubscriberRegistry registry, int ackTimeoutMs) =>
        new(NullLogger<InboundGrpcService>.Instance, registry,
            Options.Create(new SubscriberConfig
            {
                QueueCapacity = 10,
                MaxOutstanding = 1,
                AckTimeoutMs = ackTimeoutMs,
            }));

    private static InboundDelivery CreateDelivery(string message) =>
        new() { DeliveryId = string.Empty, Channel = "system", Message = message };

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
