using System;
using System.Diagnostics;
using System.Threading;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Tasks;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The scheduler is what everything else in the server is booked on, so the promises it makes are
/// worth holding it to: things run near their time, repeats keep repeating, cancelling stops them,
/// and one task falling over does not take the workers with it.
/// </summary>
[Collection("TaskManager")]
public class TaskManagerTests : IDisposable
{
    private readonly TaskManager _taskManager;

    public TaskManagerTests()
    {
        _taskManager = TaskManager.Instance;
        _taskManager.Initialize();
        _taskManager.Start();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private sealed class CountingTask : Task
    {
        private readonly ManualResetEventSlim _ran = new(false);
        private int _runs;
        private readonly Action _onExecute;

        public CountingTask(Action onExecute = null) => _onExecute = onExecute;

        public int Runs => Volatile.Read(ref _runs);

        public override void Execute()
        {
            Interlocked.Increment(ref _runs);
            _ran.Set();
            _onExecute?.Invoke();
        }

        public bool WaitForRun(int milliseconds) => _ran.Wait(milliseconds);
    }

    [Fact]
    public void ATaskRunsAtAboutTheTimeItWasBookedFor()
    {
        var task = new CountingTask();
        var clock = Stopwatch.StartNew();

        _taskManager.Schedule(task, TimeSpan.FromMilliseconds(200));

        Assert.True(task.WaitForRun(5000), "the task never ran");
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds >= 150, $"it ran far too early, after {clock.ElapsedMilliseconds} ms");
        Assert.True(clock.ElapsedMilliseconds < 3000, $"it ran far too late, after {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ATaskBookedForThePastRunsStraightAway()
    {
        // A buff with no duration asks to be dispelled in -1 ms; that is how it ends at all.
        var task = new CountingTask();

        _taskManager.Schedule(task, TimeSpan.FromMilliseconds(-1));

        Assert.True(task.WaitForRun(5000), "a delay in the past should mean now");
    }

    [Fact]
    public void ARepeatingTaskKeepsRunning()
    {
        var task = new CountingTask();

        _taskManager.Schedule(task, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (task.Runs < 3 && DateTime.UtcNow < deadline)
            Thread.Sleep(20);

        Assert.True(task.Runs >= 3, $"it only ran {task.Runs} times");
        task.Cancel();
    }

    [Fact]
    public void ARepeatCanBeAskedForAFixedNumberOfTimes()
    {
        var task = new CountingTask();

        _taskManager.Schedule(task, TimeSpan.Zero, TimeSpan.FromMilliseconds(20), 3);

        Thread.Sleep(1000);

        Assert.Equal(3, task.Runs);
    }

    [Fact]
    public void ACancelledTaskDoesNotRun()
    {
        var task = new CountingTask();

        _taskManager.Schedule(task, TimeSpan.FromMilliseconds(300));
        Assert.True(task.Cancel());

        Thread.Sleep(1200);

        Assert.Equal(0, task.Runs);
    }

    [Fact]
    public void ATaskBookedAgainAfterBeingCancelledStillRuns()
    {
        // A doodad's respawn timer is the very task its despawn cancelled.
        var task = new CountingTask();

        _taskManager.Schedule(task, TimeSpan.FromSeconds(30));
        task.Cancel();
        _taskManager.Schedule(task, TimeSpan.FromMilliseconds(50));

        Assert.True(task.WaitForRun(5000), "booking it again should mean it runs");
    }

    [Fact]
    public void BookingATaskAgainReplacesTheEarlierBooking()
    {
        var task = new CountingTask();

        _taskManager.Schedule(task, TimeSpan.FromMilliseconds(50));
        _taskManager.Schedule(task, TimeSpan.FromMilliseconds(100));

        Thread.Sleep(1200);

        Assert.Equal(1, task.Runs);
    }

    [Fact]
    public void ATaskThatCancelsItselfWhileRunningIsLetGo()
    {
        CountingTask task = null;
        task = new CountingTask(() => task.Cancel());

        _taskManager.Schedule(task, TimeSpan.Zero, TimeSpan.FromMilliseconds(20));

        Assert.True(task.WaitForRun(5000));
        Thread.Sleep(500);

        Assert.Equal(1, task.Runs);
    }

    [Fact]
    public void ATaskThatThrowsDoesNotStopTheOnesAfterIt()
    {
        var thrower = new CountingTask(() => throw new InvalidOperationException("on purpose"));
        var follower = new CountingTask();

        _taskManager.Schedule(thrower, TimeSpan.Zero);
        _taskManager.Schedule(follower, TimeSpan.FromMilliseconds(100));

        Assert.True(follower.WaitForRun(5000), "a throwing task took the worker with it");
    }

    [Fact]
    public void NoTaskAtAllIsRefusedQuietly()
    {
        var before = _taskManager.ScheduleRequestCount;

        _taskManager.Schedule(null, TimeSpan.FromMilliseconds(10));

        Assert.Equal(before + 1, _taskManager.ScheduleRequestCount);
    }

    [Fact]
    public void ADelayBeyondReasonIsRefused()
    {
        var task = new CountingTask();

        _taskManager.Schedule(task, TimeSpan.MaxValue);

        Thread.Sleep(300);
        Assert.Equal(0, task.Runs);
    }

    [Fact]
    public void WorkIsGotThroughFasterThanTheWorldMakesIt()
    {
        // The world books about 2 300 a second with nobody online, nearly all doodads changing
        // phase. The old scheduler managed about 1 900 and fell behind for good. This asks for
        // several times the offered load, so that a busy server still has room.
        const int count = 40000;
        var done = new CountdownEvent(count);
        var tasks = new CountingTask[count];
        for (var i = 0; i < count; i++)
            tasks[i] = new CountingTask(() => done.Signal());

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
            _taskManager.Schedule(tasks[i], TimeSpan.Zero);

        Assert.True(done.Wait(TimeSpan.FromSeconds(30)),
            $"only {count - done.CurrentCount} of {count} tasks ran within thirty seconds");
        clock.Stop();

        var perSecond = count / Math.Max(0.001, clock.Elapsed.TotalSeconds);
        Assert.True(perSecond > 20000,
            $"got through {perSecond:F0} tasks a second, which leaves no room above what the world asks for");
    }

    [Fact]
    public void ManyBookingsAreTakenQuickly()
    {
        // The world books a few thousand a second before anyone is online. Booking has to be
        // cheap enough that the rate is never the thing that limits the server.
        const int count = 20000;
        var tasks = new CountingTask[count];
        for (var i = 0; i < count; i++)
            tasks[i] = new CountingTask();

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
            _taskManager.Schedule(tasks[i], TimeSpan.FromHours(1));
        clock.Stop();

        foreach (var task in tasks)
            task.Cancel();

        Assert.True(clock.ElapsedMilliseconds < 4000,
            $"booking {count} tasks took {clock.ElapsedMilliseconds} ms, which is too slow to keep up");
    }
}
