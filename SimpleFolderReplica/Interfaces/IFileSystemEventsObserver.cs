using System.Reactive.Subjects;

namespace SimpleFoldersSync;

public interface IFileSystemEventsObserver : IObservable<FileSystemEventArgs>
{
}