namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The policy names, in SharedKernel and not in Api, because slices declare them
/// and composition defines them. A slice should not have to reference the project
/// that hosts it in order to say who may call it.
/// </summary>
public static class TenderoPolicyNames
{
    public const string Shopper = "Shopper";
    public const string Shopkeeper = "Shopkeeper";
    public const string AgentOrShopper = "AgentOrShopper";
}
