using CasCap.Common.Abstractions;
using CasCap.Common.Extensions;
using CasCap.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace CasCap;

public static partial class AppHost
{
    // TODO: Expose role-aware subscription/cursor progress separately from HTTP process health.
    // Defer probe changes until sparse-traffic thresholds and dependency readiness contracts are agreed;
    // lack of messages alone must not restart a healthy subscriber.
    private static void MapEndpoints(
        WebApplication app,
        IReadOnlySet<string> enabledFeatures,
        bool servesGrpc)
    {
        app.UseExceptionHandler();
        app.MapControllers();
        app.MapSignalizrMcp(enabledFeatures);

        if (servesGrpc)
            app.MapGrpcService<InboundGrpcService>();

        app.MapHealthChecks("/healthz");

        // One endpoint per Kubernetes probe type, each running only the checks carrying that tag.
        // The upstream signal-cli check is tagged "ready" by default, so an unreachable wrapper takes a
        // pod out of the Service rotation without also failing liveness — a liveness probe that depended
        // on the upstream would restart every gateway pod during a wrapper outage it cannot fix.
        foreach (var probeType in Enum.GetValues<KubernetesProbeTypes>())
        {
            if (probeType is KubernetesProbeTypes.None)
                continue;

            var tag = probeType.GetDescription();
            app.MapHealthChecks($"/healthz/{tag}", new HealthCheckOptions
            {
                Predicate = healthCheck => healthCheck.Tags.Contains(tag)
            });
        }
    }
}
