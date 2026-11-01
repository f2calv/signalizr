namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the AI agent responder for the shared Signalizr communications pipeline.</summary>
public static class CommsAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AgentCommsResponder"/> as the <see cref="ICommsResponder"/>, with its
    /// <see cref="CommsDebugNotifier"/>, poll tracker and the <see cref="ToolApprovalService"/>.
    /// </summary>
    /// <remarks>
    /// Register <see cref="IAgentRunEnricher"/> implementations to add host-specific measurements.
    /// The returned HTTP client builder accepts caller-owned authentication and resilience handlers.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The Agent Runtime HTTP client builder.</returns>
    public static IHttpClientBuilder AddCommsAgent(this IServiceCollection services)
    {
        services.AddSingleton(serviceProvider =>
        {
            var config = serviceProvider.GetRequiredService<IOptions<CommsConfig>>().Value;
            return new CommsAgentProfile(config.AgentName, config.AgentSessionId, config.AgentDefaultPrompt);
        });
        services.TryAddSingleton<CommsDebugNotifier>();
        services.TryAddSingleton<CommsAgentMetrics>();
        services.TryAddSingleton<ICommsResponder, AgentCommsResponder>();
        services.AddMessagingMcp();
        services.AddToolApproval();
        return services.AddAgentRuntimeClient();
    }

    /// <summary>
    /// Registers the <see cref="ToolApprovalService"/> that answers approval prompts posted for tools gated
    /// by <see cref="ToolApprovalMcpServerBuilderExtensions.WithToolApproval"/>.
    /// </summary>
    /// <remarks>
    /// The service is also an <see cref="IPollVoteHandler"/>, so approval votes are decided in code before
    /// the <see cref="ICommsResponder"/> sees them.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddToolApproval(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ToolApprovalService)))
            return services;

        services.AddToolApprovalConfig();
        services.AddSingleton<ToolApprovalService>();
        services.AddSingleton<IPollVoteHandler>(sp => sp.GetRequiredService<ToolApprovalService>());
        return services;
    }

    /// <summary>
    /// Registers the <see cref="MessagingMcpQueryService"/> that exposes poll operations on the
    /// <see cref="CommsConfig.GroupName"/> group as MCP tools.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMessagingMcp(this IServiceCollection services)
    {
        services.TryAddSingleton<IPollTracker, InMemoryPollTracker>();
        services.TryAddSingleton<MessagingMcpQueryService>();
        return services;
    }
}
