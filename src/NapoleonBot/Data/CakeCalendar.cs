namespace NapoleonBot.Data;

public static class CakeCalendar
{
    /// <summary>
    /// The cake day a score given on <paramref name="today"/> belongs to: today if it is a cake day,
    /// otherwise the most recent one. Scoring on Monday still counts for last Friday's cake.
    /// </summary>
    public static DateOnly MostRecentCakeDay(DateOnly today, DayOfWeek cakeDay)
    {
        var daysBack = ((int)today.DayOfWeek - (int)cakeDay + 7) % 7;
        return today.AddDays(-daysBack);
    }
}
