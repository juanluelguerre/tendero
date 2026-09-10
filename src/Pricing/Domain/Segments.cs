using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// Which price segment belongs to whoever is asking.
///
/// It exists for a security reason that is easy to miss: **if the segment
/// travelled in the request body, anybody would ask for the VIP tariff.** A
/// quoting endpoint is public — a guest has to be able to see prices — so the
/// field that decides the price cannot come from the same place as the question.
///
/// Today's rule is deliberately small: whoever is not authenticated gets the
/// default segment, whatever they send. <c>Accounts</c> exists since phase 7
/// and <c>Customer</c> already carries a segment, but nothing puts it on the
/// principal yet; the day something does, this shrinks to reading it from
/// there — and the hole is closed already, which is what matters in the
/// meantime.
/// </summary>
public static class Segments
{
    public const string Retail = "retail";

    public static string For(CommercePrincipal principal, string? requested, string @default = Retail)
    {
        // No identity, no special tariff. Asking for one is not an error: the
        // answer is simply everybody's answer.
        if (principal.Subject is null)
            return @default;

        return string.IsNullOrWhiteSpace(requested) ? @default : requested.Trim().ToLowerInvariant();
    }
}
