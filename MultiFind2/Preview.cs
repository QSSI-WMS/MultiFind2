using System.Text;
using System.Text.RegularExpressions;

namespace MultiFind2;

/// <summary>Where a hit sits inside a preview line, after tabs have been expanded.</summary>
public readonly record struct MatchSpan(int Start, int Length);

public enum PreviewRowKind
{
    /// <summary>A line that contains at least one hit.</summary>
    Match,
    /// <summary>A -A/-B context line, shown dimmed: it is there for reading, not for finding.</summary>
    Context,
    /// <summary>A break between two groups of lines that aren't adjacent in the file.</summary>
    Separator,
    /// <summary>Why there is nothing to show (too large, binary, unreadable).</summary>
    Note
}

/// <summary>One row of the preview pane.</summary>
public sealed class PreviewRow
{
    public required PreviewRowKind Kind { get; init; }

    /// <summary>1-based line number; 0 means the row has no line of its own.</summary>
    public int Line { get; init; }

    public string Text { get; init; } = "";
    public IReadOnlyList<MatchSpan> Hits { get; init; } = PreviewScanner.NoHits;
}

/// <summary>
/// Turns a file plus the content pattern into the rows the preview shows. Runs off the UI thread:
/// a selected file can be large, and the user keeps arrowing down the results while it loads.
/// </summary>
public static class PreviewScanner
{
    internal static readonly List<MatchSpan> NoHits = new();

    public const long MaxBytes = 32L * 1024 * 1024;
    public const int MaxRows = 20_000;
    public const int MaxLineChars = 4_000;
    public const int MaxHitsPerLine = 200;
    public const int TabWidth = 4;

    private const int BinarySniffBytes = 8192;

    /// <summary>
    /// The same semantics as the search, so the preview highlights exactly what Find matched.
    /// Returns null when there is nothing to highlight; a bad pattern is not worth an error here,
    /// since BuildCriteria has already reported it before the search ever ran.
    /// </summary>
    public static Regex? Pattern(string find, bool caseSensitive, bool wholeWords, bool useRegex)
    {
        if (find.Length == 0) return null;

        var options = RegexOptions.CultureInvariant;
        if (!caseSensitive) options |= RegexOptions.IgnoreCase;

        // Each line is matched as its own string, so ^ and $ already anchor line-wise.
        var pattern = useRegex ? find : Regex.Escape(find);
        if (wholeWords) pattern = $@"\b(?:{pattern})\b";

        try { return new Regex(pattern, options, TimeSpan.FromSeconds(2)); }
        catch (ArgumentException) { return null; }
    }

    public static List<PreviewRow> Note(string message) => new()
    {
        new PreviewRow { Kind = PreviewRowKind.Note, Text = message }
    };

    /// <summary>
    /// The matching lines, with <paramref name="before"/> and <paramref name="after"/> lines of
    /// context around each -- grep's -B and -A. Non-adjacent groups are parted by a separator so
    /// a jump in line numbers is visible rather than merely implied.
    /// </summary>
    public static List<PreviewRow> Matches(FileMatch file, Regex? pattern, int before, int after,
                                           CancellationToken token)
    {
        if (pattern == null)
            return Note("Type something in \"File contents\" to preview the matches.");

        var rows = new List<PreviewRow>();
        var pending = new Queue<(int Line, string Text)>();
        var afterLeft = 0;
        var lastLine = 0;

        void Emit(PreviewRowKind kind, int line, string text, List<MatchSpan> hits)
        {
            // A gap in the numbering means these lines don't follow the previous ones.
            if (rows.Count > 0 && line > lastLine + 1)
                rows.Add(new PreviewRow { Kind = PreviewRowKind.Separator });

            rows.Add(Build(kind, line, text, hits));
            lastLine = line;
        }

        try
        {
            using var content = Open(file);
            if (Reject(content) is { } reason) return Note(reason);

            using var reader = Read(content);
            var number = 0;

            while (reader.ReadLine() is { } line)
            {
                number++;
                if ((number & 0x3FF) == 0) token.ThrowIfCancellationRequested();

                var hits = Find(pattern, line);
                if (hits.Count > 0)
                {
                    while (pending.Count > 0)
                    {
                        var (n, t) = pending.Dequeue();
                        Emit(PreviewRowKind.Context, n, t, NoHits);
                    }
                    Emit(PreviewRowKind.Match, number, line, hits);
                    afterLeft = after;
                }
                else if (afterLeft > 0)
                {
                    Emit(PreviewRowKind.Context, number, line, NoHits);
                    afterLeft--;
                }
                else if (before > 0)
                {
                    pending.Enqueue((number, line));
                    if (pending.Count > before) pending.Dequeue();
                }

                if (rows.Count >= MaxRows)
                {
                    rows.Add(new PreviewRow
                    {
                        Kind = PreviewRowKind.Note,
                        Text = $"Stopped after {MaxRows} lines."
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Note(ex.Message); }

        return rows.Count > 0 ? rows : Note("No match in this file.");
    }

    /// <summary>The whole file, with every hit highlighted wherever it falls.</summary>
    public static List<PreviewRow> WholeFile(FileMatch file, Regex? pattern, CancellationToken token)
    {
        var rows = new List<PreviewRow>();

        try
        {
            using var content = Open(file);
            if (Reject(content) is { } reason) return Note(reason);

            using var reader = Read(content);
            var number = 0;

            while (reader.ReadLine() is { } line)
            {
                number++;
                if ((number & 0x3FF) == 0) token.ThrowIfCancellationRequested();

                rows.Add(Build(PreviewRowKind.Match, number, line, Find(pattern, line)));

                if (rows.Count >= MaxRows)
                {
                    rows.Add(new PreviewRow
                    {
                        Kind = PreviewRowKind.Note,
                        Text = $"Stopped after {MaxRows} lines."
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Note(ex.Message); }

        return rows.Count > 0 ? rows : Note("The file is empty.");
    }

    /// <summary>Why this file cannot be previewed, or null if it can. Leaves the stream rewound.</summary>
    private static string? Reject(Stream content)
    {
        if (content.Length > MaxBytes) return $"The file is larger than {MaxBytes / 1024 / 1024} Mb.";
        return LooksBinary(content) ? "The file looks binary." : null;
    }

    private static bool LooksBinary(Stream content)
    {
        var buffer = new byte[Math.Min(BinarySniffBytes, content.Length)];
        var read = content.Read(buffer, 0, buffer.Length);
        content.Position = 0;

        // A UTF-16 BOM means those NULs are encoding, not content.
        if (read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
            return false;

        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }

    /// <summary>
    /// The bytes behind a result row, wherever they live. A row from inside an archive has no
    /// file to open, so its entry is decompressed again here -- which is also why the preview
    /// runs off the UI thread with a debounce in front of it.
    /// </summary>
    private static Stream Open(FileMatch file)
    {
        if (file.IsInArchive)
        {
            if (!File.Exists(file.ArchivePath))
                throw new FileNotFoundException("The archive no longer exists.");

            return Archives.OpenEntry(file.ArchivePath!, file.EntryKey!);
        }

        if (!File.Exists(file.FullPath))
            throw new FileNotFoundException("The file no longer exists.");

        return new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                              bufferSize: 64 * 1024, FileOptions.SequentialScan);
    }

    private static StreamReader Read(Stream content)
        => new(content, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true,
               bufferSize: 64 * 1024, leaveOpen: true);

    private static List<MatchSpan> Find(Regex? pattern, string line)
    {
        if (pattern == null || line.Length == 0) return NoHits;

        List<MatchSpan>? spans = null;
        try
        {
            foreach (Match m in pattern.Matches(line))
            {
                if (m.Length == 0) continue;   // a zero-width match has nothing to paint
                (spans ??= new List<MatchSpan>()).Add(new MatchSpan(m.Index, m.Length));
                if (spans.Count >= MaxHitsPerLine) break;
            }
        }
        catch (RegexMatchTimeoutException) { }

        return spans ?? NoHits;
    }

    private static PreviewRow Build(PreviewRowKind kind, int line, string text, List<MatchSpan> hits)
    {
        text = ExpandTabs(text, hits);

        if (text.Length > MaxLineChars)
        {
            text = text[..MaxLineChars] + " ...";
            hits = hits.Where(h => h.Start + h.Length <= MaxLineChars).ToList();
        }

        return new PreviewRow { Kind = kind, Line = line, Text = text, Hits = hits };
    }

    /// <summary>
    /// Tabs become spaces so indented source lines up under a fixed-pitch font, and the hit
    /// offsets move with them -- a highlight drawn at the pre-expansion index would sit in the
    /// wrong place on every line that starts with a tab, which is most source code.
    /// </summary>
    private static string ExpandTabs(string line, List<MatchSpan> hits)
    {
        if (!line.Contains('\t')) return line;

        var text = new StringBuilder(line.Length + 16);
        var map = new int[line.Length + 1];

        for (var i = 0; i < line.Length; i++)
        {
            map[i] = text.Length;
            if (line[i] == '\t') text.Append(' ', TabWidth - text.Length % TabWidth);
            else text.Append(line[i]);
        }
        map[line.Length] = text.Length;

        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            hits[i] = new MatchSpan(map[hit.Start], map[hit.Start + hit.Length] - map[hit.Start]);
        }

        return text.ToString();
    }
}

/// <summary>
/// The pane under the results: line numbers down the left, the source to the right, hits on a
/// highlighter band. Virtual, because "show me the whole file" is a perfectly normal thing to ask
/// of a 200,000-line file, and owner-drawn, because a run of plain text cannot carry a highlight.
/// </summary>
internal sealed class PreviewList : ListView
{
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                                          TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

    private static readonly PreviewRow Blank = new() { Kind = PreviewRowKind.Context };

    private List<PreviewRow> _rows = new();
    private readonly Font _mono = new("Consolas", 9f);

    public PreviewList()
    {
        View = View.Details;
        VirtualMode = true;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = true;
        // No header: the two columns are self-evident, and the strip would only repeat
        // the one above it. It also means there is no dead header space to fill.
        HeaderStyle = ColumnHeaderStyle.None;
        UseCompatibleStateImageBehavior = false;

        Columns.Add("Line", 58, HorizontalAlignment.Right);
        // Far wider than the pane so long lines scroll sideways instead of being cut off.
        Columns.Add("Text", 6000);

        // Fixed pitch: this is source code, and the tab expansion above only lines up under one.
        Font = _mono;

        RetrieveVirtualItem += OnRetrieve;
        ThemePainting.AttachList(this, drawCell: Draw);
    }

    public int RowCount => _rows.Count;

    public PreviewRow Row(int index) => (uint)index < (uint)_rows.Count ? _rows[index] : Blank;

    public void SetRows(List<PreviewRow> rows)
    {
        // Virtual mode keeps indices that the new, shorter list may not have.
        SelectedIndices.Clear();
        _rows = rows;
        VirtualListSize = rows.Count;

        // Back to the top: the row that was on screen belonged to the previous file.
        if (rows.Count > 0 && IsHandleCreated) EnsureVisible(0);
        Invalidate();
    }

    public void Clear(string? note = null)
        => SetRows(note == null ? new List<PreviewRow>() : PreviewScanner.Note(note));

    private void OnRetrieve(object? sender, RetrieveVirtualItemEventArgs e)
    {
        var row = Row(e.ItemIndex);
        e.Item = new ListViewItem(new[] { row.Line > 0 ? row.Line.ToString() : "", row.Text });
    }

    private void Draw(Graphics g, int itemIndex, int column, Rectangle pad)
    {
        var theme = Theme.Current;
        var row = Row(itemIndex);

        if (row.Kind == PreviewRowKind.Separator)
        {
            if (column != 1) return;
            using var pen = new Pen(theme.Surface1);
            var y = pad.Y + pad.Height / 2;
            g.DrawLine(pen, pad.X, y, pad.X + 48, y);
            return;
        }

        if (column == 0)
        {
            TextRenderer.DrawText(g, row.Line > 0 ? row.Line.ToString() : "", Font, pad,
                                  theme.Blue, Flags | TextFormatFlags.Right);
            return;
        }

        var fore = row.Kind switch
        {
            PreviewRowKind.Context => theme.Subtext,
            PreviewRowKind.Note => theme.Overlay,
            _ => theme.Text
        };

        if (row.Hits.Count == 0)
        {
            TextRenderer.DrawText(g, row.Text, Font, pad, fore, Flags);
            return;
        }

        var x = pad.X;
        var at = 0;

        foreach (var hit in row.Hits)
        {
            if (hit.Start > at) x = Segment(g, row.Text[at..hit.Start], x, pad, fore, null);
            x = Segment(g, row.Text.Substring(hit.Start, hit.Length), x, pad,
                        theme.HighlightText, theme.HighlightBg);
            at = hit.Start + hit.Length;

            if (x > pad.Right) return;   // the rest is off the right edge of the column
        }

        if (at < row.Text.Length) Segment(g, row.Text[at..], x, pad, fore, null);
    }

    /// <summary>Draws one run and returns the x the next one starts at.</summary>
    private int Segment(Graphics g, string text, int x, Rectangle pad, Color fore, Color? back)
    {
        if (text.Length == 0) return x;

        var size = TextRenderer.MeasureText(g, text, Font, new Size(int.MaxValue, pad.Height), Flags);
        var box = new Rectangle(x, pad.Y, size.Width, pad.Height);

        if (back is { } fill)
        {
            using var brush = new SolidBrush(fill);
            g.FillRectangle(brush, box);
        }

        TextRenderer.DrawText(g, text, Font, box, fore, Flags);
        return x + size.Width;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _mono.Dispose();
    }
}
