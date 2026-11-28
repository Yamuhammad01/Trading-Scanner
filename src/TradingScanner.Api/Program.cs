using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingScanner.Api.Hubs;
using TradingScanner.Api.Services;
using TradingScanner.Infrastructure.MarketScanning;
using TradingScanner.Infrastructure.MarketScanning.Processing;
using TradingScanner.Infrastructure.TrendDetection;
using TradingScanner.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON configuration sources & environment variables
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

// Register shared infrastructure
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<CandleValidator>();
builder.Services.AddTransient<HistoricalCandleLoader>();

// Register market data provider based on configuration ("GateIo" / "Bybit")
builder.Services.AddMarketDataProviderFromConfiguration(builder.Configuration);

// Register rolling slope trend detection
builder.Services.AddRollingSlopeTrendDetection(builder.Configuration);

// Register Telegram notifications & deduplication if credentials available
var botToken = builder.Configuration["Telegram:BotToken"]
    ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
var chatId = builder.Configuration["Telegram:ChatId"]
    ?? Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

if (!string.IsNullOrWhiteSpace(botToken) && !string.IsNullOrWhiteSpace(chatId))
{
    var telegramOptions = new TradingScanner.Domain.Notifications.TelegramOptions
    {
        BotToken = botToken,
        ChatId = chatId
    };
    builder.Services.AddSingleton(telegramOptions);
    builder.Services.AddHttpClient<TradingScanner.Infrastructure.MarketScanning.Notifications.TrendNotificationService>();

    var alertsPath = Path.Combine(AppContext.BaseDirectory, "Data", "sent-alerts.json");
    builder.Services.AddSingleton(new TradingScanner.Domain.Notifications.AlertDeduplicationService(alertsPath));
    builder.Services.AddSingleton<TradingScanner.Domain.Notifications.TrendAlertCoordinator>();
}

// Register autonomous multi-market scanning infrastructure & hosted service
builder.Services.AddMarketScanning(builder.Configuration);

// Register application services
builder.Services.AddScoped<MarketDataQueryService>();

// Register ScannerEventBroadcaster as a background service
builder.Services.AddHostedService<ScannerEventBroadcaster>();

// Add ASP.NET Core controllers and SignalR
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Add CORS for frontend dashboard
builder.Services.AddCors(options =>
{
    options.AddPolicy("DashboardCors", policy =>
    {
        policy
            .WithOrigins(
                builder.Configuration["CORS:AllowedOrigins"] ?? "http://localhost:3000",
                "http://localhost:3001")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Trading Scanner API",
        Version = "v1",
        Description = "REST API for the Autonomous Multi-Market Crypto Trend Scanner dashboard."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Trading Scanner API V1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseCors("DashboardCors");
app.MapControllers();
app.MapHub<ScannerHub>("/hubs/scanner");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

// Connect ClosedCandleProcessor.TrendUpdates to ScannerEventBroadcaster for SignalR broadcasting
var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
using (var scope = scopeFactory.CreateScope())
{
    var processor = scope.ServiceProvider.GetRequiredService<ClosedCandleProcessor>();
    var broadcaster = scope.ServiceProvider.GetRequiredService<ScannerEventBroadcaster>();

    _ = Task.Run(async () =>
    {
        await foreach (var evt in processor.TrendUpdates.ReadAllAsync())
        {
            await broadcaster.Writer.WriteAsync(evt);
        }
    }, CancellationToken.None);
}

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=================================================");
logger.LogInformation("  TRADING SCANNER API + SCANNER HOST");
logger.LogInformation("=================================================");
logger.LogInformation("Host environment initialized. Starting background workers and API...");

await app.RunAsync();