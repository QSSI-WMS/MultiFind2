using System.Text;
using System.Text.RegularExpressions;

namespace MultiFind2;

/// <summary>What to substitute, mirroring the content-search options so Replace can only ever
/// touch the text that Find actually matched. A null <paramref name="Backup"/> means the
/// originals are not kept -- the store, not a flag, is what decides where they go.</summary>
public sealed record ReplacePlan(
    string Find,
    string ReplaceWith,
    bool CaseSensitive,
    bool WholeWords,
    bool UseRegex,
    BackupStore? Backup);

/// <summary><paramref name="PerFile"/> is keyed by full path and only holds the files that
/// actually changed; the results list uses it to fill in its Replaces column.</summary>
public sealed record ReplaceResult(int FilesChanged, int Replacements, Dictionary<string, int> PerFile,
                                   List<ErrorItem> Errors, bool Canceled);

/// <summary>
/// Rewrites the matched text inside already-found files. Deliberately a separate step from the
/// search: Find finds, Replace replaces, so no single click can both scan and overwrite.
/// </summary>
public static class Replacer
{
    /// <summary>Replacing rewrites the whole file in memory, so very large files are refused
    /// rather than silently exhausting memory mid-run.</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;

    public static Task<ReplaceResult> RunAsync(IReadOnlyList<string> paths, ReplacePlan plan,
                                               IProgress<int> progress, CancellationToken token)
        => Task.Run(() => Run(paths, plan, progress, token), token);

    static ReplaceResult Run(IReadOnlyList<string> paths, ReplacePlan plan,
                             IProgress<int> progress, CancellationToken token)
    {
        var pattern = BuildPattern(plan);
        var errors = new List<ErrorItem>();
        var perFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hits = 0;
        var canceled = false;

        for (var i = 0; i < paths.Count; i++)
        {
            if (token.IsCancellationRequested) { canceled = true; break; }

            try
            {
                var replaced = ReplaceInFile(paths[i], pattern, plan);
                if (replaced > 0) { perFile[paths[i]] = replaced; hits += replaced; }
            }
            catch (Exception ex)
            {
                errors.Add(new ErrorItem(paths[i], ex.Message));
            }

            progress.Report(i + 1);
        }

        return new ReplaceResult(perFile.Count, hits, perFile, errors, canceled);
    }

    /// <summary>Same semantics as ContentMatcher, so what Find highlighted is what Replace edits.</summary>
    public static Regex BuildPattern(ReplacePlan plan)
    {
        var options = RegexOptions.Multiline;
        if (!plan.CaseSensitive) options |= RegexOptions.IgnoreCase;

        var pattern = plan.UseRegex ? plan.Find : Regex.Escape(plan.Find);
        if (plan.WholeWords) pattern = $@"\b(?:{pattern})\b";

        return new Regex(pattern, options, TimeSpan.FromSeconds(5));
    }

    /// <summary>Returns the number of substitutions made; 0 means the file was left untouched.</summary>
    static int ReplaceInFile(string path, Regex pattern, ReplacePlan plan)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("File no longer exists");
        if (info.Length > MaxFileBytes) throw new IOException($"File is larger than {MaxFileBytes / 1024 / 1024} Mb");
        if (info.IsReadOnly) throw new IOException("File is read-only");

        string text;
        Encoding encoding;
        using (var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
            // Read first: CurrentEncoding only reflects a detected BOM once something has been read.
            encoding = reader.CurrentEncoding;
        }

        var count = 0;
        // In regex mode Result() expands $1 and friends; a literal search takes the text as typed.
        var updated = pattern.Replace(text, m =>
        {
            count++;
            return plan.UseRegex ? m.Result(plan.ReplaceWith) : plan.ReplaceWith;
        });

        if (count == 0) return 0;

        Commit(path, updated, encoding, plan.Backup);
        return count;
    }

    /// <summary>
    /// Writes through a sibling temp file so a failure part-way cannot leave a half-rewritten
    /// original. File.Replace does the swap atomically and keeps the destination's attributes
    /// and ACLs, which a plain Move would discard.
    /// </summary>
    static void Commit(string path, string text, Encoding encoding, BackupStore? backup)
    {
        // Backed up before anything is written, and deliberately not caught: if the original
        // cannot be put somewhere safe, it does not get overwritten either.
        backup?.Save(path);

        var temp = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetRandomFileName());

        try
        {
            File.WriteAllText(temp, text, encoding);
            File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }
}
