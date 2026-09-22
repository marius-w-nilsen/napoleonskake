using NapoleonBot.Services;

namespace NapoleonBot.Tests;

public class ScoringWindowTests
{
    private static readonly ScheduleOptions Schedule = new() { CakeDay = DayOfWeek.Friday, PostTime = "12:00", ScoringCloses = "17:00" };

    [Theory]
    [InlineData("2026-09-18T12:00:00", ScoringState.Open)]       // opens exactly at 12:00
    [InlineData("2026-09-18T14:30:00", ScoringState.Open)]
    [InlineData("2026-09-18T16:59:59", ScoringState.Open)]
    [InlineData("2026-09-18T17:00:00", ScoringState.Closed)]     // closes exactly at 17:00
    [InlineData("2026-09-18T23:00:00", ScoringState.Closed)]
    [InlineData("2026-09-18T08:00:00", ScoringState.NotYetOpen)]
    [InlineData("2026-09-18T11:59:59", ScoringState.NotYetOpen)]
    [InlineData("2026-09-17T13:00:00", ScoringState.NotCakeDay)] // Thursday
    [InlineData("2026-09-19T13:00:00", ScoringState.NotCakeDay)] // Saturday
    [InlineData("2026-09-21T13:00:00", ScoringState.NotCakeDay)] // Monday
    public void Only_open_on_cake_day_between_opening_and_closing(string localNow, ScoringState expected)
    {
        Assert.Equal(expected, ScoringWindow.Evaluate(DateTime.Parse(localNow), Schedule));
    }

    [Fact]
    public void Rejection_messages_name_the_times()
    {
        var friday = DateTime.Parse("2026-09-18T08:00:00");
        Assert.Contains("12:00", ScoringWindow.RejectionMessage(ScoringState.NotYetOpen, friday, Schedule));
        Assert.Contains("17:00", ScoringWindow.RejectionMessage(ScoringState.Closed, friday.AddHours(10), Schedule));

        var monday = DateTime.Parse("2026-09-21T13:00:00");
        var message = ScoringWindow.RejectionMessage(ScoringState.NotCakeDay, monday, Schedule);
        Assert.Contains("Fridays", message);
        Assert.Contains("Monday", message);
    }

    [Fact]
    public void Options_parse_times()
    {
        Assert.Equal(new TimeOnly(12, 0), Schedule.PostTimeOfDay);
        Assert.Equal(new TimeOnly(17, 0), Schedule.ScoringClosesTimeOfDay);
    }
}
