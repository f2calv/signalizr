using CasCap.Abstractions;

namespace CasCap.Tests.Fakes;

/// <summary>A group resolver with a fixed map, so mapping can be tested without an account.</summary>
public sealed class FakeGroupResolver(Dictionary<string, string> groupIdToName) : IGroupResolver
{
    public IReadOnlyCollection<string> GroupNames => groupIdToName.Values;

    public bool TryGetGroupId(string groupName, out string groupId)
    {
        groupId = groupIdToName.FirstOrDefault(pair => pair.Value == groupName).Key ?? string.Empty;
        return !string.IsNullOrEmpty(groupId);
    }

    public bool TryGetGroupName(string groupId, out string groupName)
        => groupIdToName.TryGetValue(groupId, out groupName!);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
