using openai_loadbalancer;
using openai_loadbalancer.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace openai_loadbalancer.Tests.Unit;

public class ValidateApiKeyTests
{
    [Fact]
    public async Task ValidApiKey_ShouldAuthenticate()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker(["valid-key-123"]);
        var context = TestHttpContextFactory.CreateWithApiKey("valid-key-123");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task InvalidApiKey_ShouldReturn401()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware(["valid-key-123"]);
        var context = TestHttpContextFactory.CreateWithApiKey("wrong-key");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task MultipleValidKeys_AnyOneShouldWork()
    {
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker(["key-1", "key-2", "key-3"]);
        var context = TestHttpContextFactory.CreateWithApiKey("key-2");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        wasNextCalled().Should().BeTrue();
    }

    [Fact]
    public async Task CaseSensitiveApiKey_ShouldRejectWrongCase()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware(["Valid-Key-123"]);
        var context = TestHttpContextFactory.CreateWithApiKey("valid-key-123");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task EmptyApiKey_ShouldReturn401()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware(["valid-key"]);
        var context = TestHttpContextFactory.CreateWithApiKey("");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task WhitespaceApiKey_ShouldReturn401()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware(["valid-key"]);
        var context = TestHttpContextFactory.CreateWithApiKey("   ");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task NoApiKeyHeader_ShouldReturn401()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware(["valid-key"]);
        var context = TestHttpContextFactory.Create("/v1/chat/completions");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ApiKeyWithLeadingTrailingSpaces_ShouldBeTrimmed()
    {
        // The middleware trims the provided key, so " valid-key " should match "valid-key"
        // Arrange
        var (middleware, wasNextCalled) = MiddlewareTestHelper.CreateMiddlewareWithNextTracker(["valid-key"]);
        var context = TestHttpContextFactory.CreateWithApiKey("  valid-key  ");

        // Act
        await middleware.InvokeAsync(context);

        // Assert - should authenticate because trim is applied
        wasNextCalled().Should().BeTrue();
    }
}
