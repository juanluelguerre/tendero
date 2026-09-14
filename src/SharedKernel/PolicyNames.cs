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

/// <summary>
/// The laboratory's three roles, as the token carries them. Here beside the
/// policy names for the same reason: the Api maps them to policies, and a
/// handler that has to tell the shopkeeper apart from a stranger — reading an
/// order that is not theirs — should not have to reference the Api to spell it.
/// </summary>
public static class TenderoRoles
{
    public const string Shopper = "shopper";
    public const string Shopkeeper = "shopkeeper";
    public const string Agent = "agent";
}
