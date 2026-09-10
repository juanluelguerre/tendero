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
/// So the request carries none. The segment is a fact <c>Accounts</c> holds
/// about the customer, the principal accessor reads it once per request, and
/// this reads it off the principal. Somebody the shop has no segment for — a
/// guest, an agent, a person who signed in and has not been registered — is
/// quoted the default.
///
/// It used to take a requested segment and honour it for anybody signed in,
/// with a comment calling the hole closed because guests were refused. It was
/// not: any signed-in shopper who sent "vip" was quoted VIP, while checkout
/// sent nothing and charged retail — so a quote could promise a price the till
/// refused, and a real VIP was never charged as one.
/// </summary>
public static class Segments
{
    public const string Retail = "retail";

    public static string For(CommercePrincipal principal, string @default = Retail) =>
        principal.Segment ?? @default;
}
