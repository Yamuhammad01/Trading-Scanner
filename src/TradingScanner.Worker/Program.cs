using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.Notifications;
using TradingScanner.Infrastructure.MarketScanning;
using TradingScanner.Infrastructure.Services;
using TradingScanner.Infrastructure.TrendDetection;

var builder = Host.CreateApplicationBuilder(args);

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
    var telegramOptions = new TelegramOptions
    {
        BotToken = botToken,
        ChatId = chatId
    };
    builder.Services.AddSingleton(telegramOptions);
    builder.Services.AddHttpClient<TelegramNotificationService>();

    var alertsPath = Path.Combine(AppContext.BaseDirectory, "Data", "sent-alerts.json");
    builder.Services.AddSingleton(new AlertDeduplicationService(alertsPath));
    builder.Services.AddSingleton<TrendAlertCoordinator>();
}

// Register autonomous multi-market scanning infrastructure & hosted service
builder.Services.AddMarketScanning(builder.Configuration);

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=================================================");
logger.LogInformation("  AUTONOMOUS MULTI-MARKET CRYPTO TREND SCANNER   ");
logger.LogInformation("=================================================");
logger.LogInformation("Host environment initialized. Starting background workers...");

await app.RunAsync();
