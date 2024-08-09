# SimpleFolderReplicator

The program periodically synchronizes a source folded to a replica folder, source to replica.
Synchronization is one-way. <br>
CLI arguments:
```
  -s, --source           Required. Source folder.
  -d, --destination      Required. Destination folder (where to sync).
  -p, --period           Required. Sync period in seconds.
  -l, --log-file-path    Required. Log file path.
  --help                 Display this help screen.
  --version              Display version information.
```

## Functionality
On start:
 - The program performs an initial check and sync of source and replica folder
_and_
 - Starts listening for File System Events in source folder and periodically processes the events.



## Implementation details
- No persistence.
- The events are buffered and processed periodically. 
- Only the latest event associated with a file/folder is processed, _except_ for __Move/Rename__ events,
which require special handling (see below).
- The program subscribes to FS events on start, to avoid missing events.
- Initial sync and listening for FS events are done in parallel (subscribe to merged Rx.Net observables). 
- File replication (create/move/delete) success or failure is logged.

### Libraries used
- [Rx.Net](https://github.com/dotnet/reactive) - took the project as on opportunity to experiment with this. 
- [Autofac](https://github.com/autofac/Autofac) - IoC
- [CommandLineParser](https://github.com/commandlineparser/commandline) - parse program arguments.
- [NLog](https://github.com/NLog/NLog) - file and console logging.
- [System.IO.Abstractions](https://github.com/TestableIO/System.IO.Abstractions) - testable `FileSystem` API
- [NSubstitute](https://nsubstitute.github.io/), [Shouldly](https://github.com/shouldly/shouldly) - Unit Testing

### Handling Move/Rename events
Samples (events that occur in a time frame, in order they occur):
 - Update `a.txt`, Rename `a.txt` to `b.txt` - copy `b.txt` to replica folder.
 - Rename `a.txt` to `b.txt`, Create `a.txt` - this chain of events need to be replicated in order in 
replica folder.

### Build
```
dotnet build SimpleFolderReplica.sln
```
- Requires .NET 8.0 SDK