using System.Globalization;
using Microsoft.Bot.Schema;
using NapoleonBot.Data;
using NapoleonBot.Services;
using Newtonsoft.Json.Linq;

namespace NapoleonBot.Cards;

/// <summary>Builds the Adaptive Cards the bot posts. All user-facing wording lives here.</summary>
public static class CardFactory
{
    public const string AdaptiveCardContentType = "application/vnd.microsoft.card.adaptive";
    private static readonly CultureInfo DateCulture = CultureInfo.GetCultureInfo("en-GB");

    public static string FormatDate(DateOnly date) => date.ToString("dddd d MMMM yyyy", DateCulture);
    public static string FormatShortDate(DateOnly date) => date.ToString("d MMM", DateCulture);

    /// <summary>Averages always use a dot as decimal separator, whatever culture the server runs with.</summary>
    public static string FormatAverage(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string ScoreLabel(int score) => score switch
    {
        10 => "Legendary. Frame it.",
        9 => "Bakery-grade",
        8 => "Very good",
        7 => "Solid Friday cake",
        6 => "Fine. Just fine.",
        5 => "It's cake, I guess",
        4 => "Something went wrong in the kitchen",
        3 => "Custard crimes were committed",
        2 => "Soggy disappointment",
        _ => "Call the authorities",
    };

    public static string Verdict(double average) => ScoreLabel(Math.Clamp((int)Math.Round(average, MidpointRounding.AwayFromZero), 1, 10));

    public static string Bar(double average)
    {
        var filled = Math.Clamp((int)Math.Round(average, MidpointRounding.AwayFromZero), 0, 10);
        return new string('█', filled) + new string('░', 10 - filled);
    }

    public static string HelpText(ScheduleOptions schedule) => $"""
        **🍰 Napoleonskake scoring**

        - `score 8` or just `8` – score today's cake (1–10). Add a comment: `score 8 extra crispy today`
        - `results` – today's scores and average
        - `history` – the last Fridays, as a trend
        - `leaderboard` – best and worst Friday ever, most generous and harshest scorers
        - `card` – post the scoring card again
        - `help` – this message

        Scoring is open every {schedule.CakeDay} from {schedule.PostTime} to {schedule.ScoringCloses}. I post the card when it opens and the final results when it closes. One score per person per {schedule.CakeDay}; scoring again replaces your earlier score.
        """;

    // ---- Scoring card -------------------------------------------------------------------

    /// <param name="isOpen">While open the card has inputs; once closed it only shows the final tally and a results button.</param>
    /// <param name="closesAt">Closing time as shown to users, e.g. "17:00".</param>
    public static Attachment ScoringCard(DateOnly cakeDate, DaySummary day, bool isOpen, string closesAt)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", size = "Large", weight = "Bolder", text = $"🍰 Napoleonskake – {FormatDate(cakeDate)}", wrap = true },
        };
        var actions = new List<object>();

        if (isOpen)
        {
            var choices = Enumerable.Range(1, 10).Reverse()
                .Select(n => new { title = $"{n} – {ScoreLabel(n)}", value = n.ToString(CultureInfo.InvariantCulture) })
                .ToArray();

            body.Add(new { type = "TextBlock", text = $"Lunch verdict time. How was today's cake? Scoring closes at {closesAt}.", wrap = true });
            body.Add(new { type = "TextBlock", text = StatusLine(day), isSubtle = true, wrap = true, spacing = "Small" });
            body.Add(new
            {
                type = "Input.ChoiceSet",
                id = "score",
                style = "compact",
                placeholder = "Pick a score (1–10)",
                isRequired = true,
                errorMessage = "Pick a score first.",
                choices,
            });
            body.Add(new { type = "Input.Text", id = "comment", placeholder = "Optional comment: crispy? soggy? custard ratio?", maxLength = 200 });
            actions.Add(new { type = "Action.Submit", title = "Submit score", style = "positive", data = new { action = "score", cakeDate = IsoDate(cakeDate) } });
        }
        else
        {
            body.Add(new { type = "TextBlock", text = $"🔒 Scoring closed at {closesAt}.", wrap = true });
            body.Add(new { type = "TextBlock", text = FinalLine(day), weight = "Bolder", wrap = true, spacing = "Small" });
        }

        actions.Add(new { type = "Action.Submit", title = "Show results", data = new { action = "results", cakeDate = IsoDate(cakeDate) } });

        return ToAttachment(new { type = "AdaptiveCard", version = "1.5", body, actions });
    }

    private static string StatusLine(DaySummary day)
    {
        if (day.Count == 0)
            return "No scores yet – be the first to judge.";

        var latest = day.Scores.MaxBy(s => s.CreatedAt)!;
        var plural = day.Count == 1 ? "score" : "scores";
        return $"{day.Count} {plural} so far · average {FormatAverage(day.Average)} {Bar(day.Average)} · latest: {latest.UserName} gave {latest.Score}";
    }

    private static string FinalLine(DaySummary day) => day.Count == 0
        ? "Nobody scored. Was there even cake?"
        : $"Final: {FormatAverage(day.Average)} {Bar(day.Average)} from {day.Count} {(day.Count == 1 ? "score" : "scores")} – {Verdict(day.Average)}";

    // ---- Results card -------------------------------------------------------------------

    public static Attachment ResultsCard(DaySummary day, bool isFinal = false)
    {
        var heading = isFinal ? "🏁 Final results" : "📊 Results so far";
        var body = new List<object>
        {
            new { type = "TextBlock", size = "Large", weight = "Bolder", text = $"{heading} – {FormatDate(day.CakeDate)}", wrap = true },
        };

        if (day.Count == 0)
        {
            body.Add(new { type = "TextBlock", text = "Nobody has scored this cake yet. Was there even cake?", wrap = true });
        }
        else
        {
            body.Add(new
            {
                type = "ColumnSet",
                columns = new object[]
                {
                    new
                    {
                        type = "Column", width = "auto",
                        items = new object[] { new { type = "TextBlock", size = "ExtraLarge", weight = "Bolder", text = $"{FormatAverage(day.Average)}" } },
                    },
                    new
                    {
                        type = "Column", width = "stretch", verticalContentAlignment = "Center",
                        items = new object[]
                        {
                            new { type = "TextBlock", text = Verdict(day.Average), weight = "Bolder", wrap = true },
                            new { type = "TextBlock", text = $"{Bar(day.Average)}  {day.Count} {(day.Count == 1 ? "score" : "scores")}", isSubtle = true, spacing = "None" },
                        },
                    },
                },
            });
            body.Add(new
            {
                type = "FactSet",
                facts = day.Scores
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.CreatedAt)
                    .Select(s => new { title = s.UserName, value = $"{s.Score}/10" + (s.Comment is null ? "" : $" – {s.Comment}") })
                    .ToArray(),
            });
        }

        return ToAttachment(new { type = "AdaptiveCard", version = "1.5", body });
    }

    // ---- History card -------------------------------------------------------------------

    public static Attachment HistoryCard(IReadOnlyList<DaySummary> days, AllTimeStats allTime)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", size = "Large", weight = "Bolder", text = "📈 Napoleonskake over time", wrap = true },
        };

        if (days.Count == 0)
        {
            body.Add(new { type = "TextBlock", text = "No Fridays scored yet. The trend line is a blank stare.", wrap = true });
        }
        else
        {
            var lines = days.Select(d => $"{FormatShortDate(d.CakeDate),-7} {Bar(d.Average)} {FormatAverage(d.Average),4} ({d.Count})");
            body.Add(new { type = "TextBlock", fontType = "Monospace", text = string.Join("\n\n", lines), wrap = true });
            body.Add(new
            {
                type = "TextBlock",
                isSubtle = true,
                wrap = true,
                text = $"All-time average {FormatAverage(allTime.OverallAverage)} over {allTime.CakeDays} {(allTime.CakeDays == 1 ? "Friday" : "Fridays")} and {allTime.TotalScores} scores.",
            });
            if (days.Count >= 2)
                body.Add(new { type = "TextBlock", wrap = true, text = TrendLine(days[0], days[1]) });
        }

        return ToAttachment(new { type = "AdaptiveCard", version = "1.5", body });
    }

    private static string TrendLine(DaySummary latest, DaySummary previous)
    {
        var delta = latest.Average - previous.Average;
        return delta switch
        {
            >= 1.5 => $"📈 Up {FormatAverage(delta)} from last time. Whoever baked this deserves a raise.",
            >= 0.3 => $"📈 Up {FormatAverage(delta)} from last time. Progress.",
            <= -1.5 => $"📉 Down {FormatAverage(Math.Abs(delta))} from last time. Emergency meeting with the bakery.",
            <= -0.3 => $"📉 Down {FormatAverage(Math.Abs(delta))} from last time. Hmm.",
            _ => "➡️ Same as last time. Consistency is a virtue, allegedly.",
        };
    }

    // ---- Leaderboard card ---------------------------------------------------------------

    public static Attachment LeaderboardCard(IReadOnlyList<ScorerStats> scorers, AllTimeStats allTime)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", size = "Large", weight = "Bolder", text = "🏆 Napoleonskake hall of fame", wrap = true },
        };

        if (allTime.TotalScores == 0)
        {
            body.Add(new { type = "TextBlock", text = "No scores yet. The hall is empty and echoing.", wrap = true });
            return ToAttachment(new { type = "AdaptiveCard", version = "1.5", body });
        }

        var facts = new List<object>();
        if (allTime.BestDay is { } best)
            facts.Add(new { title = "Best Friday", value = $"{FormatDate(best.CakeDate)} – {FormatAverage(best.Average)} ({best.Count} scores)" });
        if (allTime.WorstDay is { } worst && allTime.CakeDays > 1)
            facts.Add(new { title = "Worst Friday", value = $"{FormatDate(worst.CakeDate)} – {FormatAverage(worst.Average)} ({worst.Count} scores)" });
        facts.Add(new { title = "All-time average", value = $"{FormatAverage(allTime.OverallAverage)} over {allTime.CakeDays} Fridays" });

        var regulars = scorers.Where(s => s.Count >= 2).ToList();
        if (regulars.Count > 0)
        {
            var generous = regulars.MaxBy(s => s.Average)!;
            var harsh = regulars.MinBy(s => s.Average)!;
            facts.Add(new { title = "Most generous", value = $"{generous.UserName} – averages {FormatAverage(generous.Average)}" });
            if (harsh.UserId != generous.UserId)
                facts.Add(new { title = "Harshest critic", value = $"{harsh.UserName} – averages {FormatAverage(harsh.Average)}" });
        }
        var dedicated = scorers.MaxBy(s => s.Count)!;
        facts.Add(new { title = "Most dedicated", value = $"{dedicated.UserName} – {dedicated.Count} {(dedicated.Count == 1 ? "score" : "scores")}" });

        body.Add(new { type = "FactSet", facts });

        var table = scorers.Select(s => $"{Truncate(s.UserName, 18),-18} {s.Count,3}× {FormatAverage(s.Average),5}");
        body.Add(new { type = "TextBlock", text = "**Everyone**", spacing = "Medium" });
        body.Add(new { type = "TextBlock", fontType = "Monospace", text = string.Join("\n\n", table), wrap = true });

        return ToAttachment(new { type = "AdaptiveCard", version = "1.5", body });
    }

    // ---- Helpers ------------------------------------------------------------------------

    private static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static Attachment ToAttachment(object card) => new()
    {
        ContentType = AdaptiveCardContentType,
        Content = JObject.FromObject(card),
    };
}
