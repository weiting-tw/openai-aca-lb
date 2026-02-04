using openai_loadbalancer;
using openai_loadbalancer.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace openai_loadbalancer.Tests.Unit;

public class RateLimitingTests
{
    private const string TestIp = "192.168.1.100";
    private const string ValidKey = "valid-key-123";
    private const string InvalidKey = "wrong-key";

    [Fact]
    public async Task FourFailedAttempts_ShouldNotTriggerLockout()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);

        // Act - Make 4 failed attempts
        for (int i = 0; i < 4; i++)
        {
            var context = TestHttpContextFactory.CreateWithApiKey(InvalidKey, ipAddress: TestIp);
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().Be(401, $"attempt {i + 1} should return 401");
        }

        // Assert - 5th attempt with correct key should work
        var finalContext = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: TestIp);
        await middleware.InvokeAsync(finalContext);
        finalContext.Response.StatusCode.Should().NotBe(429);
    }

    [Fact]
    public async Task FiveFailedAttempts_ShouldTriggerLockout()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);

        // Act - Make 5 failed attempts
        for (int i = 0; i < 5; i++)
        {
            var context = TestHttpContextFactory.CreateWithApiKey(InvalidKey, ipAddress: TestIp);
            await middleware.InvokeAsync(context);
        }

        // Assert - 6th attempt should be locked out (429), even with correct key
        var lockedContext = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: TestIp);
        await middleware.InvokeAsync(lockedContext);
        lockedContext.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task LockedOut_ShouldReturn429WithRetryAfterHeader()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);

        // Make 5 failed attempts to trigger lockout
        for (int i = 0; i < 5; i++)
        {
            var context = TestHttpContextFactory.CreateWithApiKey(InvalidKey, ipAddress: TestIp);
            await middleware.InvokeAsync(context);
        }

        // Act
        var lockedContext = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: TestIp);
        await middleware.InvokeAsync(lockedContext);

        // Assert
        lockedContext.Response.StatusCode.Should().Be(429);
        lockedContext.Response.Headers["Retry-After"].ToString().Should().Be("900");
    }

    [Fact]
    public async Task SuccessfulAuth_ShouldClearFailedAttempts()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);

        // Make 4 failed attempts
        for (int i = 0; i < 4; i++)
        {
            var context = TestHttpContextFactory.CreateWithApiKey(InvalidKey, ipAddress: TestIp);
            await middleware.InvokeAsync(context);
        }

        // Successful authentication
        var successContext = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: TestIp);
        await middleware.InvokeAsync(successContext);
        successContext.Response.StatusCode.Should().NotBe(401);
        successContext.Response.StatusCode.Should().NotBe(429);

        // Act - Make 4 more failed attempts (should not trigger lockout since counter was cleared)
        for (int i = 0; i < 4; i++)
        {
            var context = TestHttpContextFactory.CreateWithApiKey(InvalidKey, ipAddress: TestIp);
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().Be(401, $"attempt {i + 1} after clear should return 401");
        }

        // Assert - Should still not be locked out
        var finalContext = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: TestIp);
        await middleware.InvokeAsync(finalContext);
        finalContext.Response.StatusCode.Should().NotBe(429);
    }

    [Fact]
    public async Task DifferentIPs_ShouldHaveSeparateLockouts()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);
        var ip1 = "192.168.1.1";
        var ip2 = "192.168.1.2";

        // Lock out IP1 with 5 failed attempts
        for (int i = 0; i < 5; i++)
        {
            var context = TestHttpContextFactory.CreateWithApiKey(InvalidKey, ipAddress: ip1);
            await middleware.InvokeAsync(context);
        }

        // Act - IP2 should still work
        var ip2Context = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: ip2);
        await middleware.InvokeAsync(ip2Context);

        // Assert
        ip2Context.Response.StatusCode.Should().NotBe(429);

        // IP1 should be locked
        var ip1Context = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: ip1);
        await middleware.InvokeAsync(ip1Context);
        ip1Context.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task LockoutExpired_ShouldAllowAccess()
    {
        // Use unique IP to avoid parallel test interference
        var uniqueIp = "10.88.88.88";

        // Arrange - Create middleware first (which clears failed attempts)
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);

        // Then set a lockout that has already expired
        MiddlewareTestHelper.SetLockout(uniqueIp, 5, DateTime.UtcNow.AddMinutes(-1)); // Expired 1 minute ago

        // Act
        var context = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: uniqueIp);
        await middleware.InvokeAsync(context);

        // Assert - Should be allowed since lockout expired
        context.Response.StatusCode.Should().NotBe(429);
    }

    [Fact]
    public async Task LockoutNotExpired_ShouldBlockAccess()
    {
        // Use unique IP to avoid parallel test interference
        var uniqueIp = "10.99.99.99";

        // Arrange - Create middleware first (which clears failed attempts)
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);

        // Then set a lockout that has not expired
        MiddlewareTestHelper.SetLockout(uniqueIp, 5, DateTime.UtcNow.AddMinutes(10)); // Expires in 10 minutes

        // Act
        var context = TestHttpContextFactory.CreateWithApiKey(ValidKey, ipAddress: uniqueIp);
        await middleware.InvokeAsync(context);

        // Assert - Should be blocked
        context.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task HealthCheckEndpoint_ShouldNotRecordFailedAttempts()
    {
        // Arrange
        var middleware = MiddlewareTestHelper.CreateMiddleware([ValidKey]);

        // Act - Access health check without API key (should not count as failed)
        var context = TestHttpContextFactory.Create("/healthz", ipAddress: TestIp);
        await middleware.InvokeAsync(context);

        // Assert
        MiddlewareTestHelper.GetFailedAttemptsCount(TestIp).Should().Be(0);
    }
}
