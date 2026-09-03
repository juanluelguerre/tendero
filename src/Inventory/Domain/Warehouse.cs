using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Inventory.Domain;

/// <summary>
/// A place stock physically is.
///
/// Two of them at laboratory depth, and two is the smallest number that makes
/// the interesting question exist at all: with one warehouse, allocation is
/// "take it or fail" and no strategy is needed. The second one is what turns
/// availability into a decision.
///
/// The code is the identity and it is stable — <c>MAD</c>, <c>BCN</c> — because
/// it is what a stock row is keyed on and what an operator types. The name is
/// user-facing, so <see cref="LocalizedText"/> (invariant 6).
/// </summary>
public sealed class Warehouse
{
    public Warehouse(string code, LocalizedText name, int priority, bool isActive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = Normalise(code);
        Name = name;
        Priority = priority;
        IsActive = isActive;
    }

    private Warehouse() { } // EF Core

    public string Code { get; private set; } = default!;

    public LocalizedText Name { get; private set; } = default!;

    /// <summary>
    /// Lower is preferred. It is what <c>priority-first</c> allocation walks, and
    /// it is a property of the warehouse rather than of the strategy: which
    /// warehouse ships first is a commercial decision (it is nearer, it is
    /// cheaper, it is the one that is open), not an algorithm's opinion.
    /// </summary>
    public int Priority { get; private set; }

    /// <summary>
    /// A warehouse that is closed still holds its stock — the rows do not
    /// disappear — but nothing is allocated from it. Deleting it instead would
    /// lose the count, which is exactly what nobody wants during a stocktake.
    /// </summary>
    public bool IsActive { get; private set; }

    public static string Normalise(string code) => code.Trim().ToUpperInvariant();
}
