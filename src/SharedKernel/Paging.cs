using FluentValidation;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The one pagination contract every list endpoint speaks (the redesign's R-7.2,
/// "never truncate in silence"): <c>page</c> from 1, <c>pageSize</c> from 1 to
/// 100, and a result that says <c>total</c>, <c>page</c> and <c>pageSize</c>.
///
/// `GET /api/catalog/products` had it first. Four backoffice lists capped their
/// rows instead — fifty orders, a hundred claims, fifty audit rows, every stock
/// row — and a cap nobody can see is a list that lies about its own length. When
/// the fifth list arrived with the same two rules, they moved here: a limit that
/// differs from one screen to the next is a divergence nobody chose.
///
/// Offset paging, <c>Skip</c> and <c>Take</c>. Keyset paging is the scale answer
/// and is deferred with the laboratory's numbers, as the scale rule says.
/// </summary>
public static class Paging
{
    public const int FirstPage = 1;

    public const int DefaultPageSize = 20;

    /// <summary>A screen, not an export: a list somebody can pull ten thousand
    /// rows of through a browser is an export endpoint nobody designed.</summary>
    public const int MaxPageSize = 100;

    /// <summary>How many rows come before <paramref name="page"/>.</summary>
    public static int Offset(int page, int pageSize) => (page - 1) * pageSize;

    public static IRuleBuilderOptions<T, int> IsAPage<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(FirstPage);

    public static IRuleBuilderOptions<T, int> IsAPageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, MaxPageSize);
}
