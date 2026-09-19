using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// A single Quick-Settings-style tile: icon glyph, label, and an on/off
    /// visual state tinted from a shared base accent color (lighter when on,
    /// darker/muted when off - matching Windows' own Wifi/Bluetooth tiles).
    /// Optionally shows a chevron for tiles that expand into more content
    /// (e.g. RGB zones) rather than toggling directly.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class QuickTile : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Glyph { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Label { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsOn { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool HasChevron { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BaseAccent { get; set; } = Color.FromArgb(60, 60, 130);

        /// <summary>
        /// Optional override color for the glyph itself (e.g. showing the
        /// keyboard's actual current color on the RGB tile instead of a
        /// generic icon). Null uses the readable foreground color instead.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? GlyphColor { get; set; }

        public QuickTile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(150, 76);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent?.BackColor ?? Color.FromArgb(40, 30, 80));

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = UiPaths.RoundedRect(rect, 8);

            Color fill = IsOn ? DwmAccentColor.Lighten(BaseAccent, 0.35f) : DwmAccentColor.Darken(BaseAccent, 0.25f);
            using (var b = new SolidBrush(fill))
                g.FillPath(b, path);

            Color fg = GlyphColor ?? DwmAccentColor.ReadableForeground(fill);
            using var glyphFont = TryGetFont("Segoe UI Emoji", 15f) ?? new Font(Font.FontFamily, 15f);
            using var glyphBrush = new SolidBrush(fg);
            g.DrawString(Glyph, glyphFont, glyphBrush, new PointF(12, 10));

            using var labelBrush = new SolidBrush(fg);
            var labelRect = new RectangleF(12, Height - 28, Width - 24, 24);
            using var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(Label, Font, labelBrush, labelRect, sf);

            if (HasChevron)
            {
                using var chevronFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                var size = g.MeasureString(">", chevronFont);
                g.DrawString(">", chevronFont, glyphBrush, new PointF(Width - size.Width - 10, 10));
            }
        }

        private static Font? TryGetFont(string name, float size)
        {
            try
            {
                using var testFamily = new FontFamily(name);
                return new Font(name, size);
            }
            catch
            {
                return null;
            }
        }
    }
}
