using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Transforms;

namespace openai_loadbalancer;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var backendConfiguration = BackendConfig.LoadConfig(builder.Configuration);
        var yarpConfiguration = new YarpConfiguration(backendConfiguration);
        builder.Services.AddSingleton<IPassiveHealthCheckPolicy, ThrottlingHealthPolicy>();
        builder.Services.AddReverseProxy().AddTransforms(m =>
        {
            m.AddRequestTransform(yarpConfiguration.TransformRequest());
            m.AddResponseTransform(yarpConfiguration.TransformResponse());
        }).LoadFromMemory(yarpConfiguration.GetRoutes(), yarpConfiguration.GetClusters());

        builder.Services.AddHealthChecks();
        var app = builder.Build();

        app.MapHealthChecks("/healthz");

        // Add API key authentication middleware specifically for reverse proxy routes
        app.MapReverseProxy(m =>
        {
            m.UseMiddleware<ApiKeyAuthenticationMiddleware>();
            m.UseMiddleware<RetryMiddleware>(backendConfiguration);
            m.UsePassiveHealthChecks();
        });

        app.Run();
    }
}
