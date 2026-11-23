using System.ComponentModel.DataAnnotations;

namespace Company.Service.Api.Infrastructure.Persistence;

public enum DatabaseProvider
{
    /// <summary>Process-local store: local runs without Docker and fast API tests. Never for production.</summary>
    InMemory,
    Postgres,
}

public sealed class DatabaseOptions : IValidatableObject
{
    public const string SectionName = "Database";

    public const string ConnectionStringName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    /// <summary>Bound from <c>ConnectionStrings:Database</c>, where secret stores and orchestrators expect it.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Apply pending EF Core migrations before the server starts. Convenient for local runs and demos; in
    /// production prefer running the migration bundle as a deployment step.
    /// </summary>
    public bool MigrateOnStartup { get; set; }

    public static DatabaseProvider ProviderFrom(IConfiguration configuration) =>
        configuration.GetValue($"{SectionName}:{nameof(Provider)}", DatabaseProvider.Postgres);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == DatabaseProvider.Postgres && string.IsNullOrWhiteSpace(ConnectionString))
        {
            yield return new ValidationResult(
                $"ConnectionStrings:{ConnectionStringName} is required when Database:Provider is Postgres.",
                [nameof(ConnectionString)]);
        }
    }
}
