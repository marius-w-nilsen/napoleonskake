using System.Globalization;
using System.Text.RegularExpressions;
using NapoleonBot.Data;

namespace NapoleonBot.Bot;

public abstract record BotCommand;

/// <summary>"score 5 crispy today", just "5", "5+", "3-", or a die face "⚄".</summary>
public sealed record ScoreCommand(int Score, int Modifier, string? Comment) : BotCommand;

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

    // "5", "5+", "3-", "5/6", "5+ / 6". The sign group is lenient ("5++") so the token is still treated as a score attempt and rejected properly.
    [GeneratedRegex(@"^(\d{1,2})([+-]*)(?:\s*/\s*6)?$")]
    private static partial Regex NumberToken();

    // "⚄", "⚄+", "⚂-"
    [GeneratedRegex(@"^([⚀-⚅])([+-]*)$")]
    private static partial Regex DieToken();

    // The score part at the start of a word, so "5-," keeps its minus while "5," loses its comma.
    [GeneratedRegex(@"^(?:\d{1,2}|[⚀-⚅])[+-]*")]
    private static partial Regex LeadingScore();

    // "5 / 6 soggy" splits as head "5", rest "/ 6 soggy": drop the "/ 6" so it does not end up in the comment.
    [GeneratedRegex(@"^/\s*6\b\s*")]
    private static partial Regex LeadingOutOfSix();

    public static BotCommand Parse(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().TrimStart('/', '!');
        if (trimmed.Length == 0)
            return new HelpCommand();

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var head = NormalizeToken(parts[0]).ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1] : null;

        if (LooksLikeScore(head))
            return ParseScore(head, rest);

        if (ScoreVerbs.Contains(head))
        {
            if (rest is null)
                return new InvalidScoreCommand(string.Empty);
            var scoreParts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ParseScore(NormalizeToken(scoreParts[0]), scoreParts.Length > 1 ? scoreParts[1] : null);
        }

        if (ResultsWords.Contains(head)) return new ResultsCommand();
        if (HistoryWords.Contains(head)) return new HistoryCommand();
        if (LeaderboardWords.Contains(head)) return new LeaderboardCommand();
        if (CardWords.Contains(head)) return new PostCardCommand();

        return new HelpCommand();
    }

    /// <summary>A number or die face with optional signs: something the user meant as a score, valid or not.</summary>
    private static bool LooksLikeScore(string token) => NumberToken().IsMatch(token) || DieToken().IsMatch(token);

    /// <summary>Parses "5", "5+", "3-", "5/6" or a die face with optional modifier. False for anything off the scale, including 6+ and 1-.</summary>
    public static bool TryParseScoreToken(string token, out int score, out int modifier)
    {
        score = 0;
        modifier = 0;

        var die = DieToken().Match(token);
        if (die.Success)
        {
            score = ScoreScale.FromDie(die.Groups[1].Value)!.Value;
            return ParseModifier(die.Groups[2].Value, out modifier) && ScoreScale.IsValid(score, modifier);
        }

        var number = NumberToken().Match(token);
        if (!number.Success || !int.TryParse(number.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out score))
            return false;

        return ParseModifier(number.Groups[2].Value, out modifier) && ScoreScale.IsValid(score, modifier);
    }

    /// <summary>"" → 0, "+" → 1, "-" → -1. Anything longer ("++") is not a modifier.</summary>
    private static bool ParseModifier(string sign, out int modifier)
    {
        modifier = sign switch { "+" => 1, "-" => -1, _ => 0 };
        return sign.Length <= 1;
    }

    /// <summary>
    /// Strips punctuation glued to a word ("score:", "5,") without eating a score's own modifier ("5-" stays "5-").
    /// </summary>
    private static string NormalizeToken(string token)
    {
        var leading = LeadingScore().Match(token);
        if (leading.Success && token[leading.Length..].All(c => Punctuation.Contains(c)))
            return leading.Value;
        return token.TrimEnd(Punctuation);
    }

    private static BotCommand ParseScore(string token, string? comment)
    {
        if (!TryParseScoreToken(token, out var score, out var modifier))
            return new InvalidScoreCommand(token);

        var stripped = comment is null ? null : LeadingOutOfSix().Replace(comment, string.Empty);
        // Drop separators people put between the number and the comment: "5 - crispy", "5: crispy".
        var cleanComment = stripped?.Trim().TrimStart(Punctuation).Trim();
        return new ScoreCommand(score, modifier, string.IsNullOrEmpty(cleanComment) ? null : cleanComment);
    }
}
