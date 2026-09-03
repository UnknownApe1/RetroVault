using System.Globalization;
using System.Xml.Linq;
using RomManager.Core.Models;

namespace RomManager.Infrastructure.Catalog;

public static class ClrMameProDatParser
{
    public static IReadOnlyList<CatalogEntry> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.AsSpan().TrimStart().StartsWith("<", StringComparison.Ordinal) ? ParseXml(text) : ParseClrMamePro(text);
    }

    private static IReadOnlyList<CatalogEntry> ParseClrMamePro(string text)
    {
        var tokens = Tokenize(text);
        var entries = new List<CatalogEntry>();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Kind != TokenKind.Word || (tokens[index].Value != "game" && tokens[index].Value != "machine")) continue;
            if (index + 1 >= tokens.Count || tokens[index + 1].Kind != TokenKind.LeftParenthesis) continue;
            index += 2;
            var game = ParseBlock(tokens, ref index);
            var gameName = game.Properties.GetValueOrDefault("name") ?? game.Properties.GetValueOrDefault("description") ?? "Unknown";
            foreach (var rom in game.Children.Where(x => x.Name is "rom" or "disk"))
            {
                var romName = rom.Properties.GetValueOrDefault("name") ?? gameName;
                _ = long.TryParse(rom.Properties.GetValueOrDefault("size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
                entries.Add(new CatalogEntry(gameName, romName, size, CleanHash(rom.Properties.GetValueOrDefault("crc")), CleanHash(rom.Properties.GetValueOrDefault("md5")), CleanHash(rom.Properties.GetValueOrDefault("sha1")), CleanHash(rom.Properties.GetValueOrDefault("sha256"))));
            }
        }
        return entries;
    }

    private static IReadOnlyList<CatalogEntry> ParseXml(string text)
    {
        var document = XDocument.Parse(text, LoadOptions.None);
        var entries = new List<CatalogEntry>();
        foreach (var game in document.Descendants().Where(x => x.Name.LocalName is "game" or "machine" or "software"))
        {
            var gameName = (string?)game.Attribute("name") ?? game.Elements().FirstOrDefault(x => x.Name.LocalName == "description")?.Value ?? "Unknown";
            foreach (var rom in game.Descendants().Where(x => x.Name.LocalName is "rom" or "disk"))
            {
                _ = long.TryParse((string?)rom.Attribute("size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
                entries.Add(new CatalogEntry(gameName, (string?)rom.Attribute("name") ?? gameName, size, CleanHash((string?)rom.Attribute("crc")), CleanHash((string?)rom.Attribute("md5")), CleanHash((string?)rom.Attribute("sha1")), CleanHash((string?)rom.Attribute("sha256"))));
            }
        }
        return entries;
    }

    private static DatBlock ParseBlock(IReadOnlyList<Token> tokens, ref int index)
    {
        var block = new DatBlock("");
        while (index < tokens.Count && tokens[index].Kind != TokenKind.RightParenthesis)
        {
            if (tokens[index].Kind != TokenKind.Word) { index++; continue; }
            var name = tokens[index++].Value.ToLowerInvariant();
            if (index < tokens.Count && tokens[index].Kind == TokenKind.LeftParenthesis)
            {
                index++;
                var child = ParseBlock(tokens, ref index);
                child.Name = name;
                block.Children.Add(child);
                if (index < tokens.Count && tokens[index].Kind == TokenKind.RightParenthesis) index++;
            }
            else if (index < tokens.Count && tokens[index].Kind is TokenKind.Word or TokenKind.String)
            {
                block.Properties[name] = tokens[index++].Value;
            }
        }
        return block;
    }

    private static List<Token> Tokenize(string text)
    {
        var result = new List<Token>();
        for (var index = 0; index < text.Length;)
        {
            if (char.IsWhiteSpace(text[index])) { index++; continue; }
            if (text[index] == '(') { result.Add(new(TokenKind.LeftParenthesis, "(")); index++; continue; }
            if (text[index] == ')') { result.Add(new(TokenKind.RightParenthesis, ")")); index++; continue; }
            if (text[index] == '"')
            {
                index++;
                var value = new System.Text.StringBuilder();
                while (index < text.Length && text[index] != '"')
                {
                    if (text[index] == '\\' && index + 1 < text.Length && text[index + 1] == '"') index++;
                    value.Append(text[index++]);
                }
                if (index < text.Length) index++;
                result.Add(new(TokenKind.String, value.ToString()));
                continue;
            }
            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not '(' and not ')') index++;
            result.Add(new(TokenKind.Word, text[start..index]));
        }
        return result;
    }

    private static string? CleanHash(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private enum TokenKind { Word, String, LeftParenthesis, RightParenthesis }
    private sealed record Token(TokenKind Kind, string Value);
    private sealed class DatBlock(string name)
    {
        public string Name { get; set; } = name;
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DatBlock> Children { get; } = [];
    }
}
