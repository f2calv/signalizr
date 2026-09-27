namespace CasCap.Abstractions;

/// <summary>Resolves exact Signal group names to group ids.</summary>
/// <remarks>
/// Resolution happens against the account's current groups, so it is a runtime lookup rather than
/// configuration. Callers use the exact group name, including case and spaces, and never see a group id.
/// </remarks>
public interface IGroupResolver
{
    /// <summary>The group names currently resolved to a group id.</summary>
    IReadOnlyCollection<string> GroupNames { get; }

    /// <summary>Looks up the Signal group id for a group name.</summary>
    /// <returns><see langword="true"/> when the group is configured and resolved.</returns>
    bool TryGetGroupId(string groupName, out string groupId);

    /// <summary>Looks up the group name for a Signal group id.</summary>
    /// <remarks>
    /// The inbound direction. An unconfigured group has no resolved name, which is normal rather
    /// than an error: the account can belong to groups this deployment ignores.
    /// </remarks>
    /// <returns><see langword="true"/> when the identifier resolves to a configured group name.</returns>
    bool TryGetGroupName(string groupId, out string groupName);

    /// <summary>
    /// Resolves every configured group against the account's groups, replacing the current map.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A configured group name matches no group, or matches more than one.
    /// </exception>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
