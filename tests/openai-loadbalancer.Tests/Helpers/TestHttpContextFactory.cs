using Microsoft.AspNetCore.Http;
using System.Net;

namespace openai_loadbalancer.Tests.Helpers;

/// <summary>
/// Factory for creating test HttpContext instances with configurable properties.
/// </summary>
public static class TestHttpContextFactory
{
    /// <summary>
    /// Creates a new HttpContext for testing with specified parameters.
    /// </summary>
    public static DefaultHttpContext Create(
        string path = "/",
        string? ipAddress = "192.168.1.1",
        Dictionary<string, string>? headers = null)
    {
        var context = new DefaultHttpContext();

        // Set request path
        context.Request.Path = path;

        // Set remote IP address
        if (ipAddress != null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        }

        // Set headers
        if (headers != null)
        {
            foreach (var header in headers)
            {
                context.Request.Headers[header.Key] = header.Value;
            }
        }

        // Set up response body stream
        context.Response.Body = new MemoryStream();

        return context;
    }

    /// <summary>
    /// Creates an HttpContext with a specific API key header.
    /// </summary>
    public static DefaultHttpContext CreateWithApiKey(
        string apiKey,
        string headerName = "LB-API-Key",
        string path = "/v1/chat/completions",
        string? ipAddress = "192.168.1.1")
    {
        var headers = new Dictionary<string, string>
        {
            [headerName] = apiKey
        };

        return Create(path, ipAddress, headers);
    }

    /// <summary>
    /// Creates an HttpContext with Bearer token authorization.
    /// </summary>
    public static DefaultHttpContext CreateWithBearerToken(
        string token,
        string path = "/v1/chat/completions",
        string? ipAddress = "192.168.1.1")
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}"
        };

        return Create(path, ipAddress, headers);
    }

    /// <summary>
    /// Gets the response body as a string.
    /// </summary>
    public static async Task<string> GetResponseBodyAsync(this HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
