using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiFind2;

public sealed class AppSettings
{
    /// <summary>Theme name; empty means "never chosen" -- keep following the Windows app theme.</summary>
    public string Theme { get; set; } = "";

    /// <summary>Most-recent-first drop-down history, shared by every page.</summary>
    public List<string> PathHistory { get; set; } = new();
    public List<string> NameHistory { get; set; } = new();
    public List<string> ContentsHistory { get; set; } = new();
    public List<string> ReplaceHistory { get; set; } = new();

    /// <summary>The open pages, restored on the next launch. Criteria only -- results go stale.</summary>
    public List<PageState> Pages { get; set; } = new();
    public int ActivePage { get; set; }

    /// <summary>
    /// Bumped on every write, so an instance can tell whether the file it is about to replace is
    /// still the one it read. Without it, a second window sitting idle since before the real work
    /// was done rolls the whole session back to its own blank page when it is finally closed.
    /// </summary>
    public long Revision { get; set; }

    /// <summary>
    /// One instance for the whole app: the pages and the history are shared, and a second copy
    /// would let one writer silently overwrite the other's half of the file.
    /// </summary>
    public static AppSettings Current { get; } = Load();

    /// <summary>
    /// MULTIFIND2_SETTINGS redirects the whole file. The test suite sets it so that exercising
    /// save/load can never reach into -- or delete -- the real pages, history and theme.
    ///
    /// Worked out on each use rather than cached in a static field: <see cref="Current"/> is
    /// itself a static initialiser, and a field declared below it is still null when it runs.
    /// That is not a hypothetical -- it cost every restored session until it was found, because
    /// File.Exists(null) is quietly false, so the app loaded blank and saved correctly.
    /// </summary>
    static string FilePath =>
        Environment.GetEnvironmentVariable("MULTIFIND2_SETTINGS") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "MultiFind2", "settings.json");

    // The relaxed encoder keeps the file hand-editable: the default one escapes '+', which
    // would store the "VS Dark+" theme as "VS Dark\u002B". Enums go in by name for the same
    // reason. Built once, on first use, for the same ordering reason as FilePath.
    static JsonSerializerOptions JsonOpts => _jsonOpts ??= new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    static JsonSerializerOptions? _jsonOpts;

    /// <summary>The revision this process last read or wrote; anything else on disk is someone else's.</summary>
    static long _seen;

    public static AppSettings Load()
    {
        var loaded = Read() ?? new AppSettings();
        _seen = loaded.Revision;
        return loaded;
    }

    /// <summary>Stores the session, replacing whatever the file held. For saves the user asked
    /// for -- running a search, opening or closing a page, picking a theme.</summary>
    public void Save() => Write(this);

    /// <summary>
    /// Stores the session only if the file is still the one this instance read. For closing the
    /// window, where there was no user action to justify replacing a session that another
    /// instance has stored in the meantime: a second window left open since before the real work
    /// was done would otherwise roll everything back to its own blank page on the way out. The
    /// drop-down history is folded in either way -- both windows contributed to it.
    /// </summary>
    public void SaveUnlessSuperseded()
    {
        var disk = Read();
        if (disk == null || disk.Revision == _seen) { Write(this); return; }

        FoldHistoryInto(disk);
        Write(disk);
    }

    void Write(AppSettings state)
    {
        // Past whatever is already on disk, so a revision never repeats or goes backwards.
        state.Revision = Math.Max(_seen, Read()?.Revision ?? 0) + 1;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, JsonOpts));
            _seen = state.Revision;
        }
        catch { }
    }

    static AppSettings? Read()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Adds whatever this instance has that <paramref name="other"/> does not, behind what it
    /// already holds: the other file is the newer one, so its order is the one to keep.
    /// </summary>
    void FoldHistoryInto(AppSettings other)
    {
        Fold(PathHistory, other.PathHistory);
        Fold(NameHistory, other.NameHistory);
        Fold(ContentsHistory, other.ContentsHistory);
        Fold(ReplaceHistory, other.ReplaceHistory);

        static void Fold(List<string> mine, List<string> theirs)
        {
            foreach (var text in mine)
                if (!theirs.Contains(text, StringComparer.OrdinalIgnoreCase))
                    theirs.Add(text);

            while (theirs.Count > MaxHistory) theirs.RemoveAt(theirs.Count - 1);
        }
    }

    public const int MaxHistory = 20;

    /// <summary>Moves <paramref name="text"/> to the front of a most-recent-first list.</summary>
    public static void Remember(List<string> history, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        history.RemoveAll(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));
        history.Insert(0, text);
        while (history.Count > MaxHistory) history.RemoveAt(history.Count - 1);
    }
}
