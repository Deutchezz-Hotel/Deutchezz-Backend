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
    bool EarlyCheckOutRequiresAdmin,
    int TimeSlotMinutes,
    string EarliestCheckInTime,
    string LatestCheckInTime,
    string EarliestCheckOutTime,
    string LatestCheckOutTime,
    int TurnaroundMinutes,
    int MaxStayNights,
    int GraceHours,
    int CheckOutReminderLeadMinutes,
    string ReceptionNotificationEmails);

public record UpdateAdminSettingsRequest(
    int? MaxRoomsPerBooking,
    string? CheckInTime,
    string? CheckOutTime,
    int? CancellationWindowHours,
    bool? RequireFullPaymentAtCheckIn,
    bool? RequirePaymentBeforeCheckOut,
    bool? EarlyCheckOutRequiresAdmin,
    int? TimeSlotMinutes,
    string? EarliestCheckInTime,
    string? LatestCheckInTime,
    string? EarliestCheckOutTime,
    string? LatestCheckOutTime,
    int? TurnaroundMinutes,
    int? MaxStayNights = null,
    int? GraceHours = null,
    int? CheckOutReminderLeadMinutes = null,
    string? ReceptionNotificationEmails = null);

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
    private const string TimeSlotMinutesKey = "TimeSlotMinutes";
    private const string EarliestCheckInTimeKey = "EarliestCheckInTime";
    private const string LatestCheckInTimeKey = "LatestCheckInTime";
    private const string EarliestCheckOutTimeKey = "EarliestCheckOutTime";
    private const string LatestCheckOutTimeKey = "LatestCheckOutTime";
    private const string TurnaroundMinutesKey = "TurnaroundMinutes";
    private const string MaxStayNightsKey = "MaxStayNights";
    private const string GraceHoursKey = "GraceHours";
    private const string CheckOutReminderLeadMinutesKey = "CheckOutReminderLeadMinutes";
    private const string ReceptionNotificationEmailsKey = "ReceptionNotificationEmails";

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
            GetBool(stored, EarlyCheckOutRequiresAdminKey, settings.Value.EarlyCheckOutRequiresAdmin),
            GetInt(stored, TimeSlotMinutesKey, settings.Value.TimeSlotMinutes),
            GetTime(stored, EarliestCheckInTimeKey, settings.Value.EarliestCheckInTime),
            GetTime(stored, LatestCheckInTimeKey, settings.Value.LatestCheckInTime),
            GetTime(stored, EarliestCheckOutTimeKey, settings.Value.EarliestCheckOutTime),
            GetTime(stored, LatestCheckOutTimeKey, settings.Value.LatestCheckOutTime),
            GetInt(stored, TurnaroundMinutesKey, settings.Value.TurnaroundMinutes),
            GetInt(stored, MaxStayNightsKey, settings.Value.MaxStayNights),
            GetInt(stored, GraceHoursKey, settings.Value.GraceHours),
            GetInt(stored, CheckOutReminderLeadMinutesKey, settings.Value.CheckOutReminderLeadMinutes),
            GetString(stored, ReceptionNotificationEmailsKey, settings.Value.ReceptionNotificationEmails));
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
        if (request.TimeSlotMinutes is < 1 or > 1440)
            return Error.Validation("settings.invalid_time_slot", "TimeSlotMinutes must be between 1 and 1440");
        if (request.TurnaroundMinutes is < 0 or > 1440)
            return Error.Validation("settings.invalid_turnaround", "TurnaroundMinutes must be between 0 and 1440");
        if (request.MaxStayNights is < 1 or > 365)
            return Error.Validation("settings.invalid_max_stay_nights", "MaxStayNights must be between 1 and 365");
        if (request.GraceHours is < 0 or > 24)
            return Error.Validation("settings.invalid_grace_hours", "GraceHours must be between 0 and 24");
        if (request.CheckOutReminderLeadMinutes is < 5 or > 2880)
            return Error.Validation("settings.invalid_checkout_reminder_lead", "CheckOutReminderLeadMinutes must be between 5 and 2880");
        if (request.ReceptionNotificationEmails is not null && !IsValidEmailList(request.ReceptionNotificationEmails))
            return Error.Validation("settings.invalid_reception_emails", "ReceptionNotificationEmails must be a comma-separated list of email addresses");
        if (request.EarliestCheckInTime is not null && !TimeOnly.TryParse(request.EarliestCheckInTime, out _))
            return Error.Validation("settings.invalid_earliest_check_in_time", "EarliestCheckInTime must be a time such as 00:00");
        if (request.LatestCheckInTime is not null && !TimeOnly.TryParse(request.LatestCheckInTime, out _))
            return Error.Validation("settings.invalid_latest_check_in_time", "LatestCheckInTime must be a time such as 23:30");
        if (request.EarliestCheckOutTime is not null && !TimeOnly.TryParse(request.EarliestCheckOutTime, out _))
            return Error.Validation("settings.invalid_earliest_check_out_time", "EarliestCheckOutTime must be a time such as 00:00");
        if (request.LatestCheckOutTime is not null && !TimeOnly.TryParse(request.LatestCheckOutTime, out _))
            return Error.Validation("settings.invalid_latest_check_out_time", "LatestCheckOutTime must be a time such as 23:30");

        var edits = new List<(string Key, string Value)>(19);

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
        if (request.TimeSlotMinutes is not null)
            edits.Add((TimeSlotMinutesKey, request.TimeSlotMinutes.Value.ToString()));
        if (request.EarliestCheckInTime is not null)
            edits.Add((EarliestCheckInTimeKey, request.EarliestCheckInTime));
        if (request.LatestCheckInTime is not null)
            edits.Add((LatestCheckInTimeKey, request.LatestCheckInTime));
        if (request.EarliestCheckOutTime is not null)
            edits.Add((EarliestCheckOutTimeKey, request.EarliestCheckOutTime));
        if (request.LatestCheckOutTime is not null)
            edits.Add((LatestCheckOutTimeKey, request.LatestCheckOutTime));
        if (request.TurnaroundMinutes is not null)
            edits.Add((TurnaroundMinutesKey, request.TurnaroundMinutes.Value.ToString()));
        if (request.MaxStayNights is not null)
            edits.Add((MaxStayNightsKey, request.MaxStayNights.Value.ToString()));
        if (request.GraceHours is not null)
            edits.Add((GraceHoursKey, request.GraceHours.Value.ToString()));
        if (request.CheckOutReminderLeadMinutes is not null)
            edits.Add((CheckOutReminderLeadMinutesKey, request.CheckOutReminderLeadMinutes.Value.ToString()));
        if (request.ReceptionNotificationEmails is not null)
            edits.Add((ReceptionNotificationEmailsKey, request.ReceptionNotificationEmails.Trim()));

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

    private static string GetString(IReadOnlyDictionary<string, string> stored, string key, string fallback) =>
        stored.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>True for an empty list (alerts fall back to the configured desk inbox) or a
    /// comma/semicolon-separated list of plausible email addresses.</summary>
    private static bool IsValidEmailList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        var parts = raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(p => p.Contains('@') && !p.Any(char.IsWhiteSpace));
    }
}