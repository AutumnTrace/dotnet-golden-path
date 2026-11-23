using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Security;

/// <summary>Authorization policies, named after the OAuth scope they require.</summary>
public static class AuthPolicies
{
    public const string OrdersRead = "orders:read";

    public const string OrdersWrite = "orders:write";
}

public static class AuthExtensions
{
    /// <summary>
    /// JWT bearer authentication configured from <c>Authentication:Schemes:Bearer</c> (Authority, ValidAudiences, ...),
    /// the same section <c>dotnet user-jwts</c> writes to. Secure by default: the fallback policy requires an
    /// authenticated caller, so a new endpoint is never accidentally public.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        // Keep claim names as issued ("sub", "scope") instead of mapping them to long WS-* URIs.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.MapInboundClaims = false);

        services.AddSingleton<IValidateOptions<JwtBearerOptions>, JwtBearerOptionsValidator>();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(AuthPolicies.OrdersRead, policy => policy.RequireAssertion(context => HasScope(context.User, AuthPolicies.OrdersRead)))
            .AddPolicy(AuthPolicies.OrdersWrite, policy => policy.RequireAssertion(context => HasScope(context.User, AuthPolicies.OrdersWrite)));

        return services;
    }

    /// <summary>OAuth 2.0 scopes arrive as one space-separated <c>scope</c> claim, several claims, or <c>scp</c> (Entra ID).</summary>
    internal static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.Claims
            .Where(claim => claim.Type is "scope" or "scp" or "http://schemas.microsoft.com/identity/claims/scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}

/// <summary>
/// Refuses to start without a way to validate tokens. In Development it only warns, so a fresh clone runs;
/// <c>dotnet user-jwts create</c> then adds a local signing key.
/// </summary>
internal sealed partial class JwtBearerOptionsValidator(IHostEnvironment environment, ILogger<JwtBearerOptionsValidator> logger)
    : IValidateOptions<JwtBearerOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return ValidateOptionsResult.Skip;
        }

        var parameters = options.TokenValidationParameters;
        var canValidate = !string.IsNullOrWhiteSpace(options.Authority)
            || options.ConfigurationManager is not null
            || parameters.IssuerSigningKey is not null
            || parameters.IssuerSigningKeys?.Any() == true;

        if (canValidate)
        {
            return ValidateOptionsResult.Success;
        }

        if (environment.IsDevelopment())
        {
            LogNoSigningKeys(logger);
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            "Authentication:Schemes:Bearer needs an Authority (your identity provider) or signing keys.");
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No JWT authority or signing keys configured: every authenticated request will fail. Run `dotnet user-jwts create --scope orders:read --scope orders:write` in the API project to mint a local token.")]
    private static partial void LogNoSigningKeys(ILogger logger);
}
