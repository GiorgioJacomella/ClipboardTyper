using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardTyper
{
    internal sealed class HistoryItem
    {
        public readonly string Text;
        private readonly string preview;

        public HistoryItem(string text)
        {
            Text = text;
            string s = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", " ⏎ ").Replace('\t', ' ');
            if (s.Length > 150) s = s.Substring(0, 150) + "…";
            preview = s;
        }

        public override string ToString() { return preview; }
    }

    internal sealed class MainForm : Form
    {
        public const string AppTitle = "ClipboardTyper";
        /// <summary>Posted by a second instance to open the panel of the running one.</summary>
        public const int WM_SHOW_PANEL = 0x8001; // WM_APP + 1
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int SPI_SETWORKAREA = 0x002F;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const int HotkeyId = 1;
        private const int MaxHistory = 100;
        private const string NoTargetText = "Target: (click into the window you want to type into)";

        private static readonly Color Accent = Color.FromArgb(16, 96, 176);
        private static readonly Color AccentHover = Color.FromArgb(44, 124, 204);
        private static readonly Color AccentDown = Color.FromArgb(10, 72, 138);
        private static readonly Color CloseHover = Color.FromArgb(196, 43, 28);
        private static readonly Color FrameColor = Color.FromArgb(96, 96, 96);

        // Layout in 96-dpi pixels; multiplied by the screen scale at runtime.
        private const int PanelWidth = 340, PanelHeight = 430;
        private const int TabWidth = 28, TabHeight = 150;
        private const int EdgeMargin = 0;

        private static readonly HashSet<string> IgnoredWindowClasses = new HashSet<string>
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "TaskListThumbnailWnd",
            "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame",
            "TaskSwitcherWnd", "Windows.UI.Core.CoreWindow", "NotifyIconOverflowWindow",
            "TopLevelWindowForOverflowXamlIsland", "#32768", "tooltips_class32"
        };

        private readonly ListBox list;
        private readonly Settings settings = new Settings();
        private readonly Label targetLabel;
        private readonly ToolStripStatusLabel statusLabel;
        private readonly ToolTip tip;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem miType, miTypeEnter, miCopy, miDelete;
        private readonly Label historyLabel;
        private readonly Button clearButton;
        private readonly Panel headerBar;
        private readonly Panel body;
        private readonly Panel tab;
        private bool collapsed;
        private float scale = 1f;

        private readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        private NativeMethods.WinEventDelegate winEventProc; // keep a reference so the GC doesn't collect it
        private IntPtr winEventHook = IntPtr.Zero;
        private IntPtr lastTarget = IntPtr.Zero;
        private bool hotkeyRegistered;
        private int tipIndex = -1;
        private volatile AutoTyper activeTyper;
        private Thread typerThread;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = AppTitle;
            // Borderless panel docked to the bottom-right corner (like TeamViewer's session panel).
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            BackColor = FrameColor;          // shows as a 1 px frame around the panel
            Padding = new Padding(1);
            ClientSize = new Size(PanelWidth, PanelHeight);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            tip = new ToolTip();
            tip.AutoPopDelay = 30000;
            tip.InitialDelay = 400;

            settings.Changed += delegate { UpdateTopMost(); };

            // ---- Title bar: name + collapse / close ---------------------------------------
            headerBar = new Panel();
            headerBar.Dock = DockStyle.Top;
            headerBar.Height = 30;
            headerBar.BackColor = Accent;

            Label title = new Label();
            title.Text = AppTitle;
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Padding = new Padding(6, 0, 0, 0);
            title.DoubleClick += delegate { SetCollapsed(true); };

            Button closeButton = MakeHeaderButton("", "Close ClipboardTyper (the history is lost)",
                delegate { Close(); });
            closeButton.FlatAppearance.MouseOverBackColor = CloseHover;
            Button collapseButton = MakeHeaderButton("", "Collapse to the side (Esc)",
                delegate { SetCollapsed(true); });
            // Docking runs from the last-added control to the first: close ends up rightmost.
            headerBar.Controls.Add(title);
            headerBar.Controls.Add(collapseButton);
            headerBar.Controls.Add(closeButton);

            // ---- Collapsed tab on the screen edge ------------------------------------------
            tab = new Panel();
            tab.Dock = DockStyle.Fill;
            tab.BackColor = Accent;
            tab.Cursor = Cursors.Hand;
            tab.Visible = false;
            tab.Paint += Tab_Paint;
            tab.Click += delegate { SetCollapsed(false); };
            tip.SetToolTip(tab, "Open ClipboardTyper");

            body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = SystemColors.Control;

            // ---- History list -------------------------------------------------------------
            list = new ListBox();
            list.Dock = DockStyle.Fill;
            list.IntegralHeight = false;
            list.HorizontalScrollbar = false;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.DoubleClick += delegate { CopySelected(); };
            list.MouseDown += List_MouseDown;
            list.MouseMove += List_MouseMove;
            list.KeyDown += List_KeyDown;

            menu = new ContextMenuStrip();
            miType = new ToolStripMenuItem("Autotype", null, delegate { TypeSelected(false); });
            miType.Font = new Font(miType.Font, FontStyle.Bold);
            miTypeEnter = new ToolStripMenuItem("Autotype + Enter", null, delegate { TypeSelected(true); });
            miCopy = new ToolStripMenuItem("Copy to clipboard", null, delegate { CopySelected(); });
            miDelete = new ToolStripMenuItem("Delete", null, delegate { DeleteSelected(); });
            menu.Items.AddRange(new ToolStripItem[]
            {
                miType, miTypeEnter, new ToolStripSeparator(), miCopy, miDelete
            });
            menu.Opening += Menu_Opening;
            list.ContextMenuStrip = menu;

            // ---- Menu bar: "Send" drops down the special key combinations ------------------
            MenuStrip top = new MenuStrip();
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(2, 2, 0, 2);
            MainMenuStrip = top;             // Alt+S opens the Send menu

            ToolStripMenuItem sendMenu = new ToolStripMenuItem("&Send");
            sendMenu.ToolTipText = "Autotype a key combination into the last active window (Target)";
            foreach (SpecialKey key in SpecialKey.All)
            {
                if (key == null)
                {
                    sendMenu.DropDownItems.Add(new ToolStripSeparator());
                    continue;
                }
                SpecialKey k = key;
                ToolStripMenuItem item = new ToolStripMenuItem(key.Name);
                if (key.Hint != null) item.ShortcutKeyDisplayString = key.Hint;
                item.Click += delegate { SendSpecialKey(k); };
                sendMenu.DropDownItems.Add(item);
            }
            top.Items.Add(sendMenu);

            // "Options" drops down Settings and Help; both open as separate windows.
            ToolStripMenuItem optionsMenu = new ToolStripMenuItem("&Options");
            optionsMenu.DropDownItems.Add(new ToolStripMenuItem("&Settings...", null,
                delegate { ShowSingleWindow(delegate { return new SettingsForm(settings); }); }));
            optionsMenu.DropDownItems.Add(new ToolStripMenuItem("&Help", null,
                delegate { ShowSingleWindow(delegate { return new HelpForm(); }); }));
            top.Items.Add(optionsMenu);

            // ---- History header: count + Clear all button -----------------------------------
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Top;
            header.AutoSize = true;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.Padding = new Padding(4, 0, 4, 2);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            historyLabel = new Label();
            historyLabel.AutoSize = true;
            historyLabel.Anchor = AnchorStyles.Left;
            historyLabel.Font = new Font(Font, FontStyle.Bold);
            historyLabel.Text = "History (0)";

            clearButton = new Button();
            clearButton.Text = "Clear all";
            clearButton.AutoSize = true;
            clearButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            clearButton.Padding = new Padding(6, 0, 6, 0);
            clearButton.Anchor = AnchorStyles.Right;
            clearButton.Enabled = false;
            clearButton.Click += delegate { ClearAll(); };
            tip.SetToolTip(clearButton, "Remove all entries from the history");

            header.Controls.Add(historyLabel, 0, 0);
            header.Controls.Add(clearButton, 1, 0);

            // ---- Bottom: target window + status ------------------------------------------
            targetLabel = new Label();
            targetLabel.Dock = DockStyle.Bottom;
            targetLabel.AutoSize = false;
            targetLabel.Height = 20;
            targetLabel.Padding = new Padding(4, 3, 4, 0);
            targetLabel.AutoEllipsis = true;
            targetLabel.Text = NoTargetText;

            StatusStrip strip = new StatusStrip();
            strip.SizingGrip = false;
            statusLabel = new ToolStripStatusLabel();
            statusLabel.Spring = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            strip.Items.Add(statusLabel);

            // Docking is applied from the last-added control to the first one.
            body.Controls.Add(list);
            body.Controls.Add(header);
            body.Controls.Add(top);
            body.Controls.Add(targetLabel);
            body.Controls.Add(strip);

            Controls.Add(body);
            Controls.Add(headerBar);
            Controls.Add(tab);

            ResumeLayout(true);
        }

        private Button MakeHeaderButton(string glyph, string tooltip, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = glyph;
            b.Font = new Font("Segoe MDL2 Assets", 8F);
            b.ForeColor = Color.White;
            b.BackColor = Accent;
            b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = AccentDown;
            b.Dock = DockStyle.Right;
            b.Width = 34;
            b.TabStop = false;
            b.Click += onClick;
            tip.SetToolTip(b, tooltip);
            return b;
        }

        // ---- Panel / tab placement ----------------------------------------------------------

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                cp.ExStyle |= 0x00000080;      // WS_EX_TOOLWINDOW: no Alt+Tab entry
                return cp;
            }
        }

        private int S(int pixels96)
        {
            return (int)Math.Round(pixels96 * scale);
        }

        private void SetCollapsed(bool value)
        {
            collapsed = value;
            SuspendLayout();
            headerBar.Visible = !value;
            body.Visible = !value;
            tab.Visible = value;
            ResumeLayout(true);
            UpdateTopMost();
            PlaceWindow();
            if (!value)
            {
                RefreshTargetLabel();
                // While typing, the target window must keep the focus.
                if (activeTyper == null)
                {
                    Activate();
                    list.Focus();
                }
            }
            tab.Invalidate();
        }

        private void UpdateTopMost()
        {
            TopMost = collapsed || settings.AlwaysOnTop;
        }

        /// <summary>Puts the panel (or the tab) into the bottom-right corner of the work area.</summary>
        private void PlaceWindow()
        {
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            Size size = collapsed ? new Size(S(TabWidth), S(TabHeight)) : new Size(S(PanelWidth), S(PanelHeight));
            int right = collapsed ? 0 : S(EdgeMargin);   // the tab sits flush on the edge
            Bounds = new Rectangle(wa.Right - size.Width - right, wa.Bottom - size.Height - S(EdgeMargin),
                size.Width, size.Height);
        }

        private void Tab_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = tab.ClientRectangle;

            using (Font glyph = new Font("Segoe MDL2 Assets", 9F))
            {
                TextRenderer.DrawText(g, "", glyph, new Rectangle(0, S(6), r.Width, S(20)), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            string label = AppTitle + " (" + list.Items.Count + ")";
            using (Font f = new Font("Segoe UI", 9F, FontStyle.Bold))
            using (Brush brush = new SolidBrush(Color.White))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform(r.Width / 2f, (r.Height + S(26)) / 2f);
                g.RotateTransform(-90);
                SizeF sz = g.MeasureString(label, f);
                g.DrawString(label, f, brush, -sz.Width / 2f, -sz.Height / 2f);
                g.ResetTransform();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !collapsed)
            {
                SetCollapsed(true);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        // ---- Window lifetime --------------------------------------------------------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.AddClipboardFormatListener(Handle);
            hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, (uint)Keys.V);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            if (hotkeyRegistered) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            hotkeyRegistered = false;
            base.OnHandleDestroyed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            PlaceWindow();

            winEventProc = OnForegroundChanged;
            winEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);

            if (hotkeyRegistered)
                SetStatus("Ready. Copy something to add it to the history.");
            else
                SetStatus("Ready. (Ctrl+Alt+V is used by another app - hotkey disabled)");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Stop typing and give the worker a moment to finish its current key and switch
            // Caps Lock back on; a background thread would otherwise be killed mid-way.
            AutoTyper t = activeTyper;
            if (t != null) t.Cancel();
            Thread th = typerThread;
            if (th != null && th.IsAlive) th.Join(1000);
            // Close the Settings/Help windows together with the app.
            List<Form> others = new List<Form>();
            foreach (Form f in Application.OpenForms) if (f != this) others.Add(f);
            foreach (Form f in others) f.Close();
            if (winEventHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(winEventHook);
            winEventHook = IntPtr.Zero;
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE && activeTyper != null)
            {
                // Typing (or waiting to type): clicks on the panel or tab still work, but must not
                // take the focus away from the target window, which would stop the typing.
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                // Read a moment later so the source app has finished writing.
                BeginInvoke(new Action(OnClipboardChanged));
            }
            else if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                OnHotkey();
            }
            else if (m.Msg == WM_SHOW_PANEL)
            {
                SetCollapsed(false);
            }
            else if (m.Msg == WM_DISPLAYCHANGE ||
                     (m.Msg == WM_SETTINGCHANGE && m.WParam.ToInt32() == SPI_SETWORKAREA))
            {
                // Taskbar moved/resized or screen resolution changed: stay in the corner.
                BeginInvoke(new Action(PlaceWindow));
            }
            base.WndProc(ref m);
        }

        // ---- Target window tracking -------------------------------------------------------

        private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero) return;
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == ownPid) return;
            if (IgnoredWindowClasses.Contains(NativeMethods.GetClassNameOf(hwnd))) return;
            lastTarget = hwnd;
            RefreshTargetLabel();
        }

        /// <summary>
        /// Shows the current title of the target window (it may have changed since it was
        /// activated, e.g. another browser tab), or the default text if the window was closed.
        /// </summary>
        private void RefreshTargetLabel()
        {
            if (lastTarget != IntPtr.Zero && !NativeMethods.IsWindow(lastTarget)) lastTarget = IntPtr.Zero;
            if (lastTarget == IntPtr.Zero)
            {
                targetLabel.Text = NoTargetText;
                return;
            }
            string title = NativeMethods.GetTitleOf(lastTarget);
            if (title.Length == 0) title = "(" + NativeMethods.GetClassNameOf(lastTarget) + ")";
            targetLabel.Text = "Target: " + title;
        }

        private bool IsOwnWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            return pid == ownPid;
        }

        // ---- Clipboard ----------------------------------------------------------------

        private void OnClipboardChanged()
        {
            if (settings.PauseCapture) return;
            string text = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    IDataObject data = Clipboard.GetDataObject();
                    if (data == null) return;
                    // Password managers mark sensitive clipboard content with these formats.
                    if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") ||
                        data.GetDataPresent("Clipboard Viewer Ignore"))
                        return;
                    if (data.GetDataPresent(DataFormats.UnicodeText))
                        text = data.GetData(DataFormats.UnicodeText) as string;
                    break;
                }
                catch (ExternalException) { Thread.Sleep(40); }
                catch (ThreadStateException) { return; }
            }
            if (string.IsNullOrEmpty(text)) return;
            AddToHistory(text);
        }

        private void AddToHistory(string text)
        {
            bool reselect = false;
            for (int i = 0; i < list.Items.Count; i++)
            {
                HistoryItem existing = (HistoryItem)list.Items[i];
                if (existing.Text == text)
                {
                    reselect = list.SelectedIndex == i;
                    list.Items.RemoveAt(i);
                    break;
                }
            }
            list.Items.Insert(0, new HistoryItem(text));
            while (list.Items.Count > MaxHistory) list.Items.RemoveAt(list.Items.Count - 1);
            if (reselect) list.SelectedIndex = 0;
            tipIndex = -1;
            UpdateHistoryCount();
        }

        private void UpdateHistoryCount()
        {
            historyLabel.Text = "History (" + list.Items.Count + ")";
            clearButton.Enabled = list.Items.Count > 0;
            if (collapsed) tab.Invalidate();
        }

        private HistoryItem SelectedItem
        {
            get { return list.SelectedItem as HistoryItem; }
        }

        private void CopySelected()
        {
            HistoryItem item = SelectedItem;
            if (item == null) return;
            try
            {
                Clipboard.SetDataObject(item.Text, true, 10, 50);
                SetStatus("Copied to clipboard.");
            }
            catch (Exception ex)
            {
                SetStatus("Copy failed: " + ex.Message);
            }
        }

        private void DeleteSelected()
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            list.Items.RemoveAt(i);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
            tipIndex = -1;
            UpdateHistoryCount();
        }

        private void ClearAll()
        {
            if (list.Items.Count == 0) return;
            if (MessageBox.Show(this, "Remove all " + list.Items.Count + " entries from the history?", AppTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            list.Items.Clear();
            tipIndex = -1;
            UpdateHistoryCount();
            SetStatus("History cleared.");
        }

        // ---- List interaction -------------------------------------------------------

        private void List_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            int i = list.IndexFromPoint(e.Location);
            if (i >= 0 && i < list.Items.Count) list.SelectedIndex = i;
        }

        private void List_MouseMove(object sender, MouseEventArgs e)
        {
            int i = list.IndexFromPoint(e.Location);
            if (i >= list.Items.Count) i = -1;
            if (i == tipIndex) return;
            tipIndex = i;
            if (i < 0) { tip.SetToolTip(list, null); return; }
            string text = ((HistoryItem)list.Items[i]).Text;
            if (text.Length > 1000) text = text.Substring(0, 1000) + "…";
            tip.SetToolTip(list, text);
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { CopySelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
        }

        private void Menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // No menu while typing: it could take the focus from the target window.
            bool has = SelectedItem != null && activeTyper == null;
            miType.Enabled = has;
            miTypeEnter.Enabled = has;
            miCopy.Enabled = has;
            miDelete.Enabled = has;
            if (!has) e.Cancel = true;   // no menu when right-clicking empty space
        }

        // ---- Autotype --------------------------------------------------------------------

        private void TypeSelected(bool pressEnter)
        {
            HistoryItem item = SelectedItem;
            if (item == null) return;
            string text = item.Text;
            StartTyping(delegate(AutoTyper t)
            {
                if (!t.TypeText(text)) return false;
                if (pressEnter) t.PressEnter();
                return true;
            });
        }

        /// <summary>
        /// Opens a Settings/Help window as a normal window: no owner (an owned window would inherit the
        /// panel's always-on-top) and not modal, so the panel stays usable. If one of that kind is
        /// already open, it is brought to the front instead. All are closed together with the app.
        /// </summary>
        private void ShowSingleWindow<T>(Func<T> create) where T : Form
        {
            T window = null;
            foreach (Form f in Application.OpenForms)
            {
                if (f is T) { window = (T)f; break; }
            }
            if (window == null)
            {
                window = create();
                window.Icon = Icon;
                window.FormClosed += delegate(object s, FormClosedEventArgs e) { ((Form)s).Dispose(); };
                window.Show();
            }
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            window.Activate();
        }

        private void SendSpecialKey(SpecialKey key)
        {
            StartTyping(delegate(AutoTyper t) { return t.SendCombo(key.Keys); });
        }

        /// <summary>Autotype triggered from this window: switch to the last target window, wait, type.</summary>
        private void StartTyping(Func<AutoTyper, bool> work)
        {
            if (activeTyper != null) { SetStatus("Already typing..."); return; }

            IntPtr target = lastTarget;
            bool haveTarget = target != IntPtr.Zero && NativeMethods.IsWindow(target);
            int delay = settings.StartDelayMs;

            if (settings.CollapseWhenTyping) SetCollapsed(true);
            if (haveTarget)
            {
                if (NativeMethods.IsIconic(target)) NativeMethods.ShowWindow(target, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(target);
                SetStatus("Typing in " + delay + " ms...  (Esc cancels)");
            }
            else
            {
                SetStatus("Click into the target window - typing in " + delay + " ms...");
            }

            RunTyper(delay, 0, work);
        }

        /// <summary>Ctrl+Alt+V: type the newest entry into the window that currently has focus.</summary>
        private void OnHotkey()
        {
            if (list.Items.Count == 0) { SetStatus("Hotkey: history is empty."); return; }
            if (activeTyper != null) return;
            if (IsOwnWindow(NativeMethods.GetForegroundWindow()))
            {
                SetStatus("Hotkey: focus the target window first, then press Ctrl+Alt+V.");
                return;
            }
            string text = ((HistoryItem)list.Items[0]).Text;
            SetStatus("Typing newest entry...  (Esc cancels)");
            RunTyper(0, (int)Keys.V, delegate(AutoTyper t) { return t.TypeText(text); });
        }

        private void RunTyper(int startDelayMs, int releaseVk, Func<AutoTyper, bool> work)
        {
            // Created now (not after the delay) so Esc and closing the app cancel at any point.
            AutoTyper typer = new AutoTyper(settings.KeyDelayMs);
            activeTyper = typer;

            Thread thread = new Thread(delegate()
            {
                string result;
                try
                {
                    IntPtr fg;
                    if (!typer.Wait(startDelayMs) || !typer.WaitForKeysReleased(5000, releaseVk))
                    {
                        result = typer.StopReason ?? "Cancelled";
                    }
                    else if ((fg = NativeMethods.GetForegroundWindow()) == IntPtr.Zero || IsOwnWindow(fg))
                    {
                        result = "Nothing typed: no target window has focus.";
                    }
                    else
                    {
                        typer.Attach(fg);
                        bool ok = work(typer);
                        string title = NativeMethods.GetTitleOf(fg);
                        result = ok ? "Done: typed into " + (title.Length > 0 ? title : "window") + "."
                                    : (typer.StopReason ?? "Stopped.");
                    }
                }
                catch (Exception ex)
                {
                    result = "Error: " + ex.Message;
                }
                activeTyper = null;
                SetStatus(result);
            });
            thread.IsBackground = true;
            typerThread = thread;
            thread.Start();
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetStatus), text); } catch (InvalidOperationException) { }
                return;
            }
            statusLabel.Text = text;
        }
    }
}
