using System.ComponentModel.DataAnnotations;

namespace CasCap.Signalizr.Client;

/// <summary>Where the gateway is and how to address it.</summary>
/// <remarks>
/// Two addresses because the gateway serves REST and gRPC on different ports: a plaintext endpoint
/// cannot negotiate protocols without TLS, so one port answers HTTP/1.1 and another HTTP/2.
/// </remarks>
public sealed record SignalizrClientConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(SignalizrClientConfig)}";

    /// <summary>Base address of the REST send surface, for example <c>http://signalizr:80</c>.</summary>
    [Required]
    public string BaseAddress { get; init; } = "http://localhost:8080";

    /// <summary>Address of the gRPC subscription surface, for example <c>http://signalizr:5001</c>.</summary>
    [Required]
    public string GrpcAddress { get; init; } = "http://localhost:5001";

    /// <summary>Stable subscriber identity used as the durable acknowledgement cursor key.</summary>
    /// <remarks>
    /// Configure one logical name per durable consumer. It must remain unchanged across restarts;
    /// machine names and generated identifiers create new cursors and defeat replay.
    /// </remarks>
    [Required, MinLength(1)]
    public string SubscriberName { get; init; } = string.Empty;
}
