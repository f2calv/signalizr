using CasCap.AgentRuntime.Contracts.V1;
using System.Text.Json;

namespace CasCap.Models;

/// <summary>Transport-neutral completed agent run used by Comms diagnostics and host enrichers.</summary>
public sealed class CommsAgentRunResult
{
    /// <summary>Gets or sets the output text.</summary>
    public required string OutputText { get; init; }

    /// <summary>Gets or sets the actual provider model.</summary>
    public required string ModelName { get; init; }

    /// <summary>Gets or sets the provider finish reason.</summary>
    public string? FinishReason { get; init; }

    /// <summary>Gets or sets total execution time.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Gets or sets time to first output token.</summary>
    public TimeSpan? TimeToFirstToken { get; init; }

    /// <summary>Gets or sets provider usage.</summary>
    public RunAgentUsage? Usage { get; init; }

    /// <summary>Gets or sets tool calls.</summary>
    public IReadOnlyList<RunAgentToolCall> ToolCalls { get; init; } = [];

    /// <summary>Gets the tool-call count.</summary>
    public int ToolCallCount => ToolCalls.Count;

    /// <summary>Gets or sets generated attachments.</summary>
    public IReadOnlyList<RunAgentAttachment> Attachments { get; init; } = [];

    /// <summary>Gets or sets resulting session status.</summary>
    public AgentSessionInfoResponse? Session { get; init; }

    /// <summary>Gets mutable host/provider diagnostics used by enrichers.</summary>
    public Dictionary<string, object?> AdditionalProperties { get; } = [];

    /// <summary>Creates a Comms result from the stable runtime response.</summary>
    public static CommsAgentRunResult FromResponse(RunAgentResponse response)
    {
        var result = new CommsAgentRunResult
        {
            OutputText = response.OutputText,
            ModelName = response.ModelName,
            FinishReason = response.FinishReason,
            Elapsed = TimeSpan.FromMilliseconds(response.ElapsedMilliseconds),
            TimeToFirstToken = response.TimeToFirstTokenMilliseconds is { } firstToken
                ? TimeSpan.FromMilliseconds(firstToken)
                : null,
            Usage = response.Usage,
            ToolCalls = response.ToolCalls,
            Attachments = response.Attachments,
            Session = response.Session,
        };
        foreach (var (key, value) in response.AdditionalProperties)
            result.AdditionalProperties[key] = ConvertValue(value);
        return result;
    }

    private static object? ConvertValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        _ => value.Clone(),
    };
}
