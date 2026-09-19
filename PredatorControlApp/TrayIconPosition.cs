using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Finds the actual on-screen rectangle of a NotifyIcon, so the Quick
    /// Settings flyout can anchor precisely above it instead of just the
    /// screen corner - matching how Windows' own Quick Settings tracks its
    /// own icon.
    ///
    /// There is no public WinForms API for this. The only way to get it is
    /// Shell_NotifyIconGetRect (shell32, Windows 7+), which needs the
    /// hWnd+uID the icon was registered with - and NotifyIcon doesn't
    /// expose those either, so this reaches into its private fields via
    /// reflection ('_id' and '_window' in modern .NET; these replaced the
    /// old 'id'/'window' names used on .NET Framework).
    ///
    /// This is inherently best-effort: a future WinForms internal change
    /// could rename these fields. Every failure path returns null rather
    /// than throwing, and the caller (QuickSettingsFlyout) always falls
    /// back to corner-anchored positioning if this comes back empty.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class TrayIconPosition
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

        public static Rectangle? TryGetRect(NotifyIcon icon)
        {
            try
            {
                var type = typeof(NotifyIcon);
                var idField = type.GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance);
                var windowField = type.GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField == null || windowField == null) return null;

                object? idValue = idField.GetValue(icon);
                if (idValue == null) return null;
                uint id = Convert.ToUInt32(idValue);

                if (windowField.GetValue(icon) is not NativeWindow nativeWindow) return null;
                IntPtr hwnd = nativeWindow.Handle;
                if (hwnd == IntPtr.Zero) return null;

                var identifier = new NOTIFYICONIDENTIFIER
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                    hWnd = hwnd,
                    uID = id,
                    guidItem = Guid.Empty
                };

                int hr = Shell_NotifyIconGetRect(ref identifier, out RECT rect);
                if (hr != 0) return null; // non-zero HRESULT = failure

                return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
            catch
            {
                return null;
            }
        }
    }
}
