namespace CasCap.Tests.Unit;

/// <summary>Verifies approval polls are decided by accepted votes, and that anything else denies the call.</summary>
[Trait("Category", "Approval")]
public sealed class ToolApprovalServiceTests
{
    private const string Approver = "+10000000001";
    private const string Bystander = "+10000000002";
    private const string ToolName = "unlock_house_door";

    [Theory]
    [InlineData(0, ToolApprovalOutcome.Approved)]
    [InlineData(1, ToolApprovalOutcome.Denied)]
    public async Task RequestApprovalAsync_VoteDecidesAndClosesPoll(int optionIndex, ToolApprovalOutcome expected)
    {
        var signalizr = new FakeSignalizrClient();
        var service = CreateService(signalizr, timeoutMs: 10_000, approverIds: [Approver]);

        var request = service.RequestApprovalAsync(ToolName, "Security Agent", null, TestContext.Current.CancellationToken);
        var pollId = await VoteWhenPendingAsync(service, signalizr, Approver, optionIndex);

        Assert.Equal(expected, await request);
        var poll = Assert.Single(signalizr.Polls);
        Assert.Equal(["Approve", "Deny"], poll.Answers);
        Assert.Contains("Security Agent", poll.Question);
        Assert.Contains(ToolName, poll.Question);
        Assert.Equal(pollId, Assert.Single(signalizr.ClosedPolls).PollId);
    }

    [Fact]
    public async Task RequestApprovalAsync_NonApproverVoteIsClaimedButIgnored()
    {
        var signalizr = new FakeSignalizrClient();
        var service = CreateService(signalizr, timeoutMs: 1_000, approverIds: [Approver]);

        var request = service.RequestApprovalAsync(ToolName, "Security Agent", null, TestContext.Current.CancellationToken);
        await VoteWhenPendingAsync(service, signalizr, Bystander, optionIndex: 0);

        Assert.Equal(ToolApprovalOutcome.TimedOut, await request);
        Assert.Single(signalizr.ClosedPolls);
    }

    [Fact]
    public async Task RequestApprovalAsync_AnyGroupMemberCountsWithoutApproverIds()
    {
        var signalizr = new FakeSignalizrClient();
        var service = CreateService(signalizr, timeoutMs: 10_000, approverIds: []);

        var request = service.RequestApprovalAsync(ToolName, "Comms Agent", null, TestContext.Current.CancellationToken);
        await VoteWhenPendingAsync(service, signalizr, Bystander, optionIndex: 0);

        Assert.Equal(ToolApprovalOutcome.Approved, await request);
    }

    [Fact]
    public async Task RequestApprovalAsync_UnreachableGatewayIsUnavailable()
    {
        var signalizr = new FakeSignalizrClient { PollFailure = new HttpRequestException("Gateway unreachable.") };
        var service = CreateService(signalizr, timeoutMs: 10_000, approverIds: []);

        var outcome = await service.RequestApprovalAsync(ToolName, "Comms Agent", null, TestContext.Current.CancellationToken);

        Assert.Equal(ToolApprovalOutcome.Unavailable, outcome);
        Assert.Empty(signalizr.ClosedPolls);
    }

    [Fact]
    public void TryHandle_UnknownPollIsNotClaimed()
    {
        var service = CreateService(new FakeSignalizrClient(), timeoutMs: 10_000, approverIds: []);

        Assert.False(service.TryHandle(Approver, new SignalizrPollVote { PollId = "unknown", OptionIndexes = [0] }));
    }

    [Fact]
    public void BuildQuestion_TruncatesLongArguments()
    {
        var question = ToolApprovalService.BuildQuestion(ToolName, "Security Agent", new string('x', 500), TimeSpan.FromSeconds(30));

        Assert.Contains("Denied automatically after 30 seconds.", question);
        Assert.DoesNotContain(new string('x', 201), question);
    }

    private static ToolApprovalService CreateService(ISignalizrClient signalizr, int timeoutMs, string[] approverIds) =>
        new(
            NullLogger<ToolApprovalService>.Instance,
            Options.Create(new CommsConfig { GroupName = "Chat Group" }),
            Options.Create(new ToolApprovalConfig { TimeoutMs = timeoutMs, ApproverIds = [.. approverIds] }),
            TimeProvider.System,
            signalizr);

    // The vote can only be claimed once the service has registered the poll it just created.
    private static async Task<string> VoteWhenPendingAsync(
        ToolApprovalService service,
        FakeSignalizrClient signalizr,
        string voter,
        int optionIndex)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (signalizr.Polls.TryPeek(out var poll)
                && service.TryHandle(voter, new SignalizrPollVote { PollId = poll.PollId, OptionIndexes = [optionIndex] }))
                return poll.PollId;
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The approval poll was never registered.");
    }
}
