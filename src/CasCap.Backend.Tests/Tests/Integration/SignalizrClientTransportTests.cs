using CasCap.Grpc;
using CasCap.Signalizr.Client;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CasCap.Tests;

/// <summary>Verifies subscription transport liveness without an external gateway.</summary>
[Trait("Category", "Integration")]
public sealed class SignalizrClientTransportTests
{
    [Fact]
    public void GrpcTransport_HasBoundedLiveness()
    {
        using var services = CreateServices("http://localhost:5001");

        var handler = GetGrpcHandler(services);

        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), handler.KeepAlivePingDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), handler.KeepAlivePingTimeout);
        Assert.Equal(HttpKeepAlivePingPolicy.WithActiveRequests, handler.KeepAlivePingPolicy);
        Assert.True(handler.EnableMultipleHttp2Connections);
    }

    [Fact]
    public void RestTransport_IsUnchanged()
    {
        using var services = CreateServices("http://localhost:5001");
        var factory = services.GetRequiredService<IHttpMessageHandlerFactory>();

        var handler = GetPrimaryHandler(factory.CreateHandler(nameof(ISignalizrClient)));

        Assert.Equal(Timeout.InfiniteTimeSpan, handler.KeepAlivePingDelay);
        Assert.Equal(Timeout.InfiniteTimeSpan, handler.ConnectTimeout);
    }

    [Fact]
    public async Task SubscribeAsync_UnresponsivePeerFails()
    {
        // Regression: a half-open subscription never reached the consumer's reconnect loop.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var services = CreateServices(address);
        var handler = GetGrpcHandler(services);
        // Keep the fault-injection test short; the production budgets are asserted separately.
        handler.KeepAlivePingDelay = TimeSpan.FromSeconds(1);
        handler.KeepAlivePingTimeout = TimeSpan.FromSeconds(1);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var pingReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerTask = IgnorePingAsync(listener, pingReceived, timeout.Token);
        try
        {
            var client = services.GetRequiredService<ISignalizrClient>();
            await using var messages = client.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
            var pending = messages.MoveNextAsync().AsTask();

            await pingReceived.Task.WaitAsync(timeout.Token);
            var exception = await Assert.ThrowsAsync<RpcException>(() => pending);

            Assert.False(timeout.IsCancellationRequested);
            Assert.NotEqual(StatusCode.Cancelled, exception.StatusCode);
        }
        finally
        {
            await timeout.CancelAsync();
            try
            {
                await peerTask;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                // The unresponsive peer stays open until test cleanup cancels it.
            }
        }
    }

    private static ServiceProvider CreateServices(string grpcAddress)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SignalizrClientConfig.ConfigurationSectionName}:GrpcAddress"] = grpcAddress,
            [$"{SignalizrClientConfig.ConfigurationSectionName}:SubscriberName"] = "example-consumer"
        }).Build();
        return new ServiceCollection().AddSignalizrClient(configuration).BuildServiceProvider();
    }

    private static SocketsHttpHandler GetGrpcHandler(ServiceProvider services)
    {
        var name = new ServiceCollection().AddGrpcClient<Inbound.InboundClient>().Name;
        return GetPrimaryHandler(services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name));
    }

    private static SocketsHttpHandler GetPrimaryHandler(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler delegating)
            handler = Assert.IsAssignableFrom<HttpMessageHandler>(delegating.InnerHandler);
        return Assert.IsType<SocketsHttpHandler>(handler);
    }

    private static async Task IgnorePingAsync(
        TcpListener listener, TaskCompletionSource pingReceived, CancellationToken cancellationToken)
    {
        using var peer = await listener.AcceptTcpClientAsync(cancellationToken);
        var stream = peer.GetStream();
        var preface = new byte[24];
        await stream.ReadExactlyAsync(preface, cancellationToken);
        Assert.Equal("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n", Encoding.ASCII.GetString(preface));
        // Empty SETTINGS frame accepts the HTTP/2 connection.
        await stream.WriteAsync(new byte[9] { 0, 0, 0, 4, 0, 0, 0, 0, 0 }, cancellationToken);

        var header = new byte[9];
        while (true)
        {
            await stream.ReadExactlyAsync(header, cancellationToken);
            var length = (header[0] << 16) | (header[1] << 8) | header[2];
            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, cancellationToken);
            switch (header[3])
            {
                case 4 when (header[4] & 1) == 0:
                    await stream.WriteAsync(new byte[9] { 0, 0, 0, 4, 1, 0, 0, 0, 0 }, cancellationToken);
                    break;
                case 1:
                    // HPACK :status=200 and content-type=application/grpc, with the response left open.
                    var headers = new byte[] { 0x88, 0x5f, 16 }
                        .Concat(Encoding.ASCII.GetBytes("application/grpc")).ToArray();
                    var responseHeader = new byte[] { 0, 0, (byte)headers.Length, 1, 4,
                        header[5], header[6], header[7], header[8] };
                    await stream.WriteAsync(responseHeader, cancellationToken);
                    await stream.WriteAsync(headers, cancellationToken);
                    break;
                case 6:
                    pingReceived.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return;
            }
        }
    }
}
