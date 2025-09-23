namespace openai_loadbalancer;

public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly string? _requiredApiKey;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _requiredApiKey = configuration["LB_API_KEY"] ?? Environment.GetEnvironmentVariable("LB_API_KEY");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for health check endpoint
        if (context.Request.Path.StartsWithSegments("/healthz"))
        {
            await _next(context);
            return;
        }

        // If no API key is configured, skip authentication
        if (string.IsNullOrWhiteSpace(_requiredApiKey))
        {
            _logger.LogWarning("No load balancer API key configured. Authentication is disabled. Set LB_API_KEY environment variable to enable authentication.");
            await _next(context);
            return;
        }

        // Check for API key in headers
        var providedApiKey = GetApiKeyFromRequest(context.Request);

        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            _logger.LogWarning("Request received without API key from {RemoteIpAddress}", context.Connection.RemoteIpAddress);
            await WriteUnauthorizedResponse(context, "API key is required");
            return;
        }

        if (!string.Equals(providedApiKey, _requiredApiKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("Request received with invalid API key from {RemoteIpAddress}", context.Connection.RemoteIpAddress);
            await WriteUnauthorizedResponse(context, "Invalid API key");
            return;
        }

        _logger.LogDebug("Request authenticated successfully from {RemoteIpAddress}", context.Connection.RemoteIpAddress);
        await _next(context);
    }

    private static string? GetApiKeyFromRequest(HttpRequest request)
    {
        // Check Authorization header (Bearer token)
        if (request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authValue = authHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authValue) && authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authValue.Substring("Bearer ".Length).Trim();
            }
        }

        // Check X-API-Key header
        if (request.Headers.TryGetValue("X-API-Key", out var apiKeyHeader))
        {
            return apiKeyHeader.FirstOrDefault();
        }

        // Check api-key header (common in Azure services)
        if (request.Headers.TryGetValue("api-key", out var azureApiKeyHeader))
        {
            return azureApiKeyHeader.FirstOrDefault();
        }

        return null;
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = new
            {
                code = "Unauthorized",
                message = message
            }
        };

        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
}