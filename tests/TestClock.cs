namespace ElGuerre.Tendero.Tests;

/// <summary>
/// Un reloj fijo que sólo avanza cuando se le dice. Escrito a mano en vez de
/// traer <c>Microsoft.Extensions.TimeProvider.Testing</c>: son quince líneas, y
/// el presupuesto de dependencias del repositorio es deliberadamente pequeño.
///
/// Se enlaza como fichero en cada proyecto de test que lo necesita. Un proyecto
/// compartido para una clase sería más ceremonia que código.
/// </summary>
public sealed class TestClock(DateTimeOffset? start = null) : TimeProvider
{
    /// <summary>
    /// Un instante concreto y legible, no <c>UtcNow</c>: si un test falla, el
    /// mensaje debe contener una fecha que se reconozca como fija.
    /// </summary>
    public static readonly DateTimeOffset Default = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    private DateTimeOffset _now = start ?? Default;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Avanza el reloj y devuelve el nuevo instante.</summary>
    public DateTimeOffset Advance(TimeSpan by) => _now = _now.Add(by);
}
