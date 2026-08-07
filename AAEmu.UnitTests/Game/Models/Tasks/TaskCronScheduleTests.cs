using System;

using AAEmu.Game.Models.Tasks;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Tasks;

public class TaskCronScheduleTests
{
    private static readonly DateTime Noon = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DailyAtMidnight_IsTheNextMidnight()
    {
        Assert.True(TaskCronSchedule.TryParse("0 0 0 ? * * *", out var schedule));

        var next = schedule.GetNextOccurrence(Noon);

        Assert.Equal(new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void DailyAtATime_LaterToday_IsToday()
    {
        Assert.True(TaskCronSchedule.TryParse("0 30 18 ? * * *", out var schedule));

        var next = schedule.GetNextOccurrence(Noon);

        Assert.Equal(new DateTime(2026, 8, 7, 18, 30, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void DailyAtATime_AlreadyPassed_IsTomorrow()
    {
        Assert.True(TaskCronSchedule.TryParse("0 0 9 ? * * *", out var schedule));

        var next = schedule.GetNextOccurrence(Noon);

        Assert.Equal(new DateTime(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void ADateInTheYear_IsThatDate()
    {
        Assert.True(TaskCronSchedule.TryParse("0 15 6 25 12 ? *", out var schedule));

        var next = schedule.GetNextOccurrence(Noon);

        Assert.Equal(new DateTime(2026, 12, 25, 6, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void AWeekday_IsTheNextSuchDay()
    {
        // 2026-08-07 is a Friday; the next Monday is the 10th.
        Assert.True(TaskCronSchedule.TryParse("0 0 0 ? * 1 *", out var schedule));

        var next = schedule.GetNextOccurrence(Noon);

        Assert.Equal(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void TheOccurrenceIsAlwaysStrictlyLater()
    {
        Assert.True(TaskCronSchedule.TryParse("0 0 12 ? * * *", out var schedule));

        var next = schedule.GetNextOccurrence(Noon);

        Assert.Equal(new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc), next);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0 0 0 0 0 ?")]          // the "no schedule" marker the game emits
    [InlineData("0 0 */5 ? * * *")]      // steps are not understood
    [InlineData("0 0 1-4 ? * * *")]      // nor ranges
    [InlineData("0 0 1,2 ? * * *")]      // nor lists
    [InlineData("0 0 99 ? * * *")]       // out of range
    [InlineData("0 0 0 ? * * 2026")]     // a named year would need real handling
    [InlineData("0 0 ? ? * * *")]        // a time must be a time
    public void SomethingItCannotRead_IsRefused(string expression)
    {
        Assert.False(TaskCronSchedule.TryParse(expression, out _));
    }
}
