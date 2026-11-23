using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Company.Service.ApiTests;

/// <summary>Mints tokens the way an identity provider would, signed with a per-run key.</summary>
public static class TestTokens
{
    public const string Issuer = "https://identity.tests.local/";

    public const string Audience = "company-service";

    public static SymmetricSecurityKey SigningKey { get; } = new(RandomNumberGenerator.GetBytes(32));

    /// <param name="expires">Defaults to five minutes from now; pass a past time for an expired token.</param>
    /// <param name="key">Defaults to the key the test server trusts; pass another for a forged token.</param>
    public static string Create(string subject, IEnumerable<string> scopes, DateTime? expires = null, SecurityKey? key = null)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        var issuedAt = expiresAt.AddMinutes(-10);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["scope"] = string.Join(' ', scopes),
            },
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
