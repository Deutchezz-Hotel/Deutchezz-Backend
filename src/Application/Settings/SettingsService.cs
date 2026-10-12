using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Deutchezz.Application.Settings;

public record AdminSettingsResponse(
    int MaxRoomsPerBooking,
    string CheckInTime,
    string CheckOutTime,
    int CancellationWindowHours,
    bool RequireFullPaymentAtCheckIn,
    bool RequirePaymentBeforeCheckOut,
    bool EarlyCheckOutRequiresAdmin);

public record UpdateAdminSettingsRequest(
    int? MaxRoomsPerBooking,
    string? CheckInTime,
    string? CheckOutTime,
    int? CancellationWindowHours,
    bool? RequireFullPaymentAtCheckIn,
    bool? RequirePaymentBeforeCheckOut,
    bool? EarlyCheckOutRequiresAdmin);

public interface IAdminSettingsService
{
    Task<Result<AdminSettingsResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<AdminSettingsResponse>> UpdateAsync(UpdateAdminSettingsRequest request, Guid adminUserId, CancellationToken cancellationToken);
}

public class AdminSettingsService(
    IAppDbContext db,
    IOptions<SettingsOptions> settings,
    IOptions<CancellationOptions> cancellation,
    IActivityLogger activityLogger) : IAdminSettingsService
{
    private const string MaxRoomsKey = "MaxRoomsPerBooking";
    private const string CheckInTimeKey = "CheckInTime";
    private const string CheckOutTimeKey = "CheckOutTime";
    private const string CancellationWindowKey = "CancellationWindowHours";
    private const string RequireFullPaymentAtCheckInKey = "RequireFullPaymentAtCheckIn";
    private const string RequirePaymentBeforeCheckOutKey = "RequirePaymentBeforeCheckOut";
    private const string EarlyCheckOutRequiresAdminKey = "EarlyCheckOutRequiresAdmin";

    public async Task<Result<AdminSettingsResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        return new AdminSettingsResponse(
            GetInt(stored, MaxRoomsKey, settings.Value.MaxRoomsPerBooking),
            GetTime(stored, CheckInTimeKey, settings.Value.CheckInTime),
            GetTime(stored, CheckOutTimeKey, settings.Value.CheckOutTime),
            GetInt(stored, CancellationWindowKey, cancellation.Value.WindowHours),
            GetBool(stored, RequireFullPaymentAtCheckInKey, settings.Value.RequireFullPaymentAtCheckIn),
            GetBool(stored, RequirePaymentBeforeCheckOutKey, settings.Value.RequirePaymentBeforeCheckOut),
            GetBool(stored, EarlyCheckOutRequiresAdminKey, settings.Value.EarlyCheckOutRequiresAdmin));
    }

    public async Task<Result<AdminSettingsResponse>> UpdateAsync(
        UpdateAdminSettingsRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        if (request.MaxRoomsPerBooking is < 1 or > 1000)
            return Error.Validation("settings.invalid_max_rooms", "MaxRoomsPerBooking must be between 1 and 1000");
        if (request.CheckInTime is not null && !TimeOnly.TryParse(request.CheckInTime, out _))
            return Error.Validation("settings.invalid_check_in_time", "CheckInTime must be a time such as 14:00");
        if (request.CheckOutTime is not null && !TimeOnly.TryParse(request.CheckOutTime, out _))
            return Error.Validation("settings.invalid_check_out_time", "CheckOutTime must be a time such as 12:00");
        if (request.CancellationWindowHours is < 1 or > 8760)
            return Error.Validation("settings.invalid_cancellation_window", "CancellationWindowHours must be between 1 and 8760");

        var edits = new List<(string Key, string Value)>(7);

        if (request.MaxRoomsPerBooking is not null)
            edits.Add((MaxRoomsKey, request.MaxRoomsPerBooking.Value.ToString()));
        if (request.CheckInTime is not null)
            edits.Add((CheckInTimeKey, request.CheckInTime));
        if (request.CheckOutTime is not null)
            edits.Add((CheckOutTimeKey, request.CheckOutTime));
        if (request.CancellationWindowHours is not null)
            edits.Add((CancellationWindowKey, request.CancellationWindowHours.Value.ToString()));
        if (request.RequireFullPaymentAtCheckIn is not null)
            edits.Add((RequireFullPaymentAtCheckInKey, request.RequireFullPaymentAtCheckIn.Value.ToString()));
        if (request.RequirePaymentBeforeCheckOut is not null)
            edits.Add((RequirePaymentBeforeCheckOutKey, request.RequirePaymentBeforeCheckOut.Value.ToString()));
        if (request.EarlyCheckOutRequiresAdmin is not null)
            edits.Add((EarlyCheckOutRequiresAdminKey, request.EarlyCheckOutRequiresAdmin.Value.ToString()));

        foreach (var (key, value) in edits)
        {
            var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (existing is null)
            {
                db.Settings.Add(new Setting { Key = key, Value = value });
            }
            else
            {
                existing.Value = value;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "settings.updated", "Setting", null,
            $"Updated {edits.Count} setting(s)", cancellationToken);

        return await GetAsync(cancellationToken);
    }

    private static int GetInt(IReadOnlyDictionary<string, string> stored, string key, int fallback) =>
        stored.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static string GetTime(IReadOnlyDictionary<string, string> stored, string key, string fallback) =>
        stored.TryGetValue(key, out var value) && TimeOnly.TryParse(value, out _) ? value : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> stored, string key, bool fallback) =>
        stored.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
}