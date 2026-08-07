using System;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks;

public abstract class Task
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public bool Cancelled { get; set; }
    public long ScheduleTime { get; set; }
    public int MaxCount { get; set; }
    public int ExecuteCount { get; set; }

    /// <summary>When this is next meant to run. Owned by the task manager.</summary>
    internal DateTime DueTime { get; set; }

    /// <summary>Gap between runs; zero for a task that runs once.</summary>
    internal TimeSpan RepeatInterval { get; set; }

    /// <summary>How many runs are left; -1 for as long as it lives.</summary>
    internal int RepeatCount { get; set; }

    internal TaskCronSchedule CronSchedule { get; set; }

    /// <summary>
    /// Which booking of this task is the live one.
    /// </summary>
    /// <remarks>
    /// The same object is booked over and over - a doodad's timer is one task reused for the life
    /// of the doodad - and a booking already sitting in the queue must not fire after a newer one
    /// replaced it. The queue carries the number it was made with and drops anything stale.
    /// </remarks>
    internal long Generation { get; set; }

    protected Task()
    {
        Name = GetType().Name;
        Cancelled = false;
        RepeatCount = -1;
    }

    public abstract void Execute();

    public async Task<bool> CancelAsync()
    {
        var result = await TaskManager.Instance.Cancel(this);
        if (result)
        {
            OnCancel();
            return true;
        }

        return false;
    }

    /// <summary>Stops this task without waiting for anything.</summary>
    public bool Cancel()
    {
        if (!TaskManager.Instance.CancelNow(this))
            return false;

        OnCancel();
        return true;
    }

    public virtual void OnCancel()
    {
    }
}
