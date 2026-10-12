using System.Diagnostics.Metrics;

namespace CasCap.Tests.Unit;

/// <summary>Builds real instrument instances for tests that do not assert on measurements.</summary>
internal static class TestMetrics
{
    /// <summary>Creates a <see cref="VoiceTranscriptionMetrics"/> backed by a throwaway meter factory.</summary>
    public static VoiceTranscriptionMetrics Voice()
    {
        var config = Options.Create(new VoiceMetricsConfig
        {
            MetricNamePrefix = "comms",
        });
        var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        return new(config, Options.Create(new SpeechToTextConfig()), meterFactory);
    }

    /// <summary>Creates a <see cref="CommsAgentMetrics"/> backed by a throwaway meter factory.</summary>
    /// <param name="metricNamePrefix">Meter and instrument prefix; use a unique value to observe measurements.</param>
    public static CommsAgentMetrics CommsAgent(string metricNamePrefix = "comms")
    {
        var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        return new(Options.Create(new CommsMetricsConfig { MetricNamePrefix = metricNamePrefix }), meterFactory);
    }
}
