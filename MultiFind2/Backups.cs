using System.Text;

namespace MultiFind2;

/// <summary>
/// Where the originals of replaced files are kept. Deliberately not a sibling .bak: backups do
/// not belong in the folder being searched, where the next search would turn them up as results.
///
/// The layout is one folder per page, one folder per run inside it, and the original directory
/// tree mirrored inside that -- so a backup is readable, and restorable, with nothing but
/// Explorer and a drag.
/// </summary>
public sealed class BackupStore
{
    /// <summary>Runs older than this are dropped at startup; nothing else ever prunes %TMP%.</summary>
    public const int KeepDays = 10;

    /// <summary>The share of free space past which backing up is worth asking about first.</summary>
    public const double FreeSpaceShare = 0.10;

    /// <summary>
    /// MULTIFIND2_BACKUPS redirects the whole store, as MULTIFIND2_SETTINGS does for the settings
    /// file. The test suite sets it so that exercising the store -- including the startup purge,
    /// which deletes -- can never reach real backups.
    /// </summary>
    public static readonly string Root =
        Environment.GetEnvironmentVariable("MULTIFIND2_BACKUPS") ??
        Path.Combine(Path.GetTempPath(), "MultiFind2", "Backups");

    private readonly string _page;
    private readonly string _header;
    private string? _run;

    public BackupStore(string pageFolder, string header)
    {
        _page = pageFolder;
        _header = header;
    }

    /// <summary>This run's folder, or null while nothing has been backed up yet.</summary>
    public string? RunFolder => _run;

    public int Count { get; private set; }

    /// <summary>
    /// Copies the file as it stands. Called before the rewrite and allowed to throw on purpose:
    /// a file that cannot be backed up must not be overwritten either.
    /// </summary>
    public void Save(string path)
    {
        _run ??= StartRun();

        var target = Path.Combine(_run, Mirror(path));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(path, target, overwrite: true);
        Count++;
    }

    /// <summary>Made on the first save, so a replace that changes nothing leaves nothing behind.</summary>
    private string StartRun()
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        var run = Path.Combine(_page, stamp);

        // Two runs inside the same second would otherwise pile into one folder.
        for (var n = 2; Directory.Exists(run); n++) run = Path.Combine(_page, $"{stamp} ({n})");

        Directory.CreateDirectory(run);

        // A wall of timestamped folders says nothing about what each one was for.
        File.WriteAllText(Path.Combine(run, InfoFile), _header, new UTF8Encoding(true));
        return run;
    }

    public const string InfoFile = "_backup-info.txt";

    /// <summary>
    /// "C:\src\app\x.cs" becomes "C\src\app\x.cs". Mirroring rather than flattening keeps two
    /// files of the same name apart, and leaves the backup looking like the tree it came from.
    /// </summary>
    public static string Mirror(string path)
    {
        var full = Path.GetFullPath(path);

        return full.StartsWith(@"\\", StringComparison.Ordinal)
            ? Path.Combine("UNC", full[2..])
            : full.Replace(":", "");   // a colon is the one character a drive adds that a folder cannot hold
    }

    /// <summary>
    /// One folder per page, named so it means something when the Backups button opens it. The id
    /// in brackets is what actually identifies the page: titles are editable, and two pages may
    /// perfectly well share one.
    /// </summary>
    public static string PageFolder(Guid id, string title)
    {
        var tag = "(" + id.ToString("N")[..8] + ")";
        var wanted = Path.Combine(Root, Safe(title) + " " + tag);

        var existing = Directory.Exists(Root)
            ? Directory.EnumerateDirectories(Root, "*" + tag).FirstOrDefault()
            : null;

        if (existing == null || string.Equals(existing, wanted, StringComparison.OrdinalIgnoreCase))
            return wanted;

        // The page has been renamed since its last replace; the history should follow the name.
        try { Directory.Move(existing, wanted); }
        catch (IOException) { return existing; }                 // open in Explorer, most likely
        catch (UnauthorizedAccessException) { return existing; }

        return wanted;
    }

    static string Safe(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        // Windows quietly drops trailing spaces and dots, which would leave the folder we just
        // created unfindable under the name we asked for.
        if (name.Length > 40) name = name[..40];
        name = name.Trim(' ', '.');

        return name.Length == 0 ? "Page" : name;
    }

    /// <summary>
    /// Drops runs older than <see cref="KeepDays"/> and any page folder they empty. Called at
    /// startup because %TMP% is nobody's idea of permanent storage: left alone this would grow
    /// without limit. Best-effort by design -- a janitor that can abort a launch is worse than
    /// one that misses a folder until the next one.
    /// </summary>
    public static void Purge()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            var cutoff = DateTime.UtcNow.AddDays(-KeepDays);

            foreach (var page in Directory.EnumerateDirectories(Root))
            {
                try
                {
                    foreach (var run in Directory.EnumerateDirectories(page))
                        if (Directory.GetCreationTimeUtc(run) < cutoff)
                            Directory.Delete(run, recursive: true);

                    if (!Directory.EnumerateFileSystemEntries(page).Any()) Directory.Delete(page);
                }
                catch (IOException) { }                  // in use; the next launch will get it
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Free bytes on the volume holding the store, or -1 when there is no volume to ask -- a
    /// %TMP% redirected onto a network share has no drive letter.
    /// </summary>
    public static long FreeSpace()
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Root))!).AvailableFreeSpace;
        }
        catch (ArgumentException) { return -1; }
        catch (IOException) { return -1; }
        catch (UnauthorizedAccessException) { return -1; }
    }

    /// <summary>
    /// Whether backing up <paramref name="bytes"/> would eat more than <see cref="FreeSpaceShare"/>
    /// of what is free. The figure is the worst case -- every candidate file copied -- because
    /// which files actually contain the text is not known until the replace has run.
    /// </summary>
    public static bool IsTight(long bytes, long free) => free >= 0 && bytes > free * FreeSpaceShare;
}
