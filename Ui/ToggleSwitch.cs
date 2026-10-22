using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacSmoothScroll
{
    /// <summary>A minimal on/off switch drawn to match the settings card.</summary>
    internal sealed class ToggleSwitch : Control
    {
        private bool isOn;
        private bool hovered;

        public ToggleSwitch()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            Size = new Size(44, 24);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
            BackColor = Theme.Window;
        }

        public event EventHandler? CheckedChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => isOn;
            set
            {
                if (isOn == value)
                    return;

                isOn = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            const float inset = 2f;
            var track = new RectangleF(inset, inset, Width - (inset * 2f), Height - (inset * 2f));
            float radius = track.Height / 2f;

            Color trackColor = isOn
                ? (hovered ? Theme.AccentHover : Theme.Accent)
                : (hovered ? Theme.TrackHover : Theme.Track);

            using (GraphicsPath path = Theme.RoundedRect(track, radius))
            using (var brush = new SolidBrush(trackColor))
                g.FillPath(brush, path);

            float knob = track.Height - 4f;
            float knobX = isOn ? track.Right - knob - 2f : track.Left + 2f;
            var knobRect = new RectangleF(knobX, track.Top + 2f, knob, knob);

            using (var brush = new SolidBrush(Color.White))
                g.FillEllipse(brush, knobRect);

            if (Focused)
            {
                using var pen = new Pen(Color.FromArgb(90, Theme.Accent), 2f);
                using GraphicsPath ring = Theme.RoundedRect(RectangleF.Inflate(track, 2f, 2f), radius + 2f);
                g.DrawPath(pen, ring);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            Checked = !Checked;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }
    }
}
