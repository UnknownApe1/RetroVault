using System.IO.Compression;
using RomManager.Core.Models;
using RomManager.Core.Services;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace RomManager.Formats.Parsers;

public sealed class CompressedArchiveInspector(IFileSystem fileSystem, IFormatIdentifier formats) : IArchiveInspector
{
    public async Task<IdentificationResult?> IdentifyContentsAsync(FileCandidate archive, CancellationToken cancellationToken)
    {
        var extension = archive.Extension.ToLowerInvariant();
        if (extension is not (".zip" or ".7z" or ".rar")) return null;
        try
        {
            var entries = extension == ".zip"
                ? await IdentifyZipEntriesAsync(archive, cancellationToken)
                : await IdentifySharpCompressEntriesAsync(archive, extension, cancellationToken);
            var candidates = entries.Where(x => x?.System is not null).Cast<IdentificationResult>().OrderByDescending(x => x.Confidence).ToArray();
            return candidates.Length == 0 ? null : candidates[0] with { Confidence = Math.Min(.98, candidates[0].Confidence + .03), Reason = $"{extension[1..].ToUpperInvariant()} archive contains a recognized {candidates[0].System!.Name} file" };
        }
        catch (Exception ex) when (ex is InvalidDataException or ExtractionException or NotSupportedException) { return null; }
    }

    private async Task<IReadOnlyList<IdentificationResult?>> IdentifyZipEntriesAsync(FileCandidate archive, CancellationToken cancellationToken)
    {
        await using var stream = await fileSystem.OpenReadAsync(archive.FullPath, cancellationToken);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return zip.Entries.Where(x => !string.IsNullOrEmpty(x.Name))
            .Select(entry => Identify(entry.FullName, entry.Name, entry.Length, archive)).ToArray();
    }

    private async Task<IReadOnlyList<IdentificationResult?>> IdentifySharpCompressEntriesAsync(FileCandidate archive, string extension, CancellationToken cancellationToken)
    {
        await using var stream = await fileSystem.OpenReadAsync(archive.FullPath, cancellationToken);
        using IArchive sharpArchive = extension == ".7z" ? SevenZipArchive.Open(stream) : RarArchive.Open(stream);
        return sharpArchive.Entries.Where(x => !x.IsDirectory && !string.IsNullOrEmpty(x.Key))
            .Select(entry => Identify(entry.Key!, Path.GetFileName(entry.Key!), entry.Size, archive)).ToArray();
    }

    private IdentificationResult? Identify(string fullName, string name, long size, FileCandidate archive)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return formats.IsCandidate(extension)
            ? formats.Identify(new FileCandidate($"{archive.FullPath}/{fullName}", name, extension, size, archive.Created, archive.Modified))
            : null;
    }
}
