using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The whole API, in process, without Postgres or Elasticsearch: neither the
/// DbContext nor the Elastic client connects on construction, so two fake strings
/// are enough to bring it up and ask it things that need no data — its OpenAPI
/// document, its policies, its development issuer.
/// </summary>
public sealed class TenderoApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The test server listens on no port, so the issuer signing the tokens is
    /// <c>http://localhost/dev-issuer</c>, with no port. The authority has to say
    /// exactly that or <c>ValidateIssuer</c> rejects perfectly valid tokens.
    /// </summary>
    private const string TestAuthority = "http://localhost/dev-issuer";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:tendero-db"] = "Host=localhost;Database=api-tests",
                ["ConnectionStrings:elasticsearch"] = "http://localhost:9200",
                ["Authentication:Authority"] = TestAuthority,
                ["Authentication:AllowHttpMetadata"] = "true"
            }));

        builder.UseEnvironment("Development");
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // JwtBearer downloads discovery and the JWKS over HTTP. There is no
            // HTTP here: there is an in-memory server. It is handed its internal
            // channel so that validation stays THE REAL ONE — signature, issuer,
            // audience and expiry — instead of being replaced by a double, which
            // is the usual trap and leaves the test with nothing to check.
            //
            // Configure and NOT PostConfigure: JwtBearer creates its own
            // ConfigurationManager in its post-configure, so a later one arrives
            // too late and the handler is silently ignored. The symptom was a 401
            // saying the issuer was invalid, with the correct issuer.
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = new LazyTestServerHandler(this));
        });
    }


    /// <summary>
    /// `factory.Server` builds the host, and asking for it WHILE that same host
    /// is being configured is re-entrant: the discovery download hung for four
    /// seconds and JwtBearer ended up with no metadata, so it rejected a
    /// perfectly valid token saying the issuer was invalid. It is resolved on the
    /// first send, when the host already exists.
    /// </summary>
    private sealed class LazyTestServerHandler(TenderoApiFactory factory) : DelegatingHandler
    {
        private HttpMessageInvoker? _inner;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _inner ??= new HttpMessageInvoker(factory.Server.CreateHandler());
            return _inner.SendAsync(request, cancellationToken);
        }
    }
}
