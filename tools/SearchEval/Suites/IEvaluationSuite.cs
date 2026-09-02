namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Una puerta de calidad ejecutable. Hoy hay una — relevancia de búsqueda — y
/// esta abstracción no añade ninguna capacidad: existe para que las siguientes
/// sean baratas.
///
/// Lo que se generaliza es lo que costó caro y no es específico de la búsqueda:
/// el formato del conjunto anotado, los umbrales commiteados, el informe en
/// Markdown y el código de salida. Las dos suites que vienen —extracción de
/// claims con fuente y confianza, y las acciones que propone el copiloto— usan
/// exactamente esa maquinaria contra otro tipo de dato.
///
/// El orden importa: esto entra ANTES que la búsqueda híbrida. Si llegara
/// después, la híbrida se mediría contra una línea base que ya no se puede
/// comparar con la que hay commiteada.
/// </summary>
public interface IEvaluationSuite
{
    /// <summary>El nombre que se pasa en <c>--suite</c>.</summary>
    string Name { get; }

    /// <summary>Qué mide, para el mensaje de ayuda.</summary>
    string Description { get; }

    /// <summary>
    /// 0 si pasa, 1 si queda por debajo de los umbrales y se pidió <c>--ci</c>,
    /// 2 si no se pudo ni ejecutar (una dependencia no responde).
    /// </summary>
    Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken);
}
