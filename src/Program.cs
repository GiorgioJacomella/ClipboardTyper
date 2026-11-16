using System;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardTyper
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "ClipboardTyper_SingleInstance_7f3c", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running: open the existing panel and bring it to the front.
                    IntPtr existing = NativeMethods.FindWindow(null, MainForm.AppTitle);
                    if (existing != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(existing);
                        NativeMethods.PostMessage(existing, MainForm.WM_SHOW_PANEL, IntPtr.Zero, IntPtr.Zero);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
