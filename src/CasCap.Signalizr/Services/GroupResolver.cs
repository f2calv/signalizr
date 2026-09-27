namespace CasCap.Services;

/// <inheritdoc cref="IGroupResolver"/>
public sealed class GroupResolver(
    ILogger<GroupResolver> logger,
    ISignalCliClient client,
    IOptions<GroupConfig> groupConfig,
    IOptions<SignalCliConfig> signalCliConfig) : IGroupResolver
{
    private IReadOnlyDictionary<string, string> _resolved =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private IReadOnlyList<KeyValuePair<SignalGroup, string>> _groups = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GroupNames => Volatile.Read(ref _resolved).Keys.ToArray();

    /// <inheritdoc/>
    public bool TryGetGroupId(string groupName, out string groupId)
        => Volatile.Read(ref _resolved).TryGetValue(groupName, out groupId!);

    /// <inheritdoc/>
    public bool TryGetGroupName(string groupId, out string groupName)
    {
        var match = Volatile.Read(ref _groups).FirstOrDefault(pair => pair.Key.Matches(groupId));
        groupName = match.Value ?? string.Empty;
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

        var resolved = Resolve(groupConfig.Value.GroupNames, groups);
        Volatile.Write(ref _resolved, resolved);
        Volatile.Write(ref _groups, BuildInboundLookup(resolved, groups));

        logger.LogInformation("{ClassName} resolved {GroupCount} Signal group(s)",
            nameof(GroupResolver), resolved.Count);
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
        IReadOnlyCollection<string> groupNames, IReadOnlyList<SignalGroup> groups)
    {
        var resolved = new Dictionary<string, string>(groupNames.Count, StringComparer.Ordinal);
        var failures = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var groupName in groupNames)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                failures.Add("a configured group name is blank");
                continue;
            }
            if (!seenNames.Add(groupName))
            {
                failures.Add("a group name is configured more than once");
                continue;
            }
            var matches = groups.Where(g => string.Equals(g.Name, groupName, StringComparison.Ordinal)).ToList();
            switch (matches.Count)
            {
                case 1:
                    resolved[groupName] = matches[0].Id;
                    break;
                case 0:
                    failures.Add("a configured name matches no group");
                    break;
                default:
                    failures.Add($"a configured name matches {matches.Count} groups");
                    break;
            }
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"Group resolution failed: {string.Join("; ", failures)}.");

        return resolved;
    }

    /// <summary>Builds the group identifier-to-name lookup used by the inbound direction.</summary>
    /// <remarks>
    /// <see cref="SignalGroup.Matches(string?)"/> owns the distinction between the send and inbound
    /// identifier forms, so this consumer treats group identifiers as opaque.
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<SignalGroup, string>> BuildInboundLookup(
        IReadOnlyDictionary<string, string> resolved, IReadOnlyList<SignalGroup> groups)
    {
        return groups
            .Where(group => resolved.TryGetValue(group.Name, out var id)
                && string.Equals(id, group.Id, StringComparison.Ordinal))
            .Select(group => new KeyValuePair<SignalGroup, string>(group, group.Name))
            .ToArray();
    }

}
