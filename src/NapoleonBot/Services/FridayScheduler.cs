using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Options;
using NapoleonBot.Data;
using Newtonsoft.Json;

namespace NapoleonBot.Services;

/// <summary>
/// Once a minute, checks the clock in the configured time zone. On cake day it
///  1. posts the scoring card to every known conversation once scoring opens, and
///  2. locks the card and posts the final results once scoring closes.
/// Both steps are idempotent, so a restart mid-Friday does not repost.
/// </summary>
public sealed class FridayScheduler(
    CloudAdapter adapter,
    ScoreStore store,
    ScoringCardPublisher publisher,
    IOptions<ScheduleOptions> options,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<FridayScheduler> logger) : BackgroundService
{
    private readonly ScheduleOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Automatic Friday posting is disabled");
            return;
        }

        logger.LogInformation("Scoring opens every {Day} at {Opens} and closes at {Closes} ({TimeZone})",
            _options.CakeDay, _options.PostTime, _options.ScoringCloses, _options.TimeZone);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        var now = _options.LocalNow(clock);
        var state = ScoringWindow.Evaluate(now, _options);
        var cakeDate = DateOnly.FromDateTime(now);

        switch (state)
        {
            case ScoringState.Open:
                await PostCardsAsync(cakeDate, ct);
                break;
            case ScoringState.Closed:
                await CloseCardsAsync(cakeDate, ct);
                break;
        }
    }

    private async Task PostCardsAsync(DateOnly cakeDate, CancellationToken ct)
    {
        foreach (var (conversationId, referenceJson) in await store.GetConversationsAsync(ct))
        {
            if (await store.GetPostedCardAsync(cakeDate, conversationId, ct) is not null)
                continue;

            await ContinueAsync(conversationId, referenceJson,
                (turnContext, innerCt) => publisher.PostAsync(turnContext, cakeDate, isOpen: true, _options.ScoringCloses, innerCt),
                $"post scoring card for {cakeDate}", ct);
        }
    }

    private async Task CloseCardsAsync(DateOnly cakeDate, CancellationToken ct)
    {
        var references = (await store.GetConversationsAsync(ct)).ToDictionary(c => c.ConversationId, c => c.ReferenceJson);

        foreach (var posted in await store.GetOpenPostedCardsAsync(cakeDate, ct))
        {
            if (!references.TryGetValue(posted.ConversationId, out var referenceJson))
            {
                logger.LogWarning("No conversation reference for {Conversation}; cannot close its card", posted.ConversationId);
                continue;
            }

            await ContinueAsync(posted.ConversationId, referenceJson,
                (turnContext, innerCt) => publisher.CloseAsync(turnContext, posted, _options.PostResultsAtClose, _options.ScoringCloses, innerCt),
                $"close scoring for {cakeDate}", ct);
        }
    }

    private async Task ContinueAsync(string conversationId, string referenceJson, Func<Microsoft.Bot.Builder.ITurnContext, CancellationToken, Task> action, string what, CancellationToken ct)
    {
        var reference = JsonConvert.DeserializeObject<ConversationReference>(referenceJson);
        if (reference is null)
        {
            logger.LogWarning("Stored conversation reference for {Conversation} is unreadable", conversationId);
            return;
        }

        try
        {
            await adapter.ContinueConversationAsync(configuration["MicrosoftAppId"] ?? string.Empty, reference, action.Invoke, ct);
            logger.LogInformation("Did {What} in {Conversation}", what, conversationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not {What} in {Conversation}", what, conversationId);
        }
    }
}
