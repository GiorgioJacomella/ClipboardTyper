namespace ClipboardTyper
{
    internal sealed class SpecialKey
    {
        public readonly string Name;
        public readonly string Hint;   // shown in gray on the right of the menu item
        public readonly ushort[] Keys;

        public SpecialKey(string name, string hint, params ushort[] keys)
        {
            Name = name;
            Hint = hint;
            Keys = keys;
        }

        public override string ToString() { return Name; }

        private const ushort CTRL = AutoTyper.VK_CONTROL, ALT = AutoTyper.VK_MENU,
            SHIFT = AutoTyper.VK_SHIFT;

        /// <summary>Entries of the Send menu; null marks a separator line.</summary>
        // Windows-key and window-switching combos (Win+R, Alt+Tab, Alt+F4, Ctrl+Shift+Esc, ...) are
        // left out until tested: depending on the remote tool they may act on the local PC.
        // See "Possible future features" in the README.
        public static readonly SpecialKey[] All = new SpecialKey[]
        {
            new SpecialKey("Ctrl+Alt+Del", null, CTRL, ALT, AutoTyper.VK_DELETE),
            new SpecialKey("Ctrl+Alt+End", "Ctrl+Alt+Del in RDP", CTRL, ALT, AutoTyper.VK_END),
            null,
            new SpecialKey("Ctrl+A", "Select all", CTRL, (ushort)'A'),
            new SpecialKey("Ctrl+C", "Copy", CTRL, (ushort)'C'),
            new SpecialKey("Ctrl+V", "Paste", CTRL, (ushort)'V'),
            new SpecialKey("Ctrl+X", "Cut", CTRL, (ushort)'X'),
            new SpecialKey("Ctrl+Z", "Undo", CTRL, (ushort)'Z'),
            null,
            new SpecialKey("Enter", null, AutoTyper.VK_RETURN),
            new SpecialKey("Tab", null, AutoTyper.VK_TAB),
            new SpecialKey("Shift+Tab", null, SHIFT, AutoTyper.VK_TAB),
            new SpecialKey("Esc", null, AutoTyper.VK_ESCAPE),
            new SpecialKey("Backspace", null, AutoTyper.VK_BACK),
            new SpecialKey("Delete", null, AutoTyper.VK_DELETE),
            new SpecialKey("F5", "Refresh", AutoTyper.VK_F5),
        };
    }
}
