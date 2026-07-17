using Meridian.Clearance;
using Meridian.Domain;
using Meridian.Web.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IClearanceGateway, ClearanceGateway>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddScoped<IRateCardRepository, RateCardRepository>();

WebApplication app = builder.Build();
app.MapControllers();
app.Run();