using Company.Service.Api.Features.Orders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Company.Service.Api.Features.Orders.Persistence;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.CustomerId).HasMaxLength(64);
        builder.Property(order => order.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(order => order.CancellationReason).HasMaxLength(200);

        // Optimistic concurrency on PostgreSQL's xmin system column: no extra column, no triggers.
        builder.Property<uint>("Version").IsRowVersion();

        // Lines are a value-object collection owned by the order, stored as one jsonb column. EF maps the private
        // list, so the aggregate keeps exposing a read-only view.
        builder.Ignore(order => order.Lines);
        builder.ComplexCollection<List<OrderLine>, OrderLine>("_lines", lines => lines.ToJson("lines"));

        // Serves "orders for a customer, newest first" with keyset paging on the time-ordered id.
        builder.HasIndex(order => new { order.CustomerId, order.Id }).IsDescending(false, true);
    }
}
