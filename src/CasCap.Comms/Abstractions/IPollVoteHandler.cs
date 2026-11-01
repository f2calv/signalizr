namespace CasCap.Abstractions;

/// <summary>
/// Claims poll votes that belong to deterministic application logic, such as an approval prompt, before
/// <see cref="CasCap.Services.CommunicationsBgService"/> hands them to the <see cref="ICommsResponder"/>.
/// </summary>
/// <remarks>
/// A claimed vote never reaches the responder, so a model cannot see or answer it. Handlers run whether
/// or not a responder is available.
/// </remarks>
public interface IPollVoteHandler
{
    /// <summary>Processes a vote when it belongs to a poll this handler owns.</summary>
    /// <param name="voter">The voter's Signal identifier. Personal data: never log it.</param>
    /// <param name="pollVote">The vote.</param>
    /// <returns><see langword="true"/> when the vote was claimed and must not be processed further.</returns>
    public bool TryHandle(string voter, SignalizrPollVote pollVote);
}
