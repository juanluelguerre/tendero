namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The most rows a read answers with when it may be read by a model (phase 12):
/// "at most 50 rows per call, always with the total" (the Steward spec). Every
/// context's read honours the same number, so it lives here.
/// </summary>
public static class ReadLimits
{
    public const int MaxRows = 50;

    /// <summary>
    /// The most facts one read hands an answer to cite (phase 12b). A first estimate —
    /// a product's specifications and its claims are a few dozen — and what bounds the
    /// answer's schema, whose citations are an enum of them.
    /// </summary>
    public const int MaxFacts = 60;
}
