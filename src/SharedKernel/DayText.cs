namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A day inside a sentence the server writes for a reader: "1 de diciembre de
/// 2026" in Spanish, "1 December 2026" in English. Written by hand, because the
/// API runs with <c>InvariantGlobalization</c> and no culture's month names are
/// there to ask. Which day it is, is the caller's to count, in the shop's zone
/// (ADR 0046): this only writes it.
///
/// Moved here from the shopper's orders (phase 12b) when the shop's campaigns
/// needed the same words (phase 13c), so a month is spelt once.
/// </summary>
public static class DayText
{
    private static readonly string[] Meses =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    private static readonly string[] Months =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public static string In(int year, int month, int day, string culture) =>
        culture == "en" ? $"{day} {Months[month - 1]} {year}" : $"{day} de {Meses[month - 1]} de {year}";
}
