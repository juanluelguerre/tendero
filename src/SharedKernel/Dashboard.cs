namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The period a dashboard figure covers (P11-7). Down here because more than one
/// context draws the dashboard — Ordering's sales and Accounts' denials — and both
/// have to mean the same week.
/// </summary>
public enum DashboardPeriod { Day, Week, Month }

/// <summary>The window a figure covers and the one before it, which is what "against the previous period" compares.</summary>
public sealed record DashboardWindow(DateTimeOffset From, DateTimeOffset To, DateTimeOffset PreviousFrom)
{
    public static DashboardWindow Ending(DateTimeOffset now, DashboardPeriod period)
    {
        var span = period switch
        {
            DashboardPeriod.Day => TimeSpan.FromDays(1),
            DashboardPeriod.Week => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(30),
        };
        return new DashboardWindow(now - span, now, now - span - span);
    }
}

public static class DashboardPeriods
{
    public const string Error = "period is one of: day, week, month.";

    /// <summary>A week when none is asked for: the period a shopkeeper opens the dashboard to see. Null for anything else.</summary>
    public static DashboardPeriod? Parse(string? period) =>
        period is null ? DashboardPeriod.Week
        : Enum.TryParse<DashboardPeriod>(period, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(period, out _) ? parsed
        : null;
}
