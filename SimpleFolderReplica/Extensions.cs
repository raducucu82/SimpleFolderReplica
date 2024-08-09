using System.IO.Abstractions;

namespace SimpleFoldersSync;

public static class Extensions
{
    public static string RelativeTo(this string path, string directory) => 
        path.Replace(directory, "").TrimStart('\\');

    public static string Trace(this bool success) => success ? "Success" : "Fail";

    public static Task<T> DoSafeAsync<T>(Func<Task<T>> func, NLog.Logger logger)
    {
        try
        {
            return func?.Invoke();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger?.Error(ex, "");
        }
        catch (TaskCanceledException)
        {
            logger?.Debug("Cancel requested.");
        }

        return Task.FromResult<T>(default);
    }
    
    public static bool DoSafe(Action action, NLog.Logger logger)
    {
        try
        {
            action?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger?.Error(ex, "");
        }
        catch (TaskCanceledException)
        {
            logger?.Debug("Cancel requested.");
        }
    
        return false;       
    }

    public static bool TryDelete(this IFile fs, string path)
    {
        return DoSafe(() =>
        {
            fs.Delete(path);
        }, null);
    }
    
    public static bool TryDelete(this IDirectory fs, string path)
    {
        return DoSafe(() =>
        {
            fs.Delete(path);
        }, null);
    }

    public static string ToHumanReadable(this FileSystemEventArgs ev)
    {
        if (ev is RenamedEventArgs { Name: not null, OldName: not null } renameEv)
        {
            return $"{renameEv.ChangeType}: Name=\"{renameEv.Name}\", FullPath=\"{renameEv.FullPath}\", OldName=\"{renameEv.OldName}\", OldFullPath=\"{renameEv.OldFullPath}\"";
        }
        if (ev.Name != null)
        {
            return $"{ev.ChangeType}: Name=\"{ev.Name}\", FullPath=\"{ev.FullPath}\"";
        }

        return "Unknown/wrong event data.";
    }
}