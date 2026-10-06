using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Deutchezz.Application.Data;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Guest> Guests { get; }
    DbSet<RoomType> RoomTypes { get; }
    DbSet<Room> Rooms { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingRoomType> BookingRoomTypes { get; }
    DbSet<RoomAssignment> RoomAssignments { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ActivityLog> ActivityLogs { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}