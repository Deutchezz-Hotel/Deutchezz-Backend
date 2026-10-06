using Deutchezz.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Deutchezz.Infrastructure.Seeders;

public static class Seeder
{
    public static async Task SeedAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seeder");
        var env = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();

        foreach (var roleName in new[] { "Admin", "Receptionist", "Guest" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new Role { Name = roleName });
            }
        }

        var adminEmail = config["Seed:Admin:Email"];
        var adminPassword = config["Seed:Admin:Password"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin == null)
            {
                admin = new User
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = config["Seed:Admin:FullName"] ?? "Administrator",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(admin, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                }
                else
                {
                    logger.LogError("Failed to create admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }

        if (env.IsDevelopment() && !context.RoomTypes.Any())
        {
            await SeedSampleRoomsAsync(context, logger);
        }
    }

    private static async Task SeedSampleRoomsAsync(Data.AppDbContext context, ILogger logger)
    {
        var deluxe = new RoomType
        {
            Name = "Deluxe",
            Description = "Deluxe room",
            NightlyRate = 30000m,
            MaxOccupancy = 2
        };
        // TODO: Confirm the second and third room type names with the hotel; only "Deluxe" is confirmed.
        var placeholderTwo = new RoomType
        {
            Name = "Standard", // TODO: placeholder name - not confirmed
            Description = "Placeholder room type (name pending confirmation)",
            NightlyRate = 40000m,
            MaxOccupancy = 3
        };
        var placeholderThree = new RoomType
        {
            Name = "Executive", // TODO: placeholder name - not confirmed
            Description = "Placeholder room type (name pending confirmation)",
            NightlyRate = 55000m,
            MaxOccupancy = 4
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