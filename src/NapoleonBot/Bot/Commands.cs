using System.Text.RegularExpressions;
using NapoleonBot.Data;

namespace NapoleonBot.Bot;

public abstract record BotCommand;

/// <summary>"score 5 crispy today", just "5", or a die face "⚄".</summary>
public sealed record ScoreCommand(int Score, string? Comment) : BotCommand;

/// <summary>A score attempt that is not a number on the scale.</summary>
public sealed record InvalidScoreCommand(string Raw) : BotCommand;

public sealed record ResultsCommand : BotCommand;
public sealed record HistoryCommand : BotCommand;
public sealed record LeaderboardCommand : BotCommand;
public sealed record PostCardCommand : BotCommand;
public sealed record HelpCommand : BotCommand;

public static partial class CommandParser
{
    private static readonly string[] ScoreVerbs = ["score", "rate", "vote", "gi", "poeng", "terning"];
    private static readonly string[] ResultsWords = ["results", "result", "today", "stats", "score?", "resultat"];
    private static readonly string[] HistoryWords = ["history", "trend", "historikk"];
    private static readonly string[] LeaderboardWords = ["leaderboard", "top", "toppliste", "alltime", "all-time"];
    private static readonly string[] CardWords = ["card", "poll", "kort", "post"];
    private static readonly char[] Punctuation = [',', '.', ':', ';', '!', '-', '–', '—'];

    // "5", "5/6", "5 / 6"
    [GeneratedRegex(@"^(\d{1,2})(?:\s*/\s*6)?$")]
    private static partial Regex ScoreToken();

    // "5 / 6 soggy" splits as head "5", rest "/ 6 soggy": drop the "/ 6" so it does not end up in the comment.
    [GeneratedRegex(@"^/\s*6\b\s*")]
    private static partial Regex LeadingOutOfSix();

    public static BotCommand Parse(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().TrimStart('/', '!');
        if (trimmed.Length == 0)
            return new HelpCommand();

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // "5, it was good" or "score: 5" – punctuation glued to the first word should not hide the keyword or number.
        var head = parts[0].ToLowerInvariant().TrimEnd(Punctuation);
        var rest = parts.Length > 1 ? parts[1] : null;

        if (ScoreToken().IsMatch(head) || ScoreScale.FromDie(head) is not null)
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

    private static BotCommand ParseScore(string token, string? comment)
    {
        var clean = token.TrimEnd(Punctuation);
        int score;
        if (ScoreScale.FromDie(clean) is { } fromDie)
        {
            score = fromDie;
        }
        else
        {
            var match = ScoreToken().Match(clean);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out score) || !ScoreScale.IsValid(score))
                return new InvalidScoreCommand(token);
        }

        var stripped = comment is null ? null : LeadingOutOfSix().Replace(comment, string.Empty);
        // Drop separators people put between the number and the comment: "5 - crispy", "5: crispy".
        var cleanComment = stripped?.Trim().TrimStart(Punctuation).Trim();
        return new ScoreCommand(score, string.IsNullOrEmpty(cleanComment) ? null : cleanComment);
    }
}
