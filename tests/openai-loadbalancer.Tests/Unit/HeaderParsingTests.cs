using openai_loadbalancer;
using openai_loadbalancer.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace openai_loadbalancer.Tests.Unit;

public class HeaderParsingTests
{
    private const string ValidKey = "valid-key-123";

    [Fact]
    public async Task LbApiKeyHeader_ShouldAuthenticate()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var context = TestHttpContextFactory.CreateWithApiKey(ValidKey, headerName: "LB-API-Key");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationBearerHeader_ShouldAuthenticate()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var context = TestHttpContextFactory.CreateWithBearerToken(ValidKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task XApiKeyHeader_ShouldAuthenticate()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var context = TestHttpContextFactory.CreateWithApiKey(ValidKey, headerName: "X-API-Key");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task ApiKeyHeader_ShouldAuthenticate()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var context = TestHttpContextFactory.CreateWithApiKey(ValidKey, headerName: "api-key");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task LbApiKeyHeader_ShouldHaveHighestPriority()
    {
        // Arrange - LB-API-Key has correct key, others have wrong keys
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var headers = new Dictionary<string, string>
        {
            ["LB-API-Key"] = ValidKey,
            ["Authorization"] = "Bearer wrong-key",
            ["X-API-Key"] = "wrong-key",
            ["api-key"] = "wrong-key"
        };
        var context = TestHttpContextFactory.Create("/v1/chat/completions", headers: headers);

        // Act
        await middleware.InvokeAsync(context);

        // Assert - Should authenticate using LB-API-Key
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationHeader_ShouldHaveSecondPriority()
    {
        // Arrange - No LB-API-Key, Authorization has correct key
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {ValidKey}",
            ["X-API-Key"] = "wrong-key",
            ["api-key"] = "wrong-key"
        };
        var context = TestHttpContextFactory.Create("/v1/chat/completions", headers: headers);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task BearerToken_ShouldBeCaseInsensitive()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"BEARER {ValidKey}"
        };
        var context = TestHttpContextFactory.Create("/v1/chat/completions", headers: headers);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task BearerTokenWithExtraSpaces_ShouldBeTrimmed()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer   {ValidKey}   "
        };
        var context = TestHttpContextFactory.Create("/v1/chat/completions", headers: headers);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationWithoutBearer_ShouldNotAuthenticate()
    {
        // Arrange - Authorization header without "Bearer " prefix
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = ValidKey // Missing "Bearer " prefix
        };
        var context = TestHttpContextFactory.Create("/v1/chat/completions", headers: headers);

        // Act
        await middleware.InvokeAsync(context);

        // Assert - Should fail because Bearer prefix is required
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task HealthCheckEndpoint_ShouldSkipAuthentication()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker([ValidKey]);
        var context = TestHttpContextFactory.Create("/healthz"); // No API key

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task NonHealthCheckEndpoint_ShouldRequireAuthentication()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);
        var context = TestHttpContextFactory.Create("/v1/chat/completions"); // No API key

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task NoApiKeyConfigured_ShouldAllowAllRequests()
    {
        // Arrange - No API keys configured
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker(null);
        var context = TestHttpContextFactory.Create("/v1/chat/completions"); // No API key in request

        // Act
        await middleware.InvokeAsync(context);

        // Assert - Should allow because no authentication is required
        wasNextCalled().Should().BeTrue();
    }
}
