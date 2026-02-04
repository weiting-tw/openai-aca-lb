using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace openai_loadbalancer.Tests.Helpers;

/// <summary>
/// Helper class for testing ApiKeyAuthenticationMiddleware.
/// </summary>
public static class MiddlewareTestHelper
{
    /// <summary>
    /// Creates an ApiKeyAuthenticationMiddleware instance with specified API keys.
    /// </summary>
    public static ApiKeyAuthenticationMiddleware CreateMiddleware(
        string[]? apiKeys = null,
        RequestDelegate? next = null)
    {
        // Clear static failed attempts before creating new middleware
        ClearFailedAttempts();

        var configuration = CreateConfiguration(apiKeys);
        var logger = CreateLogger();
        next ??= _ => Task.CompletedTask;

        return new ApiKeyAuthenticationMiddleware(next, logger, configuration);
    }

    /// <summary>
    /// Creates a middleware that tracks if next was called.
    /// </summary>
    public static (ApiKeyAuthenticationMiddleware middleware, Func<bool> wasNextCalled) CreateMiddlewareWithNextTracker(
        string[]? apiKeys = null)
    {
        ClearFailedAttempts();

        var configuration = CreateConfiguration(apiKeys);
        var logger = CreateLogger();

        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyAuthenticationMiddleware(next, logger, configuration);
        return (middleware, () => nextCalled);
    }

    /// <summary>
    /// Creates an IConfiguration with specified API keys.
    /// </summary>
    public static IConfiguration CreateConfiguration(string[]? apiKeys = null)
    {
        var configData = new Dictionary<string, string?>();

        if (apiKeys != null && apiKeys.Length > 0)
        {
            if (apiKeys.Length == 1)
            {
                configData["LB_API_KEY"] = apiKeys[0];
            }
            else
            {
                // Use hierarchical configuration for multiple keys
                for (int i = 0; i < apiKeys.Length; i++)
                {
                    configData[$"LB_API_KEYS:{i}"] = apiKeys[i];
                }
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    /// <summary>
    /// Creates a mock logger for testing.
    /// </summary>
    public static ILogger<ApiKeyAuthenticationMiddleware> CreateLogger()
    {
        return new Mock<ILogger<ApiKeyAuthenticationMiddleware>>().Object;
    }

    /// <summary>
    /// Clears the static _failedAttempts dictionary between tests.
    /// Uses reflection to access the private static field.
    /// </summary>
    public static void ClearFailedAttempts()
    {
        var field = typeof(ApiKeyAuthenticationMiddleware)
            .GetField("_failedAttempts", BindingFlags.Static | BindingFlags.NonPublic);

        if (field != null)
        {
            var dictionary = field.GetValue(null) as ConcurrentDictionary<string, (int FailCount, DateTime? LockoutUntil)>;
            dictionary?.Clear();
        }
    }

    /// <summary>
    /// Gets the current failed attempts count for an IP address.
    /// </summary>
    public static int GetFailedAttemptsCount(string ipAddress)
    {
        var field = typeof(ApiKeyAuthenticationMiddleware)
            .GetField("_failedAttempts", BindingFlags.Static | BindingFlags.NonPublic);

        if (field != null)
        {
            var dictionary = field.GetValue(null) as ConcurrentDictionary<string, (int FailCount, DateTime? LockoutUntil)>;
            if (dictionary != null && dictionary.TryGetValue(ipAddress, out var record))
            {
                return record.FailCount;
            }
        }

        return 0;
    }

    /// <summary>
    /// Sets a lockout for testing purposes.
    /// </summary>
    public static void SetLockout(string ipAddress, int failCount, DateTime? lockoutUntil)
    {
        var field = typeof(ApiKeyAuthenticationMiddleware)
            .GetField("_failedAttempts", BindingFlags.Static | BindingFlags.NonPublic);

        if (field != null)
        {
            var dictionary = field.GetValue(null) as ConcurrentDictionary<string, (int FailCount, DateTime? LockoutUntil)>;
            if (dictionary != null)
            {
                dictionary[ipAddress] = (failCount, lockoutUntil);
            }
        }
    }
}
