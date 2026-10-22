using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacSmoothScroll
{
    /// <summary>
    /// Settings dialog. Borderless card that follows the Windows theme, applies every
    /// change immediately (no Save button to forget) and remembers them on close.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private const int CardWidth = 520;      // device-independent pixels
        private const int PadX = 22;
        private const int ContentWidth = CardWidth - (PadX * 2);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly Settings settings;
        private readonly ToolTip tips = new ToolTip
        {
            AutoPopDelay = 12000,
            InitialDelay = 400,
            ReshowDelay = 150
        };

        private readonly PictureBox picIcon = new PictureBox();
        private readonly Label lblTitle = new Label();
        private readonly Label lblSubtitle = new Label();
        private readonly Button btnClose = new Button();

        private readonly Button[] presetButtons = new Button[3];
        private static readonly Preset[] presets =
        {
            new Preset("Subtle", 0.68f, 0.90f),
            new Preset("Balanced", 0.82f, 1.20f),
            new Preset("Fast", 0.90f, 1.60f)
        };

        private readonly Row glide;
        private readonly Row speed;
        private readonly Button btnAdvanced = new Button();
        private readonly Row minStep;
        private readonly Row maxSpeed;
        private readonly Row interval;

        private readonly Label lblExcluded = new Label();
        private readonly RoundedPanel listHost = new RoundedPanel();
        private readonly ListBox lstExcluded = new ListBox();

        private readonly RoundedPanel txtHost = new RoundedPanel();
        private readonly TextBox txtAdd = new TextBox();
        private readonly Button btnAdd = new Button();
        private readonly Button btnRemove = new Button();

        private readonly Button btnPick = new Button();
        private readonly Label lblPickHint = new Label();

        private readonly Panel separator = new Panel();
        private readonly Label lblStartup = new Label();
        private readonly ToggleSwitch tglStartup = new ToggleSwitch();

        private readonly Button btnReset = new Button();
        private readonly Button btnDone = new Button();
        private readonly System.Windows.Forms.Timer pickTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        private bool advancedExpanded;
        private bool startupSync;
        private int pickCountdown;

        public SettingsForm(Settings settings)
        {
            this.settings = settings;

            Text = "Smooth Scroll Settings";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            Icon = AppIcon.Value;

            BuildHeader();
            BuildPresets();

            glide = CreateRow(
                "Glide length", 60, 98, (int)Math.Round(settings.Decay * 100),
                DescribeGlide, value => settings.Decay = value / 100f,
                "How far the page keeps moving after you stop turning the wheel.");

            speed = CreateRow(
                "Speed", 50, 300, (int)Math.Round(settings.Sensitivity * 100),
                DescribeSpeed, value => settings.Sensitivity = value / 100f,
                "Multiplier for every wheel notch.");

            BuildAdvancedToggle();

            minStep = CreateRow(
                "Minimum step", 1, 60, Math.Clamp(settings.MinChunk, 1, 60),
                value => value.ToString(), value => settings.MinChunk = value,
                "Smallest movement per frame. Raise it if slow scrolling feels stuck.");

            maxSpeed = CreateRow(
                "Speed cap", 240, 3600, Math.Clamp(settings.MaxVelocity, 240, 3600),
                value => value.ToString(), value => settings.MaxVelocity = value,
                "Upper limit for very fast wheel spins, so the page cannot coast forever.");

            interval = CreateRow(
                "Update rate", 8, 33, Math.Clamp(settings.IntervalMs, 8, 33),
                value => $"{value} ms", value => settings.IntervalMs = value,
                "How often a scroll step is sent. Lower is smoother, at a little more CPU.");

            SetAdvancedVisible(false);

            BuildExclusions();
            BuildStartup();
            BuildFooter();

            ApplyLayout();
            UpdatePresetState();

            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    BeginDrag();
            };

            foreach (Control draggable in new Control[] { picIcon, lblTitle, lblSubtitle })
                draggable.MouseDown += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                        BeginDrag();
                };

            pickTimer.Tick += (_, _) => OnPickTick();

            FormClosed += (_, _) =>
            {
                pickTimer.Stop();
                pickTimer.Dispose();
                tips.Dispose();
                picIcon.Image?.Dispose();
                settings.Save();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CS_DROPSHADOW;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyLayout();
            CenterToScreen();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ApplyLayout();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        // ---------- construction ----------

        private void BuildHeader()
        {
            picIcon.BackColor = Theme.Window;
            picIcon.SizeMode = PictureBoxSizeMode.Zoom;
            using (var sized = new Icon(AppIcon.Value, 64, 64))
                picIcon.Image = sized.ToBitmap();
            Controls.Add(picIcon);

            lblTitle.Text = "Smooth Scroll";
            lblTitle.Font = Theme.Display(15f);
            lblTitle.ForeColor = Theme.Text;
            lblTitle.BackColor = Theme.Window;
            lblTitle.TextAlign = ContentAlignment.BottomLeft;
            lblTitle.AutoSize = false;
            Controls.Add(lblTitle);

            lblSubtitle.Text = "Changes apply immediately - scroll any app to feel them.";
            lblSubtitle.Font = Theme.Ui(9f);
            lblSubtitle.ForeColor = Theme.TextMuted;
            lblSubtitle.BackColor = Theme.Window;
            lblSubtitle.TextAlign = ContentAlignment.TopLeft;
            lblSubtitle.AutoSize = false;
            Controls.Add(lblSubtitle);

            btnClose.Text = "\u2715";
            btnClose.Font = Theme.Ui(10f);
            StyleButton(btnClose, Theme.Window, Theme.TextMuted, Theme.Hover, 8);
            btnClose.Click += (_, _) => Close();
            tips.SetToolTip(btnClose, "Close (Esc)");
            Controls.Add(btnClose);
        }

        private void BuildPresets()
        {
            for (int i = 0; i < presetButtons.Length; i++)
            {
                Preset preset = presets[i];
                var button = new Button { Text = preset.Name, Tag = preset };
                StyleButton(button, Theme.SurfaceAlt, Theme.Text, Theme.Hover, 16);
                button.Click += (_, _) => ApplyPreset(preset);
                presetButtons[i] = button;
                Controls.Add(button);
            }
        }

        private void BuildAdvancedToggle()
        {
            btnAdvanced.Text = "Advanced  \u25BE";
            btnAdvanced.TextAlign = ContentAlignment.MiddleLeft;
            StyleButton(btnAdvanced, Theme.Window, Theme.Accent, Theme.Window, 8);
            btnAdvanced.Click += (_, _) => ToggleAdvanced();
            tips.SetToolTip(btnAdvanced, "Fine-tune minimum step, speed cap and update rate.");
            Controls.Add(btnAdvanced);
        }

        private void BuildExclusions()
        {
            lblExcluded.Text = "Excluded apps keep their normal scrolling";
            lblExcluded.Font = Theme.Ui(9.5f);
            lblExcluded.ForeColor = Theme.Text;
            lblExcluded.BackColor = Theme.Window;
            lblExcluded.TextAlign = ContentAlignment.MiddleLeft;
            lblExcluded.AutoSize = false;
            Controls.Add(lblExcluded);

            listHost.CornerRadius = 10;
            listHost.FillColor = Theme.SurfaceAlt;
            listHost.BorderColor = Theme.Border;
            Controls.Add(listHost);

            lstExcluded.BorderStyle = BorderStyle.None;
            lstExcluded.BackColor = Theme.SurfaceAlt;
            lstExcluded.ForeColor = Theme.Text;
            lstExcluded.DrawMode = DrawMode.OwnerDrawFixed;
            lstExcluded.IntegralHeight = false;
            lstExcluded.SelectionMode = SelectionMode.One;
            lstExcluded.Font = Theme.Ui(9.5f);
            lstExcluded.DrawItem += DrawExcludedItem;
            lstExcluded.SelectedIndexChanged += (_, _) => UpdateEmptyState();
            lstExcluded.HandleCreated += (_, _) =>
            {
                if (Theme.IsDark)
                    SetWindowTheme(lstExcluded.Handle, "DarkMode_Explorer", null);
            };
            lstExcluded.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    RemoveSelected();
                    e.Handled = true;
                }
            };
            foreach (string name in settings.Blacklist)
                lstExcluded.Items.Add(name);
            listHost.Controls.Add(lstExcluded);

            txtHost.CornerRadius = 8;
            txtHost.FillColor = Theme.Surface;
            txtHost.BorderColor = Theme.Border;
            Controls.Add(txtHost);

            txtAdd.BorderStyle = BorderStyle.None;
            txtAdd.BackColor = Theme.Surface;
            txtAdd.ForeColor = Theme.Text;
            txtAdd.Font = Theme.Ui(9.5f);
            txtAdd.PlaceholderText = "process name, e.g. chrome";
            txtAdd.GotFocus += (_, _) =>
            {
                txtHost.BorderColor = Theme.Accent;
                txtHost.Invalidate();
            };
            txtAdd.LostFocus += (_, _) =>
            {
                txtHost.BorderColor = Theme.Border;
                txtHost.Invalidate();
            };
            txtAdd.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                AddTypedExcluded();
                e.SuppressKeyPress = true;
            };
            txtHost.Controls.Add(txtAdd);

            btnAdd.Text = "Add";
            StyleButton(btnAdd, Theme.SurfaceAlt, Theme.Text, Theme.Hover, 8);
            btnAdd.Click += (_, _) => AddTypedExcluded();
            Controls.Add(btnAdd);

            btnRemove.Text = "Remove";
            StyleButton(btnRemove, Theme.SurfaceAlt, Theme.Text, Theme.Hover, 8);
            btnRemove.Click += (_, _) => RemoveSelected();
            Controls.Add(btnRemove);

            btnPick.Text = "Add app under cursor...";
            StyleButton(btnPick, Theme.SurfaceAlt, Theme.Accent, Theme.Hover, 8);
            btnPick.Click += (_, _) => StartPicking();
            Controls.Add(btnPick);

            lblPickHint.Text = "Hover the app for 3 seconds";
            lblPickHint.Font = Theme.Ui(9f);
            lblPickHint.ForeColor = Theme.TextMuted;
            lblPickHint.BackColor = Theme.Window;
            lblPickHint.TextAlign = ContentAlignment.MiddleLeft;
            lblPickHint.AutoSize = false;
            Controls.Add(lblPickHint);

            UpdateEmptyState();
        }

        private void BuildStartup()
        {
            separator.BackColor = Theme.Border;
            Controls.Add(separator);

            const string hint = "Launch MacSmoothScroll automatically when you sign in to Windows.";

            lblStartup.Text = "Start with Windows";
            lblStartup.Font = Theme.Ui(9.5f);
            lblStartup.ForeColor = Theme.Text;
            lblStartup.BackColor = Theme.Window;
            lblStartup.TextAlign = ContentAlignment.MiddleLeft;
            lblStartup.AutoSize = false;
            tips.SetToolTip(lblStartup, hint);
            Controls.Add(lblStartup);

            tglStartup.AccessibleName = "Start with Windows";
            tglStartup.Checked = StartupEntry.IsEnabled;
            tglStartup.CheckedChanged += OnStartupToggled;
            tips.SetToolTip(tglStartup, hint);
            Controls.Add(tglStartup);
        }

        private void OnStartupToggled(object? sender, EventArgs e)
        {
            if (startupSync)
                return;

            if (StartupEntry.SetEnabled(tglStartup.Checked))
                return;

            startupSync = true;
            tglStartup.Checked = !tglStartup.Checked;
            startupSync = false;

            MessageBox.Show(
                this,
                "Windows would not accept the startup entry.\n\n" +
                "Start the app from MacSmoothScroll.exe (not through the build tools) and try again.",
                "Could not change startup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void BuildFooter()
        {
            btnReset.Text = "Reset to defaults";
            StyleButton(btnReset, Theme.Window, Theme.TextMuted, Theme.Window, 8);
            btnReset.Click += (_, _) => ResetToDefaults();
            Controls.Add(btnReset);

            btnDone.Text = "Done";
            StyleButton(btnDone, Theme.Accent, Color.White, Theme.AccentHover, 10);
            btnDone.Font = Theme.Ui(9.5f, FontStyle.Bold);
            btnDone.Click += (_, _) => Close();
            tips.SetToolTip(btnDone, "Close and keep your changes (Esc)");
            Controls.Add(btnDone);
        }

        private Row CreateRow(
            string caption,
            int minimum,
            int maximum,
            int initialValue,
            Func<int, string> describe,
            Action<int> apply,
            string hint)
        {
            var captionLabel = new Label
            {
                Text = caption,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Text,
                BackColor = Theme.Window,
                Font = Theme.Ui(9.5f)
            };

            var valueLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Window,
                Font = Theme.Ui(9.5f)
            };

            var slider = new SliderControl
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = Math.Clamp(initialValue, minimum, maximum),
                AccessibleName = caption
            };

            var row = new Row(captionLabel, valueLabel, slider, describe, apply);
            valueLabel.Text = describe(slider.Value);

            slider.ValueChanged += (_, _) =>
            {
                row.Apply(slider.Value);
                valueLabel.Text = row.Describe(slider.Value);
                UpdatePresetState();
            };

            tips.SetToolTip(slider, hint);
            tips.SetToolTip(captionLabel, hint);
            Controls.Add(captionLabel);
            Controls.Add(valueLabel);
            Controls.Add(slider);
            return row;
        }

        private void StyleButton(Button button, Color back, Color fore, Color hover, int radius)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = hover;
            button.BackColor = back;
            button.ForeColor = fore;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Font = Theme.Ui(9.5f);
            button.Resize += (_, _) => Theme.RoundControl(button, S(radius));
        }

        // ---------- layout ----------

        private int S(int dip) => (int)Math.Round(dip * (DeviceDpi / 96.0));

        private void ApplyLayout()
        {
            SuspendLayout();

            int listHeight = (S(8) * 2) + (S(26) * 4); // four rows, scrolls beyond that
            int height = PositionContent(listHeight);
            int maxHeight = AvailableHeight();

            if (height > maxHeight)
            {
                // Shrink the exclusions list first, then let the form scroll if needed.
                int shrunk = Math.Max(S(56), listHeight - (height - maxHeight));
                if (shrunk != listHeight)
                    height = PositionContent(shrunk);
            }

            ClientSize = new Size(S(CardWidth), Math.Min(height, maxHeight));
            AutoScroll = height > maxHeight;
            Theme.RoundControl(this, S(14));
            ResumeLayout();
        }

        /// <summary>Positions every control and returns the natural height of the card.</summary>
        private int PositionContent(int listHeight)
        {
            int content = S(ContentWidth);

            picIcon.SetBounds(S(PadX), S(20), S(30), S(30));
            lblTitle.SetBounds(S(PadX + 40), S(16), content - S(80), S(24));
            lblSubtitle.SetBounds(S(PadX + 40), S(40), content - S(80), S(18));
            btnClose.SetBounds(S(CardWidth) - S(PadX) - S(30), S(20), S(30), S(30));

            int y = S(84);
            int gap = S(8);
            int pillWidth = (content - (gap * 2)) / 3;
            for (int i = 0; i < presetButtons.Length; i++)
                presetButtons[i].SetBounds(S(PadX) + (i * (pillWidth + gap)), y, pillWidth, S(32));

            y += S(48);

            LayoutRow(glide, ref y, false);
            LayoutRow(speed, ref y, false);
            y += S(2);

            btnAdvanced.SetBounds(S(PadX), y, S(140), S(26));
            y += S(34);

            if (advancedExpanded)
            {
                LayoutRow(minStep, ref y, true);
                LayoutRow(maxSpeed, ref y, true);
                LayoutRow(interval, ref y, true);
            }

            y += S(10);

            lblExcluded.SetBounds(S(PadX), y, content, S(20));
            y += S(26);

            listHost.SetBounds(S(PadX), y, content, listHeight);
            lstExcluded.SetBounds(S(10), S(8), listHost.Width - S(20), listHost.Height - S(16));
            lstExcluded.ItemHeight = Math.Max(S(22), S(26));
            y += listHeight + S(8);

            int addWidth = S(64);
            int removeWidth = S(84);
            int textWidth = content - addWidth - removeWidth - (gap * 2);
            txtHost.SetBounds(S(PadX), y, textWidth, S(32));
            txtAdd.SetBounds(S(10), S(7), textWidth - S(20), S(20));
            btnAdd.SetBounds(S(PadX) + textWidth + gap, y, addWidth, S(32));
            btnRemove.SetBounds(S(PadX) + textWidth + gap + addWidth + gap, y, removeWidth, S(32));
            y += S(40);

            btnPick.SetBounds(S(PadX), y, S(210), S(32));
            lblPickHint.SetBounds(S(PadX) + S(218), y + S(9), content - S(218), S(18));
            y += S(44);

            separator.SetBounds(S(PadX), y, content, S(1));
            y += S(15);

            lblStartup.SetBounds(S(PadX), y, content - S(56), S(26));
            tglStartup.SetBounds(S(PadX) + content - S(44), y + S(1), S(44), S(24));
            y += S(32);

            btnReset.SetBounds(S(PadX), y + S(6), S(160), S(36));
            btnDone.SetBounds(S(CardWidth) - S(PadX) - S(120), y + S(6), S(120), S(36));
            y += S(6) + S(36) + S(20);

            return y;
        }

        private int AvailableHeight()
        {
            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            return Math.Max(S(360), screen.WorkingArea.Height - S(24));
        }

        private void LayoutRow(Row row, ref int y, bool compact)
        {
            int content = S(ContentWidth);
            int valueWidth = S(110);

            row.Caption.SetBounds(S(PadX), y, content - valueWidth, S(20));
            row.Value.SetBounds(S(PadX) + content - valueWidth, y, valueWidth, S(20));
            row.Slider.SetBounds(S(PadX), y + (compact ? S(18) : S(22)), content, compact ? S(24) : S(30));
            y += compact ? S(46) : S(56);
        }

        // ---------- behaviour ----------

        private void ToggleAdvanced()
        {
            advancedExpanded = !advancedExpanded;
            btnAdvanced.Text = advancedExpanded ? "Advanced  \u25B4" : "Advanced  \u25BE";
            SetAdvancedVisible(advancedExpanded);
            ApplyLayout();
        }

        private void SetAdvancedVisible(bool visible)
        {
            foreach (Row row in new[] { minStep, maxSpeed, interval })
            {
                row.Caption.Visible = visible;
                row.Value.Visible = visible;
                row.Slider.Visible = visible;
            }
        }

        private void ApplyPreset(Preset preset)
        {
            glide.Slider.Value = (int)Math.Round(preset.Decay * 100);
            speed.Slider.Value = (int)Math.Round(preset.Sensitivity * 100);
        }

        private void UpdatePresetState()
        {
            foreach (Button button in presetButtons)
            {
                bool active = button.Tag is Preset preset && IsActivePreset(preset);
                Color back = active ? Theme.Accent : Theme.SurfaceAlt;
                Color hover = active ? Theme.AccentHover : Theme.Hover;

                button.BackColor = back;
                button.ForeColor = active ? Color.White : Theme.Text;
                button.FlatAppearance.MouseOverBackColor = hover;
                button.FlatAppearance.MouseDownBackColor = hover;
            }
        }

        private bool IsActivePreset(Preset preset) =>
            Math.Abs(settings.Decay - preset.Decay) < 0.006f &&
            Math.Abs(settings.Sensitivity - preset.Sensitivity) < 0.006f;

        private void ResetToDefaults()
        {
            var defaults = new Settings();

            glide.Slider.Value = (int)Math.Round(defaults.Decay * 100);
            speed.Slider.Value = (int)Math.Round(defaults.Sensitivity * 100);
            minStep.Slider.Value = defaults.MinChunk;
            maxSpeed.Slider.Value = defaults.MaxVelocity;
            interval.Slider.Value = defaults.IntervalMs;

            UpdatePresetState();
        }

        private void AddTypedExcluded()
        {
            string name = Settings.NormalizeAppName(txtAdd.Text);
            if (name.Length == 0)
                return;

            if (!settings.AddToBlacklist(name))
                return;

            lstExcluded.Items.Add(name);
            txtAdd.Clear();
            UpdateEmptyState();
            txtAdd.Focus();
        }

        private void RemoveSelected()
        {
            if (lstExcluded.SelectedItem is not string selected)
                return;

            if (settings.RemoveFromBlacklist(selected))
                lstExcluded.Items.Remove(selected);

            UpdateEmptyState();
        }

        private void StartPicking()
        {
            pickCountdown = 3;
            btnPick.Enabled = false;
            btnPick.Text = $"Hover the app... {pickCountdown}";
            pickTimer.Start();
        }

        private void OnPickTick()
        {
            if (--pickCountdown > 0)
            {
                btnPick.Text = $"Hover the app... {pickCountdown}";
                return;
            }

            pickTimer.Stop();
            btnPick.Enabled = true;
            btnPick.Text = "Add app under cursor...";

            if (!WindowProbe.TryGetCursorPosition(out int x, out int y))
                return;

            string? name = WindowProbe.GetProcessNameAt(x, y);
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(
                    this,
                    "Could not identify the app under the pointer. Hover over a normal application window.",
                    "No app found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (string.Equals(name, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    "That is this settings window. Hover over the app you want to exclude.",
                    "Not that one",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!settings.AddToBlacklist(name))
                return;

            lstExcluded.Items.Add(name);
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            bool empty = lstExcluded.Items.Count == 0;
            lstExcluded.Visible = !empty;
            listHost.HintText = empty ? "No excluded apps yet" : null;
            listHost.HintColor = Theme.TextMuted;
            listHost.Invalidate();
        }

        private void DrawExcludedItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = selected ? Theme.Accent : Theme.SurfaceAlt;
            Color textColor = selected ? Color.White : Theme.Text;

            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);

            string label = lstExcluded.Items[e.Index]?.ToString() ?? string.Empty;
            var textBounds = new Rectangle(
                e.Bounds.X + S(10),
                e.Bounds.Y,
                e.Bounds.Width - S(20),
                e.Bounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                label,
                lstExcluded.Font,
                textBounds,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void BeginDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        private static string DescribeGlide(int value)
        {
            float decay = value / 100f;

            if (decay < 0.74f)
                return "Short";
            if (decay < 0.86f)
                return "Balanced";
            if (decay < 0.94f)
                return "Long";

            return "Very long";
        }

        private static string DescribeSpeed(int value) => $"{value / 100f:0.0}\u00D7";

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string? subIdList);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();

        private sealed record Preset(string Name, float Decay, float Sensitivity);

        private sealed class Row
        {
            public Row(Label caption, Label value, SliderControl slider, Func<int, string> describe, Action<int> apply)
            {
                Caption = caption;
                Value = value;
                Slider = slider;
                Describe = describe;
                Apply = apply;
            }

            public Label Caption { get; }

            public Label Value { get; }

            public SliderControl Slider { get; }

            public Func<int, string> Describe { get; }

            public Action<int> Apply { get; }
        }
    }
}
