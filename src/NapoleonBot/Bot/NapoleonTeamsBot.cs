using System.Globalization;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Options;
using NapoleonBot.Cards;
using NapoleonBot.Data;
using NapoleonBot.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NapoleonBot.Bot;

public sealed class NapoleonTeamsBot(
    ScoreStore store,
    ScoringCardPublisher publisher,
    IOptions<ScheduleOptions> schedule,
    TimeProvider clock,
    ILogger<NapoleonTeamsBot> logger) : TeamsActivityHandler
{
    private readonly ScheduleOptions _schedule = schedule.Value;

    // ---- Incoming messages ---------------------------------------------------------------

    protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken ct)
    {
        await RememberConversationAsync(turnContext, ct);

        if (turnContext.Activity.Value is JObject submit && submit["action"] is not null)
        {
            await HandleCardSubmitAsync(turnContext, submit, ct);
            return;
        }

        var text = turnContext.Activity.RemoveRecipientMention();
        var command = CommandParser.Parse(text);
        logger.LogInformation("Command {Command} from {User}", command.GetType().Name, turnContext.Activity.From?.Name);

        switch (command)
        {
            case ScoreCommand score:
                await RecordScoreAsync(turnContext, score.Score, score.Comment, cardDate: null, ct);
                break;

            case InvalidScoreCommand invalid:
                var raw = string.IsNullOrEmpty(invalid.Raw) ? "nothing" : $"\"{invalid.Raw}\"";
                await turnContext.SendActivityAsync($"{raw} is not a score I recognise. Give me a whole number from 1 to 10, e.g. `score 8`.", cancellationToken: ct);
                break;

            case ResultsCommand:
                await SendResultsAsync(turnContext, MostRecentCakeDate(), ct);
                break;

            case HistoryCommand:
                await turnContext.SendActivityAsync(
                    MessageFactory.Attachment(CardFactory.HistoryCard(await store.GetHistoryAsync(8, ct), await store.GetAllTimeAsync(ct))), ct);
                break;

            case LeaderboardCommand:
                await turnContext.SendActivityAsync(
                    MessageFactory.Attachment(CardFactory.LeaderboardCard(await store.GetScorerStatsAsync(ct), await store.GetAllTimeAsync(ct))), ct);
                break;

            case PostCardCommand:
                var isOpen = ScoringWindow.Evaluate(_schedule.LocalNow(clock), _schedule) == ScoringState.Open;
                await publisher.PostAsync(turnContext, MostRecentCakeDate(), isOpen, _schedule.ScoringCloses, ct);
                break;

            default:
                await turnContext.SendActivityAsync(MessageFactory.Text(CardFactory.HelpText(_schedule)), ct);
                break;
        }
    }

    private async Task HandleCardSubmitAsync(ITurnContext<IMessageActivity> turnContext, JObject submit, CancellationToken ct)
    {
        var cardDate = TryParseDate(submit["cakeDate"]?.ToString());

        switch (submit["action"]?.ToString())
        {
            case "score":
                var scoreText = submit["score"]?.ToString();
                if (!int.TryParse(scoreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) || score is < 1 or > 10)
                {
                    await turnContext.SendActivityAsync("Pick a score from the list first 🍰", cancellationToken: ct);
                    return;
                }
                var comment = submit["comment"]?.ToString();
                await RecordScoreAsync(turnContext, score, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), cardDate, ct);
                break;

            case "results":
                await SendResultsAsync(turnContext, cardDate ?? MostRecentCakeDate(), ct);
                break;

            default:
                logger.LogWarning("Unknown card action {Action}", submit["action"]);
                break;
        }
    }

    /// <summary>Records a score for today, but only while the scoring window is open.</summary>
    private async Task RecordScoreAsync(ITurnContext turnContext, int score, string? comment, DateOnly? cardDate, CancellationToken ct)
    {
        var now = _schedule.LocalNow(clock);
        var state = ScoringWindow.Evaluate(now, _schedule);
        if (state != ScoringState.Open)
        {
            await turnContext.SendActivityAsync(MessageFactory.Text(ScoringWindow.RejectionMessage(state, now, _schedule)), ct);
            return;
        }

        var cakeDate = DateOnly.FromDateTime(now);
        if (cardDate is { } fromCard && fromCard != cakeDate)
        {
            // Someone found last week's card and clicked it.
            await turnContext.SendActivityAsync(
                $"That card is from {CardFactory.FormatDate(fromCard)} and scoring for it has closed. Use today's card or type `score {score}`.", cancellationToken: ct);
            return;
        }

        var from = turnContext.Activity.From;
        var entry = new ScoreEntry(
            cakeDate,
            UserId: from.AadObjectId ?? from.Id,
            UserName: string.IsNullOrWhiteSpace(from.Name) ? "Anonymous cake critic" : from.Name,
            score,
            comment,
            clock.GetUtcNow());

        var isNew = await store.UpsertScoreAsync(entry, ct);
        var day = await store.GetDayAsync(cakeDate, ct);

        var verb = isNew ? "scored" : "changed their score to";
        var reply = $"🍰 **{entry.UserName}** {verb} **{score}/10** – {CardFactory.ScoreLabel(score)}."
                    + (comment is null ? "" : $" _\"{comment}\"_")
                    + $"\n\nAverage so far: **{CardFactory.FormatAverage(day.Average)}** from {day.Count} {(day.Count == 1 ? "score" : "scores")}.";
        await turnContext.SendActivityAsync(MessageFactory.Text(reply), ct);

        await publisher.RefreshAsync(turnContext, cakeDate, _schedule.ScoringCloses, ct);
    }

    private async Task SendResultsAsync(ITurnContext turnContext, DateOnly cakeDate, CancellationToken ct)
    {
        var now = _schedule.LocalNow(clock);
        var stillOpen = cakeDate == DateOnly.FromDateTime(now) && ScoringWindow.Evaluate(now, _schedule) == ScoringState.Open;
        var day = await store.GetDayAsync(cakeDate, ct);
        await turnContext.SendActivityAsync(MessageFactory.Attachment(CardFactory.ResultsCard(day, isFinal: !stillOpen)), ct);
    }

    // ---- Installation / membership -------------------------------------------------------

    protected override async Task OnInstallationUpdateAddAsync(ITurnContext<IInstallationUpdateActivity> turnContext, CancellationToken ct)
    {
        await RememberConversationAsync(turnContext, ct);
        await SendWelcomeAsync(turnContext, ct);
    }

    protected override async Task OnInstallationUpdateRemoveAsync(ITurnContext<IInstallationUpdateActivity> turnContext, CancellationToken ct)
    {
        await store.RemoveConversationAsync(ScoringCardPublisher.ConversationKey(turnContext.Activity.Conversation.Id), ct);
    }

    protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken ct)
    {
        // Teams sends installationUpdate for this; other channels (like the Bot Framework Emulator) only send membersAdded.
        if (turnContext.Activity.ChannelId == Channels.Msteams)
            return;

        if (membersAdded.Any(m => m.Id == turnContext.Activity.Recipient.Id))
        {
            await RememberConversationAsync(turnContext, ct);
            await SendWelcomeAsync(turnContext, ct);
        }
    }

    private Task SendWelcomeAsync(ITurnContext turnContext, CancellationToken ct) =>
        turnContext.SendActivityAsync(
            MessageFactory.Text($"Hei! I keep score of the {_schedule.CakeDay} Napoleonskake. I'll post a scoring card here every {_schedule.CakeDay} at {_schedule.PostTime}.\n\n" + CardFactory.HelpText(_schedule)), ct);

    // "Test in Web Chat" in the Azure portal uses these channels. Those conversations die when the browser tab closes,
    // so the Friday scheduler must not try to post into them.
    private static readonly HashSet<string> TransientChannels = [Channels.Webchat, Channels.Directline];

    private async Task RememberConversationAsync(ITurnContext turnContext, CancellationToken ct)
    {
        if (TransientChannels.Contains(turnContext.Activity.ChannelId))
            return;

        var reference = turnContext.Activity.GetConversationReference();
        var key = ScoringCardPublisher.ConversationKey(reference.Conversation.Id);
        // Store the channel root so the Friday card lands as a new post rather than inside an old thread.
        reference.Conversation.Id = key;
        reference.ActivityId = null;
        await store.SaveConversationAsync(key, JsonConvert.SerializeObject(reference), ct);
    }

    // ---- Helpers -------------------------------------------------------------------------

    /// <summary>For results and the card command: today if it is cake day, otherwise the last one.</summary>
    private DateOnly MostRecentCakeDate() =>
        CakeCalendar.MostRecentCakeDay(DateOnly.FromDateTime(_schedule.LocalNow(clock)), _schedule.CakeDay);

    private static DateOnly? TryParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
