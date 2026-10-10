using Deutchezz.Application.Common.Options;
using Deutchezz.Domain.Entities;
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
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new Role { Name = roleName });
            }
        }
    }

    public static async Task SeedAdminAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");

        if (!seed.HasAdminCredentials)
        {
            logger.LogWarning(
                "Seed:AdminEmail/Seed:AdminPassword are not configured - skipping admin user seeding. " +
                "Set them with: dotnet user-secrets set \"Seed:AdminEmail\" ... --project src/Deutchezz.csproj");
            return;
        }

        if (await userManager.FindByEmailAsync(seed.AdminEmail) is not null)
        {
            return;
        }

        var admin = new User
        {
            UserName = seed.AdminEmail,
            Email = seed.AdminEmail,
            FirstName = "Hotel",
            LastName = "Administrator",
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, seed.AdminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Administrator");
            logger.LogInformation("Seeded admin user {Email}", seed.AdminEmail);
        }
        else
        {
            logger.LogError("Failed to create admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    public static async Task SeedRoomInventoryAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");

        if (await context.RoomTypes.AnyAsync() || await context.Rooms.AnyAsync())
        {
            logger.LogInformation("Room inventory already present - skipping room seeding.");
            return;
        }

        var standard = new RoomType
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
        };
        var deluxe = new RoomType
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
        };
        var executive = new RoomType
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
        };

        // The seed only creates missing rows and never updates existing ones, so later
        // admin edits survive restarts. Prices are per night in NGN.
        context.RoomTypes.AddRange(standard, deluxe, executive);
        await context.SaveChangesAsync();

        // 20 sample rooms total: 8 Standard, 7 Deluxe, 5 Executive (sample inventory).
        var rooms = new List<Room>();
        for (var i = 1; i <= 8; i++) rooms.Add(new Room { RoomNumber = $"1{i:00}", RoomTypeId = standard.Id, Floor = "1" });
        for (var i = 1; i <= 7; i++) rooms.Add(new Room { RoomNumber = $"2{i:00}", RoomTypeId = deluxe.Id, Floor = "2" });
        for (var i = 1; i <= 5; i++) rooms.Add(new Room { RoomNumber = $"3{i:00}", RoomTypeId = executive.Id, Floor = "3" });

        context.Rooms.AddRange(rooms);
        await context.SaveChangesAsync();
        logger.LogInformation("Seeded 3 room types (Standard 30000, Deluxe 40000, Executive 55000 per night) and 20 rooms (sample inventory)");
    }
}
