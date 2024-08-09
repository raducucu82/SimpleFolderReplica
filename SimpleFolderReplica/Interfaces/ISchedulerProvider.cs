using System.Reactive.Concurrency;

namespace SimpleFoldersSync;

public interface ISchedulerProvider
{
    IScheduler Default { get;  }
    IScheduler Immediate { get;  }
    IScheduler CurrentThread { get;  }
}