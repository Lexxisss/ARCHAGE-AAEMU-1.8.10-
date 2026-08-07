using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models;
using NLog;
using Quartz;
using Quartz.Impl;
using Quartz.Simpl;
using Task = AAEmu.Game.Models.Tasks.Task;
using ThreadTask = System.Threading.Tasks.Task;

namespace AAEmu.Game.Core.Managers;

public class TaskManager : Singleton<TaskManager>, ITaskManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _initialized = false;

    /// <summary>Longest delay worth booking; beyond this the caller has miscalculated.</summary>
    private static readonly TimeSpan MaxScheduleDelay = TimeSpan.FromDays(3650);

    private DefaultThreadPool _generalPool;
    private IScheduler _generalScheduler;

    public int ScheduleRequestCount { get; private set; }
    public async Task<int> GetExecutingJobsCount()
        => (await _generalScheduler.GetCurrentlyExecutingJobs()).Count;

    public async IAsyncEnumerable<Task> GetExecutingTasks()
    {
        var jobs = await _generalScheduler.GetCurrentlyExecutingJobs();
        foreach (var job in jobs)
            yield return (Task)job.JobDetail.JobDataMap.Get("Task");
    }

    public async void Initialize()
    {
        if (_initialized)
            return;

        _generalPool = new DefaultThreadPool();
        _generalPool.MaxConcurrency = AppConfiguration.Instance.MaxConcurencyThreadPool;
        _generalPool.Initialize();

        DirectSchedulerFactory
            .Instance
            .CreateScheduler("General Scheduler", "GeneralScheduler", _generalPool, new RAMJobStore());
        _generalScheduler = await DirectSchedulerFactory.Instance.GetScheduler("General Scheduler");
        _initialized = true;
    }

    public void Start()
    {
        _generalScheduler.Start();
    }

    public void Stop()
    {
        _generalScheduler?.Shutdown(true);
    }

    public async void Schedule(Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        this.ScheduleRequestCount++;

        if (_generalScheduler.IsShutdown)
            return;

        if (task == null)
        {
            // Nothing is scheduled and nothing breaks, but somebody upstream is holding a task
            // that has already been let go. The old message named neither the caller nor the
            // file, so the same line appeared hundreds of times with no way to find its source.
            Logger.Warn(
                "Task.Schedule called with no task from {0}:{1} (start {2}, repeat {3}, count {4})",
                Path.GetFileName(callerFile), callerLine, startTime, repeatInterval, count);
            return;
        }

        // This method is async void, so anything thrown past the first await lands on the thread
        // pool as an unhandled exception and ends the process. A caller asking for something the
        // scheduler cannot do must cost that caller its task, nothing more.
        try
        {
            await ScheduleInternal(task, startTime, repeatInterval, count, callerFile, callerLine);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Could not schedule {0} requested from {1}:{2}",
                task.Name, Path.GetFileName(callerFile), callerLine);
        }
    }

    private async ThreadTask ScheduleInternal(Task task, TimeSpan? startTime, TimeSpan? repeatInterval, int count,
        string callerFile, int callerLine)
    {
        // A delay is a delay, not a date. Anything that would run past the end of DateTime is a
        // caller's arithmetic gone wrong rather than a request worth honouring.
        if (startTime is { } requestedStart)
        {
            if (requestedStart > MaxScheduleDelay)
            {
                Logger.Warn("Task {0} from {1}:{2} asked to start in {3}; ignoring the request",
                    task.Name, Path.GetFileName(callerFile), callerLine, requestedStart);
                return;
            }

            // A delay in the past means now, and callers rely on it: a buff with no duration asks
            // to be dispelled in -1 ms, which is how it comes to an end at all. Refusing those
            // outright left such buffs on their owner for good.
            if (requestedStart < TimeSpan.Zero)
            {
                startTime = TimeSpan.Zero;
            }
        }

        // Booking a task means it is meant to run. The same object is often booked again after it
        // was cancelled - a doodad's respawn timer is the very task its despawn cancelled - and a
        // task left marked as cancelled is skipped by the job for good.
        task.Cancelled = false;

        var jobKey = new JobKey(string.Empty);
        do
        {
            task.Id = TaskIdManager.Instance.GetNextId();
            jobKey.Name = task.Name + task.Id;
            jobKey.Group = task.Name;
        }
        while (await _generalScheduler.CheckExists(jobKey));

        IJobDetail job;
        var newJob = task.JobDetail == null;
        if (newJob)
        {
            job = JobBuilder
                .Create<TaskJob>()
                .WithIdentity(task.Name + task.Id, task.Name)
                .Build();
            job.JobDataMap.Put("Logger", Logger);
            job.JobDataMap.Put("Task", task);
            task.JobDetail = job;
        }

        var triggerBuild = TriggerBuilder
            .Create()
            .WithIdentity(task.JobDetail.Key.Name, task.JobDetail.Key.Group);

        if (startTime == null)
            triggerBuild.StartNow();
        else
            triggerBuild.StartAt(DateTime.UtcNow.Add((TimeSpan)startTime));

        if (task.Scheduler == null)
        {
            triggerBuild.WithSimpleSchedule(scheduler =>
            {
                if (repeatInterval == null)
                    return;

                scheduler.WithInterval((TimeSpan)repeatInterval);

                if (count > 0)
                    scheduler.WithRepeatCount(count);
                else if (count == -1)
                    scheduler.RepeatForever();
            });
        }
        else
            triggerBuild.WithSchedule(task.Scheduler);

        triggerBuild.ForJob(task.JobDetail.Key);

        task.Trigger = triggerBuild.Build();
        task.ExecuteCount = 0;
        task.MaxCount = repeatInterval == null ? 0 : count;
        task.ScheduleTime = Helpers.UnixTimeNowInMilli();

        try
        {
            if (newJob)
            {
                try
                {
                    await _generalScheduler.ScheduleJob(task.JobDetail, task.Trigger);
                }
                catch (Exception e)
                {
                    Logger.Trace(e, "Rescheduling task");
                    try
                    {
                        await _generalScheduler.RescheduleJob(task.Trigger.Key, task.Trigger);
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Error scheduling task");
                    }
                }
            }
            else
            {
                try
                {
                    await _generalScheduler.RescheduleJob(task.Trigger.Key, task.Trigger);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Error scheduling task");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error scheduling task");
        }
    }

    public async void CronSchedule(Task task, string cronExpression, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        if (_generalScheduler.IsShutdown)
            return;

        if (task == null)
        {
            Logger.Warn(
                "Task.CronSchedule called with no task from {0}:{1} (cron {2}, start {3}, repeat {4}, count {5})",
                Path.GetFileName(callerFile), callerLine, cronExpression, startTime, repeatInterval, count);
            return;
        }

        // async void again: an escaping exception would end the process rather than the request.
        try
        {
            await CronScheduleInternal(task, cronExpression, startTime, repeatInterval, count);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Could not cron schedule {0} requested from {1}:{2}",
                task.Name, Path.GetFileName(callerFile), callerLine);
        }
    }

    private async ThreadTask CronScheduleInternal(Task task, string cronExpression, TimeSpan? startTime,
        TimeSpan? repeatInterval, int count)
    {
        //var _cron = "0 0 22-7 * * *";
        task.Id = TaskIdManager.Instance.GetNextId();
        while (await _generalScheduler.CheckExists(new JobKey(task.Name + task.Id, task.Name)))
            task.Id = TaskIdManager.Instance.GetNextId();

        IJobDetail job;
        var newJob = task.JobDetail == null;
        if (newJob)
        {
            job = JobBuilder
                .Create<TaskJob>()
                .WithIdentity(task.Name + task.Id, task.Name)
                .Build();
            job.JobDataMap.Put("Logger", Logger);
            job.JobDataMap.Put("Task", task);
            task.JobDetail = job;
        }

        var triggerBuild = TriggerBuilder
            .Create()
            .WithIdentity(task.JobDetail.Key.Name, task.JobDetail.Key.Group);

        if (startTime == null)
            triggerBuild.StartNow();
        else
            triggerBuild.StartAt(DateTime.UtcNow.Add((TimeSpan)startTime));

        if (task.Scheduler == null)
        {
            triggerBuild.WithCronSchedule(cronExpression);
        }
        else
            triggerBuild.WithSchedule(CronScheduleBuilder.CronSchedule(cronExpression));

        triggerBuild.ForJob(task.JobDetail.Key);
        task.Trigger = triggerBuild.Build();

        task.ExecuteCount = 0;
        task.MaxCount = repeatInterval == null ? 0 : count;
        task.ScheduleTime = Helpers.UnixTimeNowInMilli();

        try
        {
            if (newJob)
            {
                await _generalScheduler.ScheduleJob(task.JobDetail, task.Trigger);
            }
            else
            {
                await _generalScheduler.RescheduleJob(task.Trigger.Key, task.Trigger);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error cron scheduling task");
        }
    }

    /// <summary>Marks a task cancelled once and hands its id back exactly once.</summary>
    private static void Release(Task task)
    {
        if (task.Cancelled)
            return;

        task.Cancelled = true;
        TaskIdManager.Instance.ReleaseId(task.Id);
    }

    /// <summary>
    /// Stops a task at once and clears its job afterwards, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// Every game task runs on the scheduler's own pool, and that pool has eight threads. A task
    /// that waits for the scheduler to delete a job - very often the job it is itself running -
    /// holds one of those eight for as long as the scheduler takes to agree, and doodads do this
    /// on every phase change. With enough of them in flight the pool has nothing left to run
    /// anything with, and everything booked shows up seconds late: an attack, an interaction, a
    /// buff coming to an end.
    ///
    /// Nothing needs the answer. What actually stops the task running again is the flag, and that
    /// is set here and now; removing the job is tidying up and can happen on its own time.
    /// </remarks>
    public void CancelWithoutWaiting(Task task)
    {
        if (task?.JobDetail == null)
            return;

        Release(task);
        _ = DeleteJobQuietly(task.JobDetail.Key);
    }

    private async ThreadTask DeleteJobQuietly(JobKey jobKey)
    {
        try
        {
            await _generalScheduler.DeleteJob(jobKey);
        }
        catch (SchedulerException)
        {
            // Already gone, or on its way out. Either is the outcome we wanted.
            Logger.Trace("Task {0} was already gone when it was cancelled", jobKey);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Could not delete job {0}", jobKey);
        }
    }

    public async Task<bool> Cancel(Task task)
    {
        if (task?.JobDetail == null)
            return true;
        try
        {
            var result = await _generalScheduler.DeleteJob(task.JobDetail.Key);

            // A job that is no longer there is a job that needs no cancelling. Quartz answers
            // false when it never found it and throws when it is already on its way out - a task
            // that fired while this call was in flight. Both mean the same thing, and both used
            // to leave the task id held forever: only the successful path ever gave it back, so
            // every doodad timer that finished a moment too early leaked one.
            Release(task);
            return result;
        }
        catch (SchedulerException)
        {
            // A task that fired while its cancellation was in flight is an everyday race, not a
            // fault, so it is logged at trace and without the exception: the stack trace said
            // nothing but "Quartz threw", and it said it dozens of times a minute.
            Logger.Trace("Task {0} was already gone when it was cancelled", task.JobDetail.Key);
            Release(task);
            return true;
        }
    }
}

[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public sealed class TaskJob : IJob
{
    public ThreadTask Execute(IJobExecutionContext context)
    {
        var log = (Logger)context.MergedJobDataMap.Get("Logger");
        try
        {
            var task = (Task)context.MergedJobDataMap.Get("Task");
            if (task.Cancelled)
                return ThreadTask.CompletedTask;

            task.Execute();
            task.ExecuteCount++;

            if (task.MaxCount != -1 && task.ExecuteCount > task.MaxCount)
                Clear(task.Id);
        }
        catch (Exception e)
        {
            log.Error(e);
        }

        return ThreadTask.CompletedTask;
    }

    private static void Clear(uint taskId)
    {
        ThreadTask.Run(() => TaskIdManager.Instance.ReleaseId(taskId));
    }
}
