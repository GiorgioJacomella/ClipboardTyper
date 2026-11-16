# ClipboardTyper
> **Why this exists:** I had to install and update software on a customer's server. On some of the
> access systems, copy and paste was blocked. To get files across, I switched to SwissTransfer and
> WeTransfer, but then the download links were too long to type by hand. To get around all this
> hassle, I built this tool. Next goals: file transfers and more.

A small clipboard history with **autotype**. Text you copy is typed as real
keystrokes, so it works in remote-access tools (TeamViewer, RDP, VNC consoles, VM consoles, …)
that block Ctrl+V.

**No installation & no signup:** copy `ClipboardTyper.exe` anywhere and run it. It needs only the .NET Framework 4.x
that is built into Windows 10/11. Nothing is written to disk, and the history lives only in memory.

## Usage

ClipboardTyper opens as a small panel in the bottom-right corner of the screen, similar to the
TeamViewer session panel. Click **›** in its title bar (or press **Esc**) to collapse it to a narrow
tab on the right edge that shows the number of entries. Click the tab to open the panel again.
Starting `ClipboardTyper.exe` again also opens it. To quit, click **✕**. **Options → Help** opens a
short guide with a link to this repository.

| Action | How |
|---|---|
| Add to history | Copy text anywhere (Ctrl+C). Duplicates move to the top. Up to 100 entries. |
| Copy an entry again | **Double-click** it (or select it and press Enter). |
| Autotype an entry | **Right-click → Autotype** or **Autotype + Enter**. |
| Autotype the newest entry | Click into the target window and press **Ctrl+Alt+V**. |
| Send special keys | Open the **Send** menu (or press Alt+S) and click a key combination (Ctrl+Alt+Del, Ctrl+Alt+End, Ctrl+A, Enter, …). |
| Cancel typing | Press **Esc** (also during the start delay), click into another window, or close the app. |
| Delete an entry | Right-click → Delete (or the Del key). |
| Clear the history | **Clear all** button above the list. |

How autotype from the window works: ClipboardTyper remembers the last window you used, shown as
*Target* at the bottom. It switches back to that window, waits for the **Start delay**, and then
types. During the delay you can also click into a different window yourself. While it types, you
can open or use the panel without interrupting it; the target window keeps the focus.

### Settings

Open **Options → Settings**. Changes apply immediately and last until you close the app.

- **Start delay (ms)**: the wait before typing starts (default 1500).
- **Key delay (ms)**: the pause between keystrokes (default 20). Increase it if the remote tool
  drops or scrambles characters.
- **Collapse the panel when typing starts** (off by default).
- **Keep the panel always on top** (on by default). The collapsed tab always stays on top.
- **Pause capture**: stops adding copied text to the history.

## Notes and limitations

- **Ctrl+Alt+Del**: Windows never treats simulated input as a real Ctrl+Alt+Del on your own PC, so
  sending it is safe. Whether the *remote* side receives it depends on the tool. For RDP, use
  **Ctrl+Alt+End**. TeamViewer and similar tools also have their own "Send Ctrl+Alt+Del" menu.
- **Keyboard layout**: characters are typed using your local keyboard layout (Swiss German, for
  example), so the remote machine should use the same layout. Characters that don't exist on your layout (such as `Ä` on Swiss German, or `✓`) are sent as
  Unicode, which works in RDP and TeamViewer but may not work in every VNC tool.
- **Admin windows**: if the remote tool runs as administrator, Windows blocks input from normal
  programs. In that case, start ClipboardTyper with *Run as administrator* as well.
- Text that password managers mark as private is not captured. Use the password manager's own
  autotype for those entries.
- Only one copy of ClipboardTyper runs at a time. Starting it again brings the existing window to the front.

## Possible future features (untested)

- **Windows-key and window-switching combos** in the Send menu (Win, Win+R, Win+E, Win+D, Ctrl+Esc,
  Alt+Tab, Alt+F4, Ctrl+Shift+Esc). They were removed for now because, depending on the remote tool,
  they may act on the local PC instead of the remote one. They need testing in RDP, TeamViewer and VNC.
- **Ctrl+Alt+V inside RDP or TeamViewer in full screen**: these tools capture the keyboard, so the
  hotkey may not fire while they have focus. This needs testing; a low-level keyboard hook could fix it.
  Until then, use Autotype from the panel.
- **File transfers** to remote machines where copy and paste is blocked.

## Building

Run `build.cmd`. It uses the C# compiler that ships with Windows
(`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and produces `ClipboardTyper.exe`.
No SDK or NuGet is needed.

Source code is in `src/`:

- `Program.cs`: entry point and single-instance check
- `MainForm.cs`: panel, clipboard history, target window and hotkey
- `AutoTyper.cs`: keystroke engine
- `SpecialKeys.cs`: entries of the Send menu
- `SettingsForm.cs`: settings window
- `HelpForm.cs`: help window
- `NativeMethods.cs`: Win32 calls
