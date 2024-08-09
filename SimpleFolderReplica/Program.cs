// See https://aka.ms/new-console-template for more information

using System.IO.Abstractions;
using Autofac;
using Autofac.Core;
using CommandLine;
using NLog;

namespace SimpleFoldersSync;

public static class Program
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    static async Task Main(string[] args)
    {
        SyncConfig options = null;

        Parser.Default.ParseArguments<SyncConfig>(args)
            .WithParsed<SyncConfig>(o => { options = o; });
        if (options == null)
        {
            return;
        }
       
        SetupLogging(options.LogFilePath);
        var container = SetupServices(options);
                        
        CancellationTokenSource cts = new();
        Console.CancelKeyPress += delegate(object _, ConsoleCancelEventArgs e) {
            Logger.Info("Received SIGINT, shutting down.");
            e.Cancel = true;
            cts.Cancel(); 
        };

        await using var scope = container.BeginLifetimeScope();
        await scope.Resolve<FolderReplicaDriver>().Run(cts.Token);
                         
        LogManager.Flush();
        LogManager.Shutdown();
    }

    private static IContainer SetupServices(SyncConfig configOptions)
    {
       var builder = new ContainerBuilder();
         
       builder.RegisterType<FileSystem>().As<IFileSystem>();
       builder.RegisterType<FolderReplicaDriver>().AsSelf();
       builder.RegisterType<FileSystemEventsObserver>().As<IFileSystemEventsObserver>();
       builder.RegisterType<InitialFolderReplicator>().AsSelf();
       builder.RegisterType<FileOperationUtils>().As<IFileOperationUtils>().WithParameter("buffSize", 4096);
       builder.RegisterType<SchedulerProvider>().As<ISchedulerProvider>();
       builder.RegisterInstance(configOptions);
         
       return builder.Build();
    }

    private static void SetupLogging(string logFilePath)
    {
        NLog.LogManager.Setup().LoadConfiguration(builder => {
            builder.ForLogger().FilterMinLevel(LogLevel.Info).WriteToConsole(layout: @"${date:format=HH\:mm\:ss} [${level}] ${message} ${exception}");
            builder.ForLogger().FilterMinLevel(LogLevel.Debug).WriteToFile(fileName: logFilePath);
        });
    }
}
