using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PredatorControlApp
{
    /// <summary>
    /// Reads the current Windows accent color - the same value Quick
    /// Settings and the Start menu use for their tiles. Queried fresh each
    /// time the flyout is shown, so it stays current if the user changes
    /// their wallpaper/accent color later without needing to restart.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class DwmAccentColor
    {
        [DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern void DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

        /// <summary>
        /// Returns the current accent color, or the given fallback if it
        /// can't be read for any reason (older Windows, remote session).
        /// </summary>
        public static Color GetAccentColor(Color fallback)
        {
            // Quick Settings and the Start menu source their tint from
            // AccentColorMenu (ABGR, note the reversed byte order vs the
            // usual ARGB), not from DwmGetColorizationColor - that's the
            // older Aero-glass API and can visibly drift from what the
            // modern Fluent UI actually renders, which is exactly why this
            // read a shade darker than the real Quick Settings panel.
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
                if (key?.GetValue("AccentColorMenu") is int abgr)
                {
                    byte b = (byte)((abgr >> 16) & 0xFF);
                    byte g = (byte)((abgr >> 8) & 0xFF);
                    byte r = (byte)(abgr & 0xFF);
                    return Color.FromArgb(255, r, g, b);
                }
            }
            catch { }

            // Fall back to the older DWM colorization color if the modern
            // accent key isn't available for some reason.
            try
            {
                DwmGetColorizationColor(out uint argb, out _);
                byte r = (byte)((argb >> 16) & 0xFF);
                byte g = (byte)((argb >> 8) & 0xFF);
                byte b = (byte)(argb & 0xFF);
                return Color.FromArgb(255, r, g, b);
            }
            catch
            {
                return fallback;
            }
        }

        public static Color Lighten(Color c, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            int r = c.R + (int)((255 - c.R) * amount);
            int g = c.G + (int)((255 - c.G) * amount);
            int b = c.B + (int)((255 - c.B) * amount);
            return Color.FromArgb(c.A, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        public static Color Darken(Color c, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            int r = (int)(c.R * (1 - amount));
            int g = (int)(c.G * (1 - amount));
            int b = (int)(c.B * (1 - amount));
            return Color.FromArgb(c.A, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }

        /// <summary>
        /// Perceptual brightness (0-255). Used to decide whether white or
        /// black text/icons stay readable against a given accent color,
        /// since accents range from very dark to very pale depending on
        /// the user's wallpaper.
        /// </summary>
        public static double PerceivedBrightness(Color c) =>
            (c.R * 0.299) + (c.G * 0.587) + (c.B * 0.114);

        public static Color ReadableForeground(Color background) =>
            PerceivedBrightness(background) > 150 ? Color.Black : Color.White;
    }
}
