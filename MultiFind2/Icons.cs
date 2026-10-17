using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace MultiFind2;

/// <summary>The picture on a button.</summary>
public enum Glyph
{
    /// <summary>Browse for a folder.</summary>
    Browse,
    /// <summary>Pick several folders.</summary>
    FolderStack,
    /// <summary>Pick several file names.</summary>
    DocStack,
    /// <summary>Descend into subfolders.</summary>
    SubfolderTree,
    /// <summary>Stay in the one folder.</summary>
    FolderOnly,
    MatchCase,
    WholeWord,
    Hex,
    Regex,
    /// <summary>The hidden attribute.</summary>
    Eye,
    /// <summary>The read-only attribute.</summary>
    Lock,
    /// <summary>Folders as search results.</summary>
    Folder
}

/// <summary>
/// The mark in the bottom-right corner saying which state a toggle is in. It follows the naming
/// the tooltips already use -- H, H+, H- -- so a filter set to "only" reads as a plus and one set
/// to "never" as a minus, while the unfiltered state carries no mark at all.
/// </summary>
public enum Badge { None, Plus, Minus, Check }

/// <summary>
/// The program's icons, drawn rather than loaded.
///
/// Every glyph is described in a 16-unit square and scaled to whatever the control needs, for
/// two reasons the app would otherwise fight: there are fifteen themes, light and dark, so an
/// icon has to be re-coloured to the current palette rather than baked; and the user runs at
/// 175%, where a 16px bitmap stretched to 28px is visibly soft. Both come free when the glyph
/// is a path.
/// </summary>
public static class Icons
{
    /// <summary>The side of the square each glyph is drawn in before scaling.</summary>
    private const float Box = 16f;

    private static readonly Dictionary<(Glyph, Badge, int, int, int), Bitmap> Cache = new();

    /// <summary>
    /// The glyph at <paramref name="size"/> pixels, drawn in <paramref name="fg"/> against
    /// <paramref name="bg"/> -- the background is needed, not just the foreground, because the
    /// badge and the overlapping edges of the stacked glyphs are separated by a cut of the
    /// button's own colour rather than by a gap that would close up at small sizes.
    /// </summary>
    public static Image Get(Glyph glyph, Badge badge, Color fg, Color bg, int size)
    {
        var key = (glyph, badge, size, fg.ToArgb(), bg.ToArgb());
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var bitmap = Render(glyph, badge, size, fg, bg);
        Cache[key] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// 16 design units at the control's own DPI. Asking the control rather than the screen
    /// matters on a mixed-DPI desktop, where the window may be on either monitor.
    /// </summary>
    public static int SizeFor(Control control) => (int)Math.Round(Box * control.DeviceDpi / 96.0);

    private static Bitmap Render(Glyph glyph, Badge badge, int size, Color fg, Color bg)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        // Not ClearType: sub-pixel text assumes an opaque background and writes no alpha, which
        // on a transparent bitmap leaves the letters invisible.
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.ScaleTransform(size / Box, size / Box);

        Draw(g, glyph, fg, bg);
        if (badge != Badge.None) DrawBadge(g, badge, fg, bg);

        return bitmap;
    }

    private static void Draw(Graphics g, Glyph glyph, Color fg, Color bg)
    {
        using var pen = new Pen(fg, 1.35f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var ink = new SolidBrush(fg);
        using var cut = new SolidBrush(bg);

        switch (glyph)
        {
            case Glyph.Browse:
            {
                // The back of the folder, then the front panel splayed open over it.
                using var back = FolderPath(1.6f, 3.0f, 11.4f);
                g.DrawPath(pen, back);

                using var front = new GraphicsPath();
                front.AddLines(new[]
                {
                    new PointF(3.5f, 7.6f), new PointF(15.0f, 7.6f),
                    new PointF(12.7f, 13.2f), new PointF(1.2f, 13.2f)
                });
                front.CloseFigure();
                g.FillPath(cut, front);
                g.DrawPath(pen, front);
                break;
            }

            case Glyph.FolderStack:
            {
                using var back = FolderPath(4.4f, 1.6f, 10.0f);
                g.DrawPath(pen, back);

                using var front = FolderPath(1.3f, 5.5f, 10.8f);
                g.FillPath(cut, front);
                g.DrawPath(pen, front);
                break;
            }

            case Glyph.DocStack:
            {
                using var back = SheetPath(4.8f, 1.2f, 8.0f);
                g.DrawPath(pen, back);

                using var front = SheetPath(1.5f, 4.0f, 8.4f);
                g.FillPath(cut, front);
                g.DrawPath(pen, front);
                Fold(g, pen, 1.5f, 4.0f, 8.4f);
                break;
            }

            case Glyph.SubfolderTree:
            {
                using var root = FolderPath(0.9f, 1.7f, 7.0f);
                g.DrawPath(pen, root);

                // The elbow Explorer draws: down the parent's left edge, then across to the child.
                g.DrawLines(pen, new[]
                {
                    new PointF(3.2f, 7.1f), new PointF(3.2f, 11.0f), new PointF(6.7f, 11.0f)
                });

                using var child = FolderPath(6.7f, 8.3f, 7.4f);
                g.FillPath(cut, child);
                g.DrawPath(pen, child);
                break;
            }

            case Glyph.FolderOnly:
            {
                using var only = FolderPath(1.8f, 4.0f, 12.4f);
                g.DrawPath(pen, only);
                break;
            }

            case Glyph.Folder:
            {
                using var folder = FolderPath(1.4f, 3.4f, 11.6f);
                g.DrawPath(pen, folder);
                break;
            }

            case Glyph.Eye:
            {
                using var almond = new GraphicsPath();
                almond.AddBezier(1.5f, 8.0f, 4.9f, 3.1f, 11.1f, 3.1f, 14.5f, 8.0f);
                almond.AddBezier(14.5f, 8.0f, 11.1f, 12.9f, 4.9f, 12.9f, 1.5f, 8.0f);
                g.DrawPath(pen, almond);
                g.FillEllipse(ink, 6.1f, 6.1f, 3.8f, 3.8f);
                break;
            }

            case Glyph.Lock:
            {
                // Shackle, then the straight drop to the body on each side.
                g.DrawArc(pen, 4.9f, 3.1f, 6.2f, 6.2f, 180f, 180f);
                g.DrawLine(pen, 4.9f, 6.2f, 4.9f, 7.6f);
                g.DrawLine(pen, 11.1f, 6.2f, 11.1f, 7.6f);

                using var body = RoundedPath(2.9f, 7.4f, 10.2f, 6.4f, 1.4f);
                g.FillPath(cut, body);
                g.DrawPath(pen, body);

                g.FillEllipse(ink, 7.2f, 9.1f, 1.6f, 1.6f);
                g.DrawLine(pen, 8.0f, 10.4f, 8.0f, 11.7f);
                break;
            }

            case Glyph.MatchCase:
                // The letterform is the icon here, and a drawn one at this size reads worse than
                // the real thing. Shifted off-centre to leave the badge corner clear.
                Letters(g, ink, "Aa", 9.0f, 5.9f, 5.4f);
                break;

            case Glyph.WholeWord:
                // Bars for the word boundaries the option actually tests for. They have to stand
                // clear of the letters: butted up against the b the right-hand one reads as an l.
                g.DrawLine(pen, 0.9f, 1.4f, 0.9f, 10.0f);
                g.DrawLine(pen, 12.6f, 1.0f, 12.6f, 7.2f);
                Letters(g, ink, "ab", 7.6f, 6.0f, 5.4f);
                break;

            case Glyph.Hex:
                Letters(g, ink, "0x", 9.0f, 5.9f, 5.4f);
                break;

            case Glyph.Regex:
            {
                // The pattern that names the option: a dot, and a star of six spokes.
                g.FillEllipse(ink, 1.3f, 9.4f, 2.9f, 2.9f);

                const float cx = 8.6f, cy = 5.5f, r = 4.2f;
                for (var i = 0; i < 3; i++)
                {
                    var a = Math.PI * i / 3.0;
                    var dx = (float)(Math.Cos(a) * r);
                    var dy = (float)(Math.Sin(a) * r);
                    g.DrawLine(pen, cx - dx, cy - dy, cx + dx, cy + dy);
                }
                break;
            }
        }
    }

    /// <summary>
    /// The state mark, sunk into a cut of the button's own colour so it stays legible wherever
    /// it lands on the glyph beneath.
    /// </summary>
    private static void DrawBadge(Graphics g, Badge badge, Color fg, Color bg)
    {
        // Sized for the worst case rather than the best: at 100% scaling the whole icon is 16px,
        // so a disc much smaller than this leaves the plus and the minus as the same grey smudge.
        const float cx = 11.1f, cy = 11.1f, r = 4.6f;

        using var ink = new SolidBrush(fg);
        using var cut = new SolidBrush(bg);

        g.FillEllipse(cut, cx - r - 0.9f, cy - r - 0.9f, (r + 0.9f) * 2, (r + 0.9f) * 2);
        g.FillEllipse(ink, cx - r, cy - r, r * 2, r * 2);

        using var mark = new Pen(bg, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        switch (badge)
        {
            case Badge.Plus:
                g.DrawLine(mark, cx - 2.3f, cy, cx + 2.3f, cy);
                g.DrawLine(mark, cx, cy - 2.3f, cx, cy + 2.3f);
                break;

            case Badge.Minus:
                g.DrawLine(mark, cx - 2.4f, cy, cx + 2.4f, cy);
                break;

            case Badge.Check:
                mark.LineJoin = LineJoin.Round;
                g.DrawLines(mark, new[]
                {
                    new PointF(cx - 2.4f, cy + 0.2f),
                    new PointF(cx - 0.7f, cy + 1.9f),
                    new PointF(cx + 2.5f, cy - 2.1f)
                });
                break;
        }
    }

    /// <summary>
    /// A short letterform centred on a point. Measured and placed rather than handed a rectangle
    /// to centre itself in: GDI+ pads a laid-out string with side bearings and will drop the last
    /// character rather than overrun, which at these sizes turns "Aa" into "A".
    /// <paramref name="cx"/> and <paramref name="cy"/> sit up and left of the middle so the
    /// bottom-right corner stays free for the badge.
    /// </summary>
    private static void Letters(Graphics g, Brush ink, string text, float em, float cx, float cy)
    {
        using var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tight = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
        };

        var size = g.MeasureString(text, font, PointF.Empty, tight);
        g.DrawString(text, font, ink, cx - size.Width / 2f, cy - size.Height / 2f, tight);
    }

    /// <summary>A folder seen head-on, with the raised tab on the left. Height follows width.</summary>
    private static GraphicsPath FolderPath(float x, float y, float w)
    {
        var h = w * 0.72f;
        var path = new GraphicsPath();
        path.AddLines(new[]
        {
            new PointF(x, y + h),
            new PointF(x, y),
            new PointF(x + w * 0.34f, y),
            new PointF(x + w * 0.46f, y + h * 0.20f),
            new PointF(x + w, y + h * 0.20f),
            new PointF(x + w, y + h)
        });
        path.CloseFigure();
        return path;
    }

    /// <summary>A sheet of paper with the corner turned down. Height follows width.</summary>
    private static GraphicsPath SheetPath(float x, float y, float w)
    {
        var h = w * 1.28f;
        var f = w * 0.38f;
        var path = new GraphicsPath();
        path.AddLines(new[]
        {
            new PointF(x, y),
            new PointF(x + w - f, y),
            new PointF(x + w, y + f),
            new PointF(x + w, y + h),
            new PointF(x, y + h)
        });
        path.CloseFigure();
        return path;
    }

    /// <summary>The two strokes that make the turned corner read as folded rather than cut.</summary>
    private static void Fold(Graphics g, Pen pen, float x, float y, float w)
    {
        var f = w * 0.38f;
        g.DrawLines(pen, new[]
        {
            new PointF(x + w - f, y),
            new PointF(x + w - f, y + f),
            new PointF(x + w, y + f)
        });
    }

    private static GraphicsPath RoundedPath(float x, float y, float w, float h, float r)
    {
        var d = r * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
