using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using NapoleonBot.Bot;
using NapoleonBot.Data;
using NapoleonBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.Configure<ScheduleOptions>(builder.Configuration.GetSection(ScheduleOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => ScoreStore.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

// Bot Framework plumbing: authentication from MicrosoftApp* settings, one adapter shared by HTTP and the scheduler.
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();
builder.Services.AddSingleton<AdapterWithErrorHandler>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter>(sp => sp.GetRequiredService<AdapterWithErrorHandler>());
builder.Services.AddSingleton<CloudAdapter>(sp => sp.GetRequiredService<AdapterWithErrorHandler>());

builder.Services.AddSingleton<ScoringCardPublisher>();
builder.Services.AddTransient<IBot, NapoleonTeamsBot>();
builder.Services.AddHostedService<FridayScheduler>();

var app = builder.Build();

app.MapGet("/", () => "🍰 Napoleonskake bot is running.");
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapPost("/api/messages", (HttpRequest request, HttpResponse response, IBotFrameworkHttpAdapter adapter, IBot bot, CancellationToken ct) =>
    adapter.ProcessAsync(request, response, bot, ct));

app.Run();
