using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AAEmu.Game.Core.Managers;

public interface ITaskManager
{
    Task<bool> Cancel(Models.Tasks.Task task);
    void Initialize();
    // The two trailing arguments are filled in by the compiler at each call site so that a
    // request carrying no task can name the code that made it.
    void Schedule(Models.Tasks.Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1,
        [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0);
    void Start();
    void Stop();
}
