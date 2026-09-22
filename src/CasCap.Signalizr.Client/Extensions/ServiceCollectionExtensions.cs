using CasCap.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CasCap.Signalizr.Client;

/// <summary>Registers the gateway client.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ISignalizrClient"/>, binding <c>CasCap:SignalizrClientConfig</c>.
    /// </summary>
    public static IServiceCollection AddSignalizrClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SignalizrClientConfig>()
            .Bind(configuration.GetSection(SignalizrClientConfig.ConfigurationSectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ISignalizrClient, SignalizrClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SignalizrClientConfig>>().Value;
            client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + '/');
        });

        // A separate address, not a separate scheme: the gateway serves gRPC on its own HTTP/2
        // port because a plaintext endpoint cannot negotiate protocols.
        services.AddGrpcClient<Inbound.InboundClient>((sp, options) =>
        {
            var config = sp.GetRequiredService<IOptions<SignalizrClientConfig>>().Value;
            options.Address = new Uri(config.GrpcAddress);
        });

        return services;
    }
}
