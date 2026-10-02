namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The most rows a read answers with when it may be read by a model (phase 12):
/// "at most 50 rows per call, always with the total" (the Steward spec). Every
/// context's read honours the same number, so it lives here.
/// </summary>
public static class ReadLimits
{
    public const int MaxRows = 50;
}
