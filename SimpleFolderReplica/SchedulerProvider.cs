using System.Reactive.Concurrency;

namespace SimpleFoldersSync;

public class SchedulerProvider : ISchedulerProvider
{
    public IScheduler Default => Scheduler.Default;
    public IScheduler Immediate => Scheduler.Immediate;
    public IScheduler CurrentThread => Scheduler.CurrentThread;
}