using System.IO.Abstractions;

namespace SimpleFoldersSync;

public class FileOperationUtils: IFileOperationUtils
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
    private readonly IFileSystem _fileSystem;
    private readonly int _buffSize;
    
    public FileOperationUtils(IFileSystem fileSystem, int buffSize)
    {
        _fileSystem = fileSystem;
        _buffSize = buffSize;
    }
    
    public async Task<bool> CheckContentsMatchAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var sourceStream = _fileSystem.File.OpenRead(source);
        await using var destStream = _fileSystem.File.OpenRead(destination);

        var sourceBuffer = new byte[_buffSize];
        var destBuffer = new byte[_buffSize];
        
        while (!cancellationToken.IsCancellationRequested)
        {
            Task<int> sourceReadTask = sourceStream.ReadAsync(sourceBuffer, 0, _buffSize, cancellationToken);
            Task<int> destReadTask = destStream.ReadAsync(destBuffer, 0, _buffSize, cancellationToken);
            
            await Task.WhenAll(sourceReadTask, destReadTask);

            var bytesReadSource = await sourceReadTask;
            var bytesReadDest = await destReadTask;
            
            if (bytesReadSource != bytesReadDest || bytesReadSource == 0)
            {
                return bytesReadSource == bytesReadDest;
            }
            
            if (!new ReadOnlySpan<byte>(destBuffer).SequenceEqual(sourceBuffer)) // See: https://gist.github.com/airbreather/90c5fd3ba9d77fcd7c106db3beeb569b
            {
                return false;
            }
        }

        return false;
    }

    public bool TryCopyFile(string source, string destination) =>
        Extensions.DoSafe(() =>
        {
            _fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
            _fileSystem.File.Copy(source, destination, true);
        }, Logger);


    public bool TryDeleteFileOrDirectory(string file) =>
        Extensions.DoSafe(() =>
        {
            var success = _fileSystem.File.TryDelete(file) ||
                          _fileSystem.Directory.TryDelete(file);
            if (!success)
            {
                throw new IOException(file);
            }
        }, Logger);

    public bool TryMoveFileOrDirectory(string source, string destination) =>
        Extensions.DoSafe(() =>
        {
            if (_fileSystem.File.Exists(source))
            {
                _fileSystem.File.TryDelete(destination);
                _fileSystem.File.Move(source, destination);
            }
            else if (_fileSystem.Directory.Exists(source))
            {
                 _fileSystem.Directory.TryDelete(destination);
                 _fileSystem.Directory.Move(source, destination);               
            }
            else
            {
                throw new FileNotFoundException(source);
            }
        }, Logger);

    public bool TryCreateDirectory(string directory)
    {
        return Extensions.DoSafe(() =>
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }, Logger);
    }
}