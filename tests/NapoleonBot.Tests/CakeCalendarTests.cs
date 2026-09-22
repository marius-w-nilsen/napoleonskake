using NapoleonBot.Data;

namespace NapoleonBot.Tests;

public class CakeCalendarTests
{
    [Theory]
    [InlineData("2026-09-18", "2026-09-18")] // Friday -> today
    [InlineData("2026-09-19", "2026-09-18")] // Saturday -> yesterday
    [InlineData("2026-09-21", "2026-09-18")] // Monday -> last Friday
    [InlineData("2026-09-24", "2026-09-18")] // Thursday -> six days back
    [InlineData("2026-09-25", "2026-09-25")] // next Friday -> itself
    public void Most_recent_friday(string today, string expected)
    {
        var result = CakeCalendar.MostRecentCakeDay(DateOnly.Parse(today), DayOfWeek.Friday);
        Assert.Equal(DateOnly.Parse(expected), result);
    }

    [Fact]
    public void Supports_other_cake_days()
    {
        var result = CakeCalendar.MostRecentCakeDay(DateOnly.Parse("2026-09-18"), DayOfWeek.Wednesday);
        Assert.Equal(DateOnly.Parse("2026-09-16"), result);
    }
}
