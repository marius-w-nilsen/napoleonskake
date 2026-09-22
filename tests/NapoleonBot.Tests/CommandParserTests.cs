using NapoleonBot.Bot;

namespace NapoleonBot.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("score 8", 8, null)]
    [InlineData("Score 8 extra crispy today", 8, "extra crispy today")]
    [InlineData("8", 8, null)]
    [InlineData("10/10", 10, null)]
    [InlineData("  7 / 10  soggy bottom", 7, "soggy bottom")]
    [InlineData("/score 3", 3, null)]
    [InlineData("rate 9", 9, null)]
    [InlineData("gi 6 helt greit", 6, "helt greit")]
    public void Parses_scores_with_optional_comment(string text, int expectedScore, string? expectedComment)
    {
        var command = Assert.IsType<ScoreCommand>(CommandParser.Parse(text));
        Assert.Equal(expectedScore, command.Score);
        Assert.Equal(expectedComment, command.Comment);
    }

    [Theory]
    [InlineData("score 11")]
    [InlineData("score 0")]
    [InlineData("score")]
    [InlineData("score ten")]
    [InlineData("0")]
    [InlineData("99")]
    public void Rejects_scores_outside_one_to_ten(string text)
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
