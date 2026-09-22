using System.Globalization;

namespace NapoleonBot.Services;

public enum ScoringState
{
    Open,
    NotCakeDay,
    NotYetOpen,
    Closed,
}

/// <summary>Scoring is only allowed on cake day, from the moment the card is posted until closing time.</summary>
public static class ScoringWindow
{
    public static ScoringState Evaluate(DateTime localNow, ScheduleOptions schedule)
    {
        if (localNow.DayOfWeek != schedule.CakeDay)
            return ScoringState.NotCakeDay;

        var time = TimeOnly.FromDateTime(localNow);
        if (time < schedule.PostTimeOfDay)
            return ScoringState.NotYetOpen;
        if (time >= schedule.ScoringClosesTimeOfDay)
            return ScoringState.Closed;
        return ScoringState.Open;
    }

    public static string RejectionMessage(ScoringState state, DateTime localNow, ScheduleOptions schedule)
    {
        var opens = schedule.PostTimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
        var closes = schedule.ScoringClosesTimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
        var day = schedule.CakeDay.ToString();

        return state switch
        {
            ScoringState.NotYetOpen => $"⏳ Scoring opens at {opens}. Eat first, judge later.",
            ScoringState.Closed => $"🔒 Scoring closed at {closes}. Today's cake is history now – try `results` to see how it went.",
            ScoringState.NotCakeDay => $"📅 Scoring is only open on {day}s between {opens} and {closes}. Today is {localNow.DayOfWeek}; the cake is a memory. Try `results` or `history` instead.",
            _ => string.Empty,
        };
    }
}
