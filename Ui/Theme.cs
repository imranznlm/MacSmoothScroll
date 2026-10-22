using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace MacSmoothScroll
{
    /// <summary>
    /// Palette and drawing helpers. Colours follow the Windows light/dark app theme
    /// that is active when the dialog is opened.
    /// </summary>
    internal static class Theme
    {
        public static readonly bool IsDark = DetectDarkMode();

        public static readonly Color Window = IsDark ? Color.FromArgb(28, 28, 33) : Color.FromArgb(251, 251, 253);
        public static readonly Color Surface = IsDark ? Color.FromArgb(36, 36, 42) : Color.FromArgb(255, 255, 255);
        public static readonly Color SurfaceAlt = IsDark ? Color.FromArgb(42, 43, 50) : Color.FromArgb(241, 242, 246);
        public static readonly Color Hover = IsDark ? Color.FromArgb(50, 51, 59) : Color.FromArgb(236, 238, 243);
        public static readonly Color Border = IsDark ? Color.FromArgb(56, 57, 65) : Color.FromArgb(227, 229, 235);
        public static readonly Color Text = IsDark ? Color.FromArgb(243, 244, 246) : Color.FromArgb(22, 24, 29);
        public static readonly Color TextMuted = IsDark ? Color.FromArgb(155, 161, 172) : Color.FromArgb(107, 114, 128);
        public static readonly Color Accent = IsDark ? Color.FromArgb(108, 147, 255) : Color.FromArgb(58, 123, 255);
        public static readonly Color AccentHover = IsDark ? Color.FromArgb(126, 161, 255) : Color.FromArgb(46, 104, 232);
        public static readonly Color Track = IsDark ? Color.FromArgb(58, 59, 67) : Color.FromArgb(225, 228, 235);
        public static readonly Color TrackHover = IsDark ? Color.FromArgb(70, 71, 81) : Color.FromArgb(210, 214, 222);

        private static bool DetectDarkMode()
        {
            string? forced = Environment.GetEnvironmentVariable("MACSMOOTHSCROLL_THEME");
            if (!string.IsNullOrEmpty(forced))
                return forced.Equals("dark", StringComparison.OrdinalIgnoreCase);

            try
            {
                object? value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1);

                return value is int light && light == 0;
            }
            catch
            {
                return false;
            }
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float diameter = Math.Max(0.1f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
            var path = new GraphicsPath();

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);

            path.CloseFigure();
            return path;
        }

        public static void RoundControl(Control control, float radius)
        {
            Region? previous = control.Region;
            control.Region = new Region(RoundedRect(new RectangleF(0f, 0f, control.Width, control.Height), radius));
            previous?.Dispose();
        }

        public static Font Ui(float size, FontStyle style = FontStyle.Regular) =>
            new Font("Segoe UI", size, style, GraphicsUnit.Point);

        public static Font Display(float size)
        {
            try
            {
                return new Font("Segoe UI Semibold", size, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point);
            }
        }
    }

    /// <summary>A panel that paints a rounded, filled surface with a hairline border.</summary>
    internal sealed class RoundedPanel : Panel
    {
        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Theme.Window;
        }

        /// <summary>Corner radius in device-independent pixels.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = 10;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = Theme.Surface;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Theme.Border;

        /// <summary>Optional centred placeholder text, drawn when the panel has no content.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? HintText { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HintColor { get; set; } = Theme.TextMuted;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float radius = CornerRadius * (DeviceDpi / 96f);
            var rect = new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));

            using GraphicsPath path = Theme.RoundedRect(rect, radius);
            using (var fill = new SolidBrush(FillColor))
                g.FillPath(fill, path);

            if (BorderColor.A > 0)
            {
                using var pen = new Pen(BorderColor, 1f);
                g.DrawPath(pen, path);
            }

            if (!string.IsNullOrEmpty(HintText))
            {
                TextRenderer.DrawText(
                    g,
                    HintText,
                    Font,
                    new Rectangle(0, 0, Width, Height),
                    HintColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
