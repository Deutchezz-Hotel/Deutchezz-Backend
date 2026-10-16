using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Stays;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deutchezz.Infrastructure.Seeders;

public static class Seeder
{
    private static readonly string[] RoleNames = ["Administrator", "Receptionist", "Customer"];

    public static async Task SeedRolesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        foreach (var roleName in RoleNames)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            var result = await roleManager.CreateAsync(new Role { Name = roleName });
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create role {roleName}: {string.Join(", ", result.Errors.Select(error => error.Code))}");
            }
        }
    }

    public static async Task SeedAdminAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");

        await EnsureSeedUserAsync(
            userManager, logger, seed.AdminEmail, seed.AdminPassword,
            "Hotel", "Administrator", UserRole.Administrator.ToString(), "administrator");
    }

    public static async Task SeedDemoAccountsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");

        await EnsureSeedUserAsync(
            userManager, logger, seed.ReceptionistEmail, seed.ReceptionistPassword,
            "Demo", "Receptionist", UserRole.Receptionist.ToString(), "receptionist");

        var customer = await EnsureSeedUserAsync(
            userManager, logger, seed.CustomerEmail, seed.CustomerPassword,
            "Demo", "Customer", UserRole.Customer.ToString(), "customer");

        if (customer is null)
            return;

        var guest = await db.Guests.FirstOrDefaultAsync(guest => guest.UserId == customer.Id);
        if (guest is null)
        {
            var email = customer.Email ?? seed.CustomerEmail;
            guest = await db.Guests.FirstOrDefaultAsync(
                candidate => candidate.UserId == null && candidate.Email != null
                    && candidate.Email.ToLower() == email.ToLower());
        }

        if (guest is null)
        {
            guest = new Guest
            {
                FirstName = customer.FirstName ?? "Demo",
                LastName = customer.LastName ?? "Customer",
                Email = customer.Email ?? seed.CustomerEmail,
                UserId = customer.Id
            };
            db.Guests.Add(guest);
            await db.SaveChangesAsync();
        }
        else if (guest.UserId is null)
        {
            guest.UserId = customer.Id;
            guest.FirstName = customer.FirstName ?? guest.FirstName;
            guest.LastName = customer.LastName ?? guest.LastName;
            await db.SaveChangesAsync();
        }

        if (await db.Bookings.AnyAsync(booking => booking.GuestId == guest.Id))
            return;

        var roomTypes = await db.RoomTypes
            .OrderBy(roomType => roomType.Name)
            .Take(3)
            .ToListAsync();
        if (roomTypes.Count == 0)
        {
            logger.LogWarning("Skipping demo bookings because no room types are available.");
            return;
        }

        var referenceGenerator = scope.ServiceProvider.GetRequiredService<IBookingReferenceGenerator>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
        var checkInTime = new TimeOnly(14, 0);
        var checkOutTime = checkInTime;
        var lagosOffset = TimeSpan.FromHours(1);
        var bookingLines = new List<BookingRoomType>();
        var bookings = new List<Booking>();

        for (var index = 0; index < 4; index++)
        {
            var roomType = roomTypes[index % roomTypes.Count];
            var checkInDate = today.AddDays(index + 1);
            var checkOutDate = checkInDate.AddDays(1);
            var duration = checkOutDate.ToDateTime(checkOutTime) - checkInDate.ToDateTime(checkInTime);
            var pricing = StayPricing.ComputeLine(
                roomType.BasePrice, 1, 1, duration,
                StayPricing.DefaultTiersFor(roomType.Name), 2);

            var booking = new Booking
            {
                BookingReference = referenceGenerator.Generate(),
                GuestId = guest.Id,
                Adults = 1,
                Children = 0,
                Source = BookingSource.Online,
                Status = BookingStatus.Confirmed,
                PaymentStatus = PaymentStatus.Pending,
                TotalAmount = pricing.LineTotal,
                ExtraChargeAmount = pricing.OverageAmount,
                ConcurrencyToken = Guid.NewGuid().ToByteArray(),
                SpecialRequests = $"Development demo booking {index + 1}"
            };
            booking.SetStay(checkInDate, checkInTime, checkOutDate, checkOutTime, lagosOffset);
            bookings.Add(booking);
            bookingLines.Add(new BookingRoomType
            {
                BookingId = booking.Id,
                RoomTypeId = roomType.Id,
                Quantity = 1,
                RatePerNight = roomType.BasePrice,
                NumberOfNights = pricing.Nights,
                Subtotal = roomType.BasePrice * pricing.Nights,
                ExtraCharge = pricing.OverageAmount,
                SurchargeTier = pricing.TierLabel
            });
        }

        db.Bookings.AddRange(bookings);
        db.BookingRoomTypes.AddRange(bookingLines);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded 4 sample bookings for the configured demo customer.");
    }

    public static async Task SeedRoomInventoryAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");

        var templates = new[]
        {
            new RoomType
            {
                Name = "Standard",
                Description = "Comfortable standard room for single or double occupancy.",
                BasePrice = 30000m,
                MaximumOccupancy = 2,
                BedType = "Queen",
                ImageUrls =
                [
                    "https://images.unsplash.com/photo-1560448204-e02f11c3d0e2?w=800",
                    "https://images.unsplash.com/photo-1611892440504-42a792e24d32?w=800"
                ],
                Features = ["Air conditioning", "Wi-Fi", "Smart TV", "Private bathroom"]
            },
            new RoomType
            {
                Name = "Deluxe",
                Description = "Spacious deluxe room with added comfort.",
                BasePrice = 40000m,
                MaximumOccupancy = 3,
                BedType = "King",
                ImageUrls =
                [
                    "https://images.unsplash.com/photo-1590490360182-c33d57733427?w=800",
                    "https://images.unsplash.com/photo-1611892440504-42a792e24d32?w=800"
                ],
                Features = ["Air conditioning", "Wi-Fi", "Smart TV", "Private bathroom", "Work desk"]
            },
            new RoomType
            {
                Name = "Executive",
                Description = "Premium executive room with lounge seating.",
                BasePrice = 55000m,
                MaximumOccupancy = 4,
                BedType = "King",
                ImageUrls =
                [
                    "https://images.unsplash.com/photo-1631049307264-da0ec9d70304?w=800",
                    "https://images.unsplash.com/photo-1566665797739-1674de7a421a?w=800"
                ],
                Features = ["Air conditioning", "Wi-Fi", "Smart TV", "Private bathroom", "Work desk", "Lounge seating"]
            }
        };

        var existingTypes = await context.RoomTypes.IgnoreQueryFilters().ToListAsync();
        var roomTypesByName = existingTypes.ToDictionary(roomType => roomType.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            if (!roomTypesByName.ContainsKey(template.Name))
            {
                context.RoomTypes.Add(template);
                roomTypesByName.Add(template.Name, template);
            }
        }
        await context.SaveChangesAsync();

        var roomTypeIds = roomTypesByName.Values.Select(roomType => roomType.Id).ToArray();
        var existingTiers = await context.RoomTypeSurchargeTiers
            .Where(tier => roomTypeIds.Contains(tier.RoomTypeId))
            .ToListAsync();
        foreach (var roomType in templates.Select(template => roomTypesByName[template.Name]))
        {
            var tierRanges = existingTiers
                .Where(tier => tier.RoomTypeId == roomType.Id)
                .Select(tier => tier.FromHours)
                .ToHashSet();
            foreach (var tier in StayPricing.DefaultTiersFor(roomType.Name))
            {
                if (tierRanges.Add(tier.FromHours))
                {
                    context.RoomTypeSurchargeTiers.Add(new RoomTypeSurchargeTier
                    {
                        RoomTypeId = roomType.Id,
                        FromHours = tier.FromHours,
                        ToHours = tier.ToHours,
                        Amount = tier.Amount
                    });
                }
            }
        }
        await context.SaveChangesAsync();

        var roomDefinitions = new List<(string Number, string Type, string Floor)>();
        for (var index = 1; index <= 8; index++)
            roomDefinitions.Add(($"1{index:00}", "Standard", "1"));
        for (var index = 1; index <= 7; index++)
            roomDefinitions.Add(($"2{index:00}", "Deluxe", "2"));
        for (var index = 1; index <= 5; index++)
            roomDefinitions.Add(($"3{index:00}", "Executive", "3"));

        var roomNumbers = roomDefinitions.Select(room => room.Number).ToArray();
        var existingRooms = await context.Rooms.IgnoreQueryFilters()
            .Where(room => roomNumbers.Contains(room.RoomNumber))
            .Select(room => room.RoomNumber)
            .ToListAsync();
        var existingRoomNumbers = existingRooms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in roomDefinitions)
        {
            if (existingRoomNumbers.Add(definition.Number))
            {
                context.Rooms.Add(new Room
                {
                    RoomNumber = definition.Number,
                    RoomTypeId = roomTypesByName[definition.Type].Id,
                    Floor = definition.Floor
                });
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Development room inventory is present: 3 room types and 20 sample rooms.");
    }

    private static async Task<User?> EnsureSeedUserAsync(
        UserManager<User> userManager,
        ILogger logger,
        string email,
        string password,
        string firstName,
        string lastName,
        string role,
        string accountName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Skipping demo {AccountName} because its seed email/password pair is not configured.", accountName);
            return null;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new User
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = true,
                IsActive = true
            };
            var create = await userManager.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create demo {accountName}: {string.Join(", ", create.Errors.Select(error => error.Code))}");
            }

            logger.LogInformation("Seeded demo {AccountName} account.", accountName);
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var addRole = await userManager.AddToRoleAsync(user, role);
            if (!addRole.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to assign the {role} role to the demo {accountName}: "
                    + string.Join(", ", addRole.Errors.Select(error => error.Code)));
            }
        }

        return user;
    }
}
