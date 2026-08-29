using Tendero.Ordering.Domain;
using Tendero.SharedKernel;
using Xunit;

namespace Tendero.Ordering.Tests;

public sealed class OrderTests
{
    [Fact]
    public void An_order_keeps_the_currency_it_was_placed_in()
    {
        var order = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR"), OrderBuilder.Line(5m, "EUR"))
            .Build();

        Assert.Equal("EUR", order.Currency);
    }

    [Fact]
    public void The_total_is_the_sum_of_the_lines()
    {
        var order = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR", quantity: 3), OrderBuilder.Line(5.50m, "EUR"))
            .Build();

        Assert.Equal(new Money(35.50m, "EUR"), order.Total);
    }

    [Fact]
    public void Placing_an_order_that_mixes_currencies_is_rejected()
    {
        var builder = OrderBuilder.Default()
            .WithLines(OrderBuilder.Line(10m, "EUR"), OrderBuilder.Line(12m, "USD"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("mix currencies", exception.Message);
    }

    /// <summary>
    /// La divisa vive en el pedido, no en la primera línea. Sin esto, un pedido
    /// sin líneas hacía estallar Total con un ArgumentOutOfRange desde dentro
    /// del agregado — inalcanzable hoy, pero con la espoleta puesta.
    /// </summary>
    [Fact]
    public void The_total_of_an_order_with_no_lines_is_zero_in_its_own_currency()
    {
        // El constructor privado es el que usa EF al materializar; llegar por ahí
        // es la única forma de construir el estado que preocupa, porque ningún
        // método público deja un pedido sin líneas.
        var materialised = (Order)Activator.CreateInstance(typeof(Order), nonPublic: true)!;
        typeof(Order).GetProperty(nameof(Order.Currency))!.SetValue(materialised, "EUR");

        Assert.Empty(materialised.Lines);
        Assert.Equal(Money.Zero("EUR"), materialised.Total);
    }

    [Fact]
    public void Placing_an_order_with_no_lines_is_rejected()
    {
        var builder = OrderBuilder.Default().WithLines();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void The_culture_is_normalised_to_its_language()
    {
        var order = OrderBuilder.Default().InCulture("es-ES").Build();

        Assert.Equal("es", order.Culture);
    }
}
