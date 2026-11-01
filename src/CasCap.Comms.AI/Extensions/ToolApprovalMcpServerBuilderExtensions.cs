namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds human approval of gated tools to an MCP server.</summary>
public static class ToolApprovalMcpServerBuilderExtensions
{
    /// <summary>
    /// Holds every call to a tool marked with <see cref="RequiresApprovalAttribute"/> until a person approves it.
    /// </summary>
    /// <remarks>
    /// Register this on every MCP server that exposes a gated tool. Approval prompts need
    /// <see cref="ToolApprovalService"/>, which <see cref="CommsAgentServiceCollectionExtensions.AddCommsAgent"/>
    /// registers; on a host without it, gated tools are refused rather than run.
    /// </remarks>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IMcpServerBuilder WithToolApproval(this IMcpServerBuilder builder)
    {
        builder.Services.AddToolApprovalConfig();
        return builder.WithRequestFilters(filters => filters.AddCallToolFilter(next =>
            (context, cancellationToken) => ToolApprovalMcpFilter.InvokeAsync(context, next, cancellationToken)));
    }

    /// <summary>Registers <see cref="ToolApprovalConfig"/> once.</summary>
    internal static IServiceCollection AddToolApprovalConfig(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<ToolApprovalConfig>)))
            services.AddCasCapConfiguration<ToolApprovalConfig>();
        return services;
    }
}
