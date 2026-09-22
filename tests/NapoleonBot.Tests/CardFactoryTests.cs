using NapoleonBot.Cards;
using NapoleonBot.Data;
using Newtonsoft.Json.Linq;

namespace NapoleonBot.Tests;

public class CardFactoryTests
{
    private static readonly DateOnly Friday = new(2026, 9, 18);

    private static ScoreEntry Score(string user, int score, string? comment = null, int minute = 0) =>
        new(Friday, user.ToLowerInvariant(), user, score, comment, new DateTimeOffset(2026, 9, 18, 12, minute, 0, TimeSpan.Zero));

    private static JObject Content(Microsoft.Bot.Schema.Attachment attachment)
    {
        Assert.Equal(CardFactory.AdaptiveCardContentType, attachment.ContentType);
        return Assert.IsType<JObject>(attachment.Content);
    }

    [Fact]
    public void Scoring_card_offers_ten_choices_and_carries_the_cake_date()
    {
        var card = Content(CardFactory.ScoringCard(Friday, new DaySummary(Friday, []), isOpen: true, closesAt: "17:00"));

        var choiceSet = card["body"]!.Single(b => (string?)b["type"] == "Input.ChoiceSet");
        var values = choiceSet["choices"]!.Select(c => (string)c["value"]!).ToList();
        Assert.Equal(Enumerable.Range(1, 10).Reverse().Select(n => n.ToString()), values);

        var submit = card["actions"]!.First();
        Assert.Equal("score", (string?)submit["data"]!["action"]);
        Assert.Equal("2026-09-18", (string?)submit["data"]!["cakeDate"]);

        Assert.Contains("No scores yet", card.ToString());
        Assert.Contains("Friday 18 September 2026", card.ToString());
    }

    [Fact]
    public void Scoring_card_shows_running_average_and_latest_scorer()
    {
        var day = new DaySummary(Friday, [Score("Kari", 8, minute: 1), Score("Ola", 5, minute: 2)]);
        var text = Content(CardFactory.ScoringCard(Friday, day, isOpen: true, closesAt: "17:00")).ToString();

        Assert.Contains("2 scores so far", text);
        Assert.Contains("average 6.5", text);
        Assert.Contains("latest: Ola gave 5", text);
    }

    [Fact]
    public void Closed_scoring_card_has_no_inputs_and_shows_final_tally()
    {
        var day = new DaySummary(Friday, [Score("Kari", 8), Score("Ola", 5)]);
        var card = Content(CardFactory.ScoringCard(Friday, day, isOpen: false, closesAt: "17:00"));

        Assert.DoesNotContain(card["body"]!, b => ((string?)b["type"])!.StartsWith("Input."));
        var actionTitles = card["actions"]!.Select(a => (string?)a["title"]).ToList();
        Assert.Equal(["Show results"], actionTitles);
        Assert.Contains("Scoring closed at 17:00", card.ToString());
        Assert.Contains("Final: 6.5", card.ToString());
    }

    [Fact]
    public void Results_card_lists_scores_highest_first_with_comments()
    {
        var day = new DaySummary(Friday, [Score("Kari", 6, "a bit dry"), Score("Ola", 10)]);
        var card = Content(CardFactory.ResultsCard(day));

        var facts = card["body"]!.Single(b => (string?)b["type"] == "FactSet")["facts"]!;
        Assert.Equal("Ola", (string?)facts[0]!["title"]);
        Assert.Equal("10/10", (string?)facts[0]!["value"]);
        Assert.Equal("6/10 – a bit dry", (string?)facts[1]!["value"]);
        Assert.Contains("8.0", card.ToString());
    }

    [Fact]
    public void Empty_cards_do_not_throw()
    {
        var empty = new AllTimeStats(0, 0, 0, null, null);
        Content(CardFactory.ResultsCard(new DaySummary(Friday, [])));
        Content(CardFactory.HistoryCard([], empty));
        Content(CardFactory.LeaderboardCard([], empty));
    }

    [Fact]
    public void Leaderboard_names_generous_and_harsh_regulars()
    {
        var scorers = new List<ScorerStats>
        {
            new("kari", "Kari", 3, 9.0),
            new("ola", "Ola", 2, 4.0),
            new("per", "Per", 1, 1.0), // only one score: not a regular yet
        };
        var best = new DaySummary(Friday, [Score("Kari", 9)]);
        var all = new AllTimeStats(2, 6, 6.5, best, best);

        var text = Content(CardFactory.LeaderboardCard(scorers, all)).ToString();
        Assert.Contains("Kari – averages 9.0", text);
        Assert.Contains("Ola – averages 4.0", text);
        Assert.DoesNotContain("Per – averages", text);
        Assert.Contains("Most dedicated", text);
    }

    [Theory]
    [InlineData(10.0, "██████████")]
    [InlineData(7.4, "███████░░░")]
    [InlineData(7.5, "████████░░")]
    [InlineData(0.0, "░░░░░░░░░░")]
    public void Bar_rounds_to_nearest_block(double average, string expected)
    {
        Assert.Equal(expected, CardFactory.Bar(average));
    }
}
