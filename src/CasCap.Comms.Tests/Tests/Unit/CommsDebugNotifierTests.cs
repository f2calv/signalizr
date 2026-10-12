using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Verifies that monitor diagnostics disclose message content only when explicitly enabled.</summary>
[Trait("Category", "Comms")]
public sealed class CommsDebugNotifierTests
{
    private const string MonitorGroup = "My Test Monitor Group Name";
    private const string Prompt = "open the garage for the courier";
    private const string EventText = "Doorbell pressed by a visitor";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendDebugStatsAsync_DisclosesPromptAndToolArgumentsOnlyWhenEnabled(bool contentEnabled)
    {
        var signalizr = new FakeSignalizrClient();
        var result = new CommsAgentRunResult { OutputText = "done", ModelName = "model" };
        var step = new CommsDebugStep("step", null, TimeSpan.Zero, new CommsAgentRunResult
        {
            OutputText = "done",
            ModelName = "model",
            ToolCalls = [new RunAgentToolCall
            {
                Name = "open_garage",
                Arguments = new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement("courier") },
            }],
        });

        await CreateNotifier(signalizr, contentEnabled)
            .SendDebugStatsAsync(Prompt, result, [step], null, TestContext.Current.CancellationToken);

        var message = Assert.Single(signalizr.SentTo(MonitorGroup)).Message;
        Assert.Contains("open_garage", message, StringComparison.Ordinal);
        Assert.Equal(contentEnabled, message.Contains(Prompt, StringComparison.Ordinal));
        Assert.Equal(contentEnabled, message.Contains("courier", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendStreamEventDebugAsync_DisclosesEventTextOnlyWhenEnabled(bool contentEnabled)
    {
        var signalizr = new FakeSignalizrClient();
        var commsEvent = new CommsEvent
        {
            Source = "DoorBirdBgService",
            Message = EventText,
            JsonPayload = "{\"visitor\":\"courier\"}",
            Environment = "tst",
            TimestampUtc = DateTime.UnixEpoch,
        };

        await CreateNotifier(signalizr, contentEnabled)
            .SendStreamEventDebugAsync(commsEvent, TestContext.Current.CancellationToken);

        var message = Assert.Single(signalizr.SentTo(MonitorGroup)).Message;
        Assert.Contains("DoorBirdBgService", message, StringComparison.Ordinal);
        Assert.Equal(contentEnabled, message.Contains(EventText, StringComparison.Ordinal));
        Assert.Equal(contentEnabled, message.Contains("courier", StringComparison.Ordinal));
    }

    private static CommsDebugNotifier CreateNotifier(FakeSignalizrClient signalizr, bool contentEnabled)
        => new(
            NullLogger<CommsDebugNotifier>.Instance,
            Options.Create(new CommsConfig
            {
                GroupName = "group",
                MonitorGroupName = MonitorGroup,
                MonitorContentEnabled = contentEnabled,
            }),
            TimeProvider.System,
            signalizr,
            []);
}
