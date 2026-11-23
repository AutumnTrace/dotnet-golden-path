using System.Net;
using System.Security.Claims;
using Company.Service.Api.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Company.Service.UnitTests.Infrastructure;

public class RateLimitingTests
{
    [Fact]
    public void Authenticated_callers_are_limited_per_subject()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "client-a")], "Bearer")),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");

        Assert.Equal("sub:client-a", RateLimitingExtensions.ClientKey(context));
    }

    [Fact]
    public void Anonymous_callers_are_limited_per_address()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");

        Assert.Equal("ip:10.0.0.7", RateLimitingExtensions.ClientKey(context));
    }
}
