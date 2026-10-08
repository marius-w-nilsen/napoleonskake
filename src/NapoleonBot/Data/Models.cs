namespace NapoleonBot.Data;

/// <summary>The scoring scale: a die, 1 to 6.</summary>
public static class ScoreScale
{
    public const int Min = 1;
    public const int Max = 6;

    private const string DiceFaces = "⚀⚁⚂⚃⚄⚅";

    public static bool IsValid(int score) => score is >= Min and <= Max;

    /// <summary>The die face for a score, e.g. 4 → ⚃.</summary>
    public static string Die(int score) => DiceFaces[Math.Clamp(score, Min, Max) - 1].ToString();

    /// <summary>The score a die face stands for, or null if the text is not a single die face.</summary>
    public static int? FromDie(string text) =>
        text.Length == 1 && DiceFaces.IndexOf(text[0]) is var i and >= 0 ? i + 1 : null;
}

/// <summary>One person's score for one cake day. A user can only have one score per cake day.</summary>
public sealed record ScoreEntry(
    DateOnly CakeDate,
    string UserId,
    string UserName,
    int Score,
    string? Comment,
    DateTimeOffset CreatedAt);

/// <summary>All scores for a single cake day.</summary>
public sealed record DaySummary(DateOnly CakeDate, IReadOnlyList<ScoreEntry> Scores)
{
    public int Count => Scores.Count;
    public double Average => Scores.Count == 0 ? 0 : Scores.Average(s => s.Score);
}

/// <summary>How one person tends to score, across all cake days.</summary>
public sealed record ScorerStats(string UserId, string UserName, int Count, double Average);

public sealed record AllTimeStats(
    int CakeDays,
    int TotalScores,
    double OverallAverage,
    DaySummary? BestDay,
    DaySummary? WorstDay);

/// <summary>The scoring card posted into a conversation for a cake day, remembered so it can be updated in place.</summary>
public sealed record PostedCard(DateOnly CakeDate, string ConversationId, string ActivityId);
