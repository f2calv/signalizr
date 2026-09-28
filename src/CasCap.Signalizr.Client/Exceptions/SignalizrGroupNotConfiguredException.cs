namespace CasCap.Signalizr.Client.Exceptions;

/// <summary>Thrown when the gateway does not serve every group a consumer requires.</summary>
/// <remarks>
/// A missing group is a configuration fault that retrying cannot fix. The message deliberately omits the
/// group names, which can carry private information; the caller already knows what it configured.
/// </remarks>
/// <param name="missingGroupCount">The number of required groups the gateway did not return.</param>
public sealed class SignalizrGroupNotConfiguredException(int missingGroupCount)
    : Exception($"{missingGroupCount} required Signalizr group(s) are not configured on the gateway. " +
        "Check the exact display names, including spaces and case.")
{
    /// <summary>The number of required groups the gateway did not return.</summary>
    public int MissingGroupCount { get; } = missingGroupCount;
}
