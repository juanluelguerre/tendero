using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// All the wiring lives in AddTenderoWorker, and not for tidiness: while it sat
// loose here, nothing could check that the container could be built. A test
// calls it now (WorkerContainerTests).
builder.AddTenderoWorker();

var host = builder.Build();
host.Run();
