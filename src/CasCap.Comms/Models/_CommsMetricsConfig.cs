namespace CasCap.Models;

/// <summary>Configuration for the metrics emitted by the communications pipeline.</summary>
public sealed record CommsMetricsConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(CommsMetricsConfig)}";

    /// <summary>Prefix used for the comms meter and instrument names.</summary>
    /// <remarks>
    /// Defaults to the library-neutral <c>comms</c> prefix. Set it to the host's exported meter prefix,
    /// otherwise the host's OpenTelemetry pipeline does not collect the instruments.
    /// </remarks>
    [Required, MinLength(1)]
    public string MetricNamePrefix { get; init; } = "comms";
}
