namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the AI agent responder for the shared Signalizr communications pipeline.</summary>
public static class CommsAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AgentCommsResponder"/> as the <see cref="ICommsResponder"/>, with its
    /// <see cref="CommsDebugNotifier"/> and poll tracker.
    /// </summary>
    /// <remarks>
    /// Register <see cref="IAgentRunEnricher"/> implementations to add host-specific measurements.
    /// The returned HTTP client builder accepts caller-owned authentication and resilience handlers.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="agentName">The stable tenant-local Agent Runtime name.</param>
    /// <param name="sessionId">The stable opaque conversation session identifier.</param>
    /// <param name="defaultPrompt">The prompt used when an attachment arrives without text.</param>
    /// <returns>The Agent Runtime HTTP client builder.</returns>
    public static IHttpClientBuilder AddCommsAgent(
        this IServiceCollection services,
        string agentName,
        string sessionId,
        string defaultPrompt)
    {
        services.AddSingleton(new CommsAgentProfile(agentName, sessionId, defaultPrompt));
        services.TryAddSingleton<CommsDebugNotifier>();
        services.TryAddSingleton<ICommsResponder, AgentCommsResponder>();
        services.AddMessagingMcp();
        return services.AddAgentRuntimeClient();
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
        services.TryAddSingleton(sp => new MessagingMcpQueryService(
            sp.GetRequiredService<ISignalizrClient>(),
            sp.GetRequiredService<IPollTracker>(),
            sp.GetRequiredService<IOptions<CommsConfig>>().Value.GroupName));
        return services;
    }

    /// <summary>
    /// Registers a stub <see cref="MessagingMcpQueryService"/> so agent configurations that reference it
    /// pass dependency-injection validation without a Signalizr gateway.
    /// </summary>
    /// <remarks>Its tools fail at invocation time. Use it only where the <c>Comms</c> feature is disabled.</remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMessagingMcpStub(this IServiceCollection services)
    {
        services.TryAddSingleton<IPollTracker, InMemoryPollTracker>();
        services.TryAddSingleton(sp => new MessagingMcpQueryService(
            null!,
            sp.GetRequiredService<IPollTracker>(),
            string.Empty));
        return services;
    }
}
