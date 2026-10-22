using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacSmoothScroll
{
    /// <summary>
    /// A minimal, theme-aware slider: rounded track, accent fill and a circular thumb.
    /// Supports mouse dragging, the arrow/Page/Home/End keys and keyboard focus cues.
    /// </summary>
    internal sealed class SliderControl : Control
    {
        private int minimum;
        private int maximum = 100;
        private int value;
        private bool dragging;
        private bool hovered;

        public SliderControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            Height = 30;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Slider;
            BackColor = Theme.Window;
        }

        public event EventHandler? ValueChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => minimum;
            set
            {
                minimum = value;
                if (maximum < minimum)
                    maximum = minimum;
                Value = this.value;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => maximum;
            set
            {
                maximum = Math.Max(minimum, value);
                Value = this.value;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => value;
            set
            {
                int clamped = Math.Clamp(value, minimum, maximum);
                if (clamped == this.value)
                    return;

                this.value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private float ThumbDiameter => Math.Max(10f, Height * 0.52f);

        private float SideInset => ThumbDiameter / 2f;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float trackHeight = Math.Max(4f, Height * 0.15f);
            float span = Math.Max(1f, Width - (SideInset * 2f));
            float position = maximum > minimum ? (value - minimum) / (float)(maximum - minimum) : 0f;
            float thumbX = SideInset + (span * position);
            float trackY = (Height - trackHeight) / 2f;

            using (GraphicsPath track = Theme.RoundedRect(new RectangleF(SideInset, trackY, span, trackHeight), trackHeight / 2f))
            using (var brush = new SolidBrush(Theme.Track))
            {
                g.FillPath(brush, track);
            }

            float filledWidth = Math.Max(0f, thumbX - SideInset);
            if (filledWidth > 0.5f)
            {
                using GraphicsPath filled = Theme.RoundedRect(new RectangleF(SideInset, trackY, filledWidth, trackHeight), trackHeight / 2f);
                using var brush = new SolidBrush(dragging || hovered ? Theme.AccentHover : Theme.Accent);
                g.FillPath(brush, filled);
            }

            float diameter = ThumbDiameter * (dragging ? 1.12f : hovered ? 1.06f : 1f);
            var thumbRect = new RectangleF(thumbX - (diameter / 2f), (Height - diameter) / 2f, diameter, diameter);

            // Halo so the thumb reads clearly against the filled track.
            using (var halo = new SolidBrush(BackColor))
                g.FillEllipse(halo, thumbRect);

            float inset = Math.Max(1.5f, Height * 0.06f);
            var discRect = RectangleF.Inflate(thumbRect, -inset, -inset);
            using (var disc = new SolidBrush(dragging || hovered ? Theme.AccentHover : Theme.Accent))
                g.FillEllipse(disc, discRect);

            if (Focused)
            {
                using var ring = new Pen(Color.FromArgb(110, Theme.Accent), Math.Max(1.5f, Height * 0.06f));
                g.DrawEllipse(ring, RectangleF.Inflate(thumbRect, 2f, 2f));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            Focus();
            dragging = true;
            Capture = true;
            SetFromX(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
                return;

            SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            dragging = false;
            Capture = false;
            Invalidate();
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

        protected override bool IsInputKey(Keys keyData) => keyData switch
        {
            Keys.Left or Keys.Right or Keys.Up or Keys.Down or
            Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
            _ => base.IsInputKey(keyData)
        };

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value -= 1;
                    e.Handled = true;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value += 1;
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    Value -= 10;
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    Value += 10;
                    e.Handled = true;
                    break;
                case Keys.Home:
                    Value = Minimum;
                    e.Handled = true;
                    break;
                case Keys.End:
                    Value = Maximum;
                    e.Handled = true;
                    break;
            }

            base.OnKeyDown(e);
        }

        private void SetFromX(int x)
        {
            float span = Math.Max(1f, Width - (SideInset * 2f));
            float position = Math.Clamp((x - SideInset) / span, 0f, 1f);
            Value = minimum + (int)Math.Round(position * (maximum - minimum));
        }
    }
}
