using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// A borderless flyout mimicking Windows 11's Quick Settings panel:
    /// a small set of toggle tiles, a brightness slider, and a live sensor
    /// readout, anchored above the taskbar. Opened with a single click on
    /// the tray icon; the full dashboard (Form1) remains available via the
    /// gear icon here or the tray menu's "Open Dashboard" entry, unchanged.
    ///
    /// Shares the same WmiController and reads Form1's turbo/battery-limit
    /// state through internal accessors rather than owning duplicate state -
    /// two independent copies of e.g. the per-zone color cache would drift
    /// out of sync with each other.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class QuickSettingsFlyout : Form
    {
        private readonly Form1 _owner;
        private readonly WmiController _wmi;

        private QuickTile _tileTurbo = null!;
        private QuickTile _tileRgb = null!;
        private QuickTile _tileBacklight = null!;
        private QuickTile _tileBattery = null!;

        private Panel _rgbExpandPanel = null!;
        private readonly Panel[] _zoneSwatches = new Panel[4];
        private PredatorSlider _brightnessSlider = null!;
        private ColorDialog _colorPicker = new();
        private bool _rgbExpanded;

        private Label _sensorLabel = null!;
        private Button _gearButton = null!;
        private readonly System.Windows.Forms.Timer _sensorTimer = new() { Interval = 2000 };

        private const int CollapsedHeight = 300;
        private const int ExpandedHeight = 420;
        private const int PanelWidth = 340;

        private bool _suppressDeactivate;

        /// <summary>
        /// When the flyout was last hidden by losing focus (as opposed to an
        /// explicit Hide() call elsewhere). Clicking the tray icon while the
        /// flyout is open steals focus first, auto-hiding it before the
        /// tray click handler even runs - so Form1 checks this timestamp to
        /// tell "the click that just closed it" apart from "a fresh click
        /// that should reopen it."
        /// </summary>
        public DateTime LastDeactivatedAt { get; private set; } = DateTime.MinValue;

        public QuickSettingsFlyout(Form1 owner)
        {
            _owner = owner;
            _wmi = owner.Wmi;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Width = PanelWidth;
            Height = CollapsedHeight;
            KeyPreview = true;

            BuildUi();

            Deactivate += (s, e) => { if (!_suppressDeactivate) { Hide(); LastDeactivatedAt = DateTime.UtcNow; } };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
            _sensorTimer.Tick += (s, e) => RefreshSensors();
            VisibleChanged += (s, e) =>
            {
                if (Visible) _sensorTimer.Start(); else _sensorTimer.Stop();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            using var path = UiPaths.RoundedRect(new Rectangle(0, 0, Width, Height), 14);
            Region = new Region(path);
        }

        /// <summary>
        /// Positions above the taskbar at the bottom-right of the primary
        /// screen's working area (matching real Quick Settings, which
        /// anchors to the screen corner rather than the exact tray icon
        /// position), refreshes all live state, and shows/activates.
        /// </summary>
        public void ShowNearTray()
        {
            PositionNearTray();

            BackColor = DwmAccentColor.GetAccentColor(Color.FromArgb(45, 40, 90));
            RefreshChildBackgrounds();
            RefreshTiles();
            RefreshSensors();

            Show();
            Activate();
        }

        private void PositionNearTray()
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            var iconRect = TrayIconPosition.TryGetRect(_owner.TrayIconRef);

            if (iconRect.HasValue)
            {
                var r = iconRect.Value;
                int x = r.X + r.Width / 2 - Width / 2;
                int y = r.Y - Height - 8;

                // Clamp to the working area in case the icon sits near an
                // edge (or in the overflow tray) so the panel never renders
                // partly off-screen.
                x = Math.Clamp(x, wa.Left + 4, wa.Right - Width - 4);
                y = Math.Clamp(y, wa.Top + 4, wa.Bottom - Height - 4);
                Location = new Point(x, y);
            }
            else
            {
                // Fallback: bottom-right corner, matching how Quick Settings
                // itself behaves if it can't resolve an exact icon position.
                Location = new Point(wa.Right - Width - 8, wa.Bottom - Height - 8);
            }
        }

        /// <summary>
        /// Plain WinForms Panel/Button controls don't actually support
        /// Color.Transparent the way Label does (the same reason
        /// PredatorButton/PredatorSwitch paint the parent's color manually
        /// instead of relying on it) - so child backgrounds are kept in
        /// sync with the flyout's own accent-derived BackColor explicitly,
        /// rather than attempting real transparency over a solid fill.
        /// </summary>
        private void RefreshChildBackgrounds()
        {
            _rgbExpandPanel.BackColor = BackColor;
            _sensorLabel.BackColor = BackColor;
            _gearButton.BackColor = BackColor;
            foreach (var s in _zoneSwatches) s.BackColor = BackColor;
        }

        private void BuildUi()
        {
            BackColor = Color.FromArgb(45, 40, 90);

            int pad = 14;
            int tileW = (PanelWidth - pad * 3) / 2;
            int tileH = 76;

            _tileTurbo = new QuickTile { Glyph = "\u26A1", Label = "Turbo", Location = new Point(pad, pad), Size = new Size(tileW, tileH) };
            _tileTurbo.Click += (s, e) => { _owner.ToggleTurbo(); RefreshTiles(); };

            _tileRgb = new QuickTile { Glyph = "\u25CF", Label = "RGB Keyboard", HasChevron = true, Location = new Point(pad * 2 + tileW, pad), Size = new Size(tileW, tileH) };
            _tileRgb.Click += (s, e) => ToggleRgbExpanded();

            _tileBacklight = new QuickTile { Glyph = "\U0001F4A1", Label = "Backlight Auto-Off", Location = new Point(pad, pad * 2 + tileH), Size = new Size(tileW, tileH) };
            _tileBacklight.Click += (s, e) => { _owner.ApplyBacklightTimeout(!_wmi.BacklightTimeoutEnabled); RefreshTiles(); };

            _tileBattery = new QuickTile { Glyph = "\U0001F50B", Label = "Battery Limiter", Location = new Point(pad * 2 + tileW, pad * 2 + tileH), Size = new Size(tileW, tileH) };
            _tileBattery.Click += (s, e) => { _owner.ApplyBatteryLimit(!_owner.BatteryLimitEnabled); RefreshTiles(); };

            Controls.AddRange(new Control[] { _tileTurbo, _tileRgb, _tileBacklight, _tileBattery });

            int expandY = pad * 3 + tileH * 2;
            _rgbExpandPanel = new Panel { Location = new Point(pad, expandY), Size = new Size(PanelWidth - pad * 2, 110), Visible = false };

            int swatchSize = 36, swatchGap = 10;
            for (int i = 0; i < 4; i++)
            {
                int zoneIndex = i;
                var swatch = new Panel
                {
                    Location = new Point((swatchSize + swatchGap) * i, 0),
                    Size = new Size(swatchSize, swatchSize),
                    Cursor = Cursors.Hand
                };
                swatch.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var (zr, zg, zb) = _wmi.GetZoneColor(zoneIndex);
                    using var brush = new SolidBrush(Color.FromArgb(zr, zg, zb));
                    g.FillEllipse(brush, 2, 2, swatchSize - 4, swatchSize - 4);
                    using var pen = new Pen(Color.White, 1.5f);
                    g.DrawEllipse(pen, 2, 2, swatchSize - 4, swatchSize - 4);
                };
                swatch.Click += (s, e) =>
                {
                    var (zr, zg, zb) = _wmi.GetZoneColor(zoneIndex);
                    _colorPicker.Color = Color.FromArgb(zr, zg, zb);
                    _suppressDeactivate = true;
                    var result = _colorPicker.ShowDialog(this);
                    _suppressDeactivate = false;
                    if (result == DialogResult.OK)
                    {
                        var c = _colorPicker.Color;
                        _wmi.SetZoneColor(zoneIndex, c.R, c.G, c.B);
                        swatch.Invalidate();
                    }
                };
                _zoneSwatches[i] = swatch;
                _rgbExpandPanel.Controls.Add(swatch);
            }

            var lblBrightness = new Label
            {
                Text = "Brightness",
                ForeColor = Color.White,
                Location = new Point(0, swatchSize + 14),
                AutoSize = true
            };
            _brightnessSlider = new PredatorSlider
            {
                Location = new Point(0, swatchSize + 34),
                Size = new Size(PanelWidth - pad * 2, 28),
                Minimum = 0,
                Maximum = 100,
                Value = 100
            };
            _brightnessSlider.ValueChanged += (s, e) => _wmi.SetStaticBrightness((byte)_brightnessSlider.Value);
            _rgbExpandPanel.Controls.Add(lblBrightness);
            _rgbExpandPanel.Controls.Add(_brightnessSlider);
            Controls.Add(_rgbExpandPanel);

            int bottomY = CollapsedHeight - 44;
            _sensorLabel = new Label
            {
                Text = "",
                ForeColor = Color.White,
                Location = new Point(pad, bottomY),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f)
            };
            _gearButton = new Button
            {
                Text = "\u2699",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Size = new Size(32, 32),
                Location = new Point(PanelWidth - pad - 32, bottomY - 4),
                Cursor = Cursors.Hand
            };
            _gearButton.FlatAppearance.BorderSize = 0;
            _gearButton.Click += (s, e) => { Hide(); _owner.ShowApp(); };

            Controls.Add(_sensorLabel);
            Controls.Add(_gearButton);
        }

        private void ToggleRgbExpanded()
        {
            _rgbExpanded = !_rgbExpanded;
            _rgbExpandPanel.Visible = _rgbExpanded;
            Height = _rgbExpanded ? ExpandedHeight : CollapsedHeight;

            if (_rgbExpanded)
                _brightnessSlider.Value = _wmi.StaticBrightnessPct;

            int bottomY = Height - 44;
            _sensorLabel.Location = new Point(_sensorLabel.Location.X, bottomY);
            _gearButton.Location = new Point(_gearButton.Location.X, bottomY - 4);

            // Re-anchor after resizing so the panel doesn't grow downward
            // past the screen edge (or up past the top, near the tray icon).
            PositionNearTray();

            if (_rgbExpanded)
                foreach (var s in _zoneSwatches) s.Invalidate();
        }

        internal void RefreshTiles()
        {
            _tileTurbo.BaseAccent = BackColor;
            _tileRgb.BaseAccent = BackColor;
            _tileBacklight.BaseAccent = BackColor;
            _tileBattery.BaseAccent = BackColor;

            _tileTurbo.IsOn = _owner.IsTurboOn;
            _tileBacklight.IsOn = _wmi.BacklightTimeoutEnabled;
            _tileBattery.IsOn = _owner.BatteryLimitEnabled;

            // RGB tile shows the average of the four zone colors as a quick
            // visual reference rather than a generic on/off state, since
            // "on" doesn't really apply to a set of arbitrary colors.
            int ar = 0, ag = 0, ab = 0;
            for (int i = 0; i < 4; i++)
            {
                var (r, g, b) = _wmi.GetZoneColor(i);
                ar += r; ag += g; ab += b;
            }
            _tileRgb.GlyphColor = Color.FromArgb(ar / 4, ag / 4, ab / 4);
            _tileRgb.IsOn = false;

            _tileTurbo.Invalidate();
            _tileRgb.Invalidate();
            _tileBacklight.Invalidate();
            _tileBattery.Invalidate();
        }

        private void RefreshSensors()
        {
            try
            {
                _sensorLabel.Text = $"CPU {_wmi.CpuTemp}\u00B0C \u00B7 GPU {_wmi.GpuTemp}\u00B0C";
            }
            catch
            {
                _sensorLabel.Text = "";
            }
        }
    }
}
