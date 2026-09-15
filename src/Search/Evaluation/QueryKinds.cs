namespace ElGuerre.Tendero.Search.Evaluation;

/// <summary>
/// The kinds a golden query can have: Baymard Institute's eight e-commerce query
/// types, and one it does not name.
///
/// Taken from a published reference rather than made up, so a report reads
/// against it: https://baymard.com/blog/ecommerce-search-query-types. A typo or a
/// synonym is not a kind of its own — Baymard files both under the type the query
/// would have had spelled right.
/// </summary>
public static class QueryKinds
{
    public const string Exact = "exact";
    public const string ProductType = "product-type";
    public const string Feature = "feature";
    public const string UseCase = "use-case";
    public const string Abbreviation = "abbreviation";
    public const string Compatibility = "compatibility";
    public const string Symptom = "symptom";
    public const string NonProduct = "non-product";

    /// <summary>Something the shop does not sell. Not one of Baymard's types, and met every day.</summary>
    public const string NotSold = "not-sold";

    /// <summary>
    /// A query nothing in the catalogue can answer — a product the shop does not
    /// sell, or a question that is not about a product.
    ///
    /// It has no relevant judgment by construction, so it is never scored with
    /// NDCG, never counts as unjudged and never enters a judgment pool. What it
    /// measures is the opposite failure: a search that answers anyway.
    /// </summary>
    public static bool IsNoAnswer(string? kind) => kind is NotSold or NonProduct;
}
