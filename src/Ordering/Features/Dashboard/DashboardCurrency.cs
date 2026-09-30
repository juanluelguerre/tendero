using ElGuerre.Tendero.Ordering.Domain;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

/// <summary>
/// The currency a figure is in: its orders', or the shop's default when the period
/// has none — once, for every figure the dashboard computes.
/// </summary>
public static class DashboardCurrency
{
    public const string Default = "EUR";

    public static string Of(IEnumerable<Order> orders) =>
        orders.Select(order => order.Currency).FirstOrDefault() ?? Default;
}
