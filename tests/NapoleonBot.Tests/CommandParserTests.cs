using NapoleonBot.Bot;

namespace NapoleonBot.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("score 5", 5, null)]
    [InlineData("Score 5 extra crispy today", 5, "extra crispy today")]
    [InlineData("5", 5, null)]
    [InlineData("6/6", 6, null)]
    [InlineData("  4 / 6  soggy bottom", 4, "soggy bottom")]
    [InlineData("/score 3", 3, null)]
    [InlineData("rate 2", 2, null)]
    [InlineData("gi 6 helt greit", 6, "helt greit")]
    [InlineData("terning 4", 4, null)]
    [InlineData("5, it was gooooD", 5, "it was gooooD")]
    [InlineData("5 - a bit dry", 5, "a bit dry")]
    [InlineData("score: 6! best so far", 6, "best so far")]
    [InlineData("4.", 4, null)]
    [InlineData("6/6!", 6, null)]
    [InlineData("⚄", 5, null)]
    [InlineData("⚅ brilliant", 6, "brilliant")]
    [InlineData("score ⚀ nope", 1, "nope")]
    public void Parses_scores_with_optional_comment(string text, int expectedScore, string? expectedComment)
    {
        var command = Assert.IsType<ScoreCommand>(CommandParser.Parse(text));
        Assert.Equal(expectedScore, command.Score);
        Assert.Equal(expectedComment, command.Comment);
    }

    [Theory]
    [InlineData("score 7")]
    [InlineData("score 10")]
    [InlineData("score 0")]
    [InlineData("score")]
    [InlineData("score six")]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("99")]
    public void Rejects_scores_outside_the_die(string text)
    {
        Assert.IsType<InvalidScoreCommand>(CommandParser.Parse(text));
    }

    [Theory]
    [InlineData("results", typeof(ResultsCommand))]
    [InlineData("today", typeof(ResultsCommand))]
    [InlineData("stats", typeof(ResultsCommand))]
    [InlineData("history", typeof(HistoryCommand))]
    [InlineData("trend", typeof(HistoryCommand))]
    [InlineData("leaderboard", typeof(LeaderboardCommand))]
    [InlineData("TOP", typeof(LeaderboardCommand))]
    [InlineData("card", typeof(PostCardCommand))]
    [InlineData("help", typeof(HelpCommand))]
    [InlineData("", typeof(HelpCommand))]
    [InlineData(null, typeof(HelpCommand))]
    [InlineData("what is the meaning of cake", typeof(HelpCommand))]
    public void Recognises_keywords(string? text, Type expected)
    {
        Assert.IsType(expected, CommandParser.Parse(text));
    }
}
