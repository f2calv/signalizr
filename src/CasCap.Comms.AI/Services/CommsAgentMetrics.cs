using System.Diagnostics.Metrics;

namespace CasCap.Services;

/// <summary>Publishes the outcome of every Agent Runtime run requested by <see cref="AgentCommsResponder"/>.</summary>
/// <remarks>The only dimension is the bounded <see cref="CommsAgentRunOutcome"/>; no agent input, output or sender is attached.</remarks>
public sealed class CommsAgentMetrics
{
    /// <summary>The outcome dimension carried by the run counter.</summary>
    public const string OutcomeTagName = "outcome";

    private readonly Counter<long> _runs;

    /// <summary>Initializes a new instance of the <see cref="CommsAgentMetrics"/> class.</summary>
    public CommsAgentMetrics(IOptions<CommsMetricsConfig> config, IMeterFactory meterFactory)
    {
        var prefix = config.Value.MetricNamePrefix;
        _runs = meterFactory.Create(prefix).CreateCounter<long>($"{prefix}.comms.agent.runs",
            unit: "{run}", description: "Agent Runtime runs requested by the comms responder, by outcome");
    }

    /// <summary>Records one completed or failed run.</summary>
    /// <param name="outcome">The run outcome.</param>
    public void Record(CommsAgentRunOutcome outcome)
        => _runs.Add(1, new KeyValuePair<string, object?>(OutcomeTagName, outcome.ToString()));
}
