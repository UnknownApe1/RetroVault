using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.App.ViewModels;

public sealed class TitleCleanupItem(TitleCleanupCandidate candidate) : ObservableObject
{
    private bool isBusy;
    public TitleCleanupCandidate Candidate { get; } = candidate;
    public string SystemName => Candidate.SystemName;
    public string CurrentTitle => Candidate.CurrentTitle;
    public string SuggestedTitle => Candidate.SuggestedTitle;
    public string Reason => Candidate.Reason;
    public bool IsBusy { get => isBusy; set => Set(ref isBusy, value); }
}

public sealed class TitleCleanupViewModel : ObservableObject
{
    private readonly ILibraryRepository repository;
    private string statusText;

    public TitleCleanupViewModel(ILibraryRepository repository, IEnumerable<TitleCleanupCandidate> candidates)
    {
        this.repository = repository;
        foreach (var candidate in candidates) Items.Add(new TitleCleanupItem(candidate));
        statusText = Items.Count == 0
            ? "No title cleanup suggestions found."
            : $"{Items.Count:N0} suggested title fixes. Nothing changes until you apply a suggestion — this never touches your files, only the name shown in this app.";
    }

    public BulkObservableCollection<TitleCleanupItem> Items { get; } = [];
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }

    public async Task ApplyAsync(TitleCleanupItem item)
    {
        if (item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            await repository.ApplyTitleCleanupAsync(item.Candidate.GameId, item.Candidate.SuggestedTitle, CancellationToken.None);
            Items.Remove(item);
            StatusText = $"Renamed to \"{item.SuggestedTitle}\". {Items.Count:N0} suggestions remaining.";
        }
        finally { item.IsBusy = false; }
    }

    public void Skip(TitleCleanupItem item)
    {
        Items.Remove(item);
        StatusText = $"Skipped. {Items.Count:N0} suggestions remaining.";
    }

    public async Task ApplyAllAsync()
    {
        var items = Items.ToArray();
        foreach (var item in items) await ApplyAsync(item);
        StatusText = $"Applied {items.Length:N0} title fixes.";
    }
}
