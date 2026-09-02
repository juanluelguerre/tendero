namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Identificador de un agente. Es un tipo aparte de <see cref="CustomerId"/> a
/// propósito: **un agente es un principal, no un cliente**. Cuando un agente
/// compra en nombre de alguien, hay dos identidades en juego y colapsarlas en
/// una hace imposible responder la única pregunta que importa después —
/// ¿quién actuó, y por cuenta de quién?
/// </summary>
public readonly record struct AgentId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Quién está actuando. Un cliente siempre; un agente cuando lo hay; y el
/// mandato que lo autoriza cuando exista (fase 11, AP2).
/// </summary>
public sealed record CommercePrincipal(
    CustomerId? Customer,
    AgentId? Agent,
    string? Subject,
    IReadOnlyCollection<string> Roles)
{
    /// <summary>Nadie autenticado. Es un valor, no null: un invitado navegando
    /// es un caso normal, no la ausencia de un caso.</summary>
    public static readonly CommercePrincipal Anonymous = new(null, null, null, []);

    public bool IsAgent => Agent is not null;

    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// De dónde sale el principal actual.
///
/// Es un puerto y no una lectura de <c>HttpContext</c> porque **tiene que
/// funcionar donde no hay HTTP**: sobre MCP, y dentro del worker que drena el
/// outbox, donde el actor es el sistema. Esa restricción es la que lo hace
/// innegociable; con un solo consumidor HTTP habría bastado el contexto.
/// </summary>
public interface IPrincipalAccessor
{
    CommercePrincipal Current { get; }
}

/// <summary>
/// El actor cuando no hay nadie: procesos de fondo. Se registra explícitamente
/// en vez de dejar que <c>Current</c> devuelva null, para que un handler nunca
/// tenga que preguntarse si "sin principal" significa invitado o error.
/// </summary>
public sealed class SystemPrincipalAccessor : IPrincipalAccessor
{
    public CommercePrincipal Current { get; } = CommercePrincipal.Anonymous with { Subject = "system" };
}
