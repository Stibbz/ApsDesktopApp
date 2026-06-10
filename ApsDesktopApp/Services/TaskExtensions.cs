using System.Threading.Tasks;

namespace ApsDesktopApp.Services;

// Safety net for fire-and-forget calls. Callees are expected to handle their
// own exceptions; this guarantees that anything that still escapes is logged
// instead of becoming a silent unobserved-task fault. Usage:
//   SomethingAsync().LogFaults(_log, "Category");
public static class TaskExtensions
{
    public static void LogFaults(this Task task, AppLogger log, string category)
    {
        task.ContinueWith(
            t => log.Error(category,
                $"Unhandled background task fault: {t.Exception?.GetBaseException()}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
