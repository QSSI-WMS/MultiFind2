using System.Runtime.InteropServices;

namespace MultiFind2;

/// <summary>
/// The real Explorer right-click menu for the selected files -- 7-Zip, TortoiseGit, Open with,
/// Send to, Properties and whatever else is installed -- rather than a hand-written imitation
/// of six of its entries.
///
/// This is the shell's own <c>IContextMenu</c>, so the items, their icons, their submenus and
/// what they do are all Explorer's. Three things make it more than a single call: the handlers
/// are reached through an <c>IShellItemArray</c> so a selection spanning several folders works;
/// menu messages have to be forwarded to <c>IContextMenu2/3</c> or the submenus come up empty;
/// and the menu is painted by the OS, so it follows the Windows theme and not ours.
/// </summary>
internal static class ShellMenu
{
    /// <summary>
    /// Pops the menu up at <paramref name="screen"/> and runs whatever is chosen. Returns false
    /// if the shell would not produce a menu -- a deleted file, most likely -- so the caller can
    /// fall back to its own.
    /// </summary>
    public static bool Show(IWin32Window owner, IReadOnlyList<string> paths, Point screen,
                            IReadOnlyList<(string Text, Action Run)> extra)
    {
        if (paths.Count == 0) return false;

        var pidls = new List<IntPtr>(paths.Count);
        object? menu = null;
        var hmenu = IntPtr.Zero;
        MenuHook? hook = null;

        try
        {
            foreach (var path in paths)
            {
                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                    pidls.Add(pidl);
            }

            // Every file has to still exist: a stale row would otherwise produce a menu whose
            // commands all fail one by one.
            if (pidls.Count != paths.Count) return false;

            var items = pidls.ToArray();
            if (SHCreateShellItemArrayFromIDLists((uint)items.Length, items, out var array) != 0 || array == null)
                return false;

            try
            {
                // BHID_SFUIObject is what gathers the handlers for the whole selection, however
                // many folders it spans. Binding the parent folder instead -- the older way --
                // only works when there is exactly one parent.
                var bhid = BHID_SFUIObject;
                var iid = IID_IContextMenu;
                if (array.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var native) != 0 || native == IntPtr.Zero)
                    return false;

                menu = Marshal.GetObjectForIUnknown(native);
                Marshal.Release(native);
            }
            finally
            {
                Marshal.ReleaseComObject(array);
            }

            if (menu is not IContextMenu contextMenu) return false;

            hmenu = CreatePopupMenu();
            if (hmenu == IntPtr.Zero) return false;

            // CMF_EXPLORE asks for the menu Explorer itself shows, which is the long one in the
            // screenshot rather than the short Windows 11 flyout.
            if (contextMenu.QueryContextMenu(hmenu, 0, ShellFirst, ShellLast, CMF_NORMAL | CMF_EXPLORE) < 0)
                return false;

            for (var i = 0; i < extra.Count; i++)
            {
                if (i == 0) AppendMenu(hmenu, MF_SEPARATOR, IntPtr.Zero, null);
                AppendMenu(hmenu, MF_STRING, (IntPtr)(ExtraFirst + i), extra[i].Text);
            }

            // Submenus (Send to, 7-Zip, Open with) are filled in on WM_INITMENUPOPUP, and
            // owner-drawn entries paint on WM_DRAWITEM. Without this the menu appears with
            // empty arrows and blank rows.
            hook = new MenuHook(owner.Handle, menu);

            // Documented requirement: without it the menu can be left on screen after a click
            // somewhere else.
            SetForegroundWindow(owner.Handle);

            var chosen = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                                          screen.X, screen.Y, owner.Handle, IntPtr.Zero);

            hook.ReleaseHandle();
            hook = null;

            if (chosen == 0) return true;      // dismissed, which is still a menu having worked

            if (chosen >= ExtraFirst)
            {
                var index = chosen - ExtraFirst;
                if (index < extra.Count) extra[index].Run();
                return true;
            }

            Invoke(contextMenu, chosen, owner.Handle);
            return true;
        }
        catch (COMException)
        {
            // A badly behaved shell extension is not something this app can fix, and it must
            // not be allowed to take the window down with it.
            return false;
        }
        finally
        {
            hook?.ReleaseHandle();
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (menu != null) Marshal.ReleaseComObject(menu);
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    static void Invoke(IContextMenu menu, int command, IntPtr owner)
    {
        // The verb is the command's offset, passed in the low word of a pointer -- the shell's
        // MAKEINTRESOURCE convention for "the item the user picked" rather than a named verb.
        var info = new CMINVOKECOMMANDINFO
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
            hwnd = owner,
            lpVerb = (IntPtr)(command - ShellFirst),
            nShow = SW_SHOWNORMAL
        };

        menu.InvokeCommand(ref info);
    }

    /// <summary>
    /// Forwards the menu messages the shell needs while its menu is up. Attached to the owner
    /// window only for as long as the menu is tracking, because these messages mean something
    /// quite different to the rest of the app.
    /// </summary>
    private sealed class MenuHook : NativeWindow
    {
        private readonly object _menu;

        public MenuHook(IntPtr handle, object menu)
        {
            _menu = menu;
            AssignHandle(handle);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_INITMENUPOPUP:
                case WM_DRAWITEM:
                case WM_MEASUREITEM:
                case WM_MENUCHAR:
                    // IContextMenu3 is the one that reports a result, which WM_MENUCHAR needs;
                    // older handlers only implement 2.
                    if (_menu is IContextMenu3 cm3 &&
                        cm3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var result) == 0)
                    {
                        m.Result = result;
                        return;
                    }

                    if (_menu is IContextMenu2 cm2 && cm2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0)
                    {
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
            }

            base.WndProc(ref m);
        }
    }

    // ------------------------------------------------------------------ interop

    /// <summary>Shell command ids live in [1, 0x6FFF]; ours start past them so the two never mix.</summary>
    const int ShellFirst = 1;
    const int ShellLast = 0x6FFF;
    const int ExtraFirst = 0x7000;

    const uint CMF_NORMAL = 0x0000;
    const uint CMF_EXPLORE = 0x0004;

    const uint MF_STRING = 0x0000;
    const uint MF_SEPARATOR = 0x0800;

    const uint TPM_RIGHTBUTTON = 0x0002;
    const uint TPM_RETURNCMD = 0x0100;

    const int SW_SHOWNORMAL = 1;

    const int WM_DRAWITEM = 0x002B;
    const int WM_MEASUREITEM = 0x002C;
    const int WM_INITMENUPOPUP = 0x0117;
    const int WM_MENUCHAR = 0x0120;

    static Guid BHID_SFUIObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");
    static Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr reserved, IntPtr commandString, int cch);
    }

    // COM interfaces inherit by vtable order, so every base method has to be redeclared here
    // in its original position even though it is never called through this interface.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr reserved, IntPtr commandString, int cch);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr reserved, IntPtr commandString, int cch);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    /// <summary>Only BindToHandler is declared: it is the first method, so nothing below it
    /// is needed to call it correctly.</summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl,
                                         uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    static extern int SHCreateShellItemArrayFromIDLists(uint count,
        [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, out IShellItemArray? array);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hmenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr hmenu, uint flags, IntPtr idNewItem, string? newItem);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);
}
