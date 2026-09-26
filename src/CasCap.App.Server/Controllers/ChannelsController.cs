using CasCap.Abstractions;
using CasCap.Common.Abstractions;
using CasCap.Constants;
using CasCap.Exceptions;
using CasCap.Models.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>The gateway channel surface, addressed by channel name.</summary>
/// <remarks>
/// REST rather than gRPC so it stays callable with curl and from a webhook, with no generated
/// client. Gated to the Gateway role, so a Receiver or DemoClient pod returns 404 here instead of
/// failing to activate the controller.
/// </remarks>
[ApiController]
[FeatureController(FeatureNames.Gateway)]
[Route("api/v1/channels")]
[Produces("application/json")]
public sealed class ChannelsController(
    ILogger<ChannelsController> logger,
    IChannelResolver channelResolver,
    IMessageGateway messageGateway) : ControllerBase
{
    /// <summary>Lists the channels currently resolved to a Signal group.</summary>
    /// <remarks>Names only. A group id is an account-linked identifier and is never returned.</remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<string>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<string>> GetChannels()
        => Ok(channelResolver.ChannelNames);

    /// <summary>Sends a message to a channel.</summary>
    [HttpPost("{channel}/messages")]
    [ProducesResponseType<SendMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> SendMessage(
        string channel, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(channel, async () => Ok(await messageGateway.SendAsync(channel, request, cancellationToken)));

    /// <summary>Sets a reaction on a message in a channel, replacing any earlier one.</summary>
    [HttpPost("{channel}/reactions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> SetReaction(
        string channel, [FromBody] ChannelReactionRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(channel, async () =>
        {
            await messageGateway.SetReactionAsync(channel, request, cancellationToken);
            return NoContent();
        });

    /// <summary>Removes a reaction from a message in a channel.</summary>
    [HttpDelete("{channel}/reactions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> RemoveReaction(
        string channel, [FromBody] ChannelReactionRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(channel, async () =>
        {
            await messageGateway.RemoveReactionAsync(channel, request, cancellationToken);
            return NoContent();
        });

    /// <summary>Shows the typing indicator in a channel.</summary>
    [HttpPut("{channel}/typing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> StartTyping(string channel, CancellationToken cancellationToken)
        => ExecuteAsync(channel, async () =>
        {
            await messageGateway.StartTypingAsync(channel, cancellationToken);
            return NoContent();
        });

    /// <summary>Clears the typing indicator in a channel.</summary>
    [HttpDelete("{channel}/typing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> StopTyping(string channel, CancellationToken cancellationToken)
        => ExecuteAsync(channel, async () =>
        {
            await messageGateway.StopTypingAsync(channel, cancellationToken);
            return NoContent();
        });

    /// <summary>Creates a poll in a channel.</summary>
    [HttpPost("{channel}/polls")]
    [ProducesResponseType<ChannelPollResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> CreatePoll(
        string channel, [FromBody] ChannelPollRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(channel, async () => Ok(await messageGateway.CreatePollAsync(channel, request, cancellationToken)));

    /// <summary>Closes a poll in a channel.</summary>
    [HttpDelete("{channel}/polls/{pollId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult> ClosePoll(string channel, string pollId, CancellationToken cancellationToken)
        => ExecuteAsync(channel, async () =>
        {
            await messageGateway.ClosePollAsync(channel, pollId, cancellationToken);
            return NoContent();
        });

    /// <summary>Runs one channel operation, translating gateway failures into problem responses.</summary>
    private async Task<ActionResult> ExecuteAsync(string channel, Func<Task<ActionResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (UnknownChannelException)
        {
            // The configured channels are not secret and the caller cannot guess them, so listing
            // them turns a dead end into a usable error.
            return Problem(
                title: "Unknown channel",
                detail: $"Channel '{channel}' is not configured. Configured channels: " +
                    $"{string.Join(", ", channelResolver.ChannelNames)}.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (HttpRequestException ex)
        {
            // 502 rather than 500: the gateway is healthy, its upstream is not, and the caller
            // should retry rather than treat the request as malformed.
            logger.LogError(ex, "{ClassName} could not complete an operation on channel {Channel}",
                nameof(ChannelsController), channel);
            return Problem(
                title: "Upstream unavailable",
                detail: "The signal-cli wrapper could not complete the request.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
