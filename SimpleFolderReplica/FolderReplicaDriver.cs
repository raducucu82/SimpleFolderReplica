using System.IO.Abstractions;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using NLog;

namespace SimpleFoldersSync;

public class FolderReplicaDriver
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
    private readonly IFileOperationUtils _fileUtils;
    private readonly InitialFolderReplicator _initialFolderReplicator;
    private readonly SyncConfig _config;
    private readonly IFileSystemEventsObserver _fsEventsObserver;
    private readonly ISchedulerProvider _schedulerProvider;
    private readonly IFileSystem _fileSystem;

    public FolderReplicaDriver(
        IFileOperationUtils fileUtils,  
        IFileSystemEventsObserver fsEventsObserver,
        InitialFolderReplicator initialFolderReplicator,
        SyncConfig config, 
        ISchedulerProvider schedulerProvider,
        IFileSystem fileSystem)
    {
        _fileUtils = fileUtils;
        _initialFolderReplicator = initialFolderReplicator;
        _config = config;
        _schedulerProvider = schedulerProvider;
        _fileSystem = fileSystem;
        _fsEventsObserver = fsEventsObserver;
    }

    public async Task Run(CancellationToken ct)
    {
        _initialFolderReplicator.Merge(GetBufferedFsEventsSource()).OfType<OrderedFileActions>().Subscribe(fileOps =>
        {
            fileOps.Execute(ProcessFileEvent);
        });
        
        await _initialFolderReplicator.CheckForOutOfSyncFiles(CancellationToken.None);

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Detaching from FS events.");
        }
    }

    private void ProcessFileEvent(FileSystemEventArgs ev)
    {
       _initialFolderReplicator.CancelCheckForFile(ev.Name);

        bool success;
        switch (ev.ChangeType)
        {
            case WatcherChangeTypes.Created:
            case WatcherChangeTypes.Changed:
                if (_fileSystem.File.Exists(ev.FullPath))
                {
                    success = _fileUtils.TryCopyFile(ev.FullPath, Path.Join(_config.Destination, ev.Name));
                    Logger.Info($"Copy \"{ev.Name}\" ({ev.ChangeType.ToString()}) to replica folder: {success.Trace()} ");
                }
                else if (_fileSystem.Directory.Exists(ev.FullPath))
                {
                    success = _fileUtils.TryCreateDirectory(Path.Join(_config.Destination, ev.Name));
                    Logger.Info($"Create \"{ev.Name}\" directory ({ev.ChangeType.ToString()}) in replica folder: {success.Trace()} ");
                }
                break;
            case WatcherChangeTypes.Renamed:
            {
                if (ev is RenamedEventArgs args)
                {
                    success = _fileUtils.TryMoveFileOrDirectory(
                        Path.Join(_config.Destination, args.OldName),
                        Path.Join(_config.Destination, args.Name));
                    Logger.Info($"Rename \"{args.OldName}\" to \"{args.Name}\" in replica folder: {success.Trace()} ");
                }
                break;
            }
            case WatcherChangeTypes.Deleted:
                success = _fileUtils.TryDeleteFileOrDirectory(Path.Join(_config.Destination, ev.Name));
                Logger.Info($"Delete \"{ev.Name}\" in replica folder: {success.Trace()} ");
                break;
            case WatcherChangeTypes.All:
                Logger.Warn($"Unkwnown file event: {ev.ChangeType}");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public IObservable<OrderedFileActions> GetBufferedFsEventsSource()
    {
        return _fsEventsObserver.
            Buffer(TimeSpan.FromSeconds(_config.SyncPeriodInSeconds), _schedulerProvider.Default).
            SelectMany(FilterAndMergeFsEvents);
    }

   
    /// <summary>
    /// Keep only the last events (*) for each file.
    /// (*) Special handling for rename (move) events:
    ///    If the rename event comes after a change/create event on the renamed file, this equals a create event.
    ///    If no changes on renamed file, keep as a rename event (will trigger a rename in destination) 
    /// </summary>
    /// <param name="events">The events from the FsWatcher, in received order.</param>
    public List<OrderedFileActions> FilterAndMergeFsEvents(IEnumerable<FileSystemEventArgs> events)
    {
        var opsSoFar = new Dictionary<string, FileSystemEventArgs>();

        foreach (var ev in events)
        {
            Logger.Debug(ev.ToHumanReadable());
            // TODO this need to be refactored 
            if (_fileSystem.File.Exists(ev.FullPath))
            {
                HandleFileEvent(ev, opsSoFar);
            }
            else if (_fileSystem.Directory.Exists(ev.FullPath))
            {
                HandleDirectoryEvent(ev, opsSoFar);
            }
            else if (ev.ChangeType == WatcherChangeTypes.Deleted)
            {
                if (_fileSystem.File.Exists(Path.Join(_config.Destination, ev.Name)))
                {
                    HandleFileEvent(ev, opsSoFar);
                }
                else if (_fileSystem.Directory.Exists(Path.Join(_config.Destination, ev.Name)))
                {
                    HandleDirectoryEvent(ev, opsSoFar);
                }               
            }
        }

        var merged = opsSoFar.Select(pair => pair.Value).OfType<RenamedEventArgs>()
            .Where(ev => ev.Name != null && ev.OldName != null).ToList().Select(renameEv =>
            {
                opsSoFar.Remove(renameEv.Name);
                if (opsSoFar.TryGetValue(renameEv.OldName, out var prevEv))
                {
                    opsSoFar.Remove(renameEv.OldName);
                    return new OrderedFileActions(renameEv, prevEv);
                }
                return new OrderedFileActions(renameEv);
            }).ToList();

        merged.AddRange(opsSoFar.Select(ev => new OrderedFileActions(ev.Value)));

        return merged;
    }
    
    private void HandleFileEvent(FileSystemEventArgs ev, Dictionary<string, FileSystemEventArgs> ops)
    {
        switch (ev.ChangeType)
        {
            case WatcherChangeTypes.Renamed:
                if (ev is RenamedEventArgs { Name: not null, OldName: not null } renameEv)
                {
                    ops[ev.Name] = HandleRenameEvent(renameEv, ops);
                }
                break;
            default:
                Logger.Warn($"{ev.ChangeType}, {ev.Name}, ${ev.FullPath}");
                ops[ev.Name] = ev;
                break;
        }
    }
    
    private FileSystemEventArgs HandleRenameEvent(RenamedEventArgs ev, Dictionary<string, FileSystemEventArgs> ops)
    {
        if (ops.TryGetValue(ev.OldName, out var prevOp) && 
            prevOp.Name != null &&
            (prevOp.ChangeType is WatcherChangeTypes.Changed or WatcherChangeTypes.Created))
        {
            ops.Remove(prevOp.Name);
            return new FileSystemEventArgs(WatcherChangeTypes.Created, ev.FullPath, ev.Name);
        }                   
            
        return ev;
    }
    
    private void HandleDirectoryEvent(FileSystemEventArgs ev, Dictionary<string, FileSystemEventArgs> ops)
    {
        switch (ev.ChangeType)
        {
            case WatcherChangeTypes.Created:
                var allFiles = _fileSystem.Directory.GetFiles(ev.FullPath, "*", SearchOption.AllDirectories)
                    .Where(fileOrFolder => _fileSystem.File.Exists(fileOrFolder));
                foreach (var file in allFiles)
                {
                    var name = file.RelativeTo(_config.Source);
                    ops[name] = new FileSystemEventArgs(WatcherChangeTypes.Created, _config.Source, name);
                }
                ops[ev.Name] = ev;
                break;
            case WatcherChangeTypes.Renamed:
                if (ev is RenamedEventArgs { Name: not null, OldName: not null } renameEv)
                {
                    var newEv = HandleRenameEvent(renameEv, ops);
                    if (newEv.ChangeType == WatcherChangeTypes.Created)
                    {
                        HandleDirectoryEvent(ev, ops);
                    }
                    else
                    {
                        ops[ev.Name] = ev;                       
                    }
                }
                break;
            case WatcherChangeTypes.Changed:
                // Ignored
                break;
            default:
                ops[ev.Name] = ev;
                break;
        }
    }
}

