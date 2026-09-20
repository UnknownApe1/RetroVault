using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ILibraryRepository repository;
    private readonly ILibraryScanner scanner;
    private readonly IHashService hashes;
    private readonly ICatalogVerificationService catalogVerification;
    private readonly IThumbnailService thumbnails;
    private readonly ILibraryExportService libraryExport;
    private readonly ILogger<MainViewModel> logger;
    private CancellationTokenSource? scanCancellation;
    private CancellationTokenSource? refreshCancellation;
    private CancellationTokenSource? detailsCancellation;
    private CancellationTokenSource? thumbnailCancellation;
    private string searchText = "", statusText = "Ready", currentPath = "";
    private bool isScanning, isInitialized, forceFullReverify;
    private bool hasMoreGames, isLoadingMore;
    private SystemListItem? selectedSystem;
    private LibraryFilterOption? selectedFilter;
    private GameListItem? selectedGame;
    private GameFile? selectedFile;
    private ScanLocation? selectedLocation;
    private Game? gameDetails;
    private LibraryCounts counts = new(0, 0, 0, 0);
    private string? thumbnailPath;

    public MainViewModel(ILibraryRepository repository, ILibraryScanner scanner, IHashService hashes, ICatalogVerificationService catalogVerification, IThumbnailService thumbnails, ILibraryExportService libraryExport, ILogger<MainViewModel> logger)
    {
        this.repository = repository; this.scanner = scanner; this.hashes = hashes; this.catalogVerification = catalogVerification; this.thumbnails = thumbnails; this.libraryExport = libraryExport; this.logger = logger;
        ScanCommand = new AsyncCommand(ScanAsync, () => !IsScanning);
        CancelCommand = new RelayCommand(() => scanCancellation?.Cancel(), () => IsScanning);
        AddFolderCommand = new AsyncCommand(AddFolderAsync, () => !IsScanning);
        RemoveFolderCommand = new AsyncCommand(RemoveFolderAsync, () => SelectedLocation is not null && !IsScanning);
        ToggleLocationCommand = new AsyncCommand(ToggleLocationAsync, () => SelectedLocation is not null && !IsScanning);
        ToggleRecursiveCommand = new AsyncCommand(ToggleRecursiveAsync, () => SelectedLocation is not null && !IsScanning);
        RefreshCommand = new AsyncCommand(RefreshLibraryAsync);
        LoadMoreCommand = new AsyncCommand(LoadMoreAsync, () => HasMoreGames && !IsScanning && !IsLoadingMore);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedFile is not null);
        CopyPathCommand = new RelayCommand(() => { if (SelectedFile is not null) Clipboard.SetText(SelectedFile.FullPath); }, () => SelectedFile is not null);
        VerifyHashCommand = new AsyncCommand(VerifyHashAsync, () => SelectedFile is not null && !IsScanning);
        AssignSystemCommand = new AsyncCommand(AssignSelectedFileSystemAsync, () => SelectedFile is not null && SelectedSystem is { Id: > 0 } && !IsScanning);
        ExportCsvCommand = new AsyncCommand(ExportLibraryCsvAsync, () => !IsScanning);
        VerifyCatalogCommand = new AsyncCommand(VerifyCatalogAsync, () => !IsScanning);
        ReviewDuplicateTitlesCommand = new AsyncCommand(ReviewDuplicateTitlesAsync, () => !IsScanning);
        CleanUpTitlesCommand = new AsyncCommand(CleanUpTitlesAsync, () => !IsScanning);
        MergeExactDuplicatesCommand = new AsyncCommand(MergeExactDuplicatesAsync, () => !IsScanning);
        ExportDuplicateReportCommand = new AsyncCommand(ExportDuplicateReportAsync, () => !IsScanning);
        BackupDatabaseCommand = new AsyncCommand(BackupDatabaseAsync, () => !IsScanning);
        RestoreDatabaseCommand = new AsyncCommand(RestoreDatabaseAsync, () => !IsScanning);
        ExportSettingsCommand = new AsyncCommand(ExportSettingsAsync, () => !IsScanning);
        ImportSettingsCommand = new AsyncCommand(ImportSettingsAsync, () => !IsScanning);
        PreferCopyCommand = new AsyncCommand(TogglePreferredCopyAsync, () => SelectedFile is not null && !IsScanning);
        ExcludeCopyCommand = new AsyncCommand(ToggleExcludedCopyAsync, () => SelectedFile is not null && !IsScanning);
        ExcludeNonPreferredCommand = new AsyncCommand(ExcludeNonPreferredAsync, () => SelectedGames.Count > 0 && !IsScanning);
        ClearOverridesCommand = new AsyncCommand(ClearOverridesAsync, () => SelectedGames.Count > 0 && !IsScanning);
        ExportPreferredLibraryCommand = new AsyncCommand(() => ExportLibraryAsync(onlyWanted: false), () => !IsScanning);
        ExportWantedLibraryCommand = new AsyncCommand(() => ExportLibraryAsync(onlyWanted: true), () => !IsScanning);
        MarkWantedCommand = new AsyncCommand(() => SetSelectedGamesWantedAsync(true), () => SelectedGames.Count > 0 && !IsScanning);
        UnmarkWantedCommand = new AsyncCommand(() => SetSelectedGamesWantedAsync(false), () => SelectedGames.Count > 0 && !IsScanning);
        SelectedGames.CollectionChanged += (_, _) => { ExcludeNonPreferredCommand.Refresh(); ClearOverridesCommand.Refresh(); MarkWantedCommand.Refresh(); UnmarkWantedCommand.Refresh(); Raise(nameof(SelectedGamesCountText)); };
    }

    public BulkObservableCollection<GameListItem> Games { get; } = [];
    public IReadOnlyList<LibraryFilterOption> LibraryFilters { get; } =
    [
        new(LibraryViewFilter.All, "All games"),
        new(LibraryViewFilter.Verified, "Verified"),
        new(LibraryViewFilter.Unverified, "Unverified"),
        new(LibraryViewFilter.ExactDuplicates, "Exact duplicates"),
        new(LibraryViewFilter.MultipleVersions, "Multiple versions"),
        new(LibraryViewFilter.Missing, "Missing"),
        new(LibraryViewFilter.NeedsReview, "Needs review"),
        new(LibraryViewFilter.PreferredCopies, "Preferred copies"),
        new(LibraryViewFilter.Wanted, "Wanted")
    ];
    public ObservableCollection<GameListItem> SelectedGames { get; } = [];
    public ObservableCollection<SystemListItem> Systems { get; } = [];
    public ObservableCollection<ScanLocation> ScanLocations { get; } = [];
    public AsyncCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand AddFolderCommand { get; }
    public AsyncCommand RemoveFolderCommand { get; }
    public AsyncCommand ToggleLocationCommand { get; }
    public AsyncCommand ToggleRecursiveCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand LoadMoreCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public AsyncCommand VerifyHashCommand { get; }
    public AsyncCommand AssignSystemCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand VerifyCatalogCommand { get; }
    public AsyncCommand ReviewDuplicateTitlesCommand { get; }
    public AsyncCommand CleanUpTitlesCommand { get; }
    public AsyncCommand MergeExactDuplicatesCommand { get; }
    public AsyncCommand ExportDuplicateReportCommand { get; }
    public AsyncCommand BackupDatabaseCommand { get; }
    public AsyncCommand RestoreDatabaseCommand { get; }
    public AsyncCommand ExportSettingsCommand { get; }
    public AsyncCommand ImportSettingsCommand { get; }
    public AsyncCommand PreferCopyCommand { get; }
    public AsyncCommand ExcludeCopyCommand { get; }
    public AsyncCommand ExcludeNonPreferredCommand { get; }
    public AsyncCommand ClearOverridesCommand { get; }
    public AsyncCommand ExportPreferredLibraryCommand { get; }
    public AsyncCommand ExportWantedLibraryCommand { get; }
    public AsyncCommand MarkWantedCommand { get; }
    public AsyncCommand UnmarkWantedCommand { get; }
    public string VersionText { get; } = GetVersionText();
    public string WindowTitle => $"RetroVault {VersionText}";
    public ScanLocation? SelectedLocation { get => selectedLocation; set { if (Set(ref selectedLocation, value)) { RemoveFolderCommand.Refresh(); ToggleLocationCommand.Refresh(); ToggleRecursiveCommand.Refresh(); Raise(nameof(LocationToggleLabel)); Raise(nameof(RecursiveToggleLabel)); } } }
    public string LocationToggleLabel => SelectedLocation?.Enabled == true ? "Disable" : "Enable";
    public string RecursiveToggleLabel => SelectedLocation?.Recursive == true ? "Recursive: On" : "Recursive: Off";
    public GameFile? SelectedFile { get => selectedFile; set { if (Set(ref selectedFile, value)) { OpenFolderCommand.Refresh(); CopyPathCommand.Refresh(); VerifyHashCommand.Refresh(); AssignSystemCommand.Refresh(); PreferCopyCommand.Refresh(); ExcludeCopyCommand.Refresh(); Raise(nameof(PreferCopyLabel)); Raise(nameof(ExcludeCopyLabel)); _ = LoadThumbnailAsync(); } } }
    public string PreferCopyLabel => SelectedFile?.IsManuallyPreferred == true ? "Use Automatic Choice" : "Use This Copy";
    public string ExcludeCopyLabel => SelectedFile?.IsExcluded == true ? "Include Copy" : "Exclude Copy";
    public string? ThumbnailPath { get => thumbnailPath; private set => Set(ref thumbnailPath, value); }
    public string SelectedGamesCountText => SelectedGames.Count == 0 ? "" : $"{SelectedGames.Count:N0} selected";
    public bool IsLibraryEmpty => Games.Count == 0 && !IsScanning;
    public string SearchText { get => searchText; set { if (Set(ref searchText, value) && isInitialized) _ = RefreshLibraryAsync(250); } }
    public bool ForceFullReverify { get => forceFullReverify; set => Set(ref forceFullReverify, value); }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public bool HasMoreGames { get => hasMoreGames; private set { if (Set(ref hasMoreGames, value)) LoadMoreCommand.Refresh(); } }
    public bool IsLoadingMore { get => isLoadingMore; private set { if (Set(ref isLoadingMore, value)) LoadMoreCommand.Refresh(); } }
    public string CurrentPath { get => currentPath; private set => Set(ref currentPath, value); }
    public bool IsScanning { get => isScanning; private set { if (Set(ref isScanning, value)) { Raise(nameof(IsLibraryEmpty)); ScanCommand.Refresh(); CancelCommand.Refresh(); AddFolderCommand.Refresh(); RemoveFolderCommand.Refresh(); ToggleLocationCommand.Refresh(); ToggleRecursiveCommand.Refresh(); VerifyHashCommand.Refresh(); AssignSystemCommand.Refresh(); ExportCsvCommand.Refresh(); VerifyCatalogCommand.Refresh(); ReviewDuplicateTitlesCommand.Refresh(); CleanUpTitlesCommand.Refresh(); MergeExactDuplicatesCommand.Refresh(); ExportDuplicateReportCommand.Refresh(); BackupDatabaseCommand.Refresh(); RestoreDatabaseCommand.Refresh(); ExportSettingsCommand.Refresh(); ImportSettingsCommand.Refresh(); PreferCopyCommand.Refresh(); ExcludeCopyCommand.Refresh(); ExcludeNonPreferredCommand.Refresh(); ClearOverridesCommand.Refresh(); ExportPreferredLibraryCommand.Refresh(); ExportWantedLibraryCommand.Refresh(); MarkWantedCommand.Refresh(); UnmarkWantedCommand.Refresh(); LoadMoreCommand.Refresh(); } } }
    public LibraryCounts Counts { get => counts; private set => Set(ref counts, value); }
    public SystemListItem? SelectedSystem { get => selectedSystem; set { if (Set(ref selectedSystem, value)) { AssignSystemCommand.Refresh(); if (isInitialized) _ = RefreshLibraryAsync(); } } }
    public LibraryFilterOption? SelectedFilter { get => selectedFilter; set { if (Set(ref selectedFilter, value) && isInitialized) _ = RefreshLibraryAsync(); } }
    public GameListItem? SelectedGame { get => selectedGame; set { if (Set(ref selectedGame, value)) _ = LoadDetailsAsync(value?.Id); } }
    public Game? GameDetails { get => gameDetails; private set { Set(ref gameDetails, value); Raise(nameof(DetailFiles)); } }
    public IReadOnlyList<GameFile> DetailFiles => GameDetails?.Files ?? [];

    private static string GetVersionText()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null ? "version unknown" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public async Task InitializeAsync()
    {
        StatusText = "Loading library...";
        SelectedFilter = LibraryFilters[0];
        await Task.Yield();
        var interrupted = await Task.Run(() => repository.HasInterruptedScanAsync(CancellationToken.None));
        await RefreshLocationsAsync(); await RefreshSystemsAsync(); await RefreshLibraryAsync();
        isInitialized = true;
        if (ScanLocations.Count > 0 && interrupted)
        {
            StatusText = "Interrupted scan detected. Resuming safely...";
            await ScanAsync(true);
        }
    }

    private async Task AddFolderAsync()
    {
        var picker = new OpenFolderDialog { Title = "Choose a ROM or game folder", Multiselect = false };
        if (picker.ShowDialog() != true) return;
        await repository.AddScanLocationAsync(picker.FolderName, true, CancellationToken.None); await RefreshLocationsAsync();
    }

    private async Task RemoveFolderAsync() { if (SelectedLocation is null) return; if (MessageBox.Show("Remove this scan location? Indexed records from this location will also be removed. Your game files will not be touched.", "Remove location", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; await repository.RemoveScanLocationAsync(SelectedLocation.Id, CancellationToken.None); await RefreshLocationsAsync(); await RefreshLibraryAsync(); }
    private async Task ToggleLocationAsync() { if (SelectedLocation is null) return; await repository.UpdateScanLocationAsync(SelectedLocation.Id, !SelectedLocation.Enabled, SelectedLocation.Recursive, CancellationToken.None); await RefreshLocationsAsync(); }
    private async Task ToggleRecursiveAsync() { if (SelectedLocation is null) return; await repository.UpdateScanLocationAsync(SelectedLocation.Id, SelectedLocation.Enabled, !SelectedLocation.Recursive, CancellationToken.None); await RefreshLocationsAsync(); }

    private Task ScanAsync() => ScanAsync(false);

    private async Task ScanAsync(bool isRecovery)
    {
        scanCancellation = new(); IsScanning = true; StatusText = isRecovery ? "Resuming interrupted scan..." : "Scanning...";
        var progress = new Progress<ScanProgress>(p => { CurrentPath = p.CurrentPath ?? ""; StatusText = $"Discovered {p.Discovered:N0} | Processed {p.Processed:N0} | Skipped {p.Skipped:N0} | New {p.Added:N0} | Changed {p.Changed:N0}"; });
        try
        {
            var result = await Task.Run(() => scanner.ScanAllAsync(progress, scanCancellation.Token), scanCancellation.Token);
            StatusText = $"Scan complete: {result.Added:N0} new, {result.Changed:N0} changed, {result.Errors.Count:N0} errors";
        }
        catch (OperationCanceledException) { StatusText = "Scan canceled"; }
        catch (Exception ex) { logger.LogError(ex, "Scan stopped unexpectedly but the application remained available"); StatusText = $"Scan paused safely: {ex.Message}"; }
        finally { IsScanning = false; scanCancellation.Dispose(); scanCancellation = null; CurrentPath = ""; await RefreshLocationsAsync(); await RefreshLibraryAsync(); }
    }

    public void CancelActiveScan() => scanCancellation?.Cancel();

    private async Task RefreshSystemsAsync()
    {
        var systems = await Task.Run(() => repository.GetSystemsAsync(CancellationToken.None));
        var counts = await Task.Run(() => repository.GetGameCountsBySystemAsync(CancellationToken.None));
        Systems.Clear();
        Systems.Add(new SystemListItem(new SystemDefinition { Id = 0, Key = "ALL", Name = "All systems", Manufacturer = "" }, counts.Values.Sum()));
        foreach (var s in systems) Systems.Add(new SystemListItem(s, counts.GetValueOrDefault(s.Id)));
        SelectedSystem ??= Systems[0];
    }
    private async Task RefreshLocationsAsync() { var selectedId = SelectedLocation?.Id; ScanLocations.Clear(); foreach (var item in await Task.Run(() => repository.GetScanLocationsAsync(false, CancellationToken.None))) ScanLocations.Add(item); SelectedLocation = selectedId.HasValue ? ScanLocations.FirstOrDefault(x => x.Id == selectedId) : ScanLocations.FirstOrDefault(); }
    private Task RefreshLibraryAsync() => RefreshLibraryAsync(0);

    private async Task RefreshLibraryAsync(int delayMilliseconds)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref refreshCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        var token = cancellation.Token;
        try
        {
            if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, token);
            var search = SearchText;
            var filter = SelectedFilter?.Value ?? LibraryViewFilter.All;
            int? systemId = SelectedSystem is { Id: > 0 } ? SelectedSystem.Id : null;
            var result = await Task.Run(async () =>
            {
                var games = repository is IPagedLibraryRepository paged
                    ? await paged.SearchGamesPageAsync(search, systemId, filter, 0, 500, token)
                    : new PagedGameResult(await repository.SearchGamesAsync(search, systemId, filter, token), false);
                var counts = await repository.GetCountsAsync(token);
                return (games, counts);
            }, token);
            token.ThrowIfCancellationRequested();
            Games.ReplaceAll(result.games.Games.Select(x => new GameListItem(x)));
            HasMoreGames = result.games.HasMore;
            Counts = result.counts;
            Raise(nameof(IsLibraryEmpty));
            if (!IsScanning) StatusText = $"Ready — showing {Games.Count:N0} games{(HasMoreGames ? " (load more available)" : "")}";
        }

        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not refresh the visible library");
            if (!IsScanning) StatusText = $"Library refresh failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref refreshCancellation, null, cancellation), cancellation)) cancellation.Dispose();
        }
    }

    private async Task LoadMoreAsync()
    {
        if (!HasMoreGames || IsLoadingMore || IsScanning) return;
        IsLoadingMore = true;
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref refreshCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        var token = cancellation.Token;
        try
        {
            var search = SearchText;
            var filter = SelectedFilter?.Value ?? LibraryViewFilter.All;
            int? systemId = SelectedSystem is { Id: > 0 } ? SelectedSystem.Id : null;
            var page = repository is IPagedLibraryRepository paged
                ? await paged.SearchGamesPageAsync(search, systemId, filter, Games.Count, 500, token)
                : new PagedGameResult(await repository.SearchGamesAsync(search, systemId, filter, token), false);
            token.ThrowIfCancellationRequested();
            foreach (var game in page.Games) Games.Add(new GameListItem(game));
            HasMoreGames = page.HasMore;
            StatusText = $"Ready — showing {Games.Count:N0} games{(HasMoreGames ? " (load more available)" : "")}";
        }

        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load more library results");
            StatusText = $"Could not load more games: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref refreshCancellation, null, cancellation), cancellation)) cancellation.Dispose();
            IsLoadingMore = false;
        }
    }

    private static string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CozziForged", "RomManager");
    private static string DatabasePath => Path.Combine(AppDataDirectory, "library.db");

    private async Task BackupDatabaseAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Back up RetroVault database",
            Filter = "RetroVault backup (*.db)|*.db|All files (*.*)|*.*",
            DefaultExt = ".db",
            AddExtension = true,
            FileName = $"rom-manager-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName)!);
            await Task.Run(() =>
            {
                File.Copy(DatabasePath, dialog.FileName, true);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var sidecar = DatabasePath + suffix;
                    if (File.Exists(sidecar)) File.Copy(sidecar, dialog.FileName + suffix, true);
                }

            });
            StatusText = $"Database backup created: {dialog.FileName}";
        }

        catch (Exception ex)
        {
            logger.LogError(ex, "Could not create database backup");
            StatusText = $"Database backup failed: {ex.Message}";
        }
    }

    private async Task RestoreDatabaseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore RetroVault database",
            Filter = "RetroVault database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show("RetroVault will close and restore this database the next time it starts. A backup of the current database should be created first. Continue?", "Restore database", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            await Task.Run(() =>
            {
                File.Copy(dialog.FileName, Path.Combine(AppDataDirectory, "restore.pending.db"), true);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var sidecar = dialog.FileName + suffix;
                    var target = Path.Combine(AppDataDirectory, "restore.pending.db" + suffix);
                    if (File.Exists(sidecar)) File.Copy(sidecar, target, true);
                    else if (File.Exists(target)) File.Delete(target);
                }
            });
            StatusText = "Database restore staged. RetroVault will close now and restore it on the next start.";
            await Task.Delay(250);
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not stage database restore");
            StatusText = $"Database restore failed: {ex.Message}";
        }
    }

    private async Task ExportSettingsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export RetroVault settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "rom-manager-settings.json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var settings = new SettingsTransfer(1, ScanLocations.Select(x => new ScanLocationTransfer(x.Path, x.Enabled, x.Recursive)).ToList());
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            StatusText = $"Settings exported: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not export settings");
            StatusText = $"Settings export failed: {ex.Message}";
        }
    }

    private async Task ImportSettingsAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import RetroVault settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var settings = JsonSerializer.Deserialize<SettingsTransfer>(await File.ReadAllTextAsync(dialog.FileName));
            if (settings is null || settings.Version != 1) throw new InvalidDataException("Unsupported settings file version.");
            foreach (var location in settings.ScanLocations ?? [])
            {
                if (Directory.Exists(location.Path))
                {
                    var saved = await repository.AddScanLocationAsync(location.Path, location.Recursive, CancellationToken.None);
                    await repository.UpdateScanLocationAsync(saved.Id, location.Enabled, location.Recursive, CancellationToken.None);
                }
            }
            await RefreshLocationsAsync();
            StatusText = $"Imported {settings.ScanLocations?.Count ?? 0:N0} scan location(s)";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not import settings");
            StatusText = $"Settings import failed: {ex.Message}";
        }
    }

    private sealed record SettingsTransfer(int Version, List<ScanLocationTransfer>? ScanLocations);
    private sealed record ScanLocationTransfer(string Path, bool Enabled, bool Recursive);

    private async Task LoadDetailsAsync(long? id)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref detailsCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        var token = cancellation.Token;
        try
        {
            if (!id.HasValue)
            {
                GameDetails = null;
                SelectedFile = null;
                return;
            }

            var details = await Task.Run(() => repository.GetGameDetailsAsync(id.Value, token), token);
            token.ThrowIfCancellationRequested();
            if (SelectedGame?.Id != id.Value) return;
            GameDetails = details;
            SelectedFile = details?.Files.FirstOrDefault();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref detailsCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
        }
    }
    private async Task VerifyHashAsync()
    {
        var file = SelectedFile;
        if (file is null) return;
        if (file.IsDirectory) { StatusText = "SHA-256 verification isn't available for folder-based games."; return; }
        scanCancellation = new();
        IsScanning = true;
        StatusText = $"Verifying {file.FileName}...";
        try
        {
            var token = scanCancellation.Token;
            var sha = await hashes.ComputeSha256Async(file.FullPath, token);
            await repository.SaveSha256Async(file.Id, sha, token);
            await repository.MarkExactDuplicatesAsync(sha, token);
            StatusText = $"SHA-256 verified: {sha[..12]}...";
            await RefreshLibraryAsync();
        }

        catch (OperationCanceledException) { StatusText = "SHA-256 verification canceled"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText = $"Verification failed: {ex.Message}"; }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
            CurrentPath = "";
        }
    }

    private async Task AssignSelectedFileSystemAsync()
    {
        if (SelectedFile is null || SelectedSystem is not { Id: > 0 } || repository is not IManualFileAssignmentRepository assignments) return;
        var file = SelectedFile;
        var system = SelectedSystem;
        if (MessageBox.Show($"Assign \"{file.FileName}\" to {system.Name}? This changes its indexed system only; the file on disk will not be modified.", "Assign system", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        scanCancellation = new();
        IsScanning = true;
        try
        {
            await assignments.AssignFileToSystemAsync(file.Id, system.Key, scanCancellation.Token);
            StatusText = $"Assigned {file.FileName} to {system.Name}";
            await RefreshSystemsAsync();
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
        catch (OperationCanceledException) { StatusText = "System assignment canceled"; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not assign file to system");
            StatusText = $"System assignment failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
        }
    }

    private async Task VerifyCatalogAsync()
    {
        var systemKey = SelectedSystem is { Id: > 0 } ? SelectedSystem.Key : null;
        var scope = ForceFullReverify ? "already-verified files included" : "already-checked files skipped";
        if (systemKey is null && MessageBox.Show($"Verify every supported system ({scope})? This reads each ROM in full and can take a while for a large library the first time. You can instead select one system first.", "Verify full library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        scanCancellation = new();
        IsScanning = true;
        StatusText = systemKey is null ? "Preparing full catalog verification..." : $"Preparing {SelectedSystem!.Name} verification...";
        var progress = new Progress<CatalogVerificationProgress>(p =>
        {
            CurrentPath = p.CurrentItem;
            StatusText = $"Catalogs {p.SystemsCompleted:N0}/{p.SystemsTotal:N0} | Files {p.FilesCompleted:N0}/{p.FilesTotal:N0}";
        });
        try
        {
            var result = await Task.Run(() => catalogVerification.VerifyAsync(systemKey, ForceFullReverify, progress, scanCancellation.Token), scanCancellation.Token);
            StatusText = $"Catalog verification complete: {result.Verified:N0} verified, {result.NoMatch:N0} unmatched, {result.Unsupported:N0} unsupported, {result.Errors:N0} errors";
        }
        catch (OperationCanceledException) { StatusText = "Catalog verification canceled; completed results were saved"; }
        catch (Exception ex) { logger.LogError(ex, "Catalog verification stopped unexpectedly"); StatusText = $"Catalog verification paused: {ex.Message}"; }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
            CurrentPath = "";
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
    }
    private async Task ReviewDuplicateTitlesAsync()
    {
        scanCancellation = new();
        IsScanning = true;
        StatusText = "Scanning titles for likely duplicates...";
        try
        {
            var token = scanCancellation.Token;
            var candidates = await Task.Run(() => repository.GetFuzzyMatchCandidatesAsync(token), token);
            token.ThrowIfCancellationRequested();
            var window = new DuplicateReviewWindow { Owner = Application.Current.MainWindow, DataContext = new DuplicateReviewViewModel(repository, candidates) };
            window.ShowDialog();
            StatusText = "Ready";
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
        catch (OperationCanceledException) { StatusText = "Similar-title review canceled"; }
        catch (Exception ex) { logger.LogError(ex, "Could not scan for similar titles"); StatusText = $"Similar-title scan failed: {ex.Message}"; }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
            CurrentPath = "";
        }
    }
    private async Task CleanUpTitlesAsync()
    {
        scanCancellation = new();
        IsScanning = true;
        StatusText = "Scanning titles for cleanup suggestions...";
        try
        {
            var token = scanCancellation.Token;
            var candidates = await Task.Run(() => repository.GetTitleCleanupCandidatesAsync(token), token);
            token.ThrowIfCancellationRequested();
            var window = new TitleCleanupWindow { Owner = Application.Current.MainWindow, DataContext = new TitleCleanupViewModel(repository, candidates) };
            window.ShowDialog();
            StatusText = "Ready";
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
        catch (OperationCanceledException) { StatusText = "Title cleanup review canceled"; }
        catch (Exception ex) { logger.LogError(ex, "Could not scan for title cleanup suggestions"); StatusText = $"Title cleanup scan failed: {ex.Message}"; }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
            CurrentPath = "";
        }
    }
    private async Task MergeExactDuplicatesAsync()
    {
        scanCancellation = new();
        IsScanning = true;
        StatusText = "Scanning for exact-title duplicates...";
        try
        {
            var token = scanCancellation.Token;
            var groups = await Task.Run(() => repository.GetExactTitleDuplicateGroupsAsync(token), token);
            token.ThrowIfCancellationRequested();
            var window = new ExactTitleMergeWindow { Owner = Application.Current.MainWindow, DataContext = new ExactTitleMergeViewModel(repository, groups) };
            window.ShowDialog();
            StatusText = "Ready";
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
        catch (OperationCanceledException) { StatusText = "Exact-title duplicate review canceled"; }
        catch (Exception ex) { logger.LogError(ex, "Could not scan for exact-title duplicates"); StatusText = $"Exact-title duplicate scan failed: {ex.Message}"; }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
            CurrentPath = "";
        }
    }
    private async Task LoadThumbnailAsync()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref thumbnailCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        var token = cancellation.Token;
        var file = SelectedFile;
        var systemKey = GameDetails?.SystemDefinition?.Key;
        var title = ThumbnailTitleResolver.Resolve(file?.CatalogName, GameDetails?.CanonicalTitle, file?.Region);
        if (file is null || systemKey is null || title is null) { ThumbnailPath = null; return; }
        try
        {
            var path = await thumbnails.GetThumbnailPathAsync(systemKey, title, token);
            if (!token.IsCancellationRequested) ThumbnailPath = path;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(Interlocked.CompareExchange(ref thumbnailCancellation, null, cancellation), cancellation)) cancellation.Dispose(); }
    }
    private async Task ExcludeNonPreferredAsync()
    {
        var ids = SelectedGames.Select(x => x.Id).ToArray();
        if (ids.Length == 0) return;
        StatusText = $"Excluding non-preferred copies for {ids.Length:N0} selected game(s)...";
        try
        {
            await repository.ExcludeNonPreferredCopiesAsync(ids, CancellationToken.None);
            StatusText = $"Excluded non-preferred copies for {ids.Length:N0} game(s)";
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
        catch (Exception ex) { logger.LogError(ex, "Could not exclude non-preferred copies"); StatusText = $"Bulk exclude failed: {ex.Message}"; }
    }
    private async Task ClearOverridesAsync()
    {
        var ids = SelectedGames.Select(x => x.Id).ToArray();
        if (ids.Length == 0) return;
        StatusText = $"Clearing overrides for {ids.Length:N0} selected game(s)...";
        try
        {
            await repository.ClearCopyOverridesAsync(ids, CancellationToken.None);
            StatusText = $"Cleared overrides for {ids.Length:N0} game(s)";
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
        catch (Exception ex) { logger.LogError(ex, "Could not clear copy overrides"); StatusText = $"Bulk clear failed: {ex.Message}"; }
    }
    private async Task TogglePreferredCopyAsync()
    {
        if (SelectedFile is null) return;
        await repository.SetCopyPreferenceAsync(SelectedFile.Id, !SelectedFile.IsManuallyPreferred, false, CancellationToken.None);
        if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        await RefreshLibraryAsync();
        StatusText = "Preferred-copy choice updated";
    }

    private async Task ToggleExcludedCopyAsync()
    {
        if (SelectedFile is null) return;
        await repository.SetCopyPreferenceAsync(SelectedFile.Id, false, !SelectedFile.IsExcluded, CancellationToken.None);
        if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        await RefreshLibraryAsync();
        StatusText = "Copy inclusion updated";
    }
    private async Task ExportLibraryAsync(bool onlyWanted)
    {
        var picker = new OpenFolderDialog { Title = onlyWanted ? "Choose a destination folder for your wanted games" : "Choose a destination folder for your organized library" };
        if (picker.ShowDialog() != true) return;

        scanCancellation = new();
        IsScanning = true;
        StatusText = "Preparing library export...";
        var progress = new Progress<LibraryExportProgress>(p =>
        {
            CurrentPath = p.CurrentItem;
            StatusText = $"Exporting {p.FilesCompleted:N0}/{p.FilesTotal:N0}";
        });
        try
        {
            var result = await Task.Run(() => libraryExport.ExportPreferredCopiesAsync(picker.FolderName, onlyWanted, progress, scanCancellation.Token), scanCancellation.Token);
            StatusText = $"Export complete: {result.Copied:N0} copied, {result.Skipped:N0} already up to date, {result.Errors:N0} errors";
        }
        catch (OperationCanceledException) { StatusText = "Export canceled; files already copied were kept"; }
        catch (Exception ex) { logger.LogError(ex, "Library export stopped unexpectedly"); StatusText = $"Export paused: {ex.Message}"; }
        finally { IsScanning = false; scanCancellation.Dispose(); scanCancellation = null; CurrentPath = ""; }
    }
    private async Task SetSelectedGamesWantedAsync(bool wanted)
    {
        var ids = SelectedGames.Select(x => x.Id).ToArray();
        if (ids.Length == 0) return;
        try
        {
            await repository.SetGamesWantedAsync(ids, wanted, CancellationToken.None);
            StatusText = wanted ? $"Marked {ids.Length:N0} game(s) as wanted" : $"Unmarked {ids.Length:N0} game(s) as wanted";
            await RefreshLibraryAsync();
        }
        catch (Exception ex) { logger.LogError(ex, "Could not update wanted status"); StatusText = $"Could not update wanted status: {ex.Message}"; }
    }
    private async Task ExportDuplicateReportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export duplicate-file report",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"rom-duplicates-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog() != true) return;

        scanCancellation = new();
        IsScanning = true;
        StatusText = "Preparing duplicate-file report...";
        try
        {
            var token = scanCancellation.Token;
            var rows = await Task.Run(() => repository.GetDuplicateFileReportAsync(token), token);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => WriteDuplicateReportCsvAsync(dialog.FileName, rows, token), token);
            var groups = rows.GroupBy(x => x.Sha256).ToArray();
            var reclaimable = groups.Sum(g => (long)(g.Count() - 1) * g.First().Size);
            StatusText = $"Exported {rows.Count:N0} duplicate files across {groups.Length:N0} groups to {dialog.FileName} - {reclaimable / (1024.0 * 1024 * 1024):N1} GB reclaimable if you keep one copy per group";
        }
        catch (OperationCanceledException) { StatusText = "Duplicate report export canceled"; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not export the duplicate-file report");
            StatusText = $"Duplicate report export failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
            CurrentPath = "";
        }
    }

    private static async Task WriteDuplicateReportCsvAsync(string path, IReadOnlyList<DuplicateFileRow> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("SHA-256,System,Game Title,File Name,Full Path,Size Bytes".AsMemory(), ct);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var values = new object?[] { row.Sha256, row.System, row.GameTitle, row.FileName, row.FullPath, row.Size };
            await writer.WriteLineAsync(string.Join(',', values.Select(CsvValue)).AsMemory(), ct);
        }
    }

    private async Task ExportLibraryCsvAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export ROM library inventory",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"rom-library-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog() != true) return;

        scanCancellation = new();
        IsScanning = true;
        StatusText = "Preparing full library export...";
        try
        {
            var token = scanCancellation.Token;
            var rows = await Task.Run(() => repository.GetLibraryExportRowsAsync(token), token);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => WriteCsvAsync(dialog.FileName, rows, token), token);
            StatusText = $"Exported {rows.Count:N0} files to {dialog.FileName}";
        }
        catch (OperationCanceledException) { StatusText = "Library export canceled"; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not export the library inventory");
            StatusText = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            scanCancellation.Dispose();
            scanCancellation = null;
            CurrentPath = "";
        }
    }

    private static async Task WriteCsvAsync(string path, IReadOnlyList<LibraryExportRow> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("System,Game Title,File Name,Full Path,Source Folder,Format,Size Bytes,Modified UTC,Region,Language,Revision,Version,Status,Catalog Status,Catalog Source,Catalog Name,Preferred,Manual Preference,Excluded,Preference Score,Quick Hash,SHA-256,SHA-1".AsMemory(), ct);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var values = new object?[]
            {
                row.System, row.GameTitle, row.FileName, row.FullPath, row.SourcePath, row.Format, row.Size,
                row.ModifiedDate.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), row.Region, row.Language,
                row.Revision, row.Version, row.Status, row.CatalogStatus, row.CatalogSource, row.CatalogName,
                row.IsPreferred, row.IsManuallyPreferred, row.IsExcluded, row.PreferenceScore, row.QuickHash, row.Sha256, row.Sha1
            };
            await writer.WriteLineAsync(string.Join(',', values.Select(CsvValue)).AsMemory(), ct);
        }
    }

    private static string CsvValue(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private void OpenSelectedFolder() { if (SelectedFile is null) return; Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedFile.FullPath}\"") { UseShellExecute = true }); }
}
