using CasCap.Common.Extensions;
using CasCap.Extensions;
using System.Text.Json.Serialization;

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
        builder.Services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .AddFeatureGatedControllers(enabledFeatures);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GroupsApiExceptionHandler>();
        builder.Services.AddSignalizrMcp(builder.Configuration, enabledFeatures);
    }
}
