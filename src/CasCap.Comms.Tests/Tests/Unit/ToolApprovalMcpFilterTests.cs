using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace CasCap.Tests.Unit;

/// <summary>Verifies which tool calls the approval filter gates and how it names the requester.</summary>
[Trait("Category", "Approval")]
public sealed class ToolApprovalMcpFilterTests
{
    [Theory]
    [InlineData(nameof(SampleTools.GatedAction), true, new string[0], new string[0], true)]
    [InlineData(nameof(SampleTools.GatedAction), false, new string[0], new string[0], false)]
    [InlineData(nameof(SampleTools.GatedAction), true, new string[0], new[] { "gated_action" }, false)]
    [InlineData(nameof(SampleTools.OpenAction), true, new string[0], new string[0], false)]
    [InlineData(nameof(SampleTools.OpenAction), true, new[] { "open_action" }, new string[0], true)]
    public void RequiresApproval(string methodName, bool approvalEnabled, string[] requiredTools, string[] waivedTools, bool expected)
    {
        var tool = McpServerTool.Create(typeof(SampleTools).GetMethod(methodName)!);
        var config = new ToolApprovalConfig
        {
            ApprovalEnabled = approvalEnabled,
            RequiredTools = [.. requiredTools],
            WaivedTools = [.. waivedTools],
        };

        Assert.Equal(expected, ToolApprovalMcpFilter.RequiresApproval(tool, config));
    }

    [Theory]
    [InlineData("Security Agent", "agentizr", "Security Agent")]
    [InlineData(null, "agentizr", "agentizr")]
    [InlineData(null, null, "an unidentified client")]
    public void ResolveRequester(string? agentName, string? clientName, string expected)
    {
        var meta = agentName is null ? null : new JsonObject { [McpToolCallMetaKeys.AgentName] = agentName };

        Assert.Equal(expected, ToolApprovalMcpFilter.ResolveRequester(meta, clientName));
    }

    [Theory]
    [InlineData(ToolApprovalOutcome.Denied, "denied")]
    [InlineData(ToolApprovalOutcome.TimedOut, "within 30 seconds")]
    [InlineData(ToolApprovalOutcome.Unavailable, "cannot request")]
    public void CreateRefusal_IsARefusalNotAnError(ToolApprovalOutcome outcome, string expectedText)
    {
        var result = ToolApprovalMcpFilter.CreateRefusal("gated_action", outcome, 30_000);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("gated_action did not run", text);
        Assert.Contains(expectedText, text);
    }

    /// <summary>Tool methods with and without the approval attribute.</summary>
    public static class SampleTools
    {
        /// <summary>A tool that needs approval.</summary>
        [McpServerTool, RequiresApproval]
        public static bool GatedAction() => true;

        /// <summary>A tool that does not need approval.</summary>
        [McpServerTool]
        public static bool OpenAction() => true;
    }
}
