using System.Net;
using System.Net.Http.Json;

namespace CasCap.Tests.Unit;

/// <summary>Tests the Comms responder across its Agent Runtime HTTP boundary.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentCommsResponderTests
{
    [Fact]
    public async Task RespondAsync_PreservesLiveProgressAttachmentsAndDiagnostics()
    {
        var handler = new FakeAgentRuntimeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new RunAgentStreamItem
                {
                    Event = new RunAgentEvent
                    {
                        Type = RunAgentEventTypes.DelegationStarted,
                        AgentName = "specialist",
                        Depth = 1,
                        ModelName = "special-model",
                    },
                },
                new RunAgentStreamItem
                {
                    Response = new RunAgentResponse
                    {
                        SessionId = "session",
                        OutputText = "completed",
                        DefinitionVersion = "v1",
                        ModelName = "primary-model",
                        ElapsedMilliseconds = 1250,
                        Usage = new RunAgentUsage { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 },
                        Attachments =
                        [
                            new RunAgentAttachment
                            {
                                MimeType = "image/png",
                                FileName = "result.png",
                                Base64Content = "AQID",
                            },
                        ],
                    },
                },
            }),
        }));
        var signalizr = new FakeSignalizrClient();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var responder = CreateResponder(httpClient, signalizr);

        var reply = await responder.RespondAsync(
            new CommsTurn("run", Sender: "sender", Timestamp: 123),
            TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.Equal("completed", reply.Text);
        Assert.Contains("15", reply.Footer, StringComparison.Ordinal);
        Assert.Single(reply.Base64Attachments!, attachment =>
            attachment == "data:image/png;filename=result.png;base64,AQID");
        Assert.Contains(signalizr.Reactions, reaction => reaction.Emoji == "\U0001F500");
        Assert.Contains(signalizr.Reactions, reaction => reaction.Emoji == "\u23F3");
    }

    [Fact]
    public async Task TryHandleCommandAsync_UpdatesRemoteModelOverride()
    {
        UpdateAgentOverridesRequest? update = null;
        var handler = new FakeAgentRuntimeHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AgentOverridesResponse { SessionEnabled = true }),
                };
            }

            update = await request.Content!.ReadFromJsonAsync<UpdateAgentOverridesRequest>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AgentOverridesResponse
                {
                    SessionEnabled = update!.SessionEnabled,
                    ModelName = update.ModelName,
                    Instructions = update.Instructions,
                }),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var responder = CreateResponder(httpClient, new FakeSignalizrClient());

        var outcome = await responder.TryHandleCommandAsync(
            "/model qwen3:32b",
            TestContext.Current.CancellationToken);

        Assert.Equal("Model overridden to: qwen3:32b", outcome?.ReplyText);
        Assert.Equal("qwen3:32b", update?.ModelName);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task TryHandleCommandAsync_BypassRemainsLocalDeferredTurn()
    {
        var handler = new FakeAgentRuntimeHttpMessageHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var responder = CreateResponder(httpClient, new FakeSignalizrClient());

        var outcome = await responder.TryHandleCommandAsync(
            "/session bypass inspect this",
            TestContext.Current.CancellationToken);

        Assert.Null(outcome?.ReplyText);
        Assert.Equal("inspect this", outcome?.DeferredTurn?.Prompt);
        Assert.True(outcome?.DeferredTurn?.BypassSession);
        Assert.Empty(handler.Calls);
    }

    private static AgentCommsResponder CreateResponder(HttpClient httpClient, FakeSignalizrClient signalizr)
    {
        var config = Options.Create(new CommsConfig { GroupName = "group" });
        return new AgentCommsResponder(
            NullLogger<AgentCommsResponder>.Instance,
            config,
            new CommsAgentProfile("assistant", "session", "respond"),
            new AgentRuntimeClient(httpClient),
            signalizr,
            new InMemoryPollTracker(config, TimeProvider.System),
            new CommsDebugNotifier(
                NullLogger<CommsDebugNotifier>.Instance,
                config,
                TimeProvider.System,
                signalizr,
                []),
            []);
    }
}
