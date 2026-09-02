namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Los nombres de las políticas, en SharedKernel y no en Api, porque los slices
/// los declaran y la composición los define. Un slice no debe referenciar el
/// proyecto que lo aloja para poder decir quién puede llamarlo.
/// </summary>
public static class TenderoPolicyNames
{
    public const string Shopper = "Shopper";
    public const string Shopkeeper = "Shopkeeper";
    public const string AgentOrShopper = "AgentOrShopper";
}
