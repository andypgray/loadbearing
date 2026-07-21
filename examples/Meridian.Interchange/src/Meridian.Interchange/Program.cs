using Meridian.Interchange.Host;

// Composition root for the interchange worker: the host builds the container, the AddInterchange
// extension wires every service, and the hosted dispatcher runs until shutdown. Program itself
// constructs nothing and resolves nothing.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInterchange(builder.Configuration);

builder.Build().Run();