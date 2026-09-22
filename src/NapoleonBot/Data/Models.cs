namespace NapoleonBot.Data;

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
