using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Reads the current Windows accent/colorization color via DWM - the same
    /// value Windows itself uses to tint the taskbar and title bars, and what
    /// Quick Settings uses as its background. Queried fresh each time the
    /// flyout is shown, so it stays current if the user changes their
    /// wallpaper/accent color later without needing to restart the app.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class DwmAccentColor
    {
        [DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern void DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

        /// <summary>
        /// Returns the current accent color, or the given fallback if DWM is
        /// unavailable for any reason (older Windows, remote session, etc).
        /// </summary>
        public static Color GetAccentColor(Color fallback)
        {
            try
            {
                DwmGetColorizationColor(out uint argb, out _);
                byte a = (byte)((argb >> 24) & 0xFF);
                byte r = (byte)((argb >> 16) & 0xFF);
                byte g = (byte)((argb >> 8) & 0xFF);
                byte b = (byte)(argb & 0xFF);
                // Colorization alpha is usually near-opaque already; force it
                // fully opaque since we're using this as a solid panel fill.
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
