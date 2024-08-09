using CommandLine;

namespace SimpleFoldersSync;

public class SyncConfig
{
    [Option('s', "source", Required = true, HelpText = "Source folder.")]
    public string Source { get; set; }
    
    [Option('d', "destination", Required = true, HelpText = "Destination folder (where to sync).")]   
    public string Destination { get; set; }
    
    [Option('p', "period", Required = true, HelpText = "Sync period in seconds.")] 
    public long SyncPeriodInSeconds { get; set; }
    
    [Option('l', "log-file-path", Required = true, HelpText = "Log file path.")]
    public string LogFilePath { get; set; }
}