using System.Security.Claims;
using Company.Service.Api.Infrastructure.Security;

namespace Company.Service.UnitTests.Infrastructure;

public class AuthScopeTests
{
    [Theory]
    [InlineData("scope", "orders:read orders:write")]
    [InlineData("scp", "orders:write")]
    [InlineData("scope", "  orders:write  ")]
    public void Scopes_are_found_in_any_of_the_common_claim_shapes(string claimType, string value)
    {
        var user = User(new Claim(claimType, value));

        Assert.True(AuthExtensions.HasScope(user, AuthPolicies.OrdersWrite));
    }

    [Fact]
    public void Scopes_may_also_arrive_as_separate_claims()
    {
        var user = User(new Claim("scope", "orders:read"), new Claim("scope", "orders:write"));

        Assert.True(AuthExtensions.HasScope(user, AuthPolicies.OrdersWrite));
    }

    [Theory]
    [InlineData("orders:read")]
    [InlineData("orders:writer")]
    [InlineData("ORDERS:WRITE")]
    public void Near_misses_do_not_count(string granted)
    {
        Assert.False(AuthExtensions.HasScope(User(new Claim("scope", granted)), AuthPolicies.OrdersWrite));
    }

    private static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));
}
