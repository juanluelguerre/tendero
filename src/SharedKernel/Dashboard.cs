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

/// <remarks>
/// Parsing used to live here too, a hand copy of the rule a query string's
/// closed sets now share: an endpoint binds <c>WireEnum&lt;DashboardPeriod&gt;?</c>
/// (ADR 0043), so a period is read by its name in any case, a number is refused,
/// and the document declares the three names. What is left is the one thing
/// that is the dashboard's own, and it stays in one place because five
/// endpoints in two contexts have to agree on it.
/// </remarks>
public static class DashboardPeriods
{
    /// <summary>A week when none is asked for: the period a shopkeeper opens the dashboard to see.</summary>
    public const DashboardPeriod Default = DashboardPeriod.Week;
}
