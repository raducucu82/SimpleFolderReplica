using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SimpleFoldersSync;

public class InitialFolderReplicator: IObservable<OrderedFileActions>
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly Subject<OrderedFileActions> _args;
    
    private readonly IFileSystem _fileSystem;
    private readonly SyncConfig _config;
    private readonly IFileOperationUtils _fileOperationUtils;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _filesToSkip;

    public InitialFolderReplicator(IFileSystem fileSystem, SyncConfig config, IFileOperationUtils fileOperationUtils)
    {
        _fileSystem = fileSystem;
        _config = config;
        _fileOperationUtils = fileOperationUtils;
        _args = new Subject<OrderedFileActions>();
        _filesToSkip = new ConcurrentDictionary<string, CancellationTokenSource>();
    }

    public IDisposable Subscribe(IObserver<OrderedFileActions> observer) =>
        _args.Subscribe(observer);
    
    public async Task CheckForOutOfSyncFiles(CancellationToken cancellationToken)
    {
        Logger.Info("Starting initial folder sync");
        var srcFiles = _fileSystem.Directory.GetFiles(_config.Source, "*", SearchOption.AllDirectories)
            .Select(path => path.RelativeTo(_config.Source)).ToList();

        await Parallel.ForEachAsync(
            source: srcFiles, 
            body: async (file, _) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _filesToSkip.GetOrAdd(file, new CancellationTokenSource()).Token);
                var ev = await SafeCheckFileIsOutOfSync(file, linkedCts.Token);

                if (ev != null && !linkedCts.IsCancellationRequested)
                {
                    _args.OnNext(new OrderedFileActions(ev));
                }
            },
            parallelOptions: new ParallelOptions()
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 8
            });
        
        _args.OnCompleted();
        Logger.Info("Finished initial folder sync.");
    }

    public void CancelCheckForFile(string file)
    {
        var cancelSource = _filesToSkip.GetOrAdd(file, new CancellationTokenSource());
        cancelSource.Cancel();
    }

    private async Task<FileSystemEventArgs> SafeCheckFileIsOutOfSync(string file, CancellationToken cancellationToken)
    {

        return await Extensions.DoSafeAsync(async () =>
        {
             var path = Path.Join(_config.Source, file);
             if (_fileSystem.File.Exists(path))
             {
                 return await CheckFilesAreIdentical(file, cancellationToken);
             }
             return null;
        }, Logger);
    }

    private async Task<FileSystemEventArgs> CheckFilesAreIdentical(string file, CancellationToken cancellationToken) 
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null!;
        }

        var source = Path.Join(_config.Source, file);
        var destination = Path.Join(_config.Destination, file);
        
        if (!FileExists(destination))
        {
            return new FileSystemEventArgs(WatcherChangeTypes.Created, _config.Source, file);
        }

        if (!SameSize(source, destination) || 
            !await _fileOperationUtils.CheckContentsMatchAsync(source, destination, cancellationToken))
        {
            return new FileSystemEventArgs(WatcherChangeTypes.Changed, _config.Source, file);
        }

        return null!;
    }
    
    private bool FileExists(string destination) => _fileSystem.File.Exists(destination);
    
    private bool SameSize(string source, string destination) =>
        _fileSystem.FileInfo.New(source).Length == _fileSystem.FileInfo.New(destination).Length;
}
