using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.Core.Grouping;

public sealed class GameGroupingService : IGameGroupingService
{
    public string CreateGroupingKey(ParsedFileName parsed, string systemKey) => $"{systemKey}:{parsed.NormalizedTitle}";
}
