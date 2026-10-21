using System.Net;
using System.Net.Http.Json;

namespace CasCap.Tests.Unit;

/// <summary>Returns deterministic Agent Runtime HTTP responses without network access.</summary>
public sealed class FakeAgentRuntimeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null) : HttpMessageHandler
{
    /// <summary>Gets requests observed by the handler.</summary>
    public ConcurrentQueue<(HttpMethod Method, string Path)> Calls { get; } = new();

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls.Enqueue((request.Method, request.RequestUri?.AbsolutePath ?? string.Empty));
        if (responder is not null)
            return await responder(request, cancellationToken);

        if (request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath.EndsWith("/runs/stream", StringComparison.Ordinal) is true)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new RunAgentStreamItem
                    {
                        Response = new RunAgentResponse
                        {
                            SessionId = "test-group-session",
                            OutputText = string.Empty,
                            DefinitionVersion = "test-v1",
                            ModelName = "test-model",
                        },
                    },
                }),
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
