using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.TrafficSim;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

// A shop with no customers, given some (ADR 0037).
//
//   dotnet run --project tools/TrafficSim -- --TrafficSim:Seed=42 --TrafficSim:Sessions=20
//
// or from the Aspire dashboard, where it is an explicit-start resource that knows
// the storefront's and the API's addresses. The browsers are Playwright's own
// Chromium, installed on first run.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<SimulationOptions>()
    .Bind(builder.Configuration.GetSection(SimulationOptions.SectionName))
    .PostConfigure(options =>
    {
        // Aspire hands a referenced resource's address in as service discovery
        // configuration; an explicit TrafficSim:Api or :Storefront still wins.
        if (builder.Configuration["TrafficSim:Api"] is null && builder.Configuration["services:api:http:0"] is { } api)
            options.Api = new Uri(api);
        if (builder.Configuration["TrafficSim:Storefront"] is null && builder.Configuration["services:storefront:http:0"] is { } storefront)
            options.Storefront = new Uri(storefront);
        if (builder.Configuration["TrafficSim:Backoffice"] is null && builder.Configuration["services:backoffice:http:0"] is { } backoffice)
            options.Backoffice = new Uri(backoffice);
    });

builder.Services.AddHttpClient("api");
builder.Services.AddSingleton<Simulation>();
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(Simulation.SourceName));

using var host = builder.Build();
await host.StartAsync();

var installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
if (installed != 0)
    throw new InvalidOperationException($"Playwright could not install Chromium (exit code {installed}).");

var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
await host.Services.GetRequiredService<Simulation>().RunAsync(stopping);
await host.StopAsync();
