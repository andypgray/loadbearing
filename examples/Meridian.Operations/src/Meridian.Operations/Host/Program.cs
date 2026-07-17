using Meridian.Operations.Host;

// Composition root for the modular monolith: one web app whose module boundaries are drawn by
// namespace rather than by project. The service wiring lives in OperationsBootstrap and the
// endpoints in OperationsEndpoints; both sit in the Host module.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

OperationsBootstrap.ConfigureServices(builder.Services);

WebApplication app = builder.Build();

OperationsEndpoints.Map(app);

app.Run();