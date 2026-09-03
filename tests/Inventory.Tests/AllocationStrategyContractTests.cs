using CsCheck;
using ElGuerre.Tendero.Inventory.Ledger;
using ElGuerre.Tendero.Inventory.Ports;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>
/// What every allocation strategy must honour, whoever writes it.
///
/// The same arrangement as the catalogue connectors and the tax calculators: an
/// abstract suite the adapter inherits in three lines, and a new strategy
/// without its subclass does not merge.
///
/// The load-bearing assertion is the third one. **All or nothing**: a strategy
/// that fills four of five units has removed four units from sale for a shipment
/// that cannot go out, and it is the kind of rule that is obviously right in a
/// review and quietly broken by the next optimisation.
/// </summary>
public abstract class AllocationStrategyContractTests
{
    protected abstract IAllocationStrategy Strategy { get; }

    private static WarehouseStock Warehouse(string code, int priority, int available) =>
        new(code, priority, available);

    [Fact]
    public void The_key_is_a_stable_lowercase_identifier()
    {
        Assert.False(string.IsNullOrWhiteSpace(Strategy.Key));
        Assert.Equal(Strategy.Key.ToLowerInvariant(), Strategy.Key);
        Assert.DoesNotContain(' ', Strategy.Key);
    }

    [Fact]
    public void Nothing_is_allocated_when_there_is_no_stock()
    {
        Assert.Empty(Strategy.Allocate(new StockRequest("SKU", 1), []));
        Assert.Empty(Strategy.Allocate(new StockRequest("SKU", 1), [Warehouse("MAD", 10, 0)]));
    }

    [Fact]
    public void An_allocation_is_for_exactly_what_was_asked_for_or_it_does_not_happen()
    {
        var warehouses = new[] { Warehouse("MAD", 10, 3), Warehouse("BCN", 20, 2) };

        Gen.Int[1, 12].Sample(wanted =>
        {
            var allocations = Strategy.Allocate(new StockRequest("SKU", wanted), warehouses);

            // Either nothing, or exactly the quantity. Never in between: a
            // partially filled line is stock held for an order that cannot ship.
            Assert.True(
                allocations.Count == 0 || allocations.Sum(a => a.Quantity) == wanted,
                $"{Strategy.Key} allocated {allocations.Sum(a => a.Quantity)} of {wanted}.");
        }, iter: 500);
    }

    [Fact]
    public void No_warehouse_is_ever_asked_for_more_than_it_has()
    {
        Gen.Select(Gen.Int[0, 8], Gen.Int[0, 8], Gen.Int[1, 20]).Sample(triple =>
        {
            var (mad, bcn, wanted) = triple;
            var warehouses = new[] { Warehouse("MAD", 10, mad), Warehouse("BCN", 20, bcn) };

            var allocations = Strategy.Allocate(new StockRequest("SKU", wanted), warehouses);

            foreach (var warehouse in warehouses)
            {
                var taken = allocations
                    .Where(allocation => allocation.WarehouseCode == warehouse.WarehouseCode)
                    .Sum(allocation => allocation.Quantity);

                Assert.True(taken <= warehouse.Available,
                    $"{Strategy.Key} took {taken} from {warehouse.WarehouseCode}, which has {warehouse.Available}.");
            }
        }, iter: 2_000);
    }

    [Fact]
    public void Every_allocation_is_positive_and_names_the_sku_that_was_asked_for()
    {
        var allocations = Strategy.Allocate(
            new StockRequest("B073WXYZ01-DEFAULT", 2), [Warehouse("MAD", 10, 5)]);

        Assert.All(allocations, allocation =>
        {
            Assert.Equal("B073WXYZ01-DEFAULT", allocation.Sku);
            Assert.True(allocation.Quantity > 0);
        });
    }

    /// <summary>
    /// Same inputs, same answer — including when two warehouses have the same
    /// priority, which is where an unstable sort would show. It is the same
    /// determinism argument the promotion engine makes about `(Priority, Code)`,
    /// and it matters for the same reason: an allocation is written onto a
    /// reservation and has to be reproducible.
    /// </summary>
    [Fact]
    public void The_same_question_always_gets_the_same_answer()
    {
        var warehouses = new[] { Warehouse("BCN", 10, 3), Warehouse("MAD", 10, 3) };
        var request = new StockRequest("SKU", 4);

        Assert.Equal(
            Strategy.Allocate(request, warehouses).Select(a => (a.WarehouseCode, a.Quantity)),
            Strategy.Allocate(request, warehouses).Select(a => (a.WarehouseCode, a.Quantity)));
    }

    [Fact]
    public void Preference_order_is_respected_before_anything_else()
    {
        // Both can fill it on their own; the preferred one has to win under
        // either policy.
        var allocations = Strategy.Allocate(
            new StockRequest("SKU", 2), [Warehouse("BCN", 20, 9), Warehouse("MAD", 10, 9)]);

        Assert.Equal("MAD", Assert.Single(allocations).WarehouseCode);
    }
}

public sealed class PriorityFirstAllocationContractTests : AllocationStrategyContractTests
{
    protected override IAllocationStrategy Strategy { get; } = new PriorityFirstAllocation();

    /// <summary>
    /// The behaviour that distinguishes it: two warehouses, neither enough on its
    /// own, and the order still goes out — in two parcels.
    /// </summary>
    [Fact]
    public void A_line_that_no_single_warehouse_can_fill_is_split()
    {
        var allocations = Strategy.Allocate(
            new StockRequest("SKU", 5), [new WarehouseStock("MAD", 10, 3), new WarehouseStock("BCN", 20, 2)]);

        Assert.Equal(
            [("MAD", 3), ("BCN", 2)],
            allocations.Select(allocation => (allocation.WarehouseCode, allocation.Quantity)));
    }
}

public sealed class SingleWarehouseAllocationContractTests : AllocationStrategyContractTests
{
    protected override IAllocationStrategy Strategy { get; } = new SingleWarehouseAllocation();

    /// <summary>
    /// The mirror of the test above, and the reason two strategies exist: the
    /// same stock and the same request, answered differently on purpose. Some
    /// goods must not arrive in two parcels.
    /// </summary>
    [Fact]
    public void A_line_that_no_single_warehouse_can_fill_is_refused()
    {
        var allocations = Strategy.Allocate(
            new StockRequest("SKU", 5), [new WarehouseStock("MAD", 10, 3), new WarehouseStock("BCN", 20, 2)]);

        Assert.Empty(allocations);
    }

    [Fact]
    public void It_skips_a_preferred_warehouse_that_cannot_fill_the_whole_line()
    {
        var allocations = Strategy.Allocate(
            new StockRequest("SKU", 3), [new WarehouseStock("MAD", 10, 2), new WarehouseStock("BCN", 20, 4)]);

        Assert.Equal("BCN", Assert.Single(allocations).WarehouseCode);
    }
}
