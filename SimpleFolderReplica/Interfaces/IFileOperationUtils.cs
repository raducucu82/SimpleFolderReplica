namespace SimpleFoldersSync;

public interface IFileOperationUtils
{
    Task<bool> CheckContentsMatchAsync(string source, string destination, CancellationToken cancellationToken);

    bool TryCopyFile(string source, string destination);

    bool TryDeleteFileOrDirectory(string file);

    bool TryMoveFileOrDirectory(string source, string destination);

    bool TryCreateDirectory(string directory);
}