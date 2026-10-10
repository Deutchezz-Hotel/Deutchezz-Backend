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

        var deluxe = new RoomType
        {
            Name = "Deluxe",
            Description = "Deluxe room",
            BasePrice = 30000m,
            MaximumOccupancy = 2,
            BedType = "Queen"
        };
        // TODO: Confirm the second and third room type names with the hotel; only "Deluxe" is confirmed.
        var placeholderTwo = new RoomType
        {
            Name = "Standard", // TODO: placeholder name - not confirmed
            Description = "Placeholder room type (name pending confirmation)",
            BasePrice = 40000m,
            MaximumOccupancy = 3,
            BedType = "Queen"
        };
        var placeholderThree = new RoomType
        {
            Name = "Executive", // TODO: placeholder name - not confirmed
            Description = "Placeholder room type (name pending confirmation)",
            BasePrice = 55000m,
            MaximumOccupancy = 4,
            BedType = "King"
        };

        context.RoomTypes.AddRange(deluxe, placeholderTwo, placeholderThree);
        await context.SaveChangesAsync();

        // 20 sample rooms total
        var rooms = new List<Room>();
        for (var i = 1; i <= 8; i++) rooms.Add(new Room { RoomNumber = $"1{i:00}", RoomTypeId = deluxe.Id, Floor = "1" });
        for (var i = 1; i <= 7; i++) rooms.Add(new Room { RoomNumber = $"2{i:00}", RoomTypeId = placeholderTwo.Id, Floor = "2" });
        for (var i = 1; i <= 5; i++) rooms.Add(new Room { RoomNumber = $"3{i:00}", RoomTypeId = placeholderThree.Id, Floor = "3" });

        context.Rooms.AddRange(rooms);
        await context.SaveChangesAsync();
        logger.LogInformation("Seeded 3 room types and 20 rooms (sample inventory)");
    }
}
