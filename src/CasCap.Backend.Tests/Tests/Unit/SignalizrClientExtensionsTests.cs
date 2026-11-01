using CasCap.Signalizr.Client;
using CasCap.Signalizr.Client.Exceptions;
using CasCap.Signalizr.Client.Testing;
using Xunit;

namespace CasCap.Tests;

/// <summary>Verifies the best-effort interaction and startup group helpers on <see cref="ISignalizrClient"/>.</summary>
public sealed class SignalizrClientExtensionsTests
{
    private const string ChatGroup = "My Test Group Name";
    private const string MonitorGroup = "My Test Monitor Group Name";

    [Fact]
    public async Task TrySetReactionAsync_Succeeds_RecordsReaction()
    {
        var client = new FakeSignalizrClient();

        var ok = await client.TrySetReactionAsync(ChatGroup, "\u2705", 42, "+10000000001",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(ok);
        Assert.Equal(1, client.ReactionCount("\u2705"));
    }

    [Fact]
    public async Task TrySetReactionAsync_TransportFailure_ReturnsFalseAndReportsIt()
    {
        var failure = new HttpRequestException("gateway down");
        var client = new FakeSignalizrClient { InteractionFailure = failure };
        Exception? reported = null;

        var ok = await client.TrySetReactionAsync(ChatGroup, "\u2705", 42, onFailure: ex => reported = ex,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.Same(failure, reported);
    }

    [Fact]
    public async Task TryStartTypingAsync_ClientTimeout_ReturnsFalse()
    {
        var client = new FakeSignalizrClient { InteractionFailure = new TaskCanceledException("timeout") };

        var ok = await client.TryStartTypingAsync(ChatGroup, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(ok);
    }

    [Fact]
    public async Task TryStopTypingAsync_RequestedCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = new FakeSignalizrClient { InteractionFailure = new OperationCanceledException(cts.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.TryStopTypingAsync(ChatGroup,
            cancellationToken: cts.Token));
    }

    [Fact]
    public async Task TryStartTypingAsync_UnexpectedFailure_Propagates()
    {
        var client = new FakeSignalizrClient { InteractionFailure = new InvalidOperationException("bug") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TryStartTypingAsync(ChatGroup,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForGroupsAsync_UnreachableGateway_RetriesUntilItAnswers()
    {
        var client = new FakeSignalizrClient { Groups = [ChatGroup, MonitorGroup], GroupsFailures = 2 };
        var retries = new List<int>();

        var missing = await client.WaitForGroupsAsync([ChatGroup], [MonitorGroup], TimeSpan.Zero,
            onRetry: (_, attempt) => retries.Add(attempt), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(missing);
        Assert.Equal([1, 2], retries);
        Assert.Equal(3, client.GetGroupsCallCount);
    }

    [Fact]
    public async Task WaitForGroupsAsync_MissingOptionalGroup_IsReturned()
    {
        var client = new FakeSignalizrClient { Groups = [ChatGroup] };

        var missing = await client.WaitForGroupsAsync([ChatGroup], [MonitorGroup, null, ""], TimeSpan.Zero,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([MonitorGroup], missing);
    }

    [Fact]
    public async Task WaitForGroupsAsync_MissingRequiredGroup_ThrowsWithoutNamingIt()
    {
        var client = new FakeSignalizrClient { Groups = [MonitorGroup] };

        var ex = await Assert.ThrowsAsync<SignalizrGroupNotConfiguredException>(() => client.WaitForGroupsAsync(
            [ChatGroup], null, TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, ex.MissingGroupCount);
        Assert.DoesNotContain(ChatGroup, ex.Message, StringComparison.Ordinal);
    }
}
