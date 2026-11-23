using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Company.Service.Api.Infrastructure.Security;

/// <summary>
/// A local stand-in for an identity provider: <c>POST /dev/tokens</c> mints short-lived tokens so the API can be
/// exercised from curl, the compose stack or a front end. It only exists in the Development environment; turning
/// it on anywhere else stops the service at startup.
/// </summary>
public sealed class DevelopmentTokenOptions
{
    public const string SectionName = "Authentication:DevelopmentTokens";

    public const string Issuer = "company-service-development";

    public bool Enabled { get; set; }

    /// <summary>HMAC key, at least 32 characters. Generated per service by the template; never reuse it elsewhere.</summary>
    public string? SigningKey { get; set; }

    internal SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey ?? string.Empty));
}

public sealed record DevelopmentTokenRequest(string? Subject, IReadOnlyList<string>? Scopes);

public sealed record DevelopmentTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

public static class DevelopmentTokens
{
    public static IServiceCollection AddDevelopmentTokens(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<DevelopmentTokenOptions>, DevelopmentTokenOptionsValidator>();
        services.AddOptions<DevelopmentTokenOptions>().BindConfiguration(DevelopmentTokenOptions.SectionName).ValidateOnStart();

        // Trust the development issuer and key in addition to whatever is configured (Authority, user-jwts keys).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IOptions<DevelopmentTokenOptions>>((jwt, development) =>
            {
                if (!development.Value.Enabled)
                {
                    return;
                }

                var parameters = jwt.TokenValidationParameters;
                parameters.ValidIssuers = [.. parameters.ValidIssuers ?? [], DevelopmentTokenOptions.Issuer];
                parameters.IssuerSigningKeys = [.. parameters.IssuerSigningKeys ?? [], development.Value.Key()];
            });

        return services;
    }

    public static IEndpointRouteBuilder MapDevelopmentTokens(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ServiceProvider.GetRequiredService<IOptions<DevelopmentTokenOptions>>().Value.Enabled)
        {
            app.MapPost("/dev/tokens", Issue).AllowAnonymous().ExcludeFromDescription();
        }

        return app;
    }

    private static DevelopmentTokenResponse Issue(
        DevelopmentTokenRequest? request,
        IOptions<DevelopmentTokenOptions> options,
        IOptionsMonitor<JwtBearerOptions> jwt,
        TimeProvider clock)
    {
        var parameters = jwt.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddHours(1);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = DevelopmentTokenOptions.Issuer,
            Audience = parameters.ValidAudiences?.FirstOrDefault() ?? parameters.ValidAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = request?.Subject ?? "developer",
                ["scope"] = string.Join(' ', request?.Scopes ?? [AuthPolicies.OrdersRead, AuthPolicies.OrdersWrite]),
            },
            SigningCredentials = new SigningCredentials(options.Value.Key(), SecurityAlgorithms.HmacSha256),
        });

        return new DevelopmentTokenResponse(token, expiresAt);
    }
}

internal sealed class DevelopmentTokenOptionsValidator(IHostEnvironment environment) : IValidateOptions<DevelopmentTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, DevelopmentTokenOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (!environment.IsDevelopment())
        {
            return ValidateOptionsResult.Fail(
                $"{DevelopmentTokenOptions.SectionName}:Enabled is only allowed in the Development environment (current: {environment.EnvironmentName}).");
        }

        return options.SigningKey is { Length: >= 32 }
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{DevelopmentTokenOptions.SectionName}:SigningKey must be at least 32 characters.");
    }
}
