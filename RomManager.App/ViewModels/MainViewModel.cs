using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
    private readonly ILogger<MainViewModel> logger;
    private CancellationTokenSource? scanCancellation;
    private CancellationTokenSource? refreshCancellation;
    private CancellationTokenSource? thumbnailCancellation;
    private string searchText = "", statusText = "Ready", currentPath = "";
    private bool isScanning, isInitialized;
    private SystemDefinition? selectedSystem;
    private LibraryFilterOption? selectedFilter;
    private GameListItem? selectedGame;
    private GameFile? selectedFile;
    private ScanLocation? selectedLocation;
    private Game? gameDetails;
    private LibraryCounts counts = new(0, 0, 0, 0);
    private string? thumbnailPath;

    public MainViewModel(ILibraryRepository repository, ILibraryScanner scanner, IHashService hashes, ICatalogVerificationService catalogVerification, IThumbnailService thumbnails, ILogger<MainViewModel> logger)
    {
        this.repository = repository; this.scanner = scanner; this.hashes = hashes; this.catalogVerification = catalogVerification; this.thumbnails = thumbnails; this.logger = logger;
        ScanCommand = new AsyncCommand(ScanAsync, () => !IsScanning);
        CancelCommand = new RelayCommand(() => scanCancellation?.Cancel(), () => IsScanning);
        AddFolderCommand = new AsyncCommand(AddFolderAsync, () => !IsScanning);
        RemoveFolderCommand = new AsyncCommand(RemoveFolderAsync, () => SelectedLocation is not null && !IsScanning);
        ToggleLocationCommand = new AsyncCommand(ToggleLocationAsync, () => SelectedLocation is not null && !IsScanning);
        ToggleRecursiveCommand = new AsyncCommand(ToggleRecursiveAsync, () => SelectedLocation is not null && !IsScanning);
        RefreshCommand = new AsyncCommand(RefreshLibraryAsync);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedFile is not null);
        CopyPathCommand = new RelayCommand(() => { if (SelectedFile is not null) Clipboard.SetText(SelectedFile.FullPath); }, () => SelectedFile is not null);
        VerifyHashCommand = new AsyncCommand(VerifyHashAsync, () => SelectedFile is not null && !IsScanning);
        ExportCsvCommand = new AsyncCommand(ExportLibraryCsvAsync, () => !IsScanning);
        VerifyCatalogCommand = new AsyncCommand(VerifyCatalogAsync, () => !IsScanning);
        ReviewDuplicateTitlesCommand = new AsyncCommand(ReviewDuplicateTitlesAsync, () => !IsScanning);
        PreferCopyCommand = new AsyncCommand(TogglePreferredCopyAsync, () => SelectedFile is not null && !IsScanning);
        ExcludeCopyCommand = new AsyncCommand(ToggleExcludedCopyAsync, () => SelectedFile is not null && !IsScanning);
        ExcludeNonPreferredCommand = new AsyncCommand(ExcludeNonPreferredAsync, () => SelectedGames.Count > 0 && !IsScanning);
        ClearOverridesCommand = new AsyncCommand(ClearOverridesAsync, () => SelectedGames.Count > 0 && !IsScanning);
        SelectedGames.CollectionChanged += (_, _) => { ExcludeNonPreferredCommand.Refresh(); ClearOverridesCommand.Refresh(); Raise(nameof(SelectedGamesCountText)); };
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
        new(LibraryViewFilter.PreferredCopies, "Preferred copies")
    ];
    public ObservableCollection<GameListItem> SelectedGames { get; } = [];
    public ObservableCollection<SystemDefinition> Systems { get; } = [];
    public ObservableCollection<ScanLocation> ScanLocations { get; } = [];
    public AsyncCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand AddFolderCommand { get; }
    public AsyncCommand RemoveFolderCommand { get; }
    public AsyncCommand ToggleLocationCommand { get; }
    public AsyncCommand ToggleRecursiveCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public AsyncCommand VerifyHashCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand VerifyCatalogCommand { get; }
    public AsyncCommand ReviewDuplicateTitlesCommand { get; }
    public AsyncCommand PreferCopyCommand { get; }
    public AsyncCommand ExcludeCopyCommand { get; }
    public AsyncCommand ExcludeNonPreferredCommand { get; }
    public AsyncCommand ClearOverridesCommand { get; }
    public string VersionText { get; } = GetVersionText();
    public string WindowTitle => $"ROM Manager {VersionText}";
    public ScanLocation? SelectedLocation { get => selectedLocation; set { if (Set(ref selectedLocation, value)) { RemoveFolderCommand.Refresh(); ToggleLocationCommand.Refresh(); ToggleRecursiveCommand.Refresh(); Raise(nameof(LocationToggleLabel)); Raise(nameof(RecursiveToggleLabel)); } } }
    public string LocationToggleLabel => SelectedLocation?.Enabled == true ? "Disable" : "Enable";
    public string RecursiveToggleLabel => SelectedLocation?.Recursive == true ? "Recursive: On" : "Recursive: Off";
    public GameFile? SelectedFile { get => selectedFile; set { if (Set(ref selectedFile, value)) { OpenFolderCommand.Refresh(); CopyPathCommand.Refresh(); VerifyHashCommand.Refresh(); PreferCopyCommand.Refresh(); ExcludeCopyCommand.Refresh(); Raise(nameof(PreferCopyLabel)); Raise(nameof(ExcludeCopyLabel)); _ = LoadThumbnailAsync(); } } }
    public string PreferCopyLabel => SelectedFile?.IsManuallyPreferred == true ? "Use Automatic Choice" : "Use This Copy";
    public string ExcludeCopyLabel => SelectedFile?.IsExcluded == true ? "Include Copy" : "Exclude Copy";
    public string? ThumbnailPath { get => thumbnailPath; private set => Set(ref thumbnailPath, value); }
    public string SelectedGamesCountText => SelectedGames.Count == 0 ? "" : $"{SelectedGames.Count:N0} selected";
    public string SearchText { get => searchText; set { if (Set(ref searchText, value) && isInitialized) _ = RefreshLibraryAsync(250); } }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string CurrentPath { get => currentPath; private set => Set(ref currentPath, value); }
    public bool IsScanning { get => isScanning; private set { if (Set(ref isScanning, value)) { ScanCommand.Refresh(); CancelCommand.Refresh(); AddFolderCommand.Refresh(); RemoveFolderCommand.Refresh(); ToggleLocationCommand.Refresh(); ToggleRecursiveCommand.Refresh(); VerifyHashCommand.Refresh(); ExportCsvCommand.Refresh(); VerifyCatalogCommand.Refresh(); ReviewDuplicateTitlesCommand.Refresh(); PreferCopyCommand.Refresh(); ExcludeCopyCommand.Refresh(); ExcludeNonPreferredCommand.Refresh(); ClearOverridesCommand.Refresh(); } } }
    public LibraryCounts Counts { get => counts; private set => Set(ref counts, value); }
    public SystemDefinition? SelectedSystem { get => selectedSystem; set { if (Set(ref selectedSystem, value) && isInitialized) _ = RefreshLibraryAsync(); } }
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

    private async Task RefreshSystemsAsync() { Systems.Clear(); Systems.Add(new SystemDefinition { Id = 0, Key = "ALL", Name = "All systems", Manufacturer = "" }); foreach (var s in await Task.Run(() => repository.GetSystemsAsync(CancellationToken.None))) Systems.Add(s); SelectedSystem ??= Systems[0]; }
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
                var games = await repository.SearchGamesAsync(search, systemId, filter, token);
                var counts = await repository.GetCountsAsync(token);
                return (games, counts);
            }, token);
            token.ThrowIfCancellationRequested();
            Games.ReplaceAll(result.games.Select(x => new GameListItem(x, thumbnails)));
            Counts = result.counts;
            if (!IsScanning) StatusText = $"Ready — showing {Games.Count:N0} games";
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
    private async Task LoadDetailsAsync(long? id) { GameDetails = id.HasValue ? await Task.Run(() => repository.GetGameDetailsAsync(id.Value, CancellationToken.None)) : null; SelectedFile = GameDetails?.Files.FirstOrDefault(); }
    private async Task VerifyHashAsync()
    {
        if (SelectedFile is null) return;
        StatusText = $"Verifying {SelectedFile.FileName}...";
        try { var sha = await hashes.ComputeSha256Async(SelectedFile.FullPath, CancellationToken.None); await repository.SaveSha256Async(SelectedFile.Id, sha, CancellationToken.None); await repository.MarkExactDuplicatesAsync(sha, CancellationToken.None); StatusText = $"SHA-256 verified: {sha[..12]}..."; await RefreshLibraryAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText = $"Verification failed: {ex.Message}"; }
    }
    private async Task VerifyCatalogAsync()
    {
        var systemKey = SelectedSystem is { Id: > 0 } ? SelectedSystem.Key : null;
        if (systemKey is null && MessageBox.Show("Verify every supported system? This reads each ROM in full and may take several hours for a large library. You can instead select one system first.", "Verify full library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

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
            var result = await Task.Run(() => catalogVerification.VerifyAsync(systemKey, progress, scanCancellation.Token), scanCancellation.Token);
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
        StatusText = "Scanning titles for likely duplicates...";
        try
        {
            var candidates = await Task.Run(() => repository.GetFuzzyMatchCandidatesAsync(CancellationToken.None));
            var window = new DuplicateReviewWindow { Owner = Application.Current.MainWindow, DataContext = new DuplicateReviewViewModel(repository, candidates) };
            window.ShowDialog();
            StatusText = "Ready";
            await RefreshLibraryAsync();
            if (SelectedGame is not null) await LoadDetailsAsync(SelectedGame.Id);
        }
        catch (Exception ex) { logger.LogError(ex, "Could not scan for similar titles"); StatusText = $"Similar-title scan failed: {ex.Message}"; }
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

        StatusText = "Preparing full library export...";
        try
        {
            var rows = await Task.Run(() => repository.GetLibraryExportRowsAsync(CancellationToken.None));
            await Task.Run(() => WriteCsvAsync(dialog.FileName, rows, CancellationToken.None));
            StatusText = $"Exported {rows.Count:N0} files to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not export the library inventory");
            StatusText = $"Export failed: {ex.Message}";
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
