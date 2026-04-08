using PlexTmdbSync.ApiHost;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

ApiHostModule.LoadEnvFile(".env");

var builder = WebApplication.CreateBuilder(args);
var allowedWebClientOrigins = ApiHostModule.GetConfiguredWebClientOrigins();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateLogger();

builder.Host.UseSerilog();
ApiHostModule.ConfigureApiServices(builder, allowedWebClientOrigins, enableCors: true);

var app = builder.Build();
ApiHostModule.ConfigureApiPipeline(app, enableCors: true, serveWebClient: true);
ApiHostModule.MapApiEndpoints(app);

app.Run();
