using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Autofac.Extras.NSubstitute;
using Microsoft.Reactive.Testing;
using NSubstitute;
using Shouldly;
using SimpleFoldersSync;

namespace SimpleFolderSyncTests;

public class Tests
{
    [Fact]
    public async Task Check_FileCompare_CanBeCanceled_LongRunning()
    {
        using var autoSub = new AutoSubstitute();
        
        var config = new SyncConfig()
        {
            Source = @"c:\source",
            Destination = @"c:\destination"
        };
        autoSub.Provide<SyncConfig>(config);
                
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { Path.Join(config.Source, "a.txt"), new MockFileData("")},
            { Path.Join(config.Source, "b.txt"), new MockFileData("") },
            { Path.Join(config.Source, "c.txt"), new MockFileData("") },
            { Path.Join(config.Destination, "a.txt"), new MockFileData("")},
            { Path.Join(config.Destination, "b.txt"), new MockFileData("") },
            { Path.Join(config.Destination, "c.txt"), new MockFileData("") },
        });
        autoSub.Provide<IFileSystem>(fileSystem);

        var mockFileUtils = new MockFileUtils();
        autoSub.Provide<IFileOperationUtils>(mockFileUtils);

        var sut = autoSub.Resolve<InitialFolderReplicator>();
        var checkOutOfSyncTask = sut.CheckForOutOfSyncFiles(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        sut.CancelCheckForFile("a.txt");
        sut.CancelCheckForFile("b.txt");
        sut.CancelCheckForFile("c.txt");
        await checkOutOfSyncTask;
        
        MockFileUtils.Cancellations.ShouldBe(3); 
    }
    
    [Fact]
    public async Task Check_FilesAreComparedOnStartup()
    {
        using var autoSub = new AutoSubstitute();

        var config = new SyncConfig()
        {
            Source = @"c:\source",
            Destination = @"c:\destination"
        };
        autoSub.Provide<SyncConfig>(config);
        
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { Path.Join(config.Source, "a.txt"), new MockFileData("12345")},
            { Path.Join(config.Source, "b.txt"), new MockFileData("12") },
            { Path.Join(config.Source, "c.txt"), new MockFileData("") },
            { Path.Join(config.Destination, "a.txt"), new MockFileData("1234") },
            { Path.Join(config.Destination, "b.txt"), new MockFileData("34") }, // same size as source, different content
        });
        autoSub.Provide<IFileSystem>(fileSystem);

        var scheduler = new TestScheduler();
        autoSub.Provide<ISchedulerProvider>(new TestSchedulerProvider(scheduler));
         
        var fsUtilsMock = autoSub.Resolve<IFileOperationUtils>();
        var checkContentsMatchCalls = 0;
        fsUtilsMock.CheckContentsMatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).
            Returns(Task.FromResult(false)).
            AndDoes(_ => checkContentsMatchCalls += 1);
        
        var sut = autoSub.Resolve<InitialFolderReplicator>();
        var files = new List<string>();
        sut.SubscribeOn(scheduler).Subscribe(onNext: args => 
        {
            if (args?.First?.Name != null)
            {
                files.Add(args.First.Name);
            }
        },
        onCompleted: () =>
        {
            checkContentsMatchCalls.ShouldBe(1);
            files.Count.ShouldBe(3);               
        });

        scheduler.Start();
        await sut.CheckForOutOfSyncFiles(CancellationToken.None);
    }

    [Fact]
    public void Check_MergeFsEvents_RemovesDuplicates()
    {
        using var autoSub = new AutoSubstitute();
        autoSub.Resolve<IFileSystem>().File.Exists(default).ReturnsForAnyArgs(true);

        var events = new List<FileSystemEventArgs>()
        {
            new FileSystemEventArgs(WatcherChangeTypes.Created, "", "a.txt"),
            new FileSystemEventArgs(WatcherChangeTypes.Created, "", "b.txt"),
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "", "a.txt"),
        };
        
        var sut = autoSub.Resolve<FolderReplicaDriver>();
        var merged = sut.FilterAndMergeFsEvents(events);
        
        merged.Count.ShouldBe(2);
        merged.Count(ev => ev.First.Name == "a.txt").ShouldBe(1);
        merged.Count(ev => ev.First.Name == "b.txt").ShouldBe(1);
    }

    [Fact]
    public void Check_MergeFsEvents_HandlesCreateAfterRename()
    {
        using var autoSub = new AutoSubstitute();
        autoSub.Resolve<IFileSystem>().File.Exists(default).ReturnsForAnyArgs(true);
        
        var events = new List<FileSystemEventArgs>()
        {
            new RenamedEventArgs(WatcherChangeTypes.Renamed, "", "b.txt", "a.txt"),
            new FileSystemEventArgs(WatcherChangeTypes.Created, "", "a.txt"),
        };

        var sut = autoSub.Resolve<FolderReplicaDriver>();
        var merged = sut.FilterAndMergeFsEvents(events);
         
        merged.Count.ShouldBe(1);
        merged.First().First.ChangeType.ShouldBe(WatcherChangeTypes.Renamed);
        merged.First().First.Name.ShouldBe("b.txt");
        merged.First().Second.ChangeType.ShouldBe(WatcherChangeTypes.Created);
        merged.First().Second.Name.ShouldBe("a.txt");
    }

    [Fact]
    public void Check_MergeFsEvents_HandlesRenameAfterChange()
    {
        using var autoSub = new AutoSubstitute();
        autoSub.Resolve<IFileSystem>().File.Exists(default).ReturnsForAnyArgs(true);
         
        var events = new List<FileSystemEventArgs>()
        {
            new FileSystemEventArgs(WatcherChangeTypes.Changed, @"c:\", "a.txt"),
            new RenamedEventArgs(WatcherChangeTypes.Renamed, @"c:\", "b.txt", "a.txt"),
            new FileSystemEventArgs(WatcherChangeTypes.Created, @"c:\", "a.txt"),
        };

        var sut = autoSub.Resolve<FolderReplicaDriver>();
        var merged = sut.FilterAndMergeFsEvents(events);
          
        merged.Count.ShouldBe(2);
        merged.Count(ev => ev.First.Name == "a.txt" && ev.First.ChangeType == WatcherChangeTypes.Created).ShouldBe(1);
        merged.Count(ev => ev.First.Name == "b.txt" && ev.First.ChangeType == WatcherChangeTypes.Created).ShouldBe(1);
    }
    
    [Fact] 
    public void Check_FsEvents_AreBuffered()
    {
        using var autoSub = new AutoSubstitute();
        
        var fsObsMock = new FileSystemObserverMock();
        autoSub.Provide<IFileSystemEventsObserver>(fsObsMock);

        var testScheduler = new TestSchedulerProvider(fsObsMock.Scheduler);
        autoSub.Provide<ISchedulerProvider>(testScheduler);
        
        var config = new SyncConfig()
        {
            SyncPeriodInSeconds = 5
        };
        autoSub.Provide<SyncConfig>(config);

        autoSub.Resolve<IFileSystem>().File.Exists(default).ReturnsForAnyArgs(true);
        
        var sut = autoSub.Resolve<FolderReplicaDriver>();
        var eventsCount = 0;
        sut.GetBufferedFsEventsSource().Subscribe(onNext: args =>
            {
                eventsCount += 1;
            },
            onCompleted: () => 
            {
                // eventsCount.ShouldBe(3);
            });

        // 1st batch
        fsObsMock.MockUpdate(new FileSystemEventArgs(WatcherChangeTypes.Deleted, "", "testme.txt"), TimeSpan.FromSeconds(1));
        fsObsMock.MockUpdate(new FileSystemEventArgs(WatcherChangeTypes.Changed, "", "testme.txt"), TimeSpan.FromSeconds(2));
        fsObsMock.MockUpdate(new FileSystemEventArgs(WatcherChangeTypes.Created, "", "testme.txt"), TimeSpan.FromSeconds(3));
        // No files in 2nd batch
        // 3rd batch
        fsObsMock.MockUpdate(new FileSystemEventArgs(WatcherChangeTypes.Deleted, "", "testme2.txt"), TimeSpan.FromSeconds(4));
        fsObsMock.MockUpdate(new FileSystemEventArgs(WatcherChangeTypes.Deleted, "", "testme3.txt"), TimeSpan.FromSeconds(11));
        fsObsMock.MockUpdate(new FileSystemEventArgs(WatcherChangeTypes.Created, "", "testme3.txt"), TimeSpan.FromSeconds(12));
        
        fsObsMock.Scheduler.AdvanceBy(TimeSpan.FromSeconds(5).Ticks);
        eventsCount.ShouldBe(2);
        fsObsMock.Scheduler.AdvanceBy(TimeSpan.FromSeconds(5).Ticks);
        eventsCount.ShouldBe(2);
        fsObsMock.Scheduler.AdvanceBy(TimeSpan.FromSeconds(5).Ticks);
        eventsCount.ShouldBe(3);
    }
   
    private class TestSchedulerProvider : ISchedulerProvider
    {
        private readonly TestScheduler _scheduler;

        public TestSchedulerProvider(TestScheduler scheduler)
        {
            _scheduler = scheduler;
        }
        
        public IScheduler Default => _scheduler;
        public IScheduler Immediate => _scheduler;
        public IScheduler CurrentThread => _scheduler;
    }
    
    private class MockFileUtils : IFileOperationUtils
    {
        public static int Cancellations = 0;
            
        public async Task<bool> CheckContentsMatchAsync(string source, string destination, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                Interlocked.Increment(ref Cancellations);
            }
    
            return true;
        }
    
        public bool TryCopyFile(string source, string destination) => true;
        public bool TryDeleteFileOrDirectory(string file) => true;
        public bool TryMoveFileOrDirectory(string source, string destination) => true;
        public bool TryCreateDirectory(string directory) => true;
    }
    
    private class FileSystemObserverMock : IFileSystemEventsObserver, IDisposable
    {
        private Subject<FileSystemEventArgs> _subject;
        public TestScheduler Scheduler { get; }
        
        public FileSystemObserverMock()
        {
            _subject = new Subject<FileSystemEventArgs>();
            Scheduler = new TestScheduler();
        }
    
        public void MockUpdate(FileSystemEventArgs eventArgs, TimeSpan timeOffset)
        {
            Scheduler.Schedule(timeOffset, () => _subject.OnNext(eventArgs));
        }
    
        // public void Complete(TimeSpan timeOffset)
        // {
        //     Scheduler.Schedule(timeOffset, )
        // }
        public IDisposable Subscribe(IObserver<FileSystemEventArgs> observer)
        {
            return _subject.Subscribe(observer);
        }
    
        public void Dispose()
        {
            _subject.OnCompleted();
            // _subject.Dispose();
        }
    }
}

