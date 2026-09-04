using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.App.ViewModels;

public sealed class ExactTitleMergeItem(ExactTitleDuplicateGroup group) : ObservableObject
{
    private bool isBusy;
    public ExactTitleDuplicateGroup Group { get; } = group;
    public string SystemName => Group.SystemName;
    public string Title => Group.Title;
    public int CopyCount => Group.GameIds.Count;
    public int TotalFiles => Group.TotalFiles;
    public string SummaryText => $"{CopyCount} copies, {TotalFiles} file(s) total";
    public bool IsBusy { get => isBusy; set => Set(ref isBusy, value); }
}

public sealed class ExactTitleMergeViewModel : ObservableObject
{
    private readonly ILibraryRepository repository;
    private string statusText;

    public ExactTitleMergeViewModel(ILibraryRepository repository, IEnumerable<ExactTitleDuplicateGroup> groups)
    {
        this.repository = repository;
        foreach (var group in groups) Items.Add(new ExactTitleMergeItem(group));
        statusText = Items.Count == 0
            ? "No exact-title duplicates found."
            : $"{Items.Count:N0} groups of games sharing an exact title within the same system. Merging keeps the copy with the most files and reassigns every other copy's files onto it — nothing is deleted from disk.";
    }

    public BulkObservableCollection<ExactTitleMergeItem> Items { get; } = [];
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }

    public async Task MergeAsync(ExactTitleMergeItem item)
    {
        if (item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            await repository.MergeExactTitleDuplicateGroupAsync(item.Group.GameIds, CancellationToken.None);
            Items.Remove(item);
            StatusText = $"Merged \"{item.Title}\". {Items.Count:N0} groups remaining.";
        }
        finally { item.IsBusy = false; }
    }

    public void Skip(ExactTitleMergeItem item)
    {
        Items.Remove(item);
        StatusText = $"Skipped. {Items.Count:N0} groups remaining.";
    }

    public async Task MergeAllAsync()
    {
        var items = Items.ToArray();
        foreach (var item in items) await MergeAsync(item);
        StatusText = $"Merged {items.Length:N0} groups.";
    }
}
