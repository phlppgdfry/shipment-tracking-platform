using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using ShipmentTracking.Application.Common.Interfaces;
using ShipmentTracking.Domain.Entities;

namespace ShipmentTracking.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options,
    ICurrentUserService? currentUser = null) : IdentityDbContext<ApplicationUser>(options)
{
    // No customer claim means no operational reads. Roles do not bypass this boundary.
    public string CustomerId => currentUser?.CustomerId ?? string.Empty;
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentStatusHistory> ShipmentStatusHistories => Set<ShipmentStatusHistory>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OperationalRecord> OperationalRecords => Set<OperationalRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.Entity<ApplicationUser>().Property(u => u.CustomerId).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<Shipment>().HasQueryFilter(s => !s.IsDeleted && CustomerId != "" && s.CustomerId == CustomerId);
        modelBuilder.Entity<Document>().HasQueryFilter(d => CustomerId != "" && d.CustomerId == CustomerId &&
            Shipments.Any(s => s.Id == d.ShipmentId));
        modelBuilder.Entity<ShipmentStatusHistory>().HasQueryFilter(h => Shipments.Any(s => s.Id == h.ShipmentId));
        var events = modelBuilder.Entity<OperationalRecord>();
        events.HasKey(e => e.Id); events.Property(e => e.Id).ValueGeneratedNever();
        events.Property(e => e.CustomerId).HasMaxLength(64).IsRequired();
        events.Property(e => e.EventId).HasMaxLength(80).IsRequired();
        events.Property(e => e.ReferenceId).HasMaxLength(80).IsRequired();
        events.Property(e => e.Type).HasMaxLength(40).IsRequired();
        events.Property(e => e.Source).HasMaxLength(80).IsRequired();
        events.Property(e => e.Detail).HasMaxLength(500).IsRequired();
        events.HasIndex(e => new { e.ShipmentId, e.EventId }).IsUnique();
        events.HasIndex(e => new { e.ShipmentId, e.Type }).IsUnique().HasFilter("[Type] IN ('gate_in', 'gate_out')");
        events.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        events.HasQueryFilter(e => CustomerId != "" && e.CustomerId == CustomerId && Shipments.Any(s => s.Id == e.ShipmentId));
    }

    private void ValidateCustomerWrites()
    {
        // Null service is reserved for explicit trusted provisioning/migration contexts.
        // Normal requests cannot create a record for another customer, even through a future handler.
        if (currentUser is not null)
        {
            foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                var owner = entry.Entity switch { Shipment s => s.CustomerId, Document d => d.CustomerId, OperationalRecord e => e.CustomerId, _ => null };
                if (owner is not null && (CustomerId == "" || owner != CustomerId))
                    throw new InvalidOperationException("An operational write must belong to the authenticated customer.");
                if (entry.State != EntityState.Added && owner is not null && entry.Property("CustomerId").OriginalValue?.ToString() != owner)
                    throw new InvalidOperationException("Customer ownership cannot be changed by an operational request.");
            }
        }
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { ValidateCustomerWrites(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    { ValidateCustomerWrites(); return base.SaveChanges(acceptAllChangesOnSuccess); }
}
