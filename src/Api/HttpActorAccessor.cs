using ElGuerre.Tendero.Mcp;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Catalog;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// The request's <see cref="Actor"/>: the surface from the route it arrived on,
/// and the kind from the surface and the principal.
///
/// MCP and UCP are agent surfaces by definition, and both are anonymous by
/// decision, so the principal alone would count every external agent as a
/// person; on the web, an authenticated agent (an <c>agent_id</c> claim) is an
/// agent and everybody else is a person. The route is the server's own fact —
/// no header a client sends can move a request from one column to another.
/// </summary>
internal sealed class HttpActorAccessor(IHttpContextAccessor accessor, IPrincipalAccessor principals) : IActorAccessor
{
    public Actor Current => field ??= Resolve();

    private Actor Resolve()
    {
        if (accessor.HttpContext is not { } http)
            return Actor.System;

        var path = http.Request.Path;
        var surface =
            path.StartsWithSegments(TenderoMcpServer.Path, StringComparison.OrdinalIgnoreCase) ? Surfaces.Mcp
            : path.StartsWithSegments(UcpCatalogRestBinding.BasePath, StringComparison.OrdinalIgnoreCase) ? Surfaces.Ucp
            : Surfaces.Web;

        var kind = surface != Surfaces.Web || principals.Current.IsAgent ? ActorKinds.Agent : ActorKinds.Human;

        return new Actor(kind, surface);
    }
}
