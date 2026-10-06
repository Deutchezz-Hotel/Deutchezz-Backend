using Deutchezz.Application.Auth;
using Deutchezz.Application.Availability;
using Deutchezz.Application.Bookings;
using Deutchezz.Application.Notifications;
using Deutchezz.Application.Payments;
using Deutchezz.Application.Profile;
using Deutchezz.Application.Rooms;
using Deutchezz.Application.RoomTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Deutchezz.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IRoomTypesService, RoomTypesService>();
        services.AddScoped<IRoomsService, RoomsService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<INotificationService, NotificationService>();
        return services;
    }
}