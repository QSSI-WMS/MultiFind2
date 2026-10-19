using SharpCompress.Archives;
using SharpCompress.Readers;

namespace MultiFind2;

/// <summary>
/// One file inside an archive. <see cref="Open"/> is only valid while this entry is the one the
/// enumeration is sitting on: most formats are read forward-only, so the bytes are gone once the
/// walk has moved on.
/// </summary>
public sealed class ArchiveEntry
{
    public required string Key { get; init; }
    public required long Size { get; init; }
    public required DateTime Modified { get; init; }
    public required bool IsEncrypted { get; init; }

    /// <summary>The entry's contents, buffered so the result is seekable.</summary>
    public required Func<Stream> Open { get; init; }
}

/// <summary>
/// Reading the files inside a compressed archive. Deliberately read-only and one level deep:
/// an archive inside an archive is left alone, and nothing here ever writes.
/// </summary>
public static class Archives
{
    /// <summary>
    /// What "compressed file" means here. <c>.tar.gz</c> needs no entry of its own -- it ends in
    /// <c>.gz</c>, and the reader sees the tar inside the gzip by itself.
    /// </summary>
    private static readonly string[] Extensions = { ".zip", ".7z", ".rar", ".gz", ".tgz", ".tar" };

    /// <summary>
    /// Above this an entry is spooled to a temp file rather than held in memory. Entry streams
    /// cannot seek, and both the binary sniff and the chunked scan rewind, so the bytes have to
    /// land somewhere first -- but a 2 Gb entry must not land in RAM.
    /// </summary>
    private const long MemoryLimit = 16L * 1024 * 1024;

    public static bool IsArchive(string fileName)
        => Extensions.Any(x => fileName.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The files inside <paramref name="path"/>, directories and nested archives aside. Throws if
    /// the archive is corrupt, encrypted at the header, or not the format its name claims.
    /// </summary>
    public static IEnumerable<ArchiveEntry> Entries(string path, CancellationToken token)
    {
        // 7-Zip is the one format with no forward-only reader: its index lives at the end of the
        // file, so it has to be opened for random access. Everything else streams, which for a
        // solid archive is the difference between decompressing once and once per entry.
        if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions());
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (Describe(path, entry.Key, entry.Size, entry.LastModifiedTime, entry.IsDirectory,
                             entry.IsEncrypted, entry.OpenEntryStream) is { } described)
                    yield return described;
            }
            yield break;
        }

        using var reader = ReaderFactory.OpenReader(path, new ReaderOptions());
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (Describe(path, entry.Key, entry.Size, entry.LastModifiedTime, entry.IsDirectory,
                         entry.IsEncrypted, reader.OpenEntryStream) is { } described)
                yield return described;
        }
    }

    private static ArchiveEntry? Describe(string archivePath, string? key, long size, DateTime? modified,
                                          bool isDirectory, bool isEncrypted, Func<Stream> open)
    {
        if (isDirectory) return null;

        // A single-file gzip carries the original name in its header, but only if the compressor
        // bothered to write one; dropping the .gz off the archive is what every tool falls back to.
        var name = Clean(key) is { Length: > 0 } cleaned
            ? cleaned
            : Path.GetFileNameWithoutExtension(archivePath);

        return new ArchiveEntry
        {
            Key = name,
            Size = size,
            Modified = modified ?? DateTime.MinValue,
            IsEncrypted = isEncrypted,
            Open = () => Buffer(open(), size)
        };
    }

    /// <summary>
    /// An entry key is whatever the archive says it is: forward slashes, a <c>./</c> prefix from
    /// tar, and -- in a hostile archive -- a drive letter or a <c>..</c> meant to escape the
    /// extraction folder. Nothing here is ever opened by this path, but it is shown as one, so a
    /// key that would read as somewhere else on disk is flattened to a relative name.
    /// </summary>
    private static string Clean(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";

        var parts = key.Replace('/', '\\')
                       .Split('\\', StringSplitOptions.RemoveEmptyEntries)
                       .Where(p => p != "." && p != ".." && !p.Contains(':'));

        return string.Join('\\', parts);
    }

    /// <summary>
    /// The entry's bytes in a stream that can seek. Small entries stay in memory; large ones go
    /// to a temp file that deletes itself when the stream is closed, however that happens.
    /// </summary>
    private static Stream Buffer(Stream source, long size)
    {
        using (source)
        {
            Stream target = size >= 0 && size <= MemoryLimit
                ? new MemoryStream((int)size)
                : new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
                                 FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);

            try
            {
                source.CopyTo(target);
                target.Position = 0;
                return target;
            }
            catch
            {
                target.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// How an entry is shown and sorted: the archive's own path with the entry's path below it,
    /// so a row reads as a place even though no such file exists on disk.
    /// </summary>
    public static string Display(string archivePath, string key) => Path.Combine(archivePath, key);

    /// <summary>
    /// One entry's contents, for the preview pane. Walks the archive again rather than holding it
    /// open across the search: the row may be looked at minutes later, or never.
    /// </summary>
    public static Stream OpenEntry(string archivePath, string key)
    {
        foreach (var entry in Entries(archivePath, CancellationToken.None))
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return entry.Open();

        throw new FileNotFoundException($"\"{key}\" is no longer in this archive.");
    }
}
