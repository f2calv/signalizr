using CasCap.Common.Abstractions;
using CasCap.Constants;
using CasCap.Services;
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
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string attachmentId,
        CancellationToken cancellationToken)
        => await attachmentSvc.GetAsync(attachmentId, cancellationToken).ConfigureAwait(false) is { } content
            ? File(content, "application/octet-stream")
            : NotFound();
}
