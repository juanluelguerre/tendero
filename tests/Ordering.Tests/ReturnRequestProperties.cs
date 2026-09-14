using CsCheck;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Returns over any sequence of requests and decisions, and not the six a
/// person thought of.
///
/// <see cref="ReturnRequestTests"/> already had a test for "no more than was
/// bought", and it passed while the shop refunded one shirt twice: the example
/// checked one request, and the defect needed two. That is the shape of bug a
/// property finds and an example misses — the promotion engine taught the same
/// lesson with two lines of one SKU — so the invariant is stated here over every
/// sequence the generator can build.
/// </summary>
public sealed class ReturnRequestProperties
{
    private static readonly TestClock Clock = new();

    private const int Iterations = 2_000;

    private static readonly string[] Skus = ["SHIRT", "PANS", "LAMP"];

    /// <summary>One attempt: which SKU, how many, and what then happens to it
    /// if it is accepted — including being rejected or withdrawn, which is what
    /// hands its quantity back.</summary>
    private sealed record Attempt(string Sku, int Quantity, ReturnStatus Fate);

    private static readonly Gen<Attempt> GenAttempt =
        Gen.Select(
            Gen.Int[0, Skus.Length - 1],
            Gen.Int[1, 3],
            Gen.Enum<ReturnStatus>(),
            (sku, quantity, fate) => new Attempt(Skus[sku], quantity, fate));

    private static readonly Gen<(int[] Bought, Attempt[] Attempts)> GenScenario =
        Gen.Select(
            Gen.Int[1, 3].Array[Skus.Length],
            GenAttempt.Array[1, 8]);

    /// <summary>
    /// However the requests arrive and however the shop decides them, the units
    /// spoken for never exceed the units bought — which is what keeps both the
    /// refunds and the restocking honest, since both are counted from the lines
    /// of the returns that hold them.
    /// </summary>
    [Fact]
    public void No_sequence_of_returns_ever_claims_more_than_was_bought()
    {
        GenScenario.Sample(scenario =>
        {
            var order = Delivered(scenario.Bought);
            var opened = new List<ReturnRequest>();

            foreach (var attempt in scenario.Attempts)
            {
                try
                {
                    var request = ReturnRequest.Open(
                        Clock, order,
                        [new ReturnLine(VariantId.New(), attempt.Sku, attempt.Quantity, ReturnReason.WrongSize, null)],
                        opened);

                    MoveTo(request, attempt.Fate);
                    opened.Add(request);
                }
                catch (InvalidOperationException)
                {
                    // A refusal is an allowed answer. What is checked is that
                    // whatever was accepted still adds up.
                }

                for (var index = 0; index < Skus.Length; index++)
                {
                    var held = opened
                        .Where(request => request.Status is not (ReturnStatus.Rejected or ReturnStatus.Cancelled))
                        .SelectMany(request => request.Lines)
                        .Where(line => line.Sku == Skus[index])
                        .Sum(line => line.Quantity);

                    Assert.True(held <= scenario.Bought[index],
                        $"{Skus[index]}: {held} held by returns, {scenario.Bought[index]} bought.");
                    Assert.Equal(scenario.Bought[index] - held, ReturnRequest.Returnable(order, opened)[Skus[index]]);
                }
            }
        }, iter: Iterations);
    }

    private static Order Delivered(int[] bought)
    {
        var order = OrderBuilder.Default()
            .WithLines(
            [
                .. Skus.Select((sku, index) => new OrderLine(
                    ProductId.New(), VariantId.New(), sku, "A product", null,
                    ImageId: null, new Money(20m, "EUR"), bought[index]))
            ])
            .Build();

        order.AuthorizePayment(Clock);
        order.Confirm(Clock);
        order.Ship(Clock);
        order.Deliver(Clock);

        return order;
    }

    private static void MoveTo(ReturnRequest request, ReturnStatus status)
    {
        switch (status)
        {
            case ReturnStatus.Approved:
                request.Approve(Clock);
                break;
            case ReturnStatus.Received:
                request.Approve(Clock);
                request.Receive(Clock);
                break;
            case ReturnStatus.Refunded:
                request.Approve(Clock);
                request.Receive(Clock);
                request.Refund(Clock, new Money(20m, "EUR"), "ref_1");
                break;
            case ReturnStatus.Rejected:
                request.Reject(Clock, "no");
                break;
            case ReturnStatus.Cancelled:
                request.Cancel(Clock);
                break;
        }
    }
}
