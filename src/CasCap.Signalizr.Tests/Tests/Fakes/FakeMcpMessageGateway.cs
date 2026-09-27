using CasCap.Abstractions;
using CasCap.Exceptions;
using CasCap.Models.Dtos;

namespace CasCap.Tests.Fakes;

/// <summary>Records MCP send attempts without contacting Signal or running other gateway actions.</summary>
public sealed class FakeMcpMessageGateway(IGroupResolver groupResolver) : IMessageGateway
{
    /// <summary>Number of calls made to the gateway send method.</summary>
    public int SendCalls { get; private set; }

    /// <summary>The exact requested group name.</summary>
    public string? LastGroupName { get; private set; }

    /// <summary>The last send payload, containing synthetic test data only.</summary>
    public SendMessageRequest? LastRequest { get; private set; }

    /// <summary>The cancellation token supplied to the gateway.</summary>
    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>An optional simulated transport failure.</summary>
    public Exception? Failure { get; set; }

    /// <summary>An optional callback invoked as a synthetic send begins.</summary>
    public Action? BeforeSend { get; set; }

    /// <summary>The acknowledgement returned by a successful synthetic send.</summary>
    public string Timestamp { get; set; } = "1758518400000";

    public Task<SendMessageResponse> SendAsync(
        string groupName, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        SendCalls++;
        LastCancellationToken = cancellationToken;
        BeforeSend?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (!groupResolver.TryGetGroupId(groupName, out _))
            throw new UnknownGroupException(groupName);
        LastGroupName = groupName;
        LastRequest = request;
        if (Failure is not null)
            return Task.FromException<SendMessageResponse>(Failure);
        return Task.FromResult(new SendMessageResponse { GroupName = groupName, Timestamp = Timestamp });
    }

    public Task SetReactionAsync(string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task RemoveReactionAsync(string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task SetDeliveryReactionAsync(string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task RemoveDeliveryReactionAsync(string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<GroupPollResponse> CreatePollAsync(string groupName, GroupPollRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
