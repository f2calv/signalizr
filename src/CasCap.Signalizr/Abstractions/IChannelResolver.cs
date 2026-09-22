namespace CasCap.Abstractions;

/// <summary>Resolves application-facing channel names to Signal group ids.</summary>
/// <remarks>
/// Resolution happens against the account's current groups, so it is a runtime lookup rather than
/// configuration. Callers address channels by name and never see a group id.
/// </remarks>
public interface IChannelResolver
{
    /// <summary>The channel names currently resolved to a group id.</summary>
    IReadOnlyCollection<string> ChannelNames { get; }

    /// <summary>Looks up the Signal group id for a channel name.</summary>
    /// <returns><see langword="true"/> when the channel is configured and resolved.</returns>
    bool TryGetGroupId(string channelName, out string groupId);

    /// <summary>
    /// Resolves every configured channel against the account's groups, replacing the current map.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A configured group name matches no group, or matches more than one.
    /// </exception>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
