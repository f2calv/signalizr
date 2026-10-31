namespace CasCap.Services;

/// <summary>Checks that every subscriber's groups are groups this gateway serves.</summary>
/// <remarks>A misspelt group would otherwise leave its subscriber silently receiving nothing.</remarks>
public sealed class SubscriberConfigValidator(IOptions<GroupConfig> groupConfig) : IValidateOptions<SubscriberConfig>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, SubscriberConfig options)
    {
        var served = groupConfig.Value.GroupNames.ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var (subscriberName, groups) in options.SubscriberGroups)
        {
            if (groups is not { Length: > 0 })
                failures.Add($"Subscriber '{subscriberName}' must list at least one group.");
            else if (groups.Any(group => !served.Contains(group)))
                failures.Add($"Subscriber '{subscriberName}' lists a group that is not in GroupConfig:GroupNames.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
