using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// La API entera, en proceso, sin Postgres ni Elasticsearch: ni el DbContext ni
/// el cliente de Elastic conectan al construirse, así que dos cadenas falsas
/// bastan para levantarla y preguntarle cosas que no requieren datos — su
/// documento de OpenAPI, sus políticas, su emisor de desarrollo.
/// </summary>
public sealed class TenderoApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// El servidor de test no escucha en ningún puerto, así que el emisor que
    /// firma los tokens es <c>http://localhost/dev-issuer</c>, sin puerto. La
    /// autoridad tiene que decir exactamente eso o <c>ValidateIssuer</c> rechaza
    /// tokens perfectamente válidos.
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
            // JwtBearer descarga el discovery y el JWKS por HTTP. Aquí no hay
            // HTTP: hay un servidor en memoria. Se le da su canal interno para
            // que la validación siga siendo LA DE VERDAD — firma, emisor,
            // audiencia y caducidad — en vez de sustituirla por un doble, que
            // es la trampa habitual y deja el test sin nada que comprobar.
            //
            // Configure y NO PostConfigure: el propio JwtBearer crea su
            // ConfigurationManager en su post-configure, así que uno posterior
            // llega tarde y el handler se ignora en silencio. El síntoma era un
            // 401 diciendo que el emisor no valía, con el emisor correcto.
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = new LazyTestServerHandler(this));
        });
    }


    /// <summary>
    /// `factory.Server` construye el host, y pedirlo MIENTRAS se está
    /// configurando ese mismo host es reentrante: la descarga del discovery se
    /// quedaba colgada cuatro segundos y JwtBearer acababa sin metadatos, con lo
    /// que rechazaba un token perfectamente válido diciendo que el emisor no
    /// valía. Se resuelve en el primer envío, cuando el host ya existe.
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
