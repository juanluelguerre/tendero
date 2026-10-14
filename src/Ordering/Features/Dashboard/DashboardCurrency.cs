using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

/// <summary>
/// The currency a figure is in: its orders', or the shop's default when the period
/// has none — once, for every figure the dashboard computes.
/// </summary>
public static class DashboardCurrency
{
    public const string Default = ShopOptions.DefaultCurrency;

    public static string Of(IEnumerable<Order> orders) =>
        orders.Select(order => order.Currency).FirstOrDefault() ?? Default;
}
