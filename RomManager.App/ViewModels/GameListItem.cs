using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.App.ViewModels;

// Wraps a GameSummary row so the list can carry a per-row, lazily-loaded thumbnail without fetching for
// every row up front — EnsureThumbnailLoadedAsync is meant to be called only when a row is actually
// realized on screen (see MainWindow's DataContextChanged handler on the thumbnail cell).
public sealed class GameListItem(GameSummary summary, IThumbnailService thumbnails) : ObservableObject
{
    private string? thumbnailPath;
    private bool loadStarted;

    public GameSummary Summary { get; } = summary;
    public long Id => Summary.Id;
    public string Title => Summary.Title;
    public string System => Summary.System;
    public int FileCount => Summary.FileCount;
    public int DuplicateCount => Summary.DuplicateCount;
    public string DuplicateText => Summary.DuplicateCount > 0 ? Summary.DuplicateCount.ToString() : "";
    public int VerifiedCount => Summary.VerifiedCount;
    public FileStatus WorstStatus => Summary.WorstStatus;
    public string Region => Summary.PreferredRegion ?? "";
    public string SizeText => FormatSize(Summary.TotalSizeBytes);
    public bool IsWanted => Summary.IsWanted;
    public string WantedText => Summary.IsWanted ? "★" : "";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:N0} {units[unit]}" : $"{size:N1} {units[unit]}";
    }
    public string? ThumbnailPath { get => thumbnailPath; private set => Set(ref thumbnailPath, value); }

    public async void EnsureThumbnailLoadedAsync()
    {
        if (loadStarted) return;
        loadStarted = true;
        var title = ThumbnailTitleResolver.Resolve(Summary.PreferredCatalogName, Summary.Title, Summary.PreferredRegion);
        if (title is null) return;
        // A cache hit resolves with no real I/O, so without this yield the whole lookup completes
        // synchronously inside the DataContextChanged handler that triggered it — before the cell's
        // own Image binding has finished attaching to this DataContext — and the update gets missed.
        await Task.Yield();
        try { ThumbnailPath = await thumbnails.GetThumbnailPathAsync(Summary.SystemKey, title, CancellationToken.None); }
        catch (Exception) { }
    }
}
