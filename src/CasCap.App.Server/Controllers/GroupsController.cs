using CasCap.Common.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>The gateway group surface, addressed by exact configured group name.</summary>
[ApiController]
[FeatureController(FeatureNames.Gateway)]
[Route("api/v1/groups")]
public sealed class GroupsController(
    IGroupResolver groupResolver,
    IMessageGateway messageGateway) : ControllerBase
{
    /// <summary>Lists configured Signal group names, never account-linked identifiers.</summary>
    [HttpGet]
    public Ok<IReadOnlyCollection<string>> GetGroups()
        => TypedResults.Ok(groupResolver.GroupNames);

    /// <inheritdoc cref="IMessageGateway.SendAsync(string, SendMessageRequest, CancellationToken)"/>
    [HttpPost("messages")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<Ok<SendMessageResponse>> SendMessage(
        [FromQuery] string groupName, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
        => TypedResults.Ok(await messageGateway.SendAsync(groupName, request, cancellationToken));

    /// <inheritdoc cref="IMessageGateway.SetReactionAsync(string, GroupReactionRequest, CancellationToken)"/>
    [HttpPost("reactions")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<NoContent> SetReaction(
        [FromQuery] string groupName, [FromBody] GroupReactionRequest request, CancellationToken cancellationToken)
    {
        await messageGateway.SetReactionAsync(groupName, request, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <inheritdoc cref="IMessageGateway.RemoveReactionAsync(string, GroupReactionRequest, CancellationToken)"/>
    [HttpDelete("reactions")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<NoContent> RemoveReaction(
        [FromQuery] string groupName, [FromBody] GroupReactionRequest request, CancellationToken cancellationToken)
    {
        await messageGateway.RemoveReactionAsync(groupName, request, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <inheritdoc cref="IMessageGateway.SetDeliveryReactionAsync(string, string, string, CancellationToken)"/>
    [HttpPost("messages/{deliveryId}/reactions")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<NoContent> SetDeliveryReaction(
        [FromQuery] string groupName, string deliveryId, [FromBody] DeliveryReactionRequest request, CancellationToken cancellationToken)
    {
        await messageGateway.SetDeliveryReactionAsync(groupName, deliveryId, request.Reaction, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <inheritdoc cref="IMessageGateway.RemoveDeliveryReactionAsync(string, string, string, CancellationToken)"/>
    [HttpDelete("messages/{deliveryId}/reactions")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<NoContent> RemoveDeliveryReaction(
        [FromQuery] string groupName, string deliveryId, [FromBody] DeliveryReactionRequest request, CancellationToken cancellationToken)
    {
        await messageGateway.RemoveDeliveryReactionAsync(groupName, deliveryId, request.Reaction, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <inheritdoc cref="IMessageGateway.StartTypingAsync(string, CancellationToken)"/>
    [HttpPut("typing")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<NoContent> StartTyping([FromQuery] string groupName, CancellationToken cancellationToken)
    {
        await messageGateway.StartTypingAsync(groupName, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <inheritdoc cref="IMessageGateway.StopTypingAsync(string, CancellationToken)"/>
    [HttpDelete("typing")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<NoContent> StopTyping([FromQuery] string groupName, CancellationToken cancellationToken)
    {
        await messageGateway.StopTypingAsync(groupName, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <inheritdoc cref="IMessageGateway.CreatePollAsync(string, GroupPollRequest, CancellationToken)"/>
    [HttpPost("polls")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<Ok<GroupPollResponse>> CreatePoll(
        [FromQuery] string groupName, [FromBody] GroupPollRequest request, CancellationToken cancellationToken)
        => TypedResults.Ok(await messageGateway.CreatePollAsync(groupName, request, cancellationToken));

    /// <inheritdoc cref="IMessageGateway.ClosePollAsync(string, string, CancellationToken)"/>
    [HttpDelete("polls/{pollId}")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<NoContent> ClosePoll([FromQuery] string groupName, string pollId, CancellationToken cancellationToken)
    {
        await messageGateway.ClosePollAsync(groupName, pollId, cancellationToken);
        return TypedResults.NoContent();
    }
}
