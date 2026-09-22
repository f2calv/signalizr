namespace CasCap.Services;

/// <inheritdoc cref="IChannelResolver"/>
public sealed class ChannelResolver(
    ILogger<ChannelResolver> logger,
    ISignalCliClient client,
    IOptions<ChannelConfig> channelConfig,
    IOptions<SignalCliConfig> signalCliConfig) : IChannelResolver
{
    private IReadOnlyDictionary<string, string> _resolved =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Group ids are opaque and case-sensitive, unlike the channel names callers type.
    private IReadOnlyDictionary<string, string> _byGroupId =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> ChannelNames => Volatile.Read(ref _resolved).Keys.ToArray();

    /// <inheritdoc/>
    public bool TryGetGroupId(string channelName, out string groupId)
        => Volatile.Read(ref _resolved).TryGetValue(channelName, out groupId!);

    /// <inheritdoc/>
    public bool TryGetChannelName(string groupId, out string channelName)
        => Volatile.Read(ref _byGroupId).TryGetValue(groupId, out channelName!);

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var number = signalCliConfig.Value.PhoneNumber;
        // HttpRequestException, not InvalidOperationException: an absent group list means the
        // wrapper is unhealthy, which callers retry. InvalidOperationException is reserved for
        // configuration faults that retrying cannot fix.
        var groups = await client.ListGroups(number, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The signal-cli wrapper returned no group list.");

        var resolved = Resolve(channelConfig.Value.Channels, groups);
        Volatile.Write(ref _resolved, resolved);
        Volatile.Write(ref _byGroupId, Invert(resolved));

        // Group ids are account-linked identifiers, so log the channel names only.
        logger.LogInformation("{ClassName} resolved {ChannelCount} channel(s): {Channels}",
            nameof(ChannelResolver), resolved.Count, string.Join(", ", resolved.Keys));
    }

    /// <summary>Matches configured group names against the account's groups.</summary>
    /// <remarks>
    /// Separated from the fetch so the failure rules are testable without a Signal account.
    /// Group names are compared with <see cref="StringComparison.Ordinal"/>: Signal treats a group
    /// name as an opaque display string, and a near-match is far more likely to be a different
    /// group than a spelling variant of the intended one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A configured group name matches no group, or matches more than one. Ambiguity is fatal
    /// rather than first-match: silently picking one would route messages to the wrong group, and
    /// nothing downstream could detect it.
    /// </exception>
    public static IReadOnlyDictionary<string, string> Resolve(
        IReadOnlyDictionary<string, string> channels, IReadOnlyList<SignalGroup> groups)
    {
        var resolved = new Dictionary<string, string>(channels.Count, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        foreach (var (channelName, groupName) in channels)
        {
            var matches = groups.Where(g => string.Equals(g.Name, groupName, StringComparison.Ordinal)).ToList();
            switch (matches.Count)
            {
                case 1:
                    resolved[channelName] = matches[0].Id;
                    break;
                case 0:
                    failures.Add($"channel '{channelName}' matches no group named '{groupName}'");
                    break;
                default:
                    failures.Add($"channel '{channelName}' matches {matches.Count} groups named '{groupName}'");
                    break;
            }
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"Channel resolution failed: {string.Join("; ", failures)}.");

        return resolved;
    }

    /// <summary>Builds the group-id to channel-name index used by the inbound direction.</summary>
    /// <remarks>
    /// Two channel names may point at the same group, which <see cref="Resolve"/> permits because
    /// sending to either is unambiguous. Inbound is not: the last name wins, so the mapping is
    /// deterministic by ordinal name order rather than by dictionary enumeration order.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Invert(IReadOnlyDictionary<string, string> resolved)
    {
        var inverted = new Dictionary<string, string>(resolved.Count, StringComparer.Ordinal);

        foreach (var (channelName, groupId) in resolved.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            inverted[groupId] = channelName;

        return inverted;
    }
}
