using CasCap.Common.Abstractions;
using CasCap.Common.Authentication;
using CasCap.Common.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore;

namespace CasCap.Extensions;

/// <summary>Registers and maps only the explicitly enabled MCP tools and prompts.</summary>
public static class McpServiceExtensions
{
    /// <summary>Adds the opt-in MCP services alongside the gateway and receiver.</summary>
    /// <remarks>
    /// Enabling MCP requires <see cref="ApiAuthConfig"/>; startup fails when its credentials are missing.
    /// </remarks>
    public static IServiceCollection AddSignalizrMcp(
        this IServiceCollection services, IConfiguration configuration, IReadOnlySet<string> enabledFeatures)
    {
        if (!enabledFeatures.Contains(FeatureNames.Mcp))
            return services;

        services.AddCasCapConfiguration<ApiAuthConfig>();
        services.AddAuthentication(BasicAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, null);
        services.AddAuthorization();

        var mcpConfig = configuration.GetSection(McpConfig.ConfigurationSectionName)
            .Get<McpConfig>() ?? new McpConfig();
        var mcp = services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<SignalizrMcpQueryService>()
            .WithPrompts<SignalizrMcpPrompts>();

        if (mcpConfig.MessageHistoryEnabled)
            mcp.WithTools<SignalizrMcpMessageHistoryQueryService>();

        if (mcpConfig.MessageSendingEnabled)
            mcp.WithTools<SignalizrMcpMessagingService>();

        return services;
    }

    /// <summary>Omits the endpoint entirely when the MCP role is disabled.</summary>
    public static IEndpointRouteBuilder MapSignalizrMcp(
        this IEndpointRouteBuilder endpoints, IReadOnlySet<string> enabledFeatures)
    {
        if (enabledFeatures.Contains(FeatureNames.Mcp))
            // This operator-only endpoint has no browser client or cross-origin use case.
            endpoints.MapGroup(McpRoutes.Endpoint)
                .AddEndpointFilter((context, next) =>
                    context.HttpContext.Request.Headers.ContainsKey(HeaderNames.Origin)
                        ? ValueTask.FromResult<object?>(TypedResults.StatusCode(StatusCodes.Status403Forbidden))
                        : next(context))
                .RequireAuthorization()
                .MapMcp();

        return endpoints;
    }
}
