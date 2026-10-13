using Deutchezz.Application.ActivityLogs;
using Deutchezz.Application.Auth;
using Deutchezz.Application.Availability;
using Deutchezz.Application.Bookings;
using Deutchezz.Application.Guests;
using Deutchezz.Application.Notifications;
using Deutchezz.Application.Payments;
using Deutchezz.Application.Profile;
using Deutchezz.Application.Reports;
using Deutchezz.Application.Rooms;
using Deutchezz.Application.RoomTypes;
using Deutchezz.Application.Settings;
using Deutchezz.Application.Staff;
using Deutchezz.Application.Stays;
using Deutchezz.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        services.AddScoped<IGuestsService, GuestsService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IAdminSettingsService, AdminSettingsService>();
        services.AddScoped<IReportsService, ReportsService>();
        services.AddScoped<IActivityLogsService, ActivityLogsService>();
        services.AddScoped<IStayPolicyService, StayPolicyService>();
        services.AddScoped<IStayConflictService, StayConflictService>();
        services.AddHostedService<PendingBookingExpiryWorker>();
        services.AddHostedService<PendingRegistrationCleanupWorker>();
        return services;
    }
}