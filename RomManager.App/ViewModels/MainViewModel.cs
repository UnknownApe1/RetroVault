using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private readonly ILogger<MainViewModel> logger;
    private CancellationTokenSource? scanCancellation;
    private string searchText = "", statusText = "Ready", currentPath = "";
    private bool isScanning;
    private SystemDefinition? selectedSystem;
    private GameSummary? selectedGame;
    private GameFile? selectedFile;
    private ScanLocation? selectedLocation;
    private Game? gameDetails;
    private LibraryCounts counts = new(0, 0, 0, 0);

    public MainViewModel(ILibraryRepository repository, ILibraryScanner scanner, IHashService hashes, ILogger<MainViewModel> logger)
    {
        this.repository = repository; this.scanner = scanner; this.hashes = hashes; this.logger = logger;
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
    }

    public ObservableCollection<GameSummary> Games { get; } = [];
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
    public ScanLocation? SelectedLocation { get => selectedLocation; set { if (Set(ref selectedLocation, value)) { RemoveFolderCommand.Refresh(); ToggleLocationCommand.Refresh(); ToggleRecursiveCommand.Refresh(); Raise(nameof(LocationToggleLabel)); Raise(nameof(RecursiveToggleLabel)); } } }
    public string LocationToggleLabel => SelectedLocation?.Enabled == true ? "Disable" : "Enable";
    public string RecursiveToggleLabel => SelectedLocation?.Recursive == true ? "Recursive: On" : "Recursive: Off";
    public GameFile? SelectedFile { get => selectedFile; set { if (Set(ref selectedFile, value)) { OpenFolderCommand.Refresh(); CopyPathCommand.Refresh(); VerifyHashCommand.Refresh(); } } }
    public string SearchText { get => searchText; set { if (Set(ref searchText, value)) _ = RefreshLibraryAsync(); } }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string CurrentPath { get => currentPath; private set => Set(ref currentPath, value); }
    public bool IsScanning { get => isScanning; private set { if (Set(ref isScanning, value)) { ScanCommand.Refresh(); CancelCommand.Refresh(); AddFolderCommand.Refresh(); RemoveFolderCommand.Refresh(); ToggleLocationCommand.Refresh(); ToggleRecursiveCommand.Refresh(); VerifyHashCommand.Refresh(); } } }
    public LibraryCounts Counts { get => counts; private set => Set(ref counts, value); }
    public SystemDefinition? SelectedSystem { get => selectedSystem; set { if (Set(ref selectedSystem, value)) _ = RefreshLibraryAsync(); } }
    public GameSummary? SelectedGame { get => selectedGame; set { if (Set(ref selectedGame, value)) _ = LoadDetailsAsync(value?.Id); } }
    public Game? GameDetails { get => gameDetails; private set { Set(ref gameDetails, value); Raise(nameof(DetailFiles)); } }
    public IReadOnlyList<GameFile> DetailFiles => GameDetails?.Files ?? [];

    public async Task InitializeAsync()
    {
        var interrupted = await repository.HasInterruptedScanAsync(CancellationToken.None);
        await RefreshLocationsAsync(); await RefreshSystemsAsync(); await RefreshLibraryAsync();
        if (ScanLocations.Count > 0)
        {
            if (interrupted) StatusText = "Interrupted scan detected. Resuming safely...";
            await ScanAsync(interrupted);
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

    private async Task RefreshSystemsAsync() { Systems.Clear(); Systems.Add(new SystemDefinition { Id = 0, Key = "ALL", Name = "All systems", Manufacturer = "" }); foreach (var s in await repository.GetSystemsAsync(CancellationToken.None)) Systems.Add(s); SelectedSystem ??= Systems[0]; }
    private async Task RefreshLocationsAsync() { var selectedId = SelectedLocation?.Id; ScanLocations.Clear(); foreach (var item in await repository.GetScanLocationsAsync(false, CancellationToken.None)) ScanLocations.Add(item); SelectedLocation = selectedId.HasValue ? ScanLocations.FirstOrDefault(x => x.Id == selectedId) : null; }
    private async Task RefreshLibraryAsync() { var games = await repository.SearchGamesAsync(SearchText, SelectedSystem is { Id: > 0 } ? SelectedSystem.Id : null, CancellationToken.None); Games.Clear(); foreach (var game in games) Games.Add(game); Counts = await repository.GetCountsAsync(CancellationToken.None); }
    private async Task LoadDetailsAsync(long? id) { GameDetails = id.HasValue ? await repository.GetGameDetailsAsync(id.Value, CancellationToken.None) : null; SelectedFile = GameDetails?.Files.FirstOrDefault(); }
    private async Task VerifyHashAsync()
    {
        if (SelectedFile is null) return;
        StatusText = $"Verifying {SelectedFile.FileName}...";
        try { var sha = await hashes.ComputeSha256Async(SelectedFile.FullPath, CancellationToken.None); await repository.SaveSha256Async(SelectedFile.Id, sha, CancellationToken.None); await repository.MarkExactDuplicatesAsync(sha, CancellationToken.None); StatusText = $"SHA-256 verified: {sha[..12]}..."; await RefreshLibraryAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText = $"Verification failed: {ex.Message}"; }
    }
    private void OpenSelectedFolder() { if (SelectedFile is null) return; Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedFile.FullPath}\"") { UseShellExecute = true }); }
}
