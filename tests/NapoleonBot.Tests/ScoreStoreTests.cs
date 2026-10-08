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
        Assert.True(await _store.UpsertScoreAsync(Score(Friday3, "Kari", 3)));
        Assert.False(await _store.UpsertScoreAsync(Score(Friday3, "Kari", 6, "changed my mind")));

        var day = await _store.GetDayAsync(Friday3);
        var only = Assert.Single(day.Scores);
        Assert.Equal(6, only.Score);
        Assert.Equal("changed my mind", only.Comment);
        Assert.Equal(6.0, day.Average);
    }

    [Fact]
    public async Task Rejects_scores_outside_the_die()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.UpsertScoreAsync(Score(Friday3, "Ola", 7)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.UpsertScoreAsync(Score(Friday3, "Ola", 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.UpsertScoreAsync(Score(Friday3, "Ola", 6) with { Modifier = 1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.UpsertScoreAsync(Score(Friday3, "Ola", 1) with { Modifier = -1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.UpsertScoreAsync(Score(Friday3, "Ola", 4) with { Modifier = 2 }));
    }

    [Fact]
    public async Task Modifiers_round_trip_and_count_a_quarter_in_averages()
    {
        await _store.UpsertScoreAsync(Score(Friday3, "Kari", 5) with { Modifier = 1 });   // 5.25
        await _store.UpsertScoreAsync(Score(Friday3, "Ola", 3) with { Modifier = -1 });   // 2.75
        await _store.UpsertScoreAsync(Score(Friday2, "Kari", 4));                          // 4.0

        var day = await _store.GetDayAsync(Friday3);
        Assert.Equal(1, day.Scores.Single(s => s.UserName == "Kari").Modifier);
        Assert.Equal("⚄ 5+", day.Scores.Single(s => s.UserName == "Kari").Display);
        Assert.Equal("⚂ 3-", day.Scores.Single(s => s.UserName == "Ola").Display);
        Assert.Equal(4.0, day.Average, precision: 6);

        var kari = (await _store.GetScorerStatsAsync()).Single(s => s.UserName == "Kari");
        Assert.Equal((5.25 + 4.0) / 2, kari.Average, precision: 6);

        var all = await _store.GetAllTimeAsync();
        Assert.Equal((5.25 + 2.75 + 4.0) / 3, all.OverallAverage, precision: 6);
    }

    [Fact]
    public async Task Adds_modifier_column_to_a_database_from_before_the_feature()
    {
        var oldDb = Path.Combine(Path.GetTempPath(), $"napoleon-old-{Guid.NewGuid():N}.db");
        try
        {
            await using (var conn = new SqliteConnection($"Data Source={oldDb};Pooling=False"))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE scores (
                        cake_date TEXT NOT NULL, user_id TEXT NOT NULL, user_name TEXT NOT NULL,
                        score INTEGER NOT NULL CHECK (score BETWEEN 1 AND 10), comment TEXT, created_at TEXT NOT NULL,
                        PRIMARY KEY (cake_date, user_id));
                    INSERT INTO scores VALUES ('2026-09-18', 'kari', 'Kari', 4, NULL, '2026-09-18T12:00:00+00:00');
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var store = new ScoreStore($"Data Source={oldDb};Pooling=False");
            var day = await store.GetDayAsync(new DateOnly(2026, 9, 18));
            var kari = Assert.Single(day.Scores);
            Assert.Equal(4, kari.Score);
            Assert.Equal(0, kari.Modifier);

            await store.UpsertScoreAsync(Score(new DateOnly(2026, 9, 18), "Kari", 4) with { Modifier = 1 });
            Assert.Equal(4.25, (await store.GetDayAsync(new DateOnly(2026, 9, 18))).Average);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(oldDb);
        }
    }

    [Fact]
    public async Task Day_average_and_empty_day()
    {
        await _store.UpsertScoreAsync(Score(Friday3, "Kari", 5));
        await _store.UpsertScoreAsync(Score(Friday3, "Ola", 2));
        await _store.UpsertScoreAsync(Score(Friday3, "Per", 6));

        var day = await _store.GetDayAsync(Friday3);
        Assert.Equal(3, day.Count);
        Assert.Equal(13 / 3.0, day.Average, precision: 6);

        var empty = await _store.GetDayAsync(Friday2);
        Assert.Equal(0, empty.Count);
        Assert.Equal(0, empty.Average);
    }

    [Fact]
    public async Task History_is_newest_first_and_limited()
    {
        await _store.UpsertScoreAsync(Score(Friday1, "Kari", 2));
        await _store.UpsertScoreAsync(Score(Friday2, "Kari", 4));
        await _store.UpsertScoreAsync(Score(Friday2, "Ola", 6));
        await _store.UpsertScoreAsync(Score(Friday3, "Kari", 6));

        var history = await _store.GetHistoryAsync(2);
        Assert.Equal([Friday3, Friday2], history.Select(d => d.CakeDate));
        Assert.Equal(5.0, history[1].Average);
    }

    [Fact]
    public async Task All_time_stats_find_best_and_worst_friday()
    {
        await _store.UpsertScoreAsync(Score(Friday1, "Kari", 1));
        await _store.UpsertScoreAsync(Score(Friday1, "Ola", 2));
        await _store.UpsertScoreAsync(Score(Friday2, "Kari", 6));
        await _store.UpsertScoreAsync(Score(Friday3, "Kari", 4));

        var all = await _store.GetAllTimeAsync();
        Assert.Equal(3, all.CakeDays);
        Assert.Equal(4, all.TotalScores);
        Assert.Equal(13 / 4.0, all.OverallAverage, precision: 6);
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
        await _store.UpsertScoreAsync(Score(Friday1, "Kari", 6));
        await _store.UpsertScoreAsync(Score(Friday2, "Kari", 4));
        await _store.UpsertScoreAsync(Score(Friday2, "Ola", 1));

        var scorers = await _store.GetScorerStatsAsync();
        Assert.Equal(2, scorers.Count);
        var kari = scorers.Single(s => s.UserName == "Kari");
        Assert.Equal(2, kari.Count);
        Assert.Equal(5.0, kari.Average);
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
