using CasCap.Abstractions;

namespace CasCap.Tests.Fakes;

/// <summary>A channel resolver with a fixed map, so mapping can be tested without an account.</summary>
public sealed class FakeChannelResolver(Dictionary<string, string> groupIdToChannel) : IChannelResolver
{
    public IReadOnlyCollection<string> ChannelNames => groupIdToChannel.Values;

    public bool TryGetGroupId(string channelName, out string groupId)
    {
        groupId = groupIdToChannel.FirstOrDefault(pair => pair.Value == channelName).Key ?? string.Empty;
        return !string.IsNullOrEmpty(groupId);
    }

    public bool TryGetChannelName(string groupId, out string channelName)
        => groupIdToChannel.TryGetValue(groupId, out channelName!);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
