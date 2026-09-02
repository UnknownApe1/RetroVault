using System.Text.Json;
using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.Formats.FormatDefinitions;

public sealed class SystemCatalog : ISystemDefinitionProvider, IFormatIdentifier
{
    private readonly IReadOnlyList<SystemDefinition> systems;
    private readonly Dictionary<string, List<(SystemDefinition System, SystemFormat Format)>> byExtension;

    public SystemCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Systems", "systems.json");
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "systems.json");
        string json;
        if (File.Exists(path)) json = File.ReadAllText(path);
        else
        {
            using var stream = typeof(SystemCatalog).Assembly.GetManifestResourceStream("RomManager.Formats.Systems.systems.json") ?? throw new FileNotFoundException("Embedded system definitions were not found.");
            using var reader = new StreamReader(stream); json = reader.ReadToEnd();
        }
        systems = JsonSerializer.Deserialize<List<SystemDefinition>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
            ?? throw new InvalidDataException("System definition file is empty.");
        var systemId = 1;
        var formatId = 1;
        foreach (var system in systems)
        {
            system.Id = systemId++;
            foreach (var format in system.Formats)
            {
                format.Id = formatId++;
                format.SystemDefinitionId = system.Id;
                format.SystemDefinition = system;
                format.Extension = NormalizeExtension(format.Extension);
            }
        }
        byExtension = systems.SelectMany(s => s.Formats.Select(f => (s, f))).GroupBy(x => x.f.Extension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.s, x.f)).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<SystemDefinition>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(systems);
    public bool IsCandidate(string extension) => byExtension.ContainsKey(NormalizeExtension(extension));

    public IdentificationResult Identify(FileCandidate file)
    {
        if (!byExtension.TryGetValue(NormalizeExtension(file.Extension), out var candidates)) return new(null, null, 0, "Unsupported extension");
        var path = file.FullPath.Replace('\\', '/');
        var scored = candidates.Select(c =>
        {
            var score = c.Format.Priority;
            if (path.Contains($"/{c.System.Key}/", StringComparison.OrdinalIgnoreCase)) score += 100;
            if (path.Contains(c.System.Name, StringComparison.OrdinalIgnoreCase)) score += 75;
            if (candidates.Count == 1) score += 50;
            return (c.System, c.Format, Score: score);
        }).OrderByDescending(x => x.Score).ToArray();
        var best = scored[0];
        var confidence = scored.Length == 1 ? .95 : best.Score > scored[1].Score ? .8 : .5;
        return new(best.System, best.Format, confidence, confidence <= .5 ? "Ambiguous extension; selected highest-priority format" : "Extension and path hints");
    }

    private static string NormalizeExtension(string extension) => extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
}
