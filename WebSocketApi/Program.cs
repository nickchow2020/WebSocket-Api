using WebSocketApi.Configuration;
using WebSocketApi.Handlers;
using WebSocketApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration settings
builder.Services.Configure<WebSocketSettings>(
    builder.Configuration.GetSection(WebSocketSettings.SectionName));
builder.Services.Configure<CorsSettings>(
    builder.Configuration.GetSection(CorsSettings.SectionName));

// Register services
builder.Services.AddSingleton<WebSocketConnectionManager>();
builder.Services.AddSingleton<DashboardDataService>();
builder.Services.AddScoped<WebSocketHandler>();

// Configure CORS from settings
const string CorsPolicy = "FrontendOrigins";
var corsSettings = builder.Configuration
    .GetSection(CorsSettings.SectionName)
    .Get<CorsSettings>() ?? new CorsSettings();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, p =>
    {
        var origins = corsSettings.AllowedOrigins.Length > 0
            ? corsSettings.AllowedOrigins
            : new[] { "http://localhost:3000" }; // Fallback default

        p.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure middleware pipeline
app.UseCors(CorsPolicy);

// Configure WebSocket options from settings
var wsSettings = app.Configuration
    .GetSection(WebSocketSettings.SectionName)
    .Get<WebSocketSettings>() ?? new WebSocketSettings();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(wsSettings.KeepAliveIntervalSeconds)
});

// Health endpoint
app.MapHealthChecks("/health");

// WebSocket endpoint with proper error handling
app.Map("/ws", async (HttpContext context, WebSocketHandler handler) =>
{
    await handler.HandleWebSocketAsync(context);
});

// Graceful shutdown - close all WebSocket connections
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(async () =>
{
    var connectionManager = app.Services.GetRequiredService<WebSocketConnectionManager>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Application stopping, closing all WebSocket connections...");
    await connectionManager.CloseAllConnectionsAsync();
});

app.Run();
