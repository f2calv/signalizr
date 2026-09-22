namespace CasCap.Models;

/// <summary>Kestrel endpoints for the two protocols this host serves.</summary>
/// <remarks>
/// Two endpoints rather than one, because there is no ALPN without TLS: a plaintext port cannot
/// negotiate between HTTP/1.1 and HTTP/2, so a single endpoint answers one or the other. REST and
/// the health probes need HTTP/1.1; gRPC needs HTTP/2. Sharing a port fails with
/// <c>HTTP_1_1_REQUIRED</c> at the first gRPC call.
/// <para>
/// Both are configurable so several applications can run side by side during local debugging
/// without colliding.
/// </para>
/// </remarks>
public sealed record GrpcHostConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(GrpcHostConfig)}";

    /// <summary>The HTTP/1.1 port serving REST and the health probes.</summary>
    [Range(1, 65535)]
    public int Http1Port { get; init; } = 8080;

    /// <summary>The HTTP/2 port serving gRPC.</summary>
    [Range(1, 65535)]
    public int Http2Port { get; init; } = 5001;
}
