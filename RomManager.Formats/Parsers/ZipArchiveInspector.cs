using System.IO.Compression;
using RomManager.Core.Models;
using RomManager.Core.Services;

namespace RomManager.Formats.Parsers;

public sealed class ZipArchiveInspector(IFileSystem fileSystem, IFormatIdentifier formats) : IArchiveInspector
{
    public async Task<IdentificationResult?> IdentifyContentsAsync(FileCandidate archive, CancellationToken cancellationToken)
    {
        if (!archive.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            await using var stream = await fileSystem.OpenReadAsync(archive.FullPath, cancellationToken);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var candidates = zip.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).Select(entry =>
            {
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                return formats.IsCandidate(extension)
                    ? formats.Identify(new FileCandidate($"{archive.FullPath}/{entry.FullName}", entry.Name, extension, entry.Length, archive.Created, archive.Modified))
                    : null;
            }).Where(x => x?.System is not null).Cast<IdentificationResult>().OrderByDescending(x => x.Confidence).ToArray();
            return candidates.Length == 0 ? null : candidates[0] with { Confidence = Math.Min(.98, candidates[0].Confidence + .03), Reason = $"ZIP contains a recognized {candidates[0].System!.Name} file" };
        }
        catch (InvalidDataException) { return null; }
    }
}
