using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardTyper
{
    /// <summary>The window opened by the Help menu: how to use the app, plus a link to the repo.</summary>
    internal sealed class HelpForm : Form
    {
        public const string RepoUrl = "https://github.com/GiorgioJacomella/ClipboardTyper";

        private static readonly string[,] Rows =
        {
            { "Add to history", "Copy text anywhere (Ctrl+C). Duplicates move to the top. Up to 100 entries." },
            { "Copy an entry again", "Double-click it, or select it and press Enter." },
            { "Autotype an entry", "Right-click it → Autotype, or Autotype + Enter." },
            { "Autotype the newest entry", "Click into the target window and press Ctrl+Alt+V." },
            { "Send special keys", "Open the Send menu (Alt+S) and click a key combination." },
            { "Cancel typing", "Press Esc (also during the start delay), click into another window, or close the app." },
            { "Delete an entry", "Right-click → Delete, or press Del." },
            { "Collapse the panel", "Click › in the title bar or press Esc. Click the tab on the screen edge to open it again." },
            { "Settings", "Options menu → Settings (delays, always on top, pause capture)." },
        };

        public HelpForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = MainForm.AppTitle + " - Help";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;    // not on top, so it must be findable in the taskbar
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14, 12, 14, 12);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.AutoSize = true;
            layout.ColumnCount = 2;
            layout.Dock = DockStyle.Fill;

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(440, 0);
            intro.Margin = new Padding(0, 0, 0, 10);
            intro.Text = "ClipboardTyper keeps a history of the text you copy and types it as real keystrokes, " +
                         "so it works in remote-access tools (TeamViewer, RDP, VNC, VM consoles) that block Ctrl+V.\n\n" +
                         "Autotype goes to the Target window shown at the bottom: the last window you used.";
            layout.Controls.Add(intro, 0, 0);
            layout.SetColumnSpan(intro, 2);

            Font bold = new Font(Font, FontStyle.Bold);
            for (int i = 0; i < Rows.GetLength(0); i++)
            {
                Label name = new Label();
                name.AutoSize = true;
                name.Font = bold;
                name.Text = Rows[i, 0];
                name.Margin = new Padding(0, 2, 12, 4);

                Label how = new Label();
                how.AutoSize = true;
                how.MaximumSize = new Size(270, 0);
                how.Text = Rows[i, 1];
                how.Margin = new Padding(0, 2, 0, 4);

                layout.Controls.Add(name, 0, i + 1);
                layout.Controls.Add(how, 1, i + 1);
            }

            LinkLabel link = new LinkLabel();
            link.AutoSize = true;
            link.Text = "Source code and README on GitHub";
            link.Margin = new Padding(0, 12, 0, 4);
            link.LinkClicked += delegate
            {
                try { Process.Start(RepoUrl); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not open the browser:\n" + ex.Message + "\n\n" + RepoUrl,
                        MainForm.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            new ToolTip().SetToolTip(link, RepoUrl);

            Button ok = new Button();
            ok.Text = "Close";
            ok.Click += delegate { Close(); };   // non-modal: DialogResult alone would not close it
            ok.Anchor = AnchorStyles.Right;
            ok.Margin = new Padding(0, 10, 0, 0);
            AcceptButton = ok;
            CancelButton = ok;

            int last = Rows.GetLength(0) + 1;
            layout.Controls.Add(link, 0, last);
            layout.SetColumnSpan(link, 2);
            layout.Controls.Add(ok, 1, last + 1);

            Controls.Add(layout);
            ResumeLayout(true);
        }
    }
}
