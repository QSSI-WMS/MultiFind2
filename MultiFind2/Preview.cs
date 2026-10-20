using System.Runtime.InteropServices;
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
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int LVM_SCROLL = 0x1014;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                                          TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

    private static readonly PreviewRow Blank = new() { Kind = PreviewRowKind.Context };

    private List<PreviewRow> _rows = new();
    private readonly Font _mono = new("Consolas", 9f);

    // Where the text selection starts and where the drag has got to, as row plus character
    // offset into that row's text. -1 means there is no selection.
    private int _anchorRow = -1, _anchorCol, _caretRow = -1, _caretCol;
    private bool _dragging;
    private int _charWidth;

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

        ContextMenuStrip = BuildMenu();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ThemeMenuRenderer() };
        var copy = new ToolStripMenuItem("&Copy", null, (_, _) => Copy())
        {
            ShortcutKeyDisplayString = "Ctrl+C"
        };
        menu.Items.Add(copy);
        menu.Items.Add("Select &all", null, (_, _) => SelectAllText());
        menu.Opening += (_, _) => copy.Enabled = SelectedText().Length > 0;
        return menu;
    }

    /// <summary>
    /// The advance of one character. The pane is fixed-pitch -- the tab expansion in
    /// <see cref="PreviewScanner"/> already depends on that -- so a single number places every
    /// column, and hit testing and the selection band both come down to a multiplication.
    /// </summary>
    private int CharWidth
    {
        get
        {
            if (_charWidth > 0) return _charWidth;

            using var g = CreateGraphics();
            var ruler = TextRenderer.MeasureText(g, new string('0', 32), Font,
                                                 new Size(int.MaxValue, Height), Flags).Width;
            return _charWidth = Math.Max(1, ruler / 32);
        }
    }

    public int RowCount => _rows.Count;

    public PreviewRow Row(int index) => (uint)index < (uint)_rows.Count ? _rows[index] : Blank;

    public void SetRows(List<PreviewRow> rows)
    {
        // Virtual mode keeps indices that the new, shorter list may not have.
        SelectedIndices.Clear();
        ClearTextSelection();
        _rows = rows;
        VirtualListSize = rows.Count;

        if (rows.Count > 0 && IsHandleCreated) ShowFirstHit();
        Invalidate();
    }

    /// <summary>
    /// Opens on the first highlighted hit rather than on line 1. With context lines switched on
    /// the top row is usually a plain line ahead of the match, and on a long line the hit can sit
    /// well off the right edge of the pane, so this scrolls both ways.
    /// </summary>
    private void ShowFirstHit()
    {
        var row = _rows.FindIndex(r => r.Hits.Count > 0);
        EnsureVisible(row < 0 ? 0 : row);

        if (row < 0) { ScrollTo(0); return; }

        var hit = _rows[row].Hits[0];
        var gutter = Columns[0].Width + 4;
        var start = gutter + hit.Start * CharWidth;
        var end = start + hit.Length * CharWidth;

        // Only scroll sideways when the hit genuinely does not fit, and when it does not, keep a
        // few characters of the line ahead of it in view so the match is not flush to the gutter.
        ScrollTo(end <= ClientSize.Width ? 0 : Math.Max(0, start - gutter - 8 * CharWidth));
    }

    /// <summary>Scrolls so that <paramref name="offset"/> pixels of the line sit off to the left.</summary>
    private void ScrollTo(int offset)
    {
        var now = VirtualListSize > 0 ? -Items[0].Bounds.X : 0;
        if (offset != now) SendMessage(Handle, LVM_SCROLL, (IntPtr)(offset - now), IntPtr.Zero);
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

        Line(g, row, pad, theme);
        Band(g, itemIndex, pad, theme);
    }

    private void Line(Graphics g, PreviewRow row, Rectangle pad, Theme theme)
    {
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

    /// <summary>
    /// The text selection, drawn over the line rather than behind it. A band underneath would
    /// have to repaint every run in selection colours and would swallow the highlighter; a
    /// translucent one leaves both the characters and the hits they sit on readable.
    /// </summary>
    private void Band(Graphics g, int itemIndex, Rectangle pad, Theme theme)
    {
        var (from, to) = SelectionOn(itemIndex);
        if (to <= from) return;

        using var brush = new SolidBrush(Color.FromArgb(110, theme.Blue));
        g.FillRectangle(brush, new Rectangle(pad.X + from * CharWidth, pad.Y,
                                             (to - from) * CharWidth, pad.Height));
    }

    // ------------------------------------------------------------- selecting text

    /// <summary>The two ends of the selection in reading order.</summary>
    private (int Row, int Col, int EndRow, int EndCol) Ordered()
        => _anchorRow < _caretRow || (_anchorRow == _caretRow && _anchorCol <= _caretCol)
            ? (_anchorRow, _anchorCol, _caretRow, _caretCol)
            : (_caretRow, _caretCol, _anchorRow, _anchorCol);

    /// <summary>
    /// The character range of <paramref name="row"/> that is selected. An empty range (To not
    /// greater than From) means none of it is.
    /// </summary>
    private (int From, int To) SelectionOn(int row)
    {
        if (_anchorRow < 0 || _caretRow < 0) return (0, 0);

        var (first, from, last, to) = Ordered();
        if (row < first || row > last) return (0, 0);
        if (first == last) return (from, to);

        // A whole line that is followed by more selected lines runs one character long, so the
        // line break it carries is visible rather than merely implied by the next row.
        var end = Row(row).Text.Length + 1;
        if (row == first) return (from, end);
        return row == last ? (0, to) : (0, end);
    }

    public string SelectedText()
    {
        if (_anchorRow < 0 || _caretRow < 0) return "";

        var (first, from, last, to) = Ordered();
        var text = new StringBuilder();

        for (var row = first; row <= last; row++)
        {
            var line = Row(row).Text;
            var start = row == first ? Math.Min(from, line.Length) : 0;
            var stop = row == last ? Math.Min(to, line.Length) : line.Length;

            if (stop > start) text.Append(line, start, stop - start);
            if (row < last) text.AppendLine();
        }

        return text.ToString();
    }

    private void ClearTextSelection()
    {
        _anchorRow = _caretRow = -1;
        _anchorCol = _caretCol = 0;
    }

    private void SelectAllText()
    {
        if (VirtualListSize == 0) return;

        _anchorRow = 0;
        _anchorCol = 0;
        _caretRow = VirtualListSize - 1;
        _caretCol = Row(_caretRow).Text.Length;
        Invalidate();
    }

    private void Copy()
    {
        var text = SelectedText();
        if (text.Length > 0) Clipboard.SetText(text);
    }

    /// <summary>The row under <paramref name="y"/>, clamped to the list so that dragging past
    /// either edge keeps extending the selection instead of stalling.</summary>
    private int RowAt(int y)
    {
        if (VirtualListSize == 0) return -1;

        // Probe just inside the left edge of the pane. Rows are full-row select and span the whole
        // scrollable width, so this lands on the row whatever the sideways scroll -- whereas a
        // probe at the row's own origin would be off screen to the left once scrolled.
        var item = GetItemAt(2, Math.Clamp(y, 0, Math.Max(0, ClientSize.Height - 1)));

        if (item != null) return item.Index;
        return y <= 0 ? 0 : VirtualListSize - 1;
    }

    /// <summary>The character offset under <paramref name="x"/>, rounded so that the caret lands
    /// on whichever side of the character the click was nearer to.</summary>
    private int ColAt(int row, int x)
    {
        var left = Items[row].Bounds.X + Columns[0].Width + 4;
        var col = (int)Math.Round((x - left) / (double)CharWidth);
        return Math.Clamp(col, 0, Row(row).Text.Length);
    }

    private static bool PartOfWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void SelectWord(int row, int at)
    {
        var line = Row(row).Text;
        if (line.Length == 0) return;

        var i = Math.Min(at, line.Length - 1);
        if (!PartOfWord(line[i])) return;

        var start = i;
        while (start > 0 && PartOfWord(line[start - 1])) start--;
        var stop = i;
        while (stop < line.Length && PartOfWord(line[stop])) stop++;

        _anchorCol = start;
        _caretCol = stop;
    }

    protected override void WndProc(ref Message m)
    {
        // Taken before comctl32 sees it: left-dragging a list view means rubber-band row
        // selection, which is not what dragging across text is asking for any more.
        if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK)
        {
            if (!Focused) Focus();

            var lp = (int)m.LParam;
            var row = RowAt(unchecked((short)(lp >> 16)));

            if (row >= 0)
            {
                _anchorRow = _caretRow = row;
                _anchorCol = _caretCol = ColAt(row, unchecked((short)lp));
                if (m.Msg == WM_LBUTTONDBLCLK) SelectWord(row, _anchorCol);

                _dragging = true;
                Capture = true;
                Invalidate();
            }
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var row = RowAt(e.Y);
        if (row < 0) return;

        _caretRow = row;
        _caretCol = ColAt(row, e.X);
        EnsureVisible(row);   // dragging past the top or bottom scrolls, since RowAt clamps
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
        }
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C) Copy();
        else if (e.Control && e.KeyCode == Keys.A) SelectAllText();
        else if (e.KeyCode == Keys.Escape && _anchorRow >= 0) { ClearTextSelection(); Invalidate(); }
        else { base.OnKeyDown(e); return; }

        e.Handled = e.SuppressKeyPress = true;
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
        var menu = ContextMenuStrip;
        base.Dispose(disposing);

        if (!disposing) return;
        _mono.Dispose();
        menu?.Dispose();
    }
}
