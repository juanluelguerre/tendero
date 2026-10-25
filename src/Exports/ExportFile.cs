namespace ElGuerre.Tendero.Exports;

/// <summary>A file an export wrote, to be handed over as a download.</summary>
/// <param name="Rows">What it carried: products, order lines.</param>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName, int Rows);
