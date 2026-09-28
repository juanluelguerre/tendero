namespace ElGuerre.Tendero.Inventory.Domain;

/// <summary>
/// When a SKU needs restocking: what is available across every warehouse, at or
/// under a line. A laboratory's shelf, not a replenishment policy — two
/// warehouses and a hundred products do not earn reorder points per SKU.
///
/// It lived in the backoffice's dashboard until R-7.1 (2026-09-28), as a
/// constant beside the widget that drew it, and the widget downloaded every
/// stock row to count 38 of them. A rule the stock screen filters by and the
/// dashboard counts is the server's, so both read this one.
/// </summary>
public static class Restocking
{
    /// <summary>At or under this many available, summed over the warehouses.</summary>
    public const int At = 5;

    public static bool Needed(int available) => available <= At;

    public static bool SoldOut(int available) => available <= 0;
}
