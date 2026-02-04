using Microsoft.Extensions.Configuration;
using openai_loadbalancer;
using openai_loadbalancer.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace openai_loadbalancer.Tests.Unit;

public class LoadApiKeysTests
{
    [Fact]
    public async Task SingleLbApiKey_ShouldWork()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEY"] = "single-key"
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        var context = TestHttpContextFactory.CreateWithApiKey("single-key");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task CommaSeparatedLbApiKeys_ShouldWork()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEYS"] = "key1,key2,key3"
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        // Act & Assert - All keys should work
        foreach (var key in new[] { "key1", "key2", "key3" })
        {
            var context = TestHttpContextFactory.CreateWithApiKey(key);
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().NotBe(401, $"key '{key}' should be valid");
        }
    }

    [Fact]
    public async Task SemicolonSeparatedLbApiKeys_ShouldWork()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEYS"] = "key1;key2;key3"
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        var context = TestHttpContextFactory.CreateWithApiKey("key2");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task HierarchicalLbApiKeys_ShouldWork()
    {
        // Arrange - Using hierarchical configuration (LB_API_KEYS:0, LB_API_KEYS:1)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEYS:0"] = "hier-key-0",
                ["LB_API_KEYS:1"] = "hier-key-1",
                ["LB_API_KEYS:2"] = "hier-key-2"
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        var context = TestHttpContextFactory.CreateWithApiKey("hier-key-1");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task MixedConfiguration_ShouldMergeAllKeys()
    {
        // Arrange - Both LB_API_KEY and LB_API_KEYS configured
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEY"] = "single-key",
                ["LB_API_KEYS"] = "list-key-1,list-key-2",
                ["LB_API_KEYS:0"] = "hier-key-0"
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        // Act & Assert - All keys from different sources should work
        foreach (var key in new[] { "single-key", "list-key-1", "list-key-2", "hier-key-0" })
        {
            var context = TestHttpContextFactory.CreateWithApiKey(key);
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().NotBe(401, $"key '{key}' should be valid");
        }
    }

    [Fact]
    public async Task EmptyKeysInList_ShouldBeIgnored()
    {
        // Arrange - List with empty entries
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEYS"] = "key1,,key2,  ,key3"
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        var context = TestHttpContextFactory.CreateWithApiKey("key2");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task KeysWithWhitespace_ShouldBeTrimmed()
    {
        // Arrange - Keys with leading/trailing whitespace
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEYS"] = "  key1  ,  key2  "
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        // "key1" without spaces should work because stored keys are trimmed
        var context = TestHttpContextFactory.CreateWithApiKey("key1");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task NoKeysConfigured_ShouldDisableAuthentication()
    {
        // Arrange - No API keys configured
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            MiddlewareTestHelper.CreateLogger(),
            config);

        var context = TestHttpContextFactory.Create("/v1/chat/completions"); // No API key

        // Act
        await middleware.InvokeAsync(context);

        // Assert - Should pass through because authentication is disabled
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateKeys_ShouldBeDeduped()
    {
        // Arrange - Same key in multiple places
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LB_API_KEY"] = "same-key",
                ["LB_API_KEYS"] = "same-key,same-key"
            })
            .Build();

        MiddlewareTestHelper.ClearFailedAttempts();
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            MiddlewareTestHelper.CreateLogger(),
            config);

        var context = TestHttpContextFactory.CreateWithApiKey("same-key");

        // Act
        await middleware.InvokeAsync(context);

        // Assert - Should work (HashSet handles deduplication)
        context.Response.StatusCode.Should().NotBe(401);
    }
}
