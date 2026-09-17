using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker;

public sealed class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<InventoryReservation> InventoryReservations =>
        Set<InventoryReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.AvailableQuantity)
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .IsRequired();

            entity.HasData(
                new Product
                {
                    Id = 1,
                    Name = "Mechanical Keyboard",
                    AvailableQuantity = 10,
                    UpdatedAtUtc = new DateTime(
                        2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new Product
                {
                    Id = 2,
                    Name = "Wireless Mouse",
                    AvailableQuantity = 5,
                    UpdatedAtUtc = new DateTime(
                        2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
        });

        modelBuilder.Entity<InventoryReservation>(entity =>
        {
            entity.ToTable("InventoryReservations");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.OrderId)
                .IsRequired();

            entity.Property(x => x.ProductId)
                .IsRequired();

            entity.Property(x => x.Quantity)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.FailureReason)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();

            // This is important for idempotency: an order can only
            // reserve inventory once.
            entity.HasIndex(x => x.OrderId)
                .IsUnique();

            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}