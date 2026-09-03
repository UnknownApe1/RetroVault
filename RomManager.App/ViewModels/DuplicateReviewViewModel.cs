using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.App.ViewModels;

public sealed class DuplicateReviewItem(FuzzyMatchCandidate candidate) : ObservableObject
{
    private bool isBusy;
    public FuzzyMatchCandidate Candidate { get; } = candidate;
    public string SystemName => Candidate.SystemName;
    public string TitleA => Candidate.GameATitle;
    public string TitleB => Candidate.GameBTitle;
    public int FilesA => Candidate.FileCountA;
    public int FilesB => Candidate.FileCountB;
    public string SimilarityText => $"{Candidate.Similarity:P0} similar";
    public bool IsBusy { get => isBusy; set => Set(ref isBusy, value); }
}

public sealed class DuplicateReviewViewModel : ObservableObject
{
    private readonly ILibraryRepository repository;
    private string statusText;

    public DuplicateReviewViewModel(ILibraryRepository repository, IEnumerable<FuzzyMatchCandidate> candidates)
    {
        this.repository = repository;
        foreach (var candidate in candidates) Items.Add(new DuplicateReviewItem(candidate));
        statusText = Items.Count == 0 ? "No likely duplicate titles found." : $"{Items.Count:N0} possible duplicate title pairs found. Nothing is merged until you choose a copy to keep.";
    }

    public BulkObservableCollection<DuplicateReviewItem> Items { get; } = [];
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }

    public async Task KeepAsync(DuplicateReviewItem item, bool keepA)
    {
        if (item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            var keepId = keepA ? item.Candidate.GameAId : item.Candidate.GameBId;
            var mergeId = keepA ? item.Candidate.GameBId : item.Candidate.GameAId;
            await repository.MergeGamesAsync(keepId, mergeId, CancellationToken.None);
            Items.Remove(item);
            StatusText = $"Merged \"{(keepA ? item.TitleB : item.TitleA)}\" into \"{(keepA ? item.TitleA : item.TitleB)}\". {Items.Count:N0} pairs remaining.";
        }
        finally { item.IsBusy = false; }
    }

    public void Dismiss(DuplicateReviewItem item)
    {
        Items.Remove(item);
        StatusText = $"Dismissed. {Items.Count:N0} pairs remaining.";
    }
}
