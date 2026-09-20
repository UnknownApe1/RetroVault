using RomManager.Core.Models;

namespace RomManager.App.ViewModels;

// Wraps a SystemDefinition so the sidebar can show a game count next to each system without adding a
// UI-only field to the shared entity model (SystemDefinition is EF-mapped; an extra property there would
// need explicit exclusion from the schema to avoid a column mismatch against the raw-SQL-created table).
public sealed class SystemListItem(SystemDefinition definition, long gameCount)
{
    public SystemDefinition Definition { get; } = definition;
    public int Id => Definition.Id;
    public string Key => Definition.Key;
    public string Name => Definition.Name;
    public long GameCount { get; } = gameCount;
    public string DisplayText => GameCount > 0 ? $"{Name} ({GameCount:N0})" : Name;
}
