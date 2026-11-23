using System.Text.Json.Serialization;
using Asp.Versioning;
using Company.Service.Api.Infrastructure.Errors;
#if (UseJwt)
using Company.Service.Api.Infrastructure.Security;
#endif

namespace Company.Service.Api.Infrastructure.Api;

public static class ApiExtensions
{
    /// <summary>Conventions every endpoint gets: problem details, request validation, JSON settings, versioning and OpenAPI.</summary>
    public static IServiceCollection AddApiDefaults(this IServiceCollection services)
    {
        services.AddProblemDetailsDefaults();

        // Built-in minimal API validation (.NET 10): DataAnnotations on request types, 400 problem details on failure.
        services.AddValidation();

        // Binding failures throw in every environment (not only Development), so they take the same
        // problem-details path as everything else and carry an errorCode.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            })
#if (UseJwt)
            .AddOpenApi(options => options.Document
                .AddDocumentTransformer<BearerSecuritySchemeTransformer>()
                .AddOperationTransformer<BearerSecuritySchemeTransformer>());
#else
            .AddOpenApi();
#endif

        return services;
    }

    /// <summary>Serves one OpenAPI document per API version, e.g. <c>/openapi/v1.json</c>.</summary>
    public static IEndpointRouteBuilder MapApiDocuments(this IEndpointRouteBuilder app)
    {
        app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();
        return app;
    }
}
