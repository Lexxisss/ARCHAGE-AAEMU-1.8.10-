using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using NLog;
using Task = AAEmu.Game.Models.Tasks.Task;
using ThreadTask = System.Threading.Tasks.Task;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Runs the game's booked work: one thread watching the clock, a few running what is due.
/// </summary>
/// <remarks>
/// This used to be Quartz. Quartz is built for jobs measured in minutes, and it charges for it -
/// an existence check, a job and a trigger object, and an insert into a locking store, for every
/// booking. The world books about two and a half thousand a second before a single player is
/// online, nearly all of them doodads changing phase, and eight Quartz threads could push about
/// nineteen hundred. The quarter it fell short by never came back: the queue grew without bound,
/// everything booked arrived seconds late, and only a restart cleared it.
///
/// What replaces it is a heap keyed by due time and a channel of ready work. Booking is a heap
/// insert, cancelling is a flag, and idling costs nothing at all - the watcher sleeps until the
/// nearest due time rather than sweeping a list. There is no ceiling worth speaking of at the
/// loads this server sees.
/// </remarks>
public class TaskManager : Singleton<TaskManager>, ITaskManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Longest delay worth booking; beyond this the caller has miscalculated.</summary>
    private static readonly TimeSpan MaxScheduleDelay = TimeSpan.FromDays(3650);

    /// <summary>Never sleep longer than this, so a clock change cannot strand the queue.</summary>
    private static readonly TimeSpan MaxWatcherSleep = TimeSpan.FromSeconds(1);

    private readonly object _queueLock = new();
    private readonly PriorityQueue<Booking, DateTime> _queue = new();
    private readonly BlockingCollection<Task> _ready = new(new ConcurrentQueue<Task>());

    private Thread _watcher;
    private Thread[] _workers;
    private CancellationTokenSource _shutdown;
    private bool _initialized;
    private int _started;
    private long _generation;
    private long _nextTaskId;

    private int _lastReportedRequestCount;
    private long _latenessTicks;
    private int _latenessSamples;
    private long _worstLatenessTicks;
    private readonly ConcurrentDictionary<string, int> _requestsByTaskName = new();

    public int ScheduleRequestCount { get; private set; }

    /// <summary>One booking of a task. Stale ones are recognised by their generation.</summary>
    private readonly record struct Booking(Task Task, long Generation);

    public int WorkerCount => _workers?.Length ?? 0;

    public System.Threading.Tasks.Task<int> GetExecutingJobsCount() => ThreadTask.FromResult(_ready.Count);

    /// <summary>How many bookings are waiting their turn.</summary>
    public System.Threading.Tasks.Task<int> GetScheduledJobCount()
    {
        lock (_queueLock)
        {
            return ThreadTask.FromResult(_queue.Count);
        }
    }

    public async IAsyncEnumerable<Task> GetExecutingTasks()
    {
        foreach (var task in _ready.ToArray())
            yield return task;

        await ThreadTask.CompletedTask;
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        _shutdown = new CancellationTokenSource();

        // More hands than the machine has cores buys nothing; fewer than four leaves no room when
        // one of them is held up by a slow packet or a database write.
        var configured = AppConfiguration.Instance?.MaxConcurencyThreadPool ?? 0;
        var workerCount = Math.Max(4, Math.Max(configured, Environment.ProcessorCount));

        _workers = new Thread[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            _workers[i] = new Thread(RunWorker)
            {
                Name = $"TaskWorker{i}",
                IsBackground = true
            };
        }

        _watcher = new Thread(RunWatcher)
        {
            Name = "TaskWatcher",
            IsBackground = true
        };

        _initialized = true;
        Logger.Info("Task manager ready with {0} worker threads", workerCount);
    }

    public void Start()
    {
        if (!_initialized)
            Initialize();

        // Starting a thread that is already running throws, so saying start twice would have
        // brought the server down rather than doing nothing.
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        _watcher.Start();
        foreach (var worker in _workers)
            worker.Start();

        TickManager.Instance.OnTick.Subscribe(OnLoadReportTick, TimeSpan.FromSeconds(10), true);
    }

    public void Stop()
    {
        _shutdown?.Cancel();
        lock (_queueLock)
        {
            _queue.Clear();
            Monitor.PulseAll(_queueLock);
        }

        _ready.CompleteAdding();
    }

    public void Schedule(Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        ScheduleRequestCount++;
        if (task != null)
            _requestsByTaskName.AddOrUpdate(task.Name, 1, static (_, current) => current + 1);

        if (_shutdown == null || _shutdown.IsCancellationRequested)
            return;

        if (task == null)
        {
            Logger.Warn(
                "Task.Schedule called with no task from {0}:{1} (start {2}, repeat {3}, count {4})",
                Path.GetFileName(callerFile), callerLine, startTime, repeatInterval, count);
            return;
        }

        var delay = startTime ?? TimeSpan.Zero;
        if (delay > MaxScheduleDelay)
        {
            Logger.Warn("Task {0} from {1}:{2} asked to start in {3}; ignoring the request",
                task.Name, Path.GetFileName(callerFile), callerLine, delay);
            return;
        }

        // A delay in the past means now, and callers rely on it: a buff with no duration asks to
        // be dispelled in -1 ms, which is how it comes to an end at all.
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        task.CronSchedule = null;
        task.RepeatInterval = repeatInterval ?? TimeSpan.Zero;
        task.RepeatCount = task.RepeatInterval == TimeSpan.Zero ? 1 : count;
        task.MaxCount = repeatInterval == null ? 0 : count;
        task.ExecuteCount = 0;
        task.ScheduleTime = Helpers.UnixTimeNowInMilli();

        Enqueue(task, DateTime.UtcNow + delay);
    }

    public void CronSchedule(Task task, string cronExpression, TimeSpan? startTime = null,
        TimeSpan? repeatInterval = null, int count = -1,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        ScheduleRequestCount++;
        if (task != null)
            _requestsByTaskName.AddOrUpdate(task.Name, 1, static (_, current) => current + 1);

        if (_shutdown == null || _shutdown.IsCancellationRequested)
            return;

        if (task == null)
        {
            Logger.Warn(
                "Task.CronSchedule called with no task from {0}:{1} (cron {2})",
                Path.GetFileName(callerFile), callerLine, cronExpression);
            return;
        }

        if (!Models.Tasks.TaskCronSchedule.TryParse(cronExpression, out var schedule))
        {
            Logger.Warn("Task {0} from {1}:{2} was given a schedule that cannot be read: \"{3}\"",
                task.Name, Path.GetFileName(callerFile), callerLine, cronExpression);
            return;
        }

        var from = DateTime.UtcNow + (startTime ?? TimeSpan.Zero);
        var next = schedule.GetNextOccurrence(from);
        if (next == null)
        {
            Logger.Warn("Task {0} from {1}:{2} has a schedule that never comes round: \"{3}\"",
                task.Name, Path.GetFileName(callerFile), callerLine, cronExpression);
            return;
        }

        task.CronSchedule = schedule;
        task.RepeatInterval = TimeSpan.Zero;
        task.RepeatCount = count;
        task.MaxCount = count;
        task.ExecuteCount = 0;
        task.ScheduleTime = Helpers.UnixTimeNowInMilli();

        Enqueue(task, next.Value);
    }

    private void Enqueue(Task task, DateTime dueTime)
    {
        lock (_queueLock)
        {
            // Booking means it is meant to run. The same object is often booked again after being
            // cancelled - a doodad's respawn timer is the very task its despawn cancelled.
            task.Cancelled = false;

            // A plain counter. The id is only ever read back in diagnostics, so borrowing one from
            // the shared id manager bought nothing and put a lock in the busiest path there is.
            if (task.Id == 0)
                task.Id = unchecked((uint)Interlocked.Increment(ref _nextTaskId));

            task.DueTime = dueTime;
            task.Generation = ++_generation;

            _queue.Enqueue(new Booking(task, task.Generation), dueTime);

            // Only stir the watcher when this lands before whatever it is already waiting for.
            if (_queue.TryPeek(out _, out var head) && head >= dueTime)
                Monitor.Pulse(_queueLock);
        }
    }

    private void RunWatcher()
    {
        var token = _shutdown.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                Booking booking;
                lock (_queueLock)
                {
                    while (!_queue.TryPeek(out _, out var due))
                    {
                        // Nothing booked at all: wait to be woken.
                        Monitor.Wait(_queueLock, MaxWatcherSleep);
                        if (token.IsCancellationRequested)
                            return;
                    }

                    _queue.TryPeek(out _, out var head);
                    var wait = head - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        Monitor.Wait(_queueLock, wait < MaxWatcherSleep ? wait : MaxWatcherSleep);
                        continue;
                    }

                    booking = _queue.Dequeue();
                }

                var task = booking.Task;
                if (task == null || task.Cancelled || task.Generation != booking.Generation)
                    continue; // cancelled, or replaced by a newer booking

                RecordLateness(DateTime.UtcNow - task.DueTime);
                _ready.Add(task, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return; // the ready queue was closed; that is the shutdown
            }
            catch (Exception e)
            {
                Logger.Error(e, "The task watcher tripped over something");
            }
        }
    }

    private void RunWorker()
    {
        var token = _shutdown.Token;
        try
        {
            foreach (var task in _ready.GetConsumingEnumerable(token))
            {
                if (task.Cancelled)
                    continue;

                try
                {
                    task.Execute();
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Task {0} threw while running", task.Name);
                }

                task.ExecuteCount++;
                Reschedule(task);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // The collection was completed while we were waiting on it; that is the shutdown.
        }
    }

    /// <summary>Books the next run of a repeating task, if it has one left.</summary>
    private void Reschedule(Task task)
    {
        if (task.Cancelled)
            return;

        if (task.RepeatCount >= 0 && task.ExecuteCount >= task.RepeatCount)
        {
            Release(task);
            return;
        }

        DateTime next;
        if (task.CronSchedule != null)
        {
            var occurrence = task.CronSchedule.GetNextOccurrence(DateTime.UtcNow);
            if (occurrence == null)
            {
                Release(task);
                return;
            }

            next = occurrence.Value;
        }
        else if (task.RepeatInterval > TimeSpan.Zero)
        {
            // Counted from when it was meant to run, not from now, so a repeating task does not
            // drift later and later under load.
            next = task.DueTime + task.RepeatInterval;
            var now = DateTime.UtcNow;
            if (next < now)
                next = now;
        }
        else
        {
            Release(task);
            return;
        }

        lock (_queueLock)
        {
            if (task.Cancelled)
                return;

            task.DueTime = next;
            task.Generation = ++_generation;
            _queue.Enqueue(new Booking(task, task.Generation), next);
            if (_queue.TryPeek(out _, out var head) && head >= next)
                Monitor.Pulse(_queueLock);
        }
    }

    /// <summary>Marks a task cancelled once and hands its id back exactly once.</summary>
    private static void Release(Task task)
    {
        if (task.Cancelled)
            return;

        task.Cancelled = true;
        task.Id = 0;
    }

    /// <summary>
    /// Stops a task at once. Its booking stays in the queue until its turn comes and is dropped
    /// then, which costs nothing and keeps cancelling free of the queue lock's contention.
    /// </summary>
    public bool CancelNow(Task task)
    {
        if (task == null)
            return true;

        Release(task);
        return true;
    }

    public void CancelWithoutWaiting(Task task) => CancelNow(task);

    public System.Threading.Tasks.Task<bool> Cancel(Task task) => ThreadTask.FromResult(CancelNow(task));

    private void RecordLateness(TimeSpan lateness)
    {
        if (lateness < TimeSpan.Zero)
            lateness = TimeSpan.Zero;

        Interlocked.Add(ref _latenessTicks, lateness.Ticks);
        Interlocked.Increment(ref _latenessSamples);

        var ticks = lateness.Ticks;
        var worst = Interlocked.Read(ref _worstLatenessTicks);
        while (ticks > worst)
        {
            var seen = Interlocked.CompareExchange(ref _worstLatenessTicks, ticks, worst);
            if (seen == worst)
                break;
            worst = seen;
        }
    }

    private void OnLoadReportTick(TimeSpan delta) => ReportLoad();

    /// <summary>
    /// Reports what the scheduler is carrying and how late it is running.
    /// </summary>
    /// <remarks>
    /// Lateness is the number that matters: a queue of any size is fine as long as what comes out
    /// of it comes out on time. The rest is there to say why, when it is not.
    /// </remarks>
    public void ReportLoad()
    {
        try
        {
            int waiting;
            lock (_queueLock)
                waiting = _queue.Count;

            var requests = ScheduleRequestCount;
            var since = requests - _lastReportedRequestCount;
            _lastReportedRequestCount = requests;

            var samples = Interlocked.Exchange(ref _latenessSamples, 0);
            var totalTicks = Interlocked.Exchange(ref _latenessTicks, 0);
            var worstTicks = Interlocked.Exchange(ref _worstLatenessTicks, 0);
            var average = samples > 0 ? TimeSpan.FromTicks(totalTicks / samples) : TimeSpan.Zero;
            var worst = TimeSpan.FromTicks(worstTicks);

            var level = worst > TimeSpan.FromSeconds(1) ? LogLevel.Warn : LogLevel.Info;

            Logger.Log(level,
                "Scheduler load: waiting {0}, ready {1}, workers {2}, ran {3}, late by {4:F0} ms on average and {5:F0} ms at worst, new requests {6}",
                waiting, _ready.Count, WorkerCount, samples,
                average.TotalMilliseconds, worst.TotalMilliseconds, since);

            var byName = new List<KeyValuePair<string, int>>();
            foreach (var name in _requestsByTaskName.Keys)
            {
                if (_requestsByTaskName.TryRemove(name, out var count) && count > 0)
                    byName.Add(new KeyValuePair<string, int>(name, count));
            }

            if (byName.Count > 0)
            {
                byName.Sort((left, right) => right.Value.CompareTo(left.Value));
                var top = string.Join(", ", byName.Take(8).Select(x => $"{x.Key} {x.Value}"));
                Logger.Log(level, "Scheduler load by task: {0}{1}",
                    top, byName.Count > 8 ? $", and {byName.Count - 8} more kinds" : string.Empty);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Could not read the scheduler load");
        }
    }
}
