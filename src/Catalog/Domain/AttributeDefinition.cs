using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

/// <summary>
/// Qué clase de dato es un atributo. Decide cómo se guarda, cómo se valida y
/// cómo se renderiza al índice.
/// </summary>
public enum AttributeKind
{
    /// <summary>Texto libre en una sola lengua (un código de fabricante).</summary>
    Text,

    /// <summary>Texto libre de cara al usuario, y por tanto traducible.</summary>
    LocalizedText,

    Number,
    Boolean,

    /// <summary>Uno de un conjunto cerrado de opciones con etiqueta por cultura.</summary>
    Option
}

/// <summary>
/// Una opción posible. El CÓDIGO es estable y la etiqueta es texto de cara al
/// usuario, luego <see cref="LocalizedText"/> (invariante 6).
///
/// Esta separación es la que arregla las consultas inglesas que puntúan 0.000:
/// hoy el catálogo guarda "azul marino" y nada sabe que en inglés eso se dice
/// "navy blue".
/// </summary>
public sealed record AttributeOption(string Code, LocalizedText Label);

public sealed record AttributeDefinitionChanged(string Code, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Qué significa un atributo, una sola vez y para todo el catálogo.
///
/// Antes los atributos eran <c>Dictionary&lt;string,string&gt;</c>: sin tipo, sin
/// unidad, sin etiqueta y sin traducción. <c>SetAttribute("colour", "banana")</c>
/// se aceptaba, y "azul marino" era literalmente el dato — con lo que el índice
/// inglés contenía español y ninguna consulta inglesa podía casarlo.
///
/// Es un agregado propio y no una tabla de referencia dentro de Product porque
/// tiene su propio ciclo de vida: el tendero define "COLOR" una vez y mil
/// productos lo usan.
/// </summary>
public sealed class AttributeDefinition : AggregateRoot
{
    private readonly List<AttributeOption> _options = [];
    private readonly List<string> _aliases = [];

    /// <summary>Estable y en mayúsculas: es la clave con la que un producto se
    /// refiere a esto, y cambiarla reescribiría todo el catálogo.</summary>
    public string Code { get; private set; } = default!;

    public LocalizedText Label { get; private set; } = default!;
    public AttributeKind Kind { get; private set; }

    /// <summary>"mm", "W", "L". Va aparte del valor para que el número siga
    /// siendo un número.</summary>
    public string? Unit { get; private set; }

    /// <summary>Si puede distinguir variantes (COLOR sí, POTENCIA no).</summary>
    public bool IsVariantAxis { get; private set; }

    /// <summary>Si aparece como faceta en la búsqueda.</summary>
    public bool IsFacet { get; private set; }

    /// <summary>Si su texto entra en el índice. Un código de fabricante no
    /// debería: mete ruido y nadie lo teclea.</summary>
    public bool IsSearchable { get; private set; }

    /// <summary>
    /// Propuesta por el importador y pendiente de que un humano le ponga
    /// etiquetas. Es la misma idea que Draft en un producto: importar no
    /// publica, y aquí tampoco define.
    /// </summary>
    public bool IsDraft { get; private set; }

    public IReadOnlyList<AttributeOption> Options => _options;

    /// <summary>
    /// Cómo llama cada origen a esto: <c>genero</c>, <c>gender</c>,
    /// <c>g:gender</c>. Se declaran en vez de adivinarse por la etiqueta porque
    /// adivinar falla justo donde duele — "genero" sin tilde no casa con la
    /// etiqueta "género", y el fallo es silencioso.
    /// </summary>
    public IReadOnlyList<string> Aliases => _aliases;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AttributeDefinition() { } // EF Core

    public static AttributeDefinition Define(
        TimeProvider clock,
        string code,
        LocalizedText label,
        AttributeKind kind,
        string? unit = null,
        bool isVariantAxis = false,
        bool isFacet = false,
        bool isSearchable = true,
        bool isDraft = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var now = clock.GetUtcNow();
        var definition = new AttributeDefinition
        {
            Code = Normalise(code),
            Label = label,
            Kind = kind,
            Unit = unit,
            IsVariantAxis = isVariantAxis,
            IsFacet = isFacet,
            IsSearchable = isSearchable,
            IsDraft = isDraft,
            CreatedAt = now,
            UpdatedAt = now
        };

        definition.Raise(new AttributeDefinitionChanged(definition.Code, now));
        return definition;
    }

    /// <summary>Añade o reemplaza una opción. Idempotente por código.</summary>
    public void AddOption(TimeProvider clock, string code, LocalizedText label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (Kind != AttributeKind.Option)
            throw new InvalidOperationException($"'{Code}' is {Kind}, so it has no options.");

        var normalised = Normalise(code);
        _options.RemoveAll(option => option.Code == normalised);
        _options.Add(new AttributeOption(normalised, label));
        Touch(clock);
    }

    public void AddAlias(TimeProvider clock, string alias)
    {
        var normalised = Normalise(alias);
        if (_aliases.Contains(normalised, StringComparer.OrdinalIgnoreCase))
            return;

        _aliases.Add(normalised);
        Touch(clock);
    }

    /// <summary>Si esta definición responde a la clave que manda un origen.</summary>
    public bool AnswersTo(string key)
    {
        var normalised = Normalise(key);
        return string.Equals(Code, normalised, StringComparison.OrdinalIgnoreCase)
            || _aliases.Contains(normalised, StringComparer.OrdinalIgnoreCase);
    }

    public void Relabel(TimeProvider clock, LocalizedText label)
    {
        Label = label;
        Touch(clock);
    }

    /// <summary>Sacarla de Draft: alguien ha mirado sus etiquetas.</summary>
    public void Approve(TimeProvider clock)
    {
        IsDraft = false;
        Touch(clock);
    }

    /// <summary>
    /// Busca la opción cuyo texto coincide con lo que manda un origen, EN
    /// CUALQUIER cultura. Un conector español manda "azul marino" y un feed de
    /// Google manda "navy blue"; los dos apuntan a <c>NAVY_BLUE</c>, y quien
    /// tiene que saberlo es el catálogo, no cada conector.
    /// </summary>
    public AttributeOption? ResolveOption(string incoming)
    {
        var trimmed = incoming.Trim();

        return _options.FirstOrDefault(option =>
                   string.Equals(option.Code, Normalise(trimmed), StringComparison.OrdinalIgnoreCase))
            ?? _options.FirstOrDefault(option =>
                   option.Label.Values.Values.Any(label =>
                       string.Equals(label, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Cómo se lee esto en una cultura: "azul marino" / "navy blue".</summary>
    public string LabelForOption(string optionCode, string culture) =>
        _options.FirstOrDefault(option =>
            string.Equals(option.Code, optionCode, StringComparison.OrdinalIgnoreCase))
            ?.Label.In(culture) ?? optionCode;

    private void Touch(TimeProvider clock)
    {
        UpdatedAt = clock.GetUtcNow();
        Raise(new AttributeDefinitionChanged(Code, UpdatedAt));
    }

    /// <summary>
    /// Mayúsculas y guiones bajos. Un código con espacios o acentos acaba
    /// escapado en una URL o en un nombre de campo del índice.
    /// </summary>
    public static string Normalise(string code) =>
        code.Trim().Replace(' ', '_').Replace('-', '_').ToUpperInvariant();
}
