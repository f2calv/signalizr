using CasCap.Controllers;
using CasCap.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace CasCap.Services;

/// <summary>Maps group-operation failures without exposing upstream exception content to logs or responses.</summary>
public sealed class GroupsApiExceptionHandler(ILogger<GroupsApiExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() != typeof(GroupsController))
            return false;

        var problem = exception switch
        {
            UnknownGroupException unknown => (
                StatusCodes.Status404NotFound, "Unknown group",
                $"Group '{unknown.GroupName}' is not configured. Configured groups: {string.Join(", ", httpContext.RequestServices.GetRequiredService<IGroupResolver>().GroupNames)}."),
            UnknownDeliveryException unknown => (
                StatusCodes.Status404NotFound, "Unknown delivery",
                $"Message '{unknown.DeliveryId}' is not retained in group '{unknown.GroupName}'."),
            NotSupportedException => (
                StatusCodes.Status501NotImplemented, "Not available on this role",
                "Delivery lookups need the Receiver role in the same process."),
            HttpRequestException => (
                StatusCodes.Status502BadGateway, "Upstream unavailable",
                "The signal-cli wrapper could not complete the request."),
            _ => (0, "", ""),
        };
        if (problem.Item1 == 0)
            return false;

        if (exception is HttpRequestException)
            logger.LogError("Signal group operation failed because the upstream wrapper was unavailable");

        await TypedResults.Problem(statusCode: problem.Item1, title: problem.Item2, detail: problem.Item3)
            .ExecuteAsync(httpContext);
        return true;
    }
}
