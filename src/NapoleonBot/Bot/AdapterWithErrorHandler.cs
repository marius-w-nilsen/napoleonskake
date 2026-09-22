using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;

namespace NapoleonBot.Bot;

public sealed class AdapterWithErrorHandler : CloudAdapter
{
    public AdapterWithErrorHandler(BotFrameworkAuthentication auth, ILogger<AdapterWithErrorHandler> logger)
        : base(auth, logger)
    {
        OnTurnError = async (turnContext, exception) =>
        {
            logger.LogError(exception, "Unhandled error in turn {ActivityId}", turnContext.Activity.Id);
            try
            {
                await turnContext.SendActivityAsync("Oops, I dropped the cake. Try again in a moment.");
            }
            catch (Exception sendError)
            {
                logger.LogError(sendError, "Could not send the error message to the user");
            }
        };
    }
}
