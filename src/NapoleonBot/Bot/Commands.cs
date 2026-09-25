using System.Text.RegularExpressions;

namespace NapoleonBot.Bot;

public abstract record BotCommand;

/// <summary>"score 8 crispy today" or just "8".</summary>
public sealed record ScoreCommand(int Score, string? Comment) : BotCommand;

/// <summary>A score attempt that is not a number between 1 and 10.</summary>
public sealed record InvalidScoreCommand(string Raw) : BotCommand;

public sealed record ResultsCommand : BotCommand;
public sealed record HistoryCommand : BotCommand;
public sealed record LeaderboardCommand : BotCommand;
public sealed record PostCardCommand : BotCommand;
public sealed record HelpCommand : BotCommand;

public static partial class CommandParser
{
    private static readonly string[] ScoreVerbs = ["score", "rate", "vote", "gi", "poeng"];
    private static readonly string[] ResultsWords = ["results", "result", "today", "stats", "score?", "resultat"];
    private static readonly string[] HistoryWords = ["history", "trend", "historikk"];
    private static readonly string[] LeaderboardWords = ["leaderboard", "top", "toppliste", "alltime", "all-time"];
    private static readonly string[] CardWords = ["card", "poll", "kort", "post"];

    [GeneratedRegex(@"^(\d{1,2})(?:\s*/\s*10)?$")]
    private static partial Regex ScoreToken();

    // "7 / 10 soggy" splits as head "7", rest "/ 10 soggy": drop the "/ 10" so it does not end up in the comment.
    [GeneratedRegex(@"^/\s*10\b\s*")]
    private static partial Regex LeadingOutOfTen();

    public static BotCommand Parse(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().TrimStart('/', '!');
        if (trimmed.Length == 0)
            return new HelpCommand();

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // "8, it was good" or "score: 8" – punctuation glued to the first word should not hide the keyword or number.
        var head = parts[0].ToLowerInvariant().TrimEnd(Punctuation);
        var rest = parts.Length > 1 ? parts[1] : null;

        if (ScoreToken().IsMatch(head))
            return ParseScore(head, rest);

        if (ScoreVerbs.Contains(head))
        {
            if (rest is null)
                return new InvalidScoreCommand(string.Empty);
            var scoreParts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ParseScore(scoreParts[0], scoreParts.Length > 1 ? scoreParts[1] : null);
        }

        if (ResultsWords.Contains(head)) return new ResultsCommand();
        if (HistoryWords.Contains(head)) return new HistoryCommand();
        if (LeaderboardWords.Contains(head)) return new LeaderboardCommand();
        if (CardWords.Contains(head)) return new PostCardCommand();

        return new HelpCommand();
    }

    private static readonly char[] Punctuation = [',', '.', ':', ';', '!', '-', '–', '—'];

    private static BotCommand ParseScore(string token, string? comment)
    {
        var match = ScoreToken().Match(token.TrimEnd(Punctuation));
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var score) || score is < 1 or > 10)
            return new InvalidScoreCommand(token);

        var stripped = comment is null ? null : LeadingOutOfTen().Replace(comment, string.Empty);
        // Drop separators people put between the number and the comment: "8 - crispy", "8: crispy".
        var cleanComment = stripped?.Trim().TrimStart(Punctuation).Trim();
        return new ScoreCommand(score, string.IsNullOrEmpty(cleanComment) ? null : cleanComment);
    }
}
