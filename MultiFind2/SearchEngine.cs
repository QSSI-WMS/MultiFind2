using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MultiFind2;

/// <summary>
/// Walks the search paths on a background thread. Every filesystem call is guarded: a folder we
/// cannot read becomes an Errors row, never an exception that kills the app. Cancellation is
/// checked per directory and per file so Cancel takes effect promptly.
/// </summary>
public sealed class SearchEngine
{
    private const int FlushCount = 256;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    private readonly SearchCriteria _c;
    private readonly ContentMatcher _content;
    private readonly Regex[] _include;
    private readonly Regex[] _exclude;
    private readonly string[] _excludeFolders;

    public SearchEngine(SearchCriteria criteria)
    {
        _c = criteria;
        _content = new ContentMatcher(criteria);
        _include = BuildPatterns(criteria.FileNamePatterns);
        _exclude = BuildPatterns(criteria.ExcludeFileNames);
        _excludeFolders = criteria.ExcludeFolders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.TrimEnd('\\', '/'))
            .ToArray();
    }

    private static Regex[] BuildPatterns(string delimited)
    {
        if (string.IsNullOrWhiteSpace(delimited)) return Array.Empty<Regex>();

        return delimited
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(WildcardToRegex)
            .ToArray();
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public Task<SearchResult> RunAsync(IProgress<SearchBatch> progress, CancellationToken token)
        => Task.Run(() => Run(progress, token), token);

    private SearchResult Run(IProgress<SearchBatch> progress, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var batch = new SearchBatch();
        var lastFlush = clock.Elapsed;
        var found = 0;
        var scanned = 0;
        var limitHit = false;

        void Flush(bool force)
        {
            if (batch.IsEmpty && !force) return;
            if (!force && batch.Matches.Count < FlushCount && clock.Elapsed - lastFlush < FlushInterval) return;

            batch.Scanned = scanned;
            progress.Report(batch);
            batch = new SearchBatch();
            lastFlush = clock.Elapsed;
        }

        try
        {
            foreach (var root in _c.SearchPaths)
            {
                token.ThrowIfCancellationRequested();

                if (!Directory.Exists(root))
                {
                    batch.Errors.Add(new ErrorItem(root, $"Path \"{root}\" does not exist"));
                    continue;
                }

                var stack = new Stack<string>();
                stack.Push(root);

                while (stack.Count > 0)
                {
                    token.ThrowIfCancellationRequested();

                    var dir = stack.Pop();
                    batch.CurrentFolder = dir;

                    List<FileSystemInfo> entries;
                    try
                    {
                        entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList();
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        batch.Errors.Add(new ErrorItem(dir, Describe(ex)));
                        Flush(false);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        token.ThrowIfCancellationRequested();

                        var isDir = (entry.Attributes & FileAttributes.Directory) != 0;

                        if (isDir && _c.IncludeSubfolders && !IsExcludedFolder(entry.FullName))
                            stack.Push(entry.FullName);

                        scanned++;

                        try
                        {
                            if (!Accept(entry, isDir, out var skip, token))
                            {
                                if (skip != null) batch.Skipped.Add(new SkippedItem(entry.FullName, skip));
                                continue;
                            }
                        }
                        catch (Exception ex) when (IsFileSystemError(ex))
                        {
                            batch.Errors.Add(new ErrorItem(entry.FullName, Describe(ex)));
                            continue;
                        }

                        batch.Matches.Add(ToMatch(entry, isDir));
                        found++;

                        if (_c.MaxMatches > 0 && found >= _c.MaxMatches)
                        {
                            limitHit = true;
                            break;
                        }
                    }

                    Flush(false);
                    if (limitHit) break;
                }

                if (limitHit) break;
            }
        }
        catch (OperationCanceledException)
        {
            Flush(true);
            return new SearchResult(SearchOutcome.Canceled, found, scanned, clock.Elapsed);
        }
        catch (Exception ex)
        {
            Flush(true);
            return new SearchResult(SearchOutcome.Failed, found, scanned, clock.Elapsed, Describe(ex));
        }

        Flush(true);
        return new SearchResult(limitHit ? SearchOutcome.LimitReached : SearchOutcome.Finished,
                                found, scanned, clock.Elapsed);
    }

    private bool IsExcludedFolder(string path)
        => _excludeFolders.Any(x => path.Equals(x, StringComparison.OrdinalIgnoreCase)
                                 || path.StartsWith(x + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private bool Accept(FileSystemInfo entry, bool isDir, out string? skipReason, CancellationToken token)
    {
        skipReason = null;

        switch (_c.FolderMatch)
        {
            case FolderMatch.Files when isDir: return false;
            case FolderMatch.FoldersOnly when !isDir: return false;
        }

        var name = entry.Name;
        if (_include.Length > 0 && !_include.Any(r => r.IsMatch(name))) return false;
        if (_exclude.Length > 0 && _exclude.Any(r => r.IsMatch(name))) return false;

        var attrs = entry.Attributes;
        if (!MatchesTriState(_c.Hidden, (attrs & FileAttributes.Hidden) != 0)) return false;
        if (!MatchesTriState(_c.ReadOnly, (attrs & FileAttributes.ReadOnly) != 0)) return false;

        if (_c.UseDate)
        {
            var when = _c.DateField switch
            {
                DateField.Created => entry.CreationTime,
                DateField.Accessed => entry.LastAccessTime,
                _ => entry.LastWriteTime
            };
            if (when < _c.DateFrom || when > _c.DateTo) return false;
        }

        if (isDir) return true;

        var size = ((FileInfo)entry).Length;
        if (_c.UseSize && (size < _c.MinSize || size > _c.MaxSize)) return false;

        if (!_c.HasContentFilter) return true;

        // Scanning a multi-gigabyte file must still honour Cancel, so the token goes all the way down.
        return _content.Match(entry.FullName, out skipReason, token) == ContentResult.Match;
    }

    private static bool MatchesTriState(TriState want, bool actual) => want switch
    {
        TriState.Yes => actual,
        TriState.No => !actual,
        _ => true
    };

    private static FileMatch ToMatch(FileSystemInfo entry, bool isDir) => new()
    {
        FileName = entry.Name,
        Folder = Path.GetDirectoryName(entry.FullName) ?? "",
        Size = isDir ? 0 : ((FileInfo)entry).Length,
        Modified = entry.LastWriteTime,
        Created = entry.CreationTime,
        Accessed = entry.LastAccessTime,
        IsFolder = isDir
    };

    /// <summary>Filesystem faults are expected during a broad walk and must become rows, not crashes.</summary>
    private static bool IsFileSystemError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
              or NotSupportedException or ArgumentException;

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "access denied",
        PathTooLongException => "path is too long",
        DirectoryNotFoundException => "folder not found",
        FileNotFoundException => "file not found",
        _ => ex.Message
    };
}
