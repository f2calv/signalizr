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

        Assert.Equal(13, instruments.Count);
        Assert.All(instruments, instrument =>
        {
            Assert.Equal(prefix, instrument.Meter.Name);
            Assert.StartsWith($"{prefix}.", instrument.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(instrument.Unit));
            Assert.False(string.IsNullOrWhiteSpace(instrument.Description));
        });
        Assert.Equal(prefix, metrics.ActivitySource.Name);
    }

    [Fact]
    public void StoredMessagesGauge_UsesAnAnnotationUnitSoPrometheusAddsNoRatioSuffix()
    {
        const string prefix = "custom_signalizr";
        Instrument? gauge = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Name == $"{prefix}.inbound.stored_messages")
                    gauge = instrument;
            }
        };
        listener.Start();

        using var metrics = new SignalizrMetrics(Options.Create(new AppConfig
        {
            MetricNamePrefix = prefix
        }));

        Assert.NotNull(gauge);
        Assert.Equal("{message}", gauge.Unit);
    }

    [Fact]
    public void OutboundMethods_RecordSentAndWarningCounters()
    {
        const string prefix = "custom_signalizr";
        var measurements = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == prefix)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();

        using var metrics = new SignalizrMetrics(Options.Create(new AppConfig
        {
            MetricNamePrefix = prefix
        }));

        metrics.RecordSent();
        metrics.RecordSendRateWarning();

        Assert.Contains($"{prefix}.outbound.sent", measurements);
        Assert.Contains($"{prefix}.outbound.send_rate_warnings", measurements);
    }
}
