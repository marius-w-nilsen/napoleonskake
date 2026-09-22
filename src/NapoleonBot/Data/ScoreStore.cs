using Microsoft.Data.Sqlite;

namespace NapoleonBot.Data;

/// <summary>SQLite-backed storage for scores, posted cards, and the conversations the bot has been added to.</summary>
public sealed class ScoreStore
{
    // Note: the database lives in "db/", not "data/". On case-insensitive file systems "data/" would collide with this source folder.
    public const string DefaultConnectionString = "Data Source=db/napoleon.db";

    private const string DateFormat = "yyyy-MM-dd";
    private readonly string _connectionString;

    public ScoreStore(string connectionString)
    {
        _connectionString = connectionString;
        EnsureDirectoryExists(connectionString);
        EnsureCreated();
    }

    public static ScoreStore FromConfiguration(IConfiguration configuration) =>
        new(configuration["Storage:ConnectionString"] ?? DefaultConnectionString);

    private static void EnsureDirectoryExists(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrEmpty(builder.DataSource) || builder.DataSource == ":memory:")
            return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private void EnsureCreated()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS scores (
                cake_date  TEXT    NOT NULL,
                user_id    TEXT    NOT NULL,
                user_name  TEXT    NOT NULL,
                score      INTEGER NOT NULL CHECK (score BETWEEN 1 AND 10),
                comment    TEXT,
                created_at TEXT    NOT NULL,
                PRIMARY KEY (cake_date, user_id)
            );
            CREATE TABLE IF NOT EXISTS conversations (
                conversation_id TEXT PRIMARY KEY,
                reference_json  TEXT NOT NULL,
                updated_at      TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS posted_cards (
                cake_date       TEXT NOT NULL,
                conversation_id TEXT NOT NULL,
                activity_id     TEXT NOT NULL,
                PRIMARY KEY (cake_date, conversation_id)
            );
            CREATE TABLE IF NOT EXISTS closed_days (
                cake_date       TEXT NOT NULL,
                conversation_id TEXT NOT NULL,
                closed_at       TEXT NOT NULL,
                PRIMARY KEY (cake_date, conversation_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // ---- Scores -------------------------------------------------------------------------

    /// <summary>
    /// Inserts or replaces the user's score for the cake day.
    /// Returns true when it was a new score, false when it replaced an earlier one.
    /// </summary>
    public async Task<bool> UpsertScoreAsync(ScoreEntry entry, CancellationToken ct = default)
    {
        if (entry.Score is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(entry), "Score must be between 1 and 10.");

        await using var conn = Open();
        await using var exists = conn.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM scores WHERE cake_date = $d AND user_id = $u";
        exists.Parameters.AddWithValue("$d", entry.CakeDate.ToString(DateFormat));
        exists.Parameters.AddWithValue("$u", entry.UserId);
        var isUpdate = (long)(await exists.ExecuteScalarAsync(ct))! > 0;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scores (cake_date, user_id, user_name, score, comment, created_at)
            VALUES ($d, $u, $n, $s, $c, $t)
            ON CONFLICT (cake_date, user_id) DO UPDATE SET
                user_name  = excluded.user_name,
                score      = excluded.score,
                comment    = excluded.comment,
                created_at = excluded.created_at
            """;
        cmd.Parameters.AddWithValue("$d", entry.CakeDate.ToString(DateFormat));
        cmd.Parameters.AddWithValue("$u", entry.UserId);
        cmd.Parameters.AddWithValue("$n", entry.UserName);
        cmd.Parameters.AddWithValue("$s", entry.Score);
        cmd.Parameters.AddWithValue("$c", (object?)entry.Comment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", entry.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        return !isUpdate;
    }

    public async Task<DaySummary> GetDayAsync(DateOnly cakeDate, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cake_date, user_id, user_name, score, comment, created_at
            FROM scores WHERE cake_date = $d
            ORDER BY created_at
            """;
        cmd.Parameters.AddWithValue("$d", cakeDate.ToString(DateFormat));
        var scores = await ReadScoresAsync(cmd, ct);
        return new DaySummary(cakeDate, scores);
    }

    /// <summary>The most recent cake days that received at least one score, newest first.</summary>
    public async Task<IReadOnlyList<DaySummary>> GetHistoryAsync(int lastN, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cake_date, user_id, user_name, score, comment, created_at
            FROM scores
            WHERE cake_date IN (SELECT DISTINCT cake_date FROM scores ORDER BY cake_date DESC LIMIT $n)
            ORDER BY cake_date DESC, created_at
            """;
        cmd.Parameters.AddWithValue("$n", lastN);
        var scores = await ReadScoresAsync(cmd, ct);
        return GroupByDay(scores);
    }

    public async Task<IReadOnlyList<ScorerStats>> GetScorerStatsAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        // user_name is not aggregated; SQLite returns one of the rows' names, and the upsert keeps names current anyway.
        cmd.CommandText = """
            SELECT user_id, user_name, COUNT(*), AVG(score)
            FROM scores
            GROUP BY user_id
            ORDER BY COUNT(*) DESC, AVG(score) DESC
            """;
        var result = new List<ScorerStats>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new ScorerStats(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetDouble(3)));
        return result;
    }

    public async Task<AllTimeStats> GetAllTimeAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cake_date, user_id, user_name, score, comment, created_at
            FROM scores ORDER BY cake_date, created_at
            """;
        var scores = await ReadScoresAsync(cmd, ct);
        var days = GroupByDay(scores);
        if (days.Count == 0)
            return new AllTimeStats(0, 0, 0, null, null);

        return new AllTimeStats(
            CakeDays: days.Count,
            TotalScores: scores.Count,
            OverallAverage: scores.Average(s => s.Score),
            BestDay: days.OrderByDescending(d => d.Average).ThenByDescending(d => d.Count).First(),
            WorstDay: days.OrderBy(d => d.Average).ThenByDescending(d => d.Count).First());
    }

    private static async Task<List<ScoreEntry>> ReadScoresAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var result = new List<ScoreEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ScoreEntry(
                DateOnly.ParseExact(reader.GetString(0), DateFormat),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }
        return result;
    }

    private static IReadOnlyList<DaySummary> GroupByDay(IEnumerable<ScoreEntry> scores) =>
        scores.GroupBy(s => s.CakeDate)
              .OrderByDescending(g => g.Key)
              .Select(g => new DaySummary(g.Key, g.ToList()))
              .ToList();

    // ---- Conversations (for proactive Friday posts) ---------------------------------------

    public async Task SaveConversationAsync(string conversationId, string referenceJson, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversations (conversation_id, reference_json, updated_at)
            VALUES ($id, $json, $t)
            ON CONFLICT (conversation_id) DO UPDATE SET
                reference_json = excluded.reference_json,
                updated_at     = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$id", conversationId);
        cmd.Parameters.AddWithValue("$json", referenceJson);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveConversationAsync(string conversationId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM conversations WHERE conversation_id = $id";
        cmd.Parameters.AddWithValue("$id", conversationId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(string ConversationId, string ReferenceJson)>> GetConversationsAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT conversation_id, reference_json FROM conversations";
        var result = new List<(string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    // ---- Posted cards ------------------------------------------------------------------------

    public async Task SavePostedCardAsync(PostedCard card, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO posted_cards (cake_date, conversation_id, activity_id)
            VALUES ($d, $c, $a)
            ON CONFLICT (cake_date, conversation_id) DO UPDATE SET activity_id = excluded.activity_id
            """;
        cmd.Parameters.AddWithValue("$d", card.CakeDate.ToString(DateFormat));
        cmd.Parameters.AddWithValue("$c", card.ConversationId);
        cmd.Parameters.AddWithValue("$a", card.ActivityId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PostedCard?> GetPostedCardAsync(DateOnly cakeDate, string conversationId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT activity_id FROM posted_cards WHERE cake_date = $d AND conversation_id = $c";
        cmd.Parameters.AddWithValue("$d", cakeDate.ToString(DateFormat));
        cmd.Parameters.AddWithValue("$c", conversationId);
        var activityId = (string?)await cmd.ExecuteScalarAsync(ct);
        return activityId is null ? null : new PostedCard(cakeDate, conversationId, activityId);
    }

    /// <summary>Cards posted for the cake day whose conversation has not been closed yet.</summary>
    public async Task<IReadOnlyList<PostedCard>> GetOpenPostedCardsAsync(DateOnly cakeDate, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.conversation_id, p.activity_id
            FROM posted_cards p
            LEFT JOIN closed_days c ON c.cake_date = p.cake_date AND c.conversation_id = p.conversation_id
            WHERE p.cake_date = $d AND c.cake_date IS NULL
            """;
        cmd.Parameters.AddWithValue("$d", cakeDate.ToString(DateFormat));
        var result = new List<PostedCard>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new PostedCard(cakeDate, reader.GetString(0), reader.GetString(1)));
        return result;
    }

    public async Task MarkClosedAsync(DateOnly cakeDate, string conversationId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO closed_days (cake_date, conversation_id, closed_at)
            VALUES ($d, $c, $t)
            ON CONFLICT (cake_date, conversation_id) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("$d", cakeDate.ToString(DateFormat));
        cmd.Parameters.AddWithValue("$c", conversationId);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
