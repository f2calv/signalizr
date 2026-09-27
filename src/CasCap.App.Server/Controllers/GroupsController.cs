using CasCap.Common.Abstractions;
using CasCap.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>The gateway group surface, addressed by group name.</summary>
/// <remarks>
/// REST rather than gRPC so it stays callable with curl and from a webhook, with no generated
/// client. Gated to the Gateway role, so a Receiver or DemoClient pod returns 404 here instead of
/// failing to activate the controller.
/// </remarks>
[ApiController]
[FeatureController(FeatureNames.Gateway)]
[Route("api/v1/groups")]
[Produces("application/json")]
public sealed class GroupsController(
    ILogger<GroupsController> logger,
    IGroupResolver groupResolver,
    IMessageGateway messageGateway) : ControllerBase
{
    /// <summary>Lists the configured Signal groups currently resolved.</summary>
    /// <remarks>Names only. A group id is an account-linked identifier and is never returned.</remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<string>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<string>> GetGroups()
        => Ok(groupResolver.GroupNames);

    /// <summary>Sends a message to a group.</summary>
    [HttpPost("messages")]
    [ProducesResponseType<SendMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> SendMessage(
        [FromQuery] string groupName, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () => Ok(await messageGateway.SendAsync(groupName, request, cancellationToken)));

    /// <summary>Sets a reaction on a message in a group, replacing any earlier one.</summary>
    [HttpPost("reactions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> SetReaction(
        [FromQuery] string groupName, [FromBody] GroupReactionRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () =>
        {
            await messageGateway.SetReactionAsync(groupName, request, cancellationToken);
            return NoContent();
        });

    /// <summary>Removes a reaction from a message in a group.</summary>
    [HttpDelete("reactions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> RemoveReaction(
        [FromQuery] string groupName, [FromBody] GroupReactionRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () =>
        {
            await messageGateway.RemoveReactionAsync(groupName, request, cancellationToken);
            return NoContent();
        });

    /// <summary>Sets a reaction on a delivered message, addressed by its delivery identifier.</summary>
    [HttpPost("messages/{deliveryId}/reactions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> SetDeliveryReaction(
        [FromQuery] string groupName, string deliveryId, [FromBody] DeliveryReactionRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () =>
        {
            await messageGateway.SetDeliveryReactionAsync(groupName, deliveryId, request.Reaction, cancellationToken);
            return NoContent();
        });

    /// <summary>Removes a reaction from a delivered message, addressed by its delivery identifier.</summary>
    [HttpDelete("messages/{deliveryId}/reactions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> RemoveDeliveryReaction(
        [FromQuery] string groupName, string deliveryId, [FromBody] DeliveryReactionRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () =>
        {
            await messageGateway.RemoveDeliveryReactionAsync(groupName, deliveryId, request.Reaction, cancellationToken);
            return NoContent();
        });

    /// <summary>Shows the typing indicator in a group and holds it until cleared or expired.</summary>
    [HttpPut("typing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> StartTyping([FromQuery] string groupName, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () =>
        {
            await messageGateway.StartTypingAsync(groupName, cancellationToken);
            return NoContent();
        });

    /// <summary>Clears the typing indicator in a group.</summary>
    [HttpDelete("typing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> StopTyping([FromQuery] string groupName, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () =>
        {
            await messageGateway.StopTypingAsync(groupName, cancellationToken);
            return NoContent();
        });

    /// <summary>Creates a poll in a group.</summary>
    [HttpPost("polls")]
    [ProducesResponseType<GroupPollResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> CreatePoll(
        [FromQuery] string groupName, [FromBody] GroupPollRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () => Ok(await messageGateway.CreatePollAsync(groupName, request, cancellationToken)));

    /// <summary>Closes a poll in a group.</summary>
    [HttpDelete("polls/{pollId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> ClosePoll([FromQuery] string groupName, string pollId, CancellationToken cancellationToken)
        => ExecuteAsync(groupName, async () =>
        {
            await messageGateway.ClosePollAsync(groupName, pollId, cancellationToken);
            return NoContent();
        });

    /// <summary>Runs one group operation, translating gateway failures into problem responses.</summary>
    private async Task<ActionResult> ExecuteAsync(string groupName, Func<Task<ActionResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (UnknownGroupException)
        {
            // The same authorized callers can discover these group names through GET groups.
            return Problem(
                title: "Unknown group",
                detail: $"Group '{groupName}' is not configured. Configured groups: " +
                    $"{string.Join(", ", groupResolver.GroupNames)}.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (UnknownDeliveryException ex)
        {
            return Problem(
                title: "Unknown delivery",
                detail: $"Message '{ex.DeliveryId}' is not retained in group '{groupName}'.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (NotSupportedException ex)
        {
            return Problem(
                title: "Not available on this role",
                detail: ex.Message,
                statusCode: StatusCodes.Status501NotImplemented);
        }
        catch (HttpRequestException ex)
        {
            // 502 rather than 500: the gateway is healthy, its upstream is not, and the caller
            // should retry rather than treat the request as malformed.
            logger.LogError(ex, "{ClassName} could not complete a Signal group operation",
                nameof(GroupsController));
            return Problem(
                title: "Upstream unavailable",
                detail: "The signal-cli wrapper could not complete the request.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
