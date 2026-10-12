using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<User, Role, Guid>(options), IAppDbContext
{
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingRoomType> BookingRoomTypes => Set<BookingRoomType>();
    public DbSet<RoomAssignment> RoomAssignments => Set<RoomAssignment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (typeof(Domain.Common.SoftDeletableEntity).IsAssignableFrom(clrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(clrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(Domain.Common.SoftDeletableEntity.IsDeleted));
                var condition = System.Linq.Expressions.Expression.Equal(property, System.Linq.Expressions.Expression.Constant(false));
                var lambda = System.Linq.Expressions.Expression.Lambda(condition, parameter);
                entityType.SetQueryFilter(lambda);
            }
        }
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<Domain.Common.AuditableEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}