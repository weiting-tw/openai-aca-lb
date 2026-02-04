using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace openai_loadbalancer;

public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly HashSet<string> _validApiKeys;

    // IP-based rate limiting for failed authentication attempts
    private static readonly ConcurrentDictionary<string, (int FailCount, DateTime? LockoutUntil)> _failedAttempts = new();
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _validApiKeys = LoadApiKeys(configuration);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for health check endpoint
        if (context.Request.Path.StartsWithSegments("/healthz"))
        {
            await _next(context);
            return;
        }

        // Get client IP address
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Check if IP is locked out due to too many failed attempts
        if (IsLockedOut(ipAddress))
        {
            _logger.LogWarning("Request blocked from locked out IP address {RemoteIpAddress}", ipAddress);
            await WriteTooManyRequestsResponse(context, "Too many failed authentication attempts. Please try again later.");
            return;
        }

        // If no API key is configured, skip authentication
        if (_validApiKeys.Count == 0)
        {
            _logger.LogWarning("No load balancer API key configured. Authentication is disabled. Set LB_API_KEY environment variable to enable authentication.");
            await _next(context);
            return;
        }

        // Check for API key in headers
        var providedApiKey = GetApiKeyFromRequest(context.Request);

        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            _logger.LogWarning("Request received without API key from {RemoteIpAddress}", ipAddress);
            RecordFailedAttempt(ipAddress);
            await WriteUnauthorizedResponse(context, "API key is required");
            return;
        }

        if (!ValidateApiKey(providedApiKey))
        {
            _logger.LogWarning("Request received with invalid API key from {RemoteIpAddress}", ipAddress);
            RecordFailedAttempt(ipAddress);
            await WriteUnauthorizedResponse(context, "Invalid API key");
            return;
        }

        // Successful authentication - clear any previous failed attempts
        ClearFailedAttempts(ipAddress);
        _logger.LogDebug("Request authenticated successfully from {RemoteIpAddress}", ipAddress);
        await _next(context);
    }

    private bool IsLockedOut(string ipAddress)
    {
        if (_failedAttempts.TryGetValue(ipAddress, out var record))
        {
            if (record.LockoutUntil.HasValue && DateTime.UtcNow < record.LockoutUntil.Value)
            {
                return true;
            }
            // Lockout has expired, clear the record
            if (record.LockoutUntil.HasValue && DateTime.UtcNow >= record.LockoutUntil.Value)
            {
                _failedAttempts.TryRemove(ipAddress, out _);
            }
        }
        return false;
    }

    private void RecordFailedAttempt(string ipAddress)
    {
        _failedAttempts.AddOrUpdate(
            ipAddress,
            _ => (1, null),
            (_, existing) =>
            {
                var newCount = existing.FailCount + 1;
                if (newCount >= MaxFailedAttempts)
                {
                    _logger.LogWarning("IP address {RemoteIpAddress} locked out after {FailCount} failed authentication attempts",
                        ipAddress, newCount);
                    return (newCount, DateTime.UtcNow.Add(LockoutDuration));
                }
                return (newCount, existing.LockoutUntil);
            });
    }

    private void ClearFailedAttempts(string ipAddress)
    {
        _failedAttempts.TryRemove(ipAddress, out _);
    }

    private bool ValidateApiKey(string providedKey)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        foreach (var validKey in _validApiKeys)
        {
            var validBytes = Encoding.UTF8.GetBytes(validKey);
            if (providedBytes.Length == validBytes.Length &&
                CryptographicOperations.FixedTimeEquals(providedBytes, validBytes))
            {
                return true;
            }
        }
        return false;
    }

    private static string? GetApiKeyFromRequest(HttpRequest request)
    {
        // Check dedicated LB-API-Key header first to avoid conflicts with backend headers
        if (request.Headers.TryGetValue("LB-API-Key", out var lbApiKeyHeader))
        {
            var headerValue = lbApiKeyHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.Trim();
            }
        }

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
            var headerValue = apiKeyHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.Trim();
            }
        }

        // Check api-key header (common in Azure services)
        if (request.Headers.TryGetValue("api-key", out var azureApiKeyHeader))
        {
            var headerValue = azureApiKeyHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.Trim();
            }
        }

        return null;
    }

    private static HashSet<string> LoadApiKeys(IConfiguration configuration)
    {
        var apiKeys = new HashSet<string>(StringComparer.Ordinal);

        // Support hierarchical configuration such as LB_API_KEYS__0, LB_API_KEYS__1, etc.
        var keysSection = configuration.GetSection("LB_API_KEYS");
        if (keysSection.Exists())
        {
            foreach (var child in keysSection.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(child.Value))
                {
                    apiKeys.Add(child.Value.Trim());
                }
            }
        }

        // Support comma/semicolon separated list in LB_API_KEYS
        var configuredKeys = configuration["LB_API_KEYS"] ?? Environment.GetEnvironmentVariable("LB_API_KEYS");
        if (!string.IsNullOrWhiteSpace(configuredKeys))
        {
            var splitKeys = configuredKeys.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var key in splitKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    apiKeys.Add(key);
                }
            }
        }

        // Fallback to single LB_API_KEY
        var singleKey = configuration["LB_API_KEY"] ?? Environment.GetEnvironmentVariable("LB_API_KEY");
        if (!string.IsNullOrWhiteSpace(singleKey))
        {
            apiKeys.Add(singleKey.Trim());
        }

        return apiKeys;
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

    private static async Task WriteTooManyRequestsResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = 429;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Retry-After"] = "900"; // 15 minutes in seconds

        var response = new
        {
            error = new
            {
                code = "TooManyRequests",
                message = message
            }
        };

        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
}
