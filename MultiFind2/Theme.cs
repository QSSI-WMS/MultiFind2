using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MultiFind2;

public sealed class Theme
{
    public required string Name { get; init; }
    public required bool IsDark { get; init; }

    public required Color Base { get; init; }      // window background, grid body
    public required Color Mantle { get; init; }    // bars, menu, column headers
    public required Color Crust { get; init; }     // deepest surface (inputs)
    public required Color Surface0 { get; init; }  // inputs, selected tab
    public required Color Surface1 { get; init; }  // borders, grid lines
    public required Color Overlay { get; init; }   // labels
    public required Color Text { get; init; }
    public required Color Subtext { get; init; }
    public required Color Blue { get; init; }      // accent / selection
    public required Color Yellow { get; init; }    // active filter / skipped
    public required Color Green { get; init; }     // found
    public required Color Red { get; init; }       // errors

    // Tints derived by blending an accent into Base, so they shade correctly on light
    // palettes too -- hardcoded pastels only ever look right against a dark background.
    Color? _activeBg, _activeBorder, _selectionBg, _errorBg, _skippedBg, _gridLine, _highlightBg;

    /// <summary>Background of a toggle whose filter is switched on.</summary>
    public Color ActiveFilterBg => _activeBg ??= Blend(Base, Yellow, IsDark ? 0.26 : 0.22);
    public Color ActiveFilterBorder => _activeBorder ??= Blend(Base, Yellow, IsDark ? 0.55 : 0.45);
    /// <summary>Yellow itself stays legible on its own tint: light on dark palettes, dark on light ones.</summary>
    public Color ActiveFilterText => Yellow;

    public Color SelectionBg => _selectionBg ??= Blend(Base, Blue, IsDark ? 0.30 : 0.22);
    public Color ErrorRowBg => _errorBg ??= Blend(Base, Red, IsDark ? 0.14 : 0.10);
    public Color SkippedRowBg => _skippedBg ??= Blend(Base, Yellow, IsDark ? 0.12 : 0.10);
    public Color GridLine => _gridLine ??= Blend(Base, Surface1, 0.60);

    /// <summary>The highlighter band behind a hit in the preview pane. Much stronger than the
    /// filter tint: a match has to be findable at a glance in a wall of source code.</summary>
    public Color HighlightBg => _highlightBg ??= Blend(Base, Yellow, IsDark ? 0.85 : 0.35);

    /// <summary>
    /// Whichever of Text and Base reads better on the band. The light palettes define Yellow as a
    /// dark gold (it has to be legible as text there), so a fixed foreground would be unreadable
    /// on half the themes; picking by contrast keeps all six correct without new palette entries.
    /// </summary>
    public Color HighlightText => Contrast(HighlightBg, Text) >= Contrast(HighlightBg, Base) ? Text : Base;

    static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    static double Contrast(Color a, Color b) => Math.Abs(Luma(a) - Luma(b));

    static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    static Color Hex(string hex) => Color.FromArgb(
        Convert.ToInt32(hex.Substring(0, 2), 16),
        Convert.ToInt32(hex.Substring(2, 2), 16),
        Convert.ToInt32(hex.Substring(4, 2), 16));

    public static readonly IReadOnlyList<Theme> All = new[]
    {
        new Theme
        {
            Name = "Catppuccin Mocha", IsDark = true,
            Base = Hex("1E1E2E"), Mantle = Hex("181825"), Crust = Hex("11111B"),
            Surface0 = Hex("313244"), Surface1 = Hex("45475A"), Overlay = Hex("6C7086"),
            Text = Hex("CDD6F4"), Subtext = Hex("A6ADC8"),
            Blue = Hex("89B4FA"), Yellow = Hex("F9E2AF"), Green = Hex("A6E3A1"), Red = Hex("F38BA8"),
        },
        new Theme
        {
            Name = "VS Dark+", IsDark = true,
            Base = Hex("1E1E1E"), Mantle = Hex("252526"), Crust = Hex("181818"),
            Surface0 = Hex("2D2D30"), Surface1 = Hex("3E3E42"), Overlay = Hex("808080"),
            Text = Hex("D4D4D4"), Subtext = Hex("9D9D9D"),
            Blue = Hex("569CD6"), Yellow = Hex("DCDCAA"), Green = Hex("4EC9B0"), Red = Hex("F44747"),
        },
        new Theme
        {
            Name = "Nord", IsDark = true,
            Base = Hex("2E3440"), Mantle = Hex("272C36"), Crust = Hex("242933"),
            Surface0 = Hex("3B4252"), Surface1 = Hex("434C5E"), Overlay = Hex("4C566A"),
            Text = Hex("ECEFF4"), Subtext = Hex("D8DEE9"),
            Blue = Hex("88C0D0"), Yellow = Hex("EBCB8B"), Green = Hex("A3BE8C"), Red = Hex("BF616A"),
        },
        new Theme
        {
            Name = "Catppuccin Latte", IsDark = false,
            Base = Hex("EFF1F5"), Mantle = Hex("E6E9EF"), Crust = Hex("DCE0E8"),
            Surface0 = Hex("CCD0DA"), Surface1 = Hex("BCC0CC"), Overlay = Hex("9CA0B0"),
            Text = Hex("4C4F69"), Subtext = Hex("6C6F85"),
            Blue = Hex("1E66F5"), Yellow = Hex("DF8E1D"), Green = Hex("40A02B"), Red = Hex("D20F39"),
        },
        new Theme
        {
            Name = "GitHub Light", IsDark = false,
            Base = Hex("FFFFFF"), Mantle = Hex("F6F8FA"), Crust = Hex("EAEEF2"),
            Surface0 = Hex("D0D7DE"), Surface1 = Hex("AFB8C1"), Overlay = Hex("6E7781"),
            Text = Hex("1F2328"), Subtext = Hex("656D76"),
            Blue = Hex("0969DA"), Yellow = Hex("9A6700"), Green = Hex("1A7F37"), Red = Hex("CF222E"),
        },
        new Theme
        {
            Name = "Solarized Light", IsDark = false,
            Base = Hex("FDF6E3"), Mantle = Hex("EEE8D5"), Crust = Hex("E4DCC6"),
            Surface0 = Hex("D6CFB8"), Surface1 = Hex("93A1A1"), Overlay = Hex("839496"),
            Text = Hex("073642"), Subtext = Hex("586E75"),
            Blue = Hex("268BD2"), Yellow = Hex("B58900"), Green = Hex("859900"), Red = Hex("DC322F"),
        },
    };

    public static Theme Current { get; set; } = All[0];

    public static string Label(Theme t) => $"{t.Name} ({(t.IsDark ? "dark" : "light")})";

    /// <summary>Saved theme by name; falls back to the first palette matching the Windows app theme.</summary>
    public static Theme Resolve(string? savedName)
    {
        foreach (var t in All)
            if (t.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase))
                return t;
        bool dark = WindowsPrefersDark();
        foreach (var t in All)
            if (t.IsDark == dark)
                return t;
        return All[0];
    }

    static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    const uint SwpRedrawFrame = 0x0002 /*NOSIZE*/ | 0x0001 /*NOMOVE*/ | 0x0004 /*NOZORDER*/
                              | 0x0010 /*NOACTIVATE*/ | 0x0020 /*FRAMECHANGED*/;

    /// <summary>Match the window title bar to the theme.</summary>
    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            int dark = Current.IsDark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            // Switching theme on an already-visible window leaves the old caption on screen
            // until something invalidates the frame.
            if (form.Visible)
                SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, SwpRedrawFrame);
        }
        catch { }
    }
}
