using CasCap.Abstractions;
using CasCap.Common.Abstractions;
using CasCap.Constants;
using CasCap.Models;
using CasCap.Models.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>The gateway send surface, addressed by channel name.</summary>
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
    public async Task<ActionResult<SendMessageResponse>> SendMessage(
        string channel, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await messageGateway.SendAsync(channel, request, cancellationToken));
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
            logger.LogError(ex, "{ClassName} could not reach the wrapper for channel {Channel}",
                nameof(ChannelsController), channel);
            return Problem(
                title: "Upstream unavailable",
                detail: "The signal-cli wrapper could not be reached.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
