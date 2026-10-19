using System.Text;

namespace MultiFind2;

public enum DateField { Created, Modified, Accessed }

public enum SizeUnit { Bytes = 1, Kb = 1024, Mb = 1024 * 1024, Gb = 1024 * 1024 * 1024 }

public enum ContentMode { PlainText, Regex, Hex }

public enum FolderMatch { Files, FoldersToo, FoldersOnly }

public enum TriState { Any, Yes, No }

/// <summary>Everything a single search needs. Owned by a FindPage, snapshotted before the worker starts.</summary>
public sealed class SearchCriteria
{
    public List<string> SearchPaths { get; set; } = new();
    public bool IncludeSubfolders { get; set; } = true;
    public List<string> ExcludeFolders { get; set; } = new();

    /// <summary>';'-delimited wildcard patterns, e.g. "pick*;*.trc". Empty means all files.</summary>
    public string FileNamePatterns { get; set; } = "";
    public string ExcludeFileNames { get; set; } = "";

    public string Contents { get; set; } = "";
    public ContentMode ContentMode { get; set; } = ContentMode.PlainText;
    public bool CaseSensitive { get; set; }
    public bool WholeWords { get; set; }

    public bool UseSize { get; set; }
    public long MinSize { get; set; }
    public long MaxSize { get; set; }

    public bool UseDate { get; set; }
    public DateField DateField { get; set; } = DateField.Modified;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public FolderMatch FolderMatch { get; set; } = FolderMatch.Files;
    public TriState Hidden { get; set; } = TriState.Any;
    public TriState ReadOnly { get; set; } = TriState.Any;

    public bool SkipBinary { get; set; } = true;

    /// <summary>Look inside zip, 7z, rar, gzip and tar files as though they were folders.</summary>
    public bool SearchArchives { get; set; }

    public long MaxContentScanBytes { get; set; } = 256L * 1024 * 1024;
    public int MaxMatches { get; set; }

    public bool HasContentFilter => Contents.Length > 0;
}

/// <summary>
/// A page's criteria, flattened for the settings file so the open tabs come back on the next
/// launch. Results are deliberately not stored: they would be stale the moment a file changed.
/// </summary>
public sealed class PageState
{
    /// <summary>
    /// Stable identity, so a page keeps its backup folder across renames, duplicates and
    /// restarts. Generated here rather than required, so a settings file written before this
    /// existed loads with a fresh one instead of throwing.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "New page";

    public string Path { get; set; } = "";
    public string Name { get; set; } = "*.*";
    public string Contents { get; set; } = "";
    public string ExcludeNames { get; set; } = "";
    public List<string> ExcludeFolders { get; set; } = new();

    public bool Subfolders { get; set; } = true;
    public bool CaseSensitive { get; set; }
    public bool WholeWords { get; set; }
    public bool Hex { get; set; }
    public bool Regex { get; set; }

    public bool UseSize { get; set; }
    public string SizeMin { get; set; } = "";
    public string SizeMax { get; set; } = "";
    public int SizeUnit { get; set; } = 1;

    public bool UseDate { get; set; }
    public DateTime DateFrom { get; set; } = DateTime.Today.AddMonths(-1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public int DateField { get; set; }

    public bool SkipBinary { get; set; } = true;
    public bool SearchArchives { get; set; }
    public TriState Hidden { get; set; } = TriState.Any;
    public TriState ReadOnly { get; set; } = TriState.Any;
    public TriState Folders { get; set; } = TriState.No;

    public bool Replace { get; set; }
    public string ReplaceWith { get; set; } = "";
    public bool Backup { get; set; } = true;

    /// <summary>Preview context lines around each hit -- grep's -B and -A.</summary>
    public int ContextBefore { get; set; }
    public int ContextAfter { get; set; }
}

/// <summary>One result row. Sort keys are the raw typed values, never the display strings.</summary>
public sealed class FileMatch
{
    public required string FileName { get; init; }
    public required string Folder { get; init; }
    public required long Size { get; init; }
    public required DateTime Modified { get; init; }
    public required DateTime Created { get; init; }
    public required DateTime Accessed { get; init; }
    public required bool IsFolder { get; init; }

    /// <summary>The archive this row came out of, or null for a file that exists on disk.</summary>
    public string? ArchivePath { get; init; }

    /// <summary>Where inside <see cref="ArchivePath"/> this entry lives.</summary>
    public string? EntryKey { get; init; }

    /// <summary>
    /// A row with no file behind it. It can be read and previewed, but not opened, replaced in,
    /// or handed to the shell -- <see cref="FullPath"/> names a place that does not exist.
    /// </summary>
    public bool IsInArchive => ArchivePath != null;

    /// <summary>Substitutions made in this file by the last Replace. Not an init property:
    /// the row already exists when the replace runs, and re-finding would lose the selection.</summary>
    public int Replaces { get; set; }

    public string ReplacesText => Replaces > 0 ? Replaces.ToString() : "";

    public string FullPath => Path.Combine(Folder, FileName);

    public string Type => IsFolder
        ? "Folder"
        : Path.GetExtension(FileName).TrimStart('.');

    public string SizeText => IsFolder ? "" : FormatSize(Size);

    /// <summary>Fix #3: date AND time. "G" = short date + long time, so seconds are visible.</summary>
    public string ModifiedText => Modified.ToString("G");

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} b";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:0.00} Kb";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.00} Mb";
        return $"{bytes / (1024.0 * 1024 * 1024):0.00} Gb";
    }
}

public sealed record SkippedItem(string Path, string Reason);

public sealed record ErrorItem(string Path, string Message);

/// <summary>A chunk of progress handed to the UI thread. Batched so a huge folder cannot flood the message pump.</summary>
public sealed class SearchBatch
{
    public List<FileMatch> Matches { get; } = new();
    public List<SkippedItem> Skipped { get; } = new();
    public List<ErrorItem> Errors { get; } = new();
    public int Scanned { get; set; }
    public string? CurrentFolder { get; set; }

    public bool IsEmpty => Matches.Count == 0 && Skipped.Count == 0 && Errors.Count == 0;
}

public enum SearchOutcome { Finished, Canceled, Failed, LimitReached }

public sealed record SearchResult(SearchOutcome Outcome, int Found, int Scanned, TimeSpan Elapsed, string? Message = null);
