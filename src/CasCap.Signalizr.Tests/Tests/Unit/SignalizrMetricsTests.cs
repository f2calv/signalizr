using CasCap.Diagnostics;
using CasCap.Models;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Xunit;

namespace CasCap.Tests;

/// <summary>Verifies configurable Signalizr telemetry metadata.</summary>
public sealed class SignalizrMetricsTests
{
    [Fact]
    public void Instruments_UseConfiguredPrefixUnitsAndDescriptions()
    {
        const string prefix = "custom_signalizr";
        var instruments = new List<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == prefix)
                    instruments.Add(instrument);
            }
        };
        listener.Start();

        using var metrics = new SignalizrMetrics(Options.Create(new AppConfig
        {
            MetricNamePrefix = prefix
        }));

        Assert.Equal(11, instruments.Count);
        Assert.All(instruments, instrument =>
        {
            Assert.Equal(prefix, instrument.Meter.Name);
            Assert.StartsWith($"{prefix}.", instrument.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(instrument.Unit));
            Assert.False(string.IsNullOrWhiteSpace(instrument.Description));
        });
        Assert.Equal(prefix, metrics.ActivitySource.Name);
    }
}
