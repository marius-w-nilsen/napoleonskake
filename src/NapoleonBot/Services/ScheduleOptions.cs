using System.Globalization;

namespace NapoleonBot.Services;

/// <summary>When the scoring card is posted and how long scoring stays open. Bound from the "Schedule" section.</summary>
public sealed class ScheduleOptions
{
    public const string Section = "Schedule";

    public bool Enabled { get; set; } = true;
    public DayOfWeek CakeDay { get; set; } = DayOfWeek.Friday;

    /// <summary>Local time the card is posted and scoring opens, "HH:mm".</summary>
    public string PostTime { get; set; } = "12:00";

    /// <summary>Local time scoring closes, "HH:mm". Scores are rejected from this time on.</summary>
    public string ScoringCloses { get; set; } = "17:00";

    /// <summary>Post the final results card when scoring closes.</summary>
    public bool PostResultsAtClose { get; set; } = true;

    /// <summary>IANA or Windows time zone id.</summary>
    public string TimeZone { get; set; } = "Europe/Oslo";

    public TimeOnly PostTimeOfDay => TimeOnly.Parse(PostTime, CultureInfo.InvariantCulture);
    public TimeOnly ScoringClosesTimeOfDay => TimeOnly.Parse(ScoringCloses, CultureInfo.InvariantCulture);

    public TimeZoneInfo GetTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    /// <summary>The wall-clock "now" in the configured time zone.</summary>
    public DateTime LocalNow(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), GetTimeZone()).DateTime;
}
