using Microsoft.EntityFrameworkCore;

namespace Company.Service.Api.Infrastructure.Persistence;

/// <summary>
/// One context for the service. It knows no feature types: each slice contributes its own
/// <see cref="IEntityTypeConfiguration{TEntity}"/>, picked up from the assembly.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
