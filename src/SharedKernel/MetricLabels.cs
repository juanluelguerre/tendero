using System.Collections.Concurrent;
using System.Text;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// An enum member as a metric label, in the tracking plan's spelling:
/// <c>PaymentDeclined</c> → <c>payment_declined</c>.
///
/// One rule rather than a switch per metric, so a member added to an enum gets
/// a label without anybody writing one — and <c>TrackingPlanTests</c> compares
/// every enum a plan attribute names (its <c>from</c>) with the values the plan
/// lists, so that new label is a red test until the plan says what it means.
/// </summary>
public static class MetricLabels
{
    private static readonly ConcurrentDictionary<Enum, string> Cache = new();

    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Cache.GetOrAdd(value, static member => SnakeCase(member.ToString()));

    public static string SnakeCase(string name)
    {
        var label = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
                label.Append('_');
            label.Append(char.ToLowerInvariant(name[i]));
        }

        return label.ToString();
    }
}
