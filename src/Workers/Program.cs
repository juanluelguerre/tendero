using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Todo el cableado vive en AddTenderoWorker, y no por estética: mientras estuvo
// aquí suelto, nada podía comprobar que el contenedor se pudiera construir. Un
// test lo llama ahora (WorkerContainerTests).
builder.AddTenderoWorker();

var host = builder.Build();
host.Run();
