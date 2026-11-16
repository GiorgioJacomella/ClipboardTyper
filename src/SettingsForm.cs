using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTyper
{
    /// <summary>The app's settings. Kept in memory only (nothing is written to disk).</summary>
    internal sealed class Settings
    {
        public int StartDelayMs = 1500;
        public int KeyDelayMs = 20;
        public bool AlwaysOnTop = true;
        public bool CollapseWhenTyping;
        public bool PauseCapture;

        public event EventHandler Changed;

        public void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    /// <summary>The window opened by Options → Settings. Changes apply immediately.</summary>
    internal sealed class SettingsForm : Form
    {
        public SettingsForm(Settings settings)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = MainForm.AppTitle + " - Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;    // not on top, so it must be findable in the taskbar
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14, 12, 14, 12);

            ToolTip tip = new ToolTip();
            tip.AutoPopDelay = 30000;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.AutoSize = true;
            layout.ColumnCount = 2;
            layout.Dock = DockStyle.Fill;

            // ---- Typing ----------------------------------------------------------------
            layout.Controls.Add(MakeHeading("Typing"), 0, 0);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, 0), 2);

            NumericUpDown startDelay = MakeNumber(0, 10000, 100, settings.StartDelayMs);
            startDelay.ValueChanged += delegate { settings.StartDelayMs = (int)startDelay.Value; settings.RaiseChanged(); };
            AddRow(layout, 1, "Start delay (ms)", startDelay, tip,
                "Wait this long before typing starts (time to focus the target window).");

            NumericUpDown keyDelay = MakeNumber(0, 500, 5, settings.KeyDelayMs);
            keyDelay.ValueChanged += delegate { settings.KeyDelayMs = (int)keyDelay.Value; settings.RaiseChanged(); };
            AddRow(layout, 2, "Key delay (ms)", keyDelay, tip,
                "Pause between keystrokes. Increase it if the remote tool drops or scrambles characters.");

            CheckBox collapse = MakeCheck("Collapse the panel when typing starts", settings.CollapseWhenTyping);
            collapse.CheckedChanged += delegate { settings.CollapseWhenTyping = collapse.Checked; settings.RaiseChanged(); };
            layout.Controls.Add(collapse, 0, 3);
            layout.SetColumnSpan(collapse, 2);

            // ---- Window & clipboard -----------------------------------------------------
            Label windowHeading = MakeHeading("Window and clipboard");
            windowHeading.Margin = new Padding(0, 12, 0, 4);
            layout.Controls.Add(windowHeading, 0, 4);
            layout.SetColumnSpan(windowHeading, 2);

            CheckBox topMost = MakeCheck("Keep the panel always on top", settings.AlwaysOnTop);
            topMost.CheckedChanged += delegate { settings.AlwaysOnTop = topMost.Checked; settings.RaiseChanged(); };
            tip.SetToolTip(topMost, "The collapsed tab on the screen edge always stays on top.");
            layout.Controls.Add(topMost, 0, 5);
            layout.SetColumnSpan(topMost, 2);

            CheckBox pause = MakeCheck("Pause capture (don't add copied text to the history)", settings.PauseCapture);
            pause.CheckedChanged += delegate { settings.PauseCapture = pause.Checked; settings.RaiseChanged(); };
            layout.Controls.Add(pause, 0, 6);
            layout.SetColumnSpan(pause, 2);

            Button close = new Button();
            close.Text = "Close";
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(0, 14, 0, 0);
            close.Click += delegate { Close(); };
            AcceptButton = close;
            CancelButton = close;
            layout.Controls.Add(close, 1, 7);

            Controls.Add(layout);
            ResumeLayout(true);
        }

        private static void AddRow(TableLayoutPanel layout, int row, string text, Control input, ToolTip tip, string help)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 4, 16, 4);
            layout.Controls.Add(l, 0, row);
            layout.Controls.Add(input, 1, row);
            tip.SetToolTip(l, help);
            tip.SetToolTip(input, help);
        }

        private static Label MakeHeading(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            l.Margin = new Padding(0, 0, 0, 4);
            return l;
        }

        private static NumericUpDown MakeNumber(int min, int max, int step, int value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.Width = 80;
            n.Margin = new Padding(0, 3, 0, 3);
            return n;
        }

        private static CheckBox MakeCheck(string text, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Checked = value;
            c.Margin = new Padding(0, 4, 0, 2);
            return c;
        }
    }
}
