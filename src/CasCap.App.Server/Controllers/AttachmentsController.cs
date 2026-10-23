using CasCap.Common.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CasCap.Controllers;

/// <summary>Durable inbound binary attachment surface.</summary>
[ApiController]
[FeatureController(FeatureNames.Receiver)]
[Route("api/v1/attachments")]
public sealed class AttachmentsController(InboundAttachmentService attachmentSvc) : ControllerBase
{
    /// <inheritdoc cref="InboundAttachmentService.GetAsync(string, CancellationToken)"/>
    [HttpGet("{attachmentId}")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/octet-stream")]
    public async Task<Results<FileContentHttpResult, NotFound>> Get(
        string attachmentId,
        CancellationToken cancellationToken)
        => await attachmentSvc.GetAsync(attachmentId, cancellationToken).ConfigureAwait(false) is { } content
            ? TypedResults.File(content, "application/octet-stream")
            : TypedResults.NotFound();
}
