using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Money in the currency the tests' orders are in, said short: a figure asserted
/// as <c>Eur(74.90m)</c> reads as the amount it is, and fails on the currency
/// too — which a bare decimal never could.
/// </summary>
internal static class Amounts
{
    public static Money Eur(decimal amount) => new(amount, "EUR");
}
