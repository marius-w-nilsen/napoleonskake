using Microsoft.Data.Sqlite;
using NapoleonBot.Data;

namespace NapoleonBot.Tests;

public sealed class ScoreStoreTests : IDisposable
{
    private static readonly DateOnly Friday1 = new(2026, 9, 4);
    private static readonly DateOnly Friday2 = new(2026, 9, 11);
    private static readonly DateOnly Friday3 = new(2026, 9, 18);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"napoleon-test-{Guid.NewGuid():N}.db");
    private readonly ScoreStore _store;

    public ScoreStoreTests()
    {
        _store = new ScoreStore($"Data Source={_dbPath};Pooling=False");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private static ScoreEntry Score(DateOnly date, string user, int score, string? comment = null, int minute = 0) =>
        new(date, user.ToLowerInvariant(), user, score, comment, new DateTimeOffset(2026, 9, 18, 12, minute, 0, TimeSpan.Zero));

    [Fact]
    public async Task First_score_is_new_and_second_replaces_it()
    {
        Assert.True(await _store.UpsertScoreAsync(Score(Friday3, "Kari", 6)));
        Assert.False(await _store.UpsertScoreAsync(Score(Friday3, "Kari", 9, "changed my mind")));

        var day = await _store.GetDayAsync(Friday3);
        var only = Assert.Single(day.Scores);
        Assert.Equal(9, only.Score);
        Assert.Equal("changed my mind", only.Comment);
        Assert.Equal(9.0, day.Average);
    }

    [Fact]
    public async Task Rejects_out_of_range_scores()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.UpsertScoreAsync(Score(Friday3, "Ola", 11)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.UpsertScoreAsync(Score(Friday3, "Ola", 0)));
    }

    [Fact]
    public async Task Day_average_and_empty_day()
    {
        await _store.UpsertScoreAsync(Score(Friday3, "Kari", 8));
        await _store.UpsertScoreAsync(Score(Friday3, "Ola", 5));
        await _store.UpsertScoreAsync(Score(Friday3, "Per", 9));

        var day = await _store.GetDayAsync(Friday3);
        Assert.Equal(3, day.Count);
        Assert.Equal(22 / 3.0, day.Average, precision: 6);

        var empty = await _store.GetDayAsync(Friday2);
        Assert.Equal(0, empty.Count);
        Assert.Equal(0, empty.Average);
    }

    [Fact]
    public async Task History_is_newest_first_and_limited()
    {
        await _store.UpsertScoreAsync(Score(Friday1, "Kari", 4));
        await _store.UpsertScoreAsync(Score(Friday2, "Kari", 7));
        await _store.UpsertScoreAsync(Score(Friday2, "Ola", 9));
        await _store.UpsertScoreAsync(Score(Friday3, "Kari", 10));

        var history = await _store.GetHistoryAsync(2);
        Assert.Equal([Friday3, Friday2], history.Select(d => d.CakeDate));
        Assert.Equal(8.0, history[1].Average);
    }

    [Fact]
    public async Task All_time_stats_find_best_and_worst_friday()
    {
        await _store.UpsertScoreAsync(Score(Friday1, "Kari", 3));
        await _store.UpsertScoreAsync(Score(Friday1, "Ola", 4));
        await _store.UpsertScoreAsync(Score(Friday2, "Kari", 9));
        await _store.UpsertScoreAsync(Score(Friday3, "Kari", 7));

        var all = await _store.GetAllTimeAsync();
        Assert.Equal(3, all.CakeDays);
        Assert.Equal(4, all.TotalScores);
        Assert.Equal(23 / 4.0, all.OverallAverage, precision: 6);
        Assert.Equal(Friday2, all.BestDay!.CakeDate);
        Assert.Equal(Friday1, all.WorstDay!.CakeDate);
    }

    [Fact]
    public async Task All_time_stats_on_empty_store()
    {
        var all = await _store.GetAllTimeAsync();
        Assert.Equal(0, all.CakeDays);
        Assert.Null(all.BestDay);
        Assert.Null(all.WorstDay);
    }

    [Fact]
    public async Task Scorer_stats_group_by_user()
    {
        await _store.UpsertScoreAsync(Score(Friday1, "Kari", 10));
        await _store.UpsertScoreAsync(Score(Friday2, "Kari", 8));
        await _store.UpsertScoreAsync(Score(Friday2, "Ola", 2));

        var scorers = await _store.GetScorerStatsAsync();
        Assert.Equal(2, scorers.Count);
        var kari = scorers.Single(s => s.UserName == "Kari");
        Assert.Equal(2, kari.Count);
        Assert.Equal(9.0, kari.Average);
        Assert.Equal(1, scorers.Single(s => s.UserName == "Ola").Count);
    }

    [Fact]
    public async Task Conversations_and_posted_cards_round_trip()
    {
        await _store.SaveConversationAsync("19:abc@thread.tacv2", "{\"a\":1}");
        await _store.SaveConversationAsync("19:abc@thread.tacv2", "{\"a\":2}");
        await _store.SaveConversationAsync("a:personal", "{}");

        var conversations = await _store.GetConversationsAsync();
        Assert.Equal(2, conversations.Count);
        Assert.Equal("{\"a\":2}", conversations.Single(c => c.ConversationId == "19:abc@thread.tacv2").ReferenceJson);

        await _store.RemoveConversationAsync("a:personal");
        Assert.Single(await _store.GetConversationsAsync());

        Assert.Null(await _store.GetPostedCardAsync(Friday3, "19:abc@thread.tacv2"));
        await _store.SavePostedCardAsync(new PostedCard(Friday3, "19:abc@thread.tacv2", "activity-1"));
        var posted = await _store.GetPostedCardAsync(Friday3, "19:abc@thread.tacv2");
        Assert.Equal("activity-1", posted!.ActivityId);
    }

    [Fact]
    public async Task Open_posted_cards_disappear_once_the_day_is_closed()
    {
        await _store.SavePostedCardAsync(new PostedCard(Friday3, "channel-a", "act-a"));
        await _store.SavePostedCardAsync(new PostedCard(Friday3, "channel-b", "act-b"));
        await _store.SavePostedCardAsync(new PostedCard(Friday2, "channel-a", "act-old"));

        var open = await _store.GetOpenPostedCardsAsync(Friday3);
        Assert.Equal(["channel-a", "channel-b"], open.Select(p => p.ConversationId).Order());

        await _store.MarkClosedAsync(Friday3, "channel-a");
        await _store.MarkClosedAsync(Friday3, "channel-a"); // idempotent

        var remaining = await _store.GetOpenPostedCardsAsync(Friday3);
        var only = Assert.Single(remaining);
        Assert.Equal("channel-b", only.ConversationId);
        Assert.Equal("act-b", only.ActivityId);
    }
}
