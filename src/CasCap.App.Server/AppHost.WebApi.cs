using CasCap.Common.Extensions;
using CasCap.Extensions;

namespace CasCap;

public static partial class AppHost
{
    private static void AddWebApi(
        WebApplicationBuilder builder,
        IReadOnlySet<string> enabledFeatures)
    {
        builder.Services.AddHealthChecks();

        // Every controller compiles into every image, so a controller whose dependency is registered only
        // for one role would throw on activation elsewhere. Gating removes it from routing entirely.
        builder.Services.AddControllers().AddFeatureGatedControllers(enabledFeatures);
        builder.Services.AddSignalizrMcp(builder.Configuration, enabledFeatures);
    }
}