using RomManager.Core.Models;

namespace RomManager.App.ViewModels;

// Wraps a GameSummary row for display in the game list.
public sealed class GameListItem(GameSummary summary)
{
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
}
