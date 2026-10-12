using System.Text.Json;

namespace CasCap.Tests.Unit;

/// <summary>Verifies that monitor diagnostics disclose content only when enabled, and the reply footer's optional details.</summary>
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

    [Theory]
    [InlineData(false, "💾 9.5KB")]
    [InlineData(true, "💾 9.5KB, 11u/22a")]
    public async Task FormatStatsFooterAsync_ShowsSessionCountsOnlyWhenEnabled(bool countsEnabled, string expectedSession)
    {
        var result = new CommsAgentRunResult
        {
            OutputText = "done",
            ModelName = "model",
            Elapsed = TimeSpan.FromSeconds(12.9),
            Session = new AgentSessionInfoResponse
            {
                Exists = true,
                SizeBytes = 9728,
                Entries = [new AgentSessionEntry { Key = "primary", UserMessageCount = 11, AssistantMessageCount = 22 }],
            },
        };

        var footer = await CreateNotifier(new FakeSignalizrClient(), contentEnabled: false, footerSessionCounts: countsEnabled)
            .FormatStatsFooterAsync(result, TestContext.Current.CancellationToken);

        Assert.Contains("⏱ 12.9s", footer, StringComparison.Ordinal);
        Assert.EndsWith(expectedSession, footer, StringComparison.Ordinal);
    }

    private static CommsDebugNotifier CreateNotifier(FakeSignalizrClient signalizr, bool contentEnabled, bool footerSessionCounts = false)
        => new(
            NullLogger<CommsDebugNotifier>.Instance,
            Options.Create(new CommsConfig
            {
                GroupName = "group",
                MonitorGroupName = MonitorGroup,
                MonitorContentEnabled = contentEnabled,
                FooterSessionCountsEnabled = footerSessionCounts,
            }),
            TimeProvider.System,
            signalizr,
            []);
}
