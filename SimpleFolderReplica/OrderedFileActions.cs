namespace SimpleFoldersSync;

/// <summary>
/// Store FsEvents that need to be handled in order.
/// Ex scenario: move "a.txt" to "b.txt"; create "a.txt"
/// </summary>
public class OrderedFileActions
{
    private List<FileSystemEventArgs> _orderedOps = new();
    
    public OrderedFileActions(FileSystemEventArgs op)
    {
        _orderedOps.Add(op);
    }

    public OrderedFileActions(FileSystemEventArgs firstOp, FileSystemEventArgs secondOp)
    {
        _orderedOps.Add(firstOp); 
        _orderedOps.Add(secondOp);
    }

    public FileSystemEventArgs First => _orderedOps.FirstOrDefault();

    public FileSystemEventArgs Second => (_orderedOps.Count == 2) ? _orderedOps.Last() : default;

    public void Execute(Action<FileSystemEventArgs> action)
    {
        _orderedOps.ForEach(op => action?.Invoke(op));
    }
}