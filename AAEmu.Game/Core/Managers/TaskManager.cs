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
