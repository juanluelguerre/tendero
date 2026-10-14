using System.Text.Json.Nodes;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// What the document says of the contract as a whole (phase 13b): its version,
/// the one way a caller identifies itself, and the shape every refusal has.
/// </summary>
internal sealed class ContractIsVersionedTransformer : IOpenApiDocumentTransformer
{
    public const string Bearer = "bearer";

    public const string Problem = "Problem";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Tendero";
        document.Info.Version = ContractVersion.Current;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Bearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "A token the shop's identity provider signed (OIDC): a person's, or an agent's. An operation lists the policies it asks of it.",
        };

        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas[Problem] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "A refusal (RFC 9457): a code to branch on, and the shop's sentence in the language asked.",
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri", Description = "Names the kind of problem." },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "The shop's sentence, in the language of Content-Language." },
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Description = "What this refusal is about, when there is more to say." },
                ["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "What a client branches on. The sentences may be reworded; this may not." },
                ["path"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "The part of the request the refusal is about, as a JSONPath, when it is about one." },
                ["reason"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "The domain's own sentence, in English: for a log, not for a screen." },
            },
            Required = new HashSet<string> { "type", "title", "status", "code" },
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// What an operation says of itself beyond its shapes: **who may call it**, and
/// the refusals that follow from that and from a limit. Read from the endpoint's
/// own metadata — the policies it requires, the limiter it has — so the document
/// cannot say public of an operation that is not.
/// </summary>
internal sealed class OperationSaysWhoMayCallItTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        operation.Responses ??= [];

        // The refusals the endpoint named (`Refuses`): each status a problem
        // whose code is one of those and no other, with what it carries beside.
        foreach (var byStatus in metadata.OfType<Refuses>().SelectMany(declared => declared.Refusals).GroupBy(refusal => refusal.Status).OrderBy(group => group.Key))
        {
            // What a refusal carries goes with its code: a status that is several
            // refusals carrying different things says each of them, and not all
            // of them of every one.
            List<(IReadOnlyList<Refusal> Refusals, IOpenApiSchema? Carried)> byWhatTheyCarry = [];
            foreach (var group in byStatus.GroupBy(refusal => refusal.Carries))
                byWhatTheyCarry.Add(([.. group], group.Key is { } type ? await context.GetOrCreateSchemaAsync(type, null, cancellationToken) : null));

            operation.Responses[byStatus.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                Refused([.. byStatus], context.Document, byWhatTheyCarry);
        }

        // And a status the endpoint answers with no body of its own is the
        // refusal that status is: what the status pages write (Problems).
        foreach (var (status, response) in operation.Responses.ToArray())
        {
            if (response.Content is { Count: > 0 } || !int.TryParse(status, out var number) || !CommonRefusals.ByStatus.TryGetValue(number, out var common))
                continue;

            operation.Responses[status] = Refused([common], context.Document, []);
        }

        // A validation problem is the same problem, with its map of fields.
        foreach (var (status, response) in operation.Responses.ToArray())
        {
            if (status != "400" || response.Content is null || !response.Content.TryGetValue(Problems.ContentType, out var media)
                || media.Schema is not OpenApiSchemaReference { Reference.Id: "HttpValidationProblemDetails" } fields)
                continue;

            operation.Responses[status] = Refused([CommonRefusals.Invalid], context.Document, [([CommonRefusals.Invalid], fields)]);
        }

        if (!metadata.OfType<IAllowAnonymous>().Any() && metadata.OfType<IAuthorizeData>().ToArray() is { Length: > 0 } required)
        {
            // OpenAPI 3.1 lets a scheme that has no scopes list the roles an
            // operation asks for: here, the policies' names.
            string[] policies = [.. required.Select(data => data.Policy).OfType<string>().Where(policy => policy.Length > 0).Order(StringComparer.Ordinal)];
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(ContractIsVersionedTransformer.Bearer, context.Document)] = [.. policies],
                },
            ];
            operation.Responses["401"] = Refused([CommonRefusals.Unauthenticated], context.Document, []);
            // A 403 the endpoint named refusals for keeps them: who may not is one more.
            operation.Responses["403"] = Refused(
                [CommonRefusals.Forbidden, .. metadata.OfType<Refuses>().SelectMany(declared => declared.Refusals).Where(refusal => refusal.Status == 403)],
                context.Document, []);
        }

        // A 201 says where what it made is (RFC 9110): the framework sets the
        // header and does not declare it.
        if (operation.Responses.TryGetValue("201", out var created) && created is OpenApiResponse made)
        {
            made.Headers ??= new Dictionary<string, IOpenApiHeader>();
            made.Headers["Location"] = new OpenApiHeader
            {
                Description = "The address of what was created.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" },
            };
        }

        if (metadata.OfType<EnableRateLimitingAttribute>().Any())
        {
            var limited = Refused([CommonRefusals.TooManyRequests], context.Document, []);
            limited.Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["Retry-After"] = new OpenApiHeader
                {
                    Description = "Seconds to wait before asking again.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                },
            };
            operation.Responses["429"] = limited;
        }

    }

    /// <summary>
    /// A status's response: the shared problem, the codes this status has here
    /// as a closed set, and what any of them carries as members beside.
    /// </summary>
    /// <summary>
    /// A status as the problem it is: the closed set of its codes, and what a
    /// refusal carries beside it. When every refusal of the status carries the
    /// same thing — or none carries anything — that is said of the status. When
    /// they differ, each is a branch: its codes, and what those carry.
    /// </summary>
    private static OpenApiResponse Refused(
        IReadOnlyList<Refusal> refusals, OpenApiDocument? document,
        IReadOnlyList<(IReadOnlyList<Refusal> Refusals, IOpenApiSchema? Carried)> byWhatTheyCarry)
    {
        var alike = byWhatTheyCarry.Count <= 1;

        return new()
        {
            Description = string.Join(" · ", refusals.Select(refusal => refusal.Title.In("en")).Distinct()),
            Content = new Dictionary<string, IOpenApiMediaType>
            {
                [Problems.ContentType] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        AllOf =
                        [
                            new OpenApiSchemaReference(ContractIsVersionedTransformer.Problem, document),
                            .. alike ? byWhatTheyCarry.Select(group => group.Carried).OfType<IOpenApiSchema>() : [],
                        ],
                        Properties = new Dictionary<string, IOpenApiSchema> { ["code"] = Codes(refusals) },
                        AnyOf = alike
                            ? null
                            :
                            [
                                .. byWhatTheyCarry.Select(group => (IOpenApiSchema)new OpenApiSchema
                                {
                                    AllOf = group.Carried is null ? null : [group.Carried],
                                    Properties = new Dictionary<string, IOpenApiSchema> { ["code"] = Codes(group.Refusals) },
                                    Required = new HashSet<string> { "code" },
                                }),
                            ],
                    },
                },
            },
        };
    }

    private static OpenApiSchema Codes(IEnumerable<Refusal> refusals) => new()
    {
        Type = JsonSchemaType.String,
        Enum = [.. refusals.Select(refusal => refusal.Code).Distinct().Order(StringComparer.Ordinal).Select(code => (JsonNode)JsonValue.Create(code))],
    };
}
