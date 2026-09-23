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

    private IReadOnlyList<KeyValuePair<SignalGroup, string>> _groups = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> ChannelNames => Volatile.Read(ref _resolved).Keys.ToArray();

    /// <inheritdoc/>
    public bool TryGetGroupId(string channelName, out string groupId)
        => Volatile.Read(ref _resolved).TryGetValue(channelName, out groupId!);

    /// <inheritdoc/>
    public bool TryGetChannelName(string groupId, out string channelName)
    {
        var match = Volatile.Read(ref _groups).FirstOrDefault(pair => pair.Key.Matches(groupId));
        channelName = match.Value ?? string.Empty;
        return match.Key is not null;
    }

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
        Volatile.Write(ref _groups, BuildInboundLookup(resolved, groups));

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

    /// <summary>Builds the group-to-channel lookup used by the inbound direction.</summary>
    /// <remarks>
    /// <see cref="SignalGroup.Matches(string?)"/> owns the distinction between the send and inbound
    /// identifier forms, so this consumer treats group identifiers as opaque.
    /// <para>
    /// Two channel names may point at the same group, which <see cref="Resolve"/> permits because
    /// sending to either is unambiguous. Inbound is not: the last name wins, so entries are stored
    /// in descending ordinal name order and the first match is deterministic.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<SignalGroup, string>> BuildInboundLookup(
        IReadOnlyDictionary<string, string> resolved, IReadOnlyList<SignalGroup> groups)
    {
        return resolved
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(entries => new KeyValuePair<SignalGroup, string>(
                groups.Single(group => string.Equals(group.Id, entries.Key, StringComparison.Ordinal)),
                entries.OrderBy(pair => pair.Key, StringComparer.Ordinal).Last().Key))
            .ToArray();
    }
}
