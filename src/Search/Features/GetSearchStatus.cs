using Carter;
using ElGuerre.Tendero.Search.Health;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Search.Features;

/// <summary>
/// Search's status for the dashboard (R-9, D-7): each stage's state and since
/// when, from the memory the health checks keep. The shopkeeper's, never the open
/// <c>/health</c>'s — it says which of the shop's systems is down.
/// </summary>
public sealed class GetSearchStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/search-status", Ok<SearchStatus> (SearchStatusMemory memory) => TypedResults.Ok(memory.Current()))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Dashboard")
            .WithName("GetSearchStatus");
    }
}
