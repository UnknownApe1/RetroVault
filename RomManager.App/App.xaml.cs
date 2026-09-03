using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RomManager.App.ViewModels;
using RomManager.Core.Grouping;
using RomManager.Core.Hashing;
using RomManager.Core.Parsing;
using RomManager.Core.Scanner;
using RomManager.Core.Services;
using RomManager.Database.Repositories;
using RomManager.Database.SQLite;
using RomManager.Formats.FormatDefinitions;
using RomManager.Formats.Parsers;
using RomManager.Infrastructure.Export;
using RomManager.Infrastructure.FileSystem;
using RomManager.Infrastructure.Logging;
using RomManager.Infrastructure.Catalog;

namespace RomManager.App;

public partial class App : Application
{
    private IHost? host;
    private Mutex? instanceMutex;
    private bool ownsInstanceMutex;
    private string? crashLogPath;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instanceMutex = new Mutex(true, @"Local\CozziForged.RomManager", out ownsInstanceMutex);
        if (!ownsInstanceMutex)
        {
            MessageBox.Show("ROM Manager is already running. Only one instance can scan the library at a time.", "ROM Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0); return;
        }
        var appDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CozziForged", "RomManager");
        Directory.CreateDirectory(appDirectory);
        crashLogPath = Path.Combine(appDirectory, "Logs", "crash.log");
        var verboseScanLogging = e.Args.Any(x => string.Equals(x, "--verbose-scan", StringComparison.OrdinalIgnoreCase));
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            MessageBox.Show("ROM Manager encountered an unexpected error. Your indexed data was preserved, and an interrupted scan will resume next time.", "ROM Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; Shutdown(1);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { WriteCrashLog(args.Exception); args.SetObserved(); };
        host = Host.CreateDefaultBuilder().ConfigureServices((_, services) =>
        {
            services.AddSingleton<SystemCatalog>();
            services.AddSingleton<ISystemDefinitionProvider>(x => x.GetRequiredService<SystemCatalog>());
            services.AddSingleton<IFormatIdentifier>(x => x.GetRequiredService<SystemCatalog>());
            services.AddSingleton<IArchiveInspector, CompressedArchiveInspector>();
            services.AddSingleton<IFileSystem, PhysicalFileSystem>();
            services.AddSingleton<IFileNameParser, NoIntroFileNameParser>();
            services.AddSingleton<IHashService, HashService>();
            services.AddSingleton<IGameGroupingService, GameGroupingService>();
            services.AddDbContextFactory<RomManagerDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(appDirectory, "library.db")};Default Timeout=10;Pooling=True"));
            services.AddSingleton<ILibraryRepository, LibraryRepository>();
            services.AddSingleton<ILibraryScanner, LibraryScanner>();
            services.AddSingleton<ICatalogVerificationService, LibretroCatalogVerificationService>();
            services.AddSingleton<IThumbnailService, LibretroThumbnailService>();
            services.AddSingleton<ILibraryExportService, LibraryExportService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }).ConfigureLogging((_, logging) =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(verboseScanLogging ? LogLevel.Debug : LogLevel.Information);
            logging.AddProvider(new FileLoggerProvider(Path.Combine(appDirectory, "Logs"), verboseScanLogging));
        }).Build();
        await host.StartAsync();
        try
        {
            await host.Services.GetRequiredService<ILibraryRepository>().InitializeAsync(CancellationToken.None);
            var window = host.Services.GetRequiredService<MainWindow>();
            window.DataContext = host.Services.GetRequiredService<MainViewModel>();
            MainWindow = window;
            window.Show();
            await ((MainViewModel)window.DataContext).InitializeAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.ToString(), "ROM Manager could not start", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null) { await host.StopAsync(); host.Dispose(); }
        if (ownsInstanceMutex) { try { instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { } }
        instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void WriteCrashLog(Exception exception)
    {
        try
        {
            if (crashLogPath is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath)!);
            File.AppendAllText(crashLogPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
