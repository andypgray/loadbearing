using Meridian.Quoting.Application.Abstractions;
using Meridian.Quoting.Application.Handlers;
using Meridian.Quoting.Application.Messages;
using Meridian.Quoting.Domain;
using Meridian.Quoting.Infrastructure.Persistence;
using Meridian.Quoting.Infrastructure.Time;

// Composition root: the one place that wires the four layers together, so it sits in the global
// namespace rather than inside any of them.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<InMemoryDatabase>();

builder.Services.AddScoped<IQuoteRepository, InMemoryQuoteRepository>();
builder.Services.AddScoped<IRateCardRepository, InMemoryRateCardRepository>();
builder.Services.AddScoped<IUnitOfWork, InMemoryUnitOfWork>();
builder.Services.AddScoped<ICommandBus, CommandBus>();
builder.Services.AddScoped<ICommandHandler<RequestQuoteCommand>, RequestQuoteHandler>();
builder.Services.AddScoped<IQueryHandler<GetQuoteQuery, QuoteView?>, GetQuoteHandler>();

WebApplication app = builder.Build();
app.MapControllers();
app.Run();