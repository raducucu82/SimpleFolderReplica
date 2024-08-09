using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace SimpleFoldersSync;

public class FileSystemEventsObserver : IFileSystemEventsObserver
{
    private readonly ISchedulerProvider _schedulerProvider;
    private readonly IObservable<FileSystemEventArgs> _fsObserver;
    
    public FileSystemEventsObserver(ISchedulerProvider schedulerProvider, SyncConfig config)
    {
        _schedulerProvider = schedulerProvider;
        _fsObserver = CreateFsObserver(config.Source);
    }

    public IDisposable Subscribe(IObserver<FileSystemEventArgs> observer) => 
        _fsObserver.SubscribeOn(_schedulerProvider.Default).Subscribe(observer);
    
    private static IObservable<FileSystemEventArgs> CreateFsObserver(string folder)
    {
        return 
            Observable.Defer(() =>
                {
                    FileSystemWatcher fsw = new(folder)
                    {
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };
        
                    return Observable.Return(fsw);
                }).
                SelectMany(fsw =>
                    Observable.Merge(new[]
                        {
                            Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                                h => fsw.Created += h, h => fsw.Created -= h),
                            Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                                h => fsw.Changed += h, h => fsw.Changed -= h),
                            Observable.FromEventPattern<RenamedEventHandler, FileSystemEventArgs>(
                                h => fsw.Renamed += h, h => fsw.Renamed -= h),
                            Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                                h => fsw.Deleted += h, h => fsw.Deleted -= h)
                        }).
                        Select(ep => ep.EventArgs).
                        Finally(fsw.Dispose)).
                Publish().
                RefCount();
    }
}