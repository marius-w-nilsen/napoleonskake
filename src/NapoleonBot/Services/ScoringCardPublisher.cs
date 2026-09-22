using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using NapoleonBot.Cards;
using NapoleonBot.Data;

namespace NapoleonBot.Services;

/// <summary>Posts the scoring card into a conversation, keeps it updated as scores come in, and locks it when scoring closes.</summary>
public sealed class ScoringCardPublisher(ScoreStore store, ILogger<ScoringCardPublisher> logger)
{
    /// <summary>
    /// Teams thread replies carry a conversation id like "19:abc@thread.tacv2;messageid=123".
    /// Strip the thread part so every message in a channel maps to the same key.
    /// </summary>
    public static string ConversationKey(string conversationId) => conversationId.Split(';')[0];

    public async Task<PostedCard> PostAsync(ITurnContext turnContext, DateOnly cakeDate, bool isOpen, string closesAt, CancellationToken ct)
    {
        var day = await store.GetDayAsync(cakeDate, ct);
        var response = await turnContext.SendActivityAsync(MessageFactory.Attachment(CardFactory.ScoringCard(cakeDate, day, isOpen, closesAt)), ct);
        var posted = new PostedCard(cakeDate, ConversationKey(turnContext.Activity.Conversation.Id), response.Id);
        await store.SavePostedCardAsync(posted, ct);
        return posted;
    }

    /// <summary>Refreshes the open scoring card for the cake day in this conversation, if there is one.</summary>
    public async Task RefreshAsync(ITurnContext turnContext, DateOnly cakeDate, string closesAt, CancellationToken ct)
    {
        var key = ConversationKey(turnContext.Activity.Conversation.Id);
        var posted = await store.GetPostedCardAsync(cakeDate, key, ct);
        if (posted is not null)
            await UpdateCardAsync(turnContext, posted, isOpen: true, closesAt, ct);
    }

    /// <summary>Locks the card (no more inputs), optionally posts the final results, and records that this conversation is closed for the day.</summary>
    public async Task CloseAsync(ITurnContext turnContext, PostedCard posted, bool postResults, string closesAt, CancellationToken ct)
    {
        await UpdateCardAsync(turnContext, posted, isOpen: false, closesAt, ct);

        if (postResults)
        {
            var day = await store.GetDayAsync(posted.CakeDate, ct);
            await turnContext.SendActivityAsync(MessageFactory.Attachment(CardFactory.ResultsCard(day, isFinal: true)), ct);
        }

        await store.MarkClosedAsync(posted.CakeDate, posted.ConversationId, ct);
    }

    private async Task UpdateCardAsync(ITurnContext turnContext, PostedCard posted, bool isOpen, string closesAt, CancellationToken ct)
    {
        try
        {
            var day = await store.GetDayAsync(posted.CakeDate, ct);
            var update = MessageFactory.Attachment(CardFactory.ScoringCard(posted.CakeDate, day, isOpen, closesAt));
            update.Id = posted.ActivityId;
            // Go through the adapter directly so the update targets the channel root, not the current reply thread.
            update.Conversation = new ConversationAccount(id: posted.ConversationId);
            update.ServiceUrl = turnContext.Activity.ServiceUrl;
            update.ChannelId = turnContext.Activity.ChannelId;
            update.From = turnContext.Activity.Recipient;
            await turnContext.Adapter.UpdateActivityAsync(turnContext, (Activity)update, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update scoring card {ActivityId} in {Conversation}", posted.ActivityId, posted.ConversationId);
        }
    }
}
