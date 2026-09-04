# ADR 0017 — The identity provider is a port; the first adapter was a development issuer

**Status:** accepted · 2026-09-04

## Context

Phase 0 needed authentication before there was anything to authenticate. The
tempting move is to defer it — "add auth later" — and the cost of that is not the
work, it is that every screen, every policy and every handler gets written
against a system with no principal in it, and then all of them change at once.

The other tempting move is to write an authorization server. `initial-plan.md`
rules that out explicitly: OpenIddict and ASP.NET Identity stay out, optional and
future. Writing one is a project, and it is not this project.

So phase 0 took a third path, and this ADR records both the decision and the
evidence that it worked, because the two arrived a phase apart.

## Decision

**The identity provider is an external system reached through a port, and the
contract is not an interface of ours — it is OIDC.** A discovery document, a
JWKS endpoint, and signed JWTs.

That makes it ADR 0003 applied to something we do not own, and it splits the work
in two halves with very different lifespans:

| half | phase 0 | phase 7 |
|---|---|---|
| **Who signs** | `src/DevIssuer`, in-process, seeded identities, refuses to start outside Development | Keycloak 26.6, in a container, realm imported from a committed file |
| **Who validates** | `AddJwtBearer` — signature, issuer, audience, expiry, `ClockSkew = 0` | **unchanged** |

The client half is production code from the first day and never changes. What
was faked is only *who signs the token*, and swapping the signer is
configuration.

**Both issuers stay.** The development one is what lets a fresh clone get a token
without pulling a container; Keycloak is what proves the abstraction was real.
`IdentityProviderContractTests` is abstract and both inherit it.

## What the swap actually cost

Two environment variables in the AppHost:

```csharp
.WithEnvironment("Authentication__Authority", $"{keycloak.GetEndpoint("http")}/realms/tendero")
.WithEnvironment("Authentication__AllowHttpMetadata", "true")
```

`Program.cs` defaults the authority to its own issuer with `??=`, so setting it
is the whole swap. **No code path changed, no second scheme, no branch.** Verified
against a running Keycloak: a shopkeeper's token opens a shopkeeper endpoint
(200), an agent's token is refused on it (403), no token is 401, and the public
catalogue is still public (200).

**Phase 0 did not leak.** That is the finding, and it was worth measuring rather
than assuming.

## What DID leak, and it was the test suite

The roadmap promised Keycloak would inherit the contract suite "without changing
a line". It did not, and the reason is the more interesting half of this ADR:

> **A contract suite with one implementation has not been tested as a contract.**

The suite encoded its only adapter's shape in three places, none of them
obviously wrong until something external inherited it:

1. **A relative path.** `IssuerPath` was `/dev-issuer`, resolved against the API's
   own test server. A container has no such path.
2. **The test server's channel.** The in-memory backchannel is what makes
   validation real for an in-process issuer, and is exactly wrong for one that
   must be reached over HTTP.
3. **A hard-coded JWKS location.** `/.well-known/jwks.json` is where the
   development issuer serves keys; Keycloak serves `/protocol/openid-connect/certs`.
   The test now **follows the `jwks_uri` the discovery document advertises**,
   which is both more general and what `JwtBearer` actually does — so it
   exercises the runtime's path instead of a parallel one that happened to agree.

The third change made the contract truer, not merely broader. That is the shape
of a good correction.

## The realm carries the awkwardness, not the API

Two things had to be right in `keycloak/realms/tendero-realm.json`, and both were
found by running it rather than by reading documentation. In both cases the token
validated perfectly and every policy denied — the worst shape a failure can take.

**Keycloak treats `.` in a mapper's `claim.name` as a nesting separator.** The
API reads roles from `ClaimTypes.Role`, which is
`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`, and Keycloak
built a nested object rooted at `http://schemas`. Each dot is escaped in the
realm now. The awkwardness belongs there because it is Keycloak's syntax, not our
contract — the realm adapts to the API, which is what having a port means.

**Declaring `clientScopes` at realm level REPLACES Keycloak's built-in set.**
`profile`, `email` and `roles` stopped existing, so tokens carried no
`preferred_username` and no `realm_access` at all. The mappers live on the
clients now.

## The swap was configuration; the FLOW was not, and that was the real gap

Phase 7 declared victory on two environment variables, and the demo was "same
login screen, same policies, same tests". The first half of that sentence was the
problem.

The screen was an identity picker posting a **`password` grant**. That put two
things in the wrong place at once: the shop knew who existed, and the shop
handled a credential. Delegating identity is supposed to remove exactly those,
and OAuth 2.1 drops the grant entirely. It also meant the swap was invisible —
point the API at Keycloak and nothing a person could see was different, which is
a poor way to demonstrate that anything was delegated.

**Both issuers do Authorization Code with PKCE now**, and the consequence worth
recording is what it cost each side:

| | before | after |
|---|---|---|
| The shop | rendered a picker, posted a password | one button that leaves |
| Keycloak | signed a token | renders its own login form |
| The development issuer | minted tokens for names | `/connect/authorize`, single-use codes, S256 |

The development issuer growing an authorization endpoint is worth defending,
because `AddDevIssuer` names its own retirement condition — "the day it grows a
user registry or a password change". This is neither. The authorization endpoint
IS the OIDC contract this port is defined by, and a fake that could only do the
easy half was standing in for something easier than Keycloak rather than for
Keycloak. Its login page is three buttons and no password field, which is the
line that keeps the guard meaningful.

**The contract suite grew the assertion that would have caught this.** It now
demands that an issuer advertise `authorization_endpoint`, `code`, `S256` and
`authorization_code`, and that the authorization endpoint render a page. Every
test in that suite passed while the shop was posting passwords, because the suite
only ever asked whether a token could be obtained — which is the same shape of
mistake as the JWKS path below, found the same way, one adapter later.

PKCE is verified rather than advertised: a verifier that does not match, a code
spent twice, and a code redeemed against a different redirect URI are all
refused, and the first of those was confirmed by deleting the check and watching
the test go red.

## What the redirect broke, and what it proved

Three defects, and only the first was a consequence of the change.

**The token response was camelCase.** The API serialises camelCase and
`DevTokenResponse` inherited it, so the development issuer answered `accessToken`
where RFC 6749 says `access_token`. Nothing failed: the client found no token,
stayed signed out, and the route guard sent the person back to the door on every
attempt. It survived because both halves agreed with each other and with nobody
else — the shop read the name we wrote, and the contract test read it too. The
Keycloak half of that same suite reads `access_token` two hundred lines further
down, and that divergence was the clue nobody followed. **A test written against
its own adapter's field names is describing a wire format, not testing one.**

**Signing in from the door returned you to the door.** The path travels in the
OAuth `state` so that a shopper comes back to the page they were on, and the
sign-in screen's own path is `/sign-in`. The guard now says where it intercepted
somebody and the door passes it on.

**The end-to-end helper waited for the wrong thing.** `not.toHaveURL(/sign-in/)`
is satisfied the instant the browser leaves that route — which, in a redirect
flow, is while it is still on the ISSUER's domain with no token yet. It was
intermittent against the development issuer, whose two hops are fast, and
reliable against Keycloak, whose form makes the window wide. It waits for the
sign-out button now, which only exists once a token does.

**The guarantee is that the same six specs pass against both issuers.** The
helper branches once, on the URL, because the middle page differs: Keycloak wants
a password typed and the development issuer wants a name pressed. Three
consecutive green runs each way.

## Consequences

**An agent is a principal, not a customer, and Keycloak is where that stops being
a comment.** `claude-desktop` authenticates as itself with `client_credentials`,
holds the `agent` role, and carries a hardcoded `agent_id` claim — which is
exactly what `CommercePrincipal` reads to tell an agent from a person.

**Token Exchange is not done.** It is what lets an agent act *on behalf of*
somebody, carrying both `sub` and `agent_id`, and it is the half a fake issuer
could not honestly provide. It belongs with the mandates in phase 11, where there
is something for it to authorise.

**The contract test does not start Keycloak.** It takes
`TENDERO_KEYCLOAK_AUTHORITY` and skips with a reason when there is none — the
same call `tools/SearchEval` makes about Elasticsearch and the Playwright configs
make about the stack. Starting infrastructure from a test builds a second, worse
AppHost, and this repository has now declined to do that three times.

**The realm file is the "clone and run" promise.** Users, roles and clients exist
on first start and nobody clicks through an admin console. It is hand-written
rather than exported, against the roadmap's advice — and what makes that safe is
not care, it is that the contract suite passes against it. A realm that drifts
fails six tests.

**The integration passed ADR 0006's check where Elasticsearch's failed.**
`Aspire.Hosting.Keycloak` 13.5.3-preview.1.26425.3 declares MIT in its nuspec and
depends on exactly the `Aspire.Hosting 13.5.3` the AppHost already resolves. One
trap worth recording: every one of its 46 published versions is a preview, so
`dotnet package search` returns "No results found", which reads exactly like a
package that does not exist.

## Related

- ADR 0003 — ports with keyed adapters and a shared contract suite. This is that
  pattern applied to a system we do not own.
- ADR 0006 — the dependency baseline, and the licence check this integration had
  to pass.
- ADR 0027 — the audit log, which records the principal this ADR establishes.
