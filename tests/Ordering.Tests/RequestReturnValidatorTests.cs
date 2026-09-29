using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Returns;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What a return request must carry before it reaches the aggregate: at least one
/// line, every quantity positive, and a comment a person can read in one go.
/// Found without a test at R-7/R-8's close, when ADR 0043 had removed the
/// handler's own 400s and this validator was left as the only gate.
/// </summary>
public sealed class RequestReturnValidatorTests
{
    private static readonly RequestReturnValidator Validator = new();

    private static bool Valid(params ReturnLineRequest[] lines) =>
        Validator.Validate(new RequestReturnCommand(OrderId.New(), lines, "es")).IsValid;

    private static ReturnLineRequest Line(int quantity = 1, string? comment = null) =>
        new("KETTLE-SKU", quantity, ReturnReason.WrongSize, comment);

    [Fact]
    public void A_return_with_no_line_is_refused()
    {
        Assert.False(Valid());
    }

    /// <summary>Every line, not one of them: a zero beside a one is still refused.</summary>
    [Fact]
    public void Every_quantity_is_positive()
    {
        Assert.True(Valid(Line(1)));
        Assert.False(Valid(Line(0)));
        Assert.False(Valid(Line(1), Line(0)));
    }

    [Fact]
    public void A_comment_is_at_most_five_hundred_characters()
    {
        Assert.True(Valid(Line(comment: null)));
        Assert.True(Valid(Line(comment: new string('a', 500))));
        Assert.False(Valid(Line(comment: new string('a', 501))));
    }
}
