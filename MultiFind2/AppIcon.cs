using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MultiFind2;

/// <summary>
/// The program's own icon: a stack of files with one line lit up, under a magnifier. The three
/// things the program is -- several files at once, a hit inside one of them, a search.
///
/// Drawn rather than stored, and in fixed colours rather than the current theme: this is what
/// Explorer, the taskbar and Alt-Tab show, where it has to be the same picture every time. The
/// same drawing serves every size, so the 16px entry is a redrawn icon rather than a shrunken
/// 256px one -- which is the whole reason small icons usually look muddy.
/// </summary>
public static class AppIcon
{
    /// <summary>The side of the square the artwork is laid out in before scaling.</summary>
    private const float Box = 32f;

    private static readonly Color Paper = Color.FromArgb(0xF5, 0xF7, 0xFA);
    private static readonly Color PaperEdge = Color.FromArgb(0x6B, 0x7A, 0x90);
    private static readonly Color PaperBehind = Color.FromArgb(0xC3, 0xCC, 0xDB);
    private static readonly Color TextLine = Color.FromArgb(0x97, 0xA3, 0xB5);
    private static readonly Color HitLine = Color.FromArgb(0xF0, 0xA8, 0x30);
    private static readonly Color Lens = Color.FromArgb(0x2F, 0x6F, 0xED);
    private static readonly Color Grip = Color.FromArgb(0x1D, 0x4F, 0xC4);
    private static readonly Color Glass = Color.FromArgb(0xE9, 0xF0, 0xFE);

    /// <summary>The sizes the .ico carries. Everything Windows asks for, and nothing between.</summary>
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static Icon? _window;

    /// <summary>
    /// The icon for the window, the taskbar and Alt-Tab. Built from the same bytes as the file
    /// on disk, so there is one artwork and one encoder rather than a runtime path that can
    /// drift from the one Explorer sees.
    /// </summary>
    public static Icon ForWindow() => _window ??= new Icon(new MemoryStream(FileBytes()));

    public static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / Box, size / Box);

        // Below about 24px the lines of text inside the sheet collapse into grey mush and the
        // second sheet is a single ambiguous pixel, so the small sizes get a plainer drawing
        // instead of a faithful miniature of the large one.
        Draw(g, detailed: size >= 24);
        return bitmap;
    }

    private static void Draw(Graphics g, bool detailed)
    {
        using var paper = new SolidBrush(Paper);
        using var behind = new SolidBrush(PaperBehind);
        using var edge = new Pen(PaperEdge, detailed ? 1.1f : 1.5f) { LineJoin = LineJoin.Round };

        if (detailed)
        {
            // Up and to the left, and a plain rectangle: offset the other way it ran into the
            // front sheet's turned corner and the two grey shapes merged into one, and a second
            // dog-ear of its own only added to the confusion. Outlined, because filled alone it
            // reads as a shadow rather than as the second file the program is named for.
            g.FillRectangle(behind, 1.2f, 1.8f, 13.0f, 16.6f);
            g.DrawRectangle(edge, 1.2f, 1.8f, 13.0f, 16.6f);
        }

        using var front = Sheet(4.0f, 5.0f, 15.0f);
        g.FillPath(paper, front);
        g.DrawPath(edge, front);

        // The turned corner, drawn as a filled triangle so it reads at a glance.
        using var fold = new GraphicsPath();
        fold.AddLines(new[]
        {
            new PointF(4.0f + 15.0f - 5.7f, 5.0f),
            new PointF(4.0f + 15.0f, 5.0f + 5.7f),
            new PointF(4.0f + 15.0f - 5.7f, 5.0f + 5.7f)
        });
        fold.CloseFigure();
        g.FillPath(behind, fold);
        g.DrawPath(edge, fold);

        if (detailed)
        {
            using var line = new Pen(TextLine, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var hit = new Pen(HitLine, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            g.DrawLine(line, 7.2f, 13.6f, 16.0f, 13.6f);
            g.DrawLine(hit, 7.2f, 17.6f, 14.6f, 17.6f);   // the match
            g.DrawLine(line, 7.2f, 21.4f, 13.2f, 21.4f);
        }

        // The magnifier last, over everything, with the handle running off the bottom corner.
        using var grip = new Pen(Grip, detailed ? 3.4f : 3.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(grip, 25.9f, 24.9f, 29.8f, 28.8f);

        const float lx = 14.5f, ly = 13.5f, d = 14.0f;

        // Opaque, not a tint of whatever is behind: translucent glass let the sheet's corner and
        // the empty background show through as one unreadable wedge, and it changed colour with
        // the wallpaper.
        using var glass = new SolidBrush(Glass);
        using var ring = new Pen(Lens, detailed ? 2.8f : 3.2f);

        g.FillEllipse(glass, lx, ly, d, d);

        if (detailed)
        {
            // What the magnifier is looking at: the same lines as the page, enlarged, with the
            // match still the lit one. It is the point of the program, so it goes where the eye
            // lands.
            using var lens = new GraphicsPath();
            lens.AddEllipse(lx, ly, d, d);
            var saved = g.Save();
            g.SetClip(lens);

            using var big = new Pen(TextLine, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var bigHit = new Pen(HitLine, 3.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            g.DrawLine(big, 16.4f, 16.6f, 26.6f, 16.6f);
            g.DrawLine(bigHit, 16.0f, 20.6f, 27.0f, 20.6f);
            g.DrawLine(big, 16.4f, 24.5f, 24.6f, 24.5f);

            g.Restore(saved);
        }

        g.DrawEllipse(ring, lx, ly, d, d);
    }

    private static GraphicsPath Sheet(float x, float y, float w)
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

    // ------------------------------------------------------------------ .ico

    /// <summary>
    /// The whole icon file. Windows picks the entry it wants, so all nine sizes go in.
    /// </summary>
    public static byte[] FileBytes()
    {
        var images = Sizes.Select(Encode).ToList();

        using var memory = new MemoryStream();
        using var w = new BinaryWriter(memory);

        w.Write((ushort)0);             // reserved
        w.Write((ushort)1);             // 1 = icon
        w.Write((ushort)images.Count);

        var offset = 6 + images.Count * 16;
        for (var i = 0; i < images.Count; i++)
        {
            // 256 does not fit in a byte and is written as 0, which is what it means here.
            w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
            w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
            w.Write((byte)0);           // palette size: none, this is true colour
            w.Write((byte)0);           // reserved
            w.Write((ushort)1);         // planes
            w.Write((ushort)32);        // bits per pixel
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images) w.Write(image);

        w.Flush();
        return memory.ToArray();
    }

    /// <summary>
    /// One entry's payload. The big sizes go in as PNG -- a 256x256 uncompressed would be a
    /// quarter of a megabyte on its own -- and the small ones as a DIB, which every version of
    /// Windows reads without qualification.
    /// </summary>
    private static byte[] Encode(int size)
    {
        using var bitmap = Render(size);

        if (size >= 128)
        {
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return png.ToArray();
        }

        return Dib(bitmap, size);
    }

    private static byte[] Dib(Bitmap bitmap, int size)
    {
        // Rows run bottom-up, and the header claims twice the real height: the second half is
        // the 1bpp AND mask, which a 32bpp icon does not use but must still carry.
        var maskStride = (size + 31) / 32 * 4;
        var buffer = new byte[40 + size * size * 4 + maskStride * size];

        void Int32At(int at, int value) => BitConverter.GetBytes(value).CopyTo(buffer, at);

        Int32At(0, 40);                 // header size
        Int32At(4, size);               // width
        Int32At(8, size * 2);           // height, colour rows + mask rows
        buffer[12] = 1;                 // planes
        buffer[14] = 32;                // bits per pixel
        Int32At(20, size * size * 4);   // image bytes

        var bits = bitmap.LockBits(new Rectangle(0, 0, size, size),
                                   ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[size * 4];
            for (var y = 0; y < size; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                row.CopyTo(buffer, 40 + (size - 1 - y) * size * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        return buffer;
    }
}
