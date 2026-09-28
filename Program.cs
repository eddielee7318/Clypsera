using System;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardTrail
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool headless = args != null && Array.IndexOf(args, "--headless") >= 0;
            bool firstInstance;
            string mutexName = headless ? @"Local\ClipboardTrail.SmokeInstance" : @"Local\ClipboardTrail.SingleInstance";
            using (Mutex mutex = new Mutex(true, mutexName, out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Clypsera 已经在运行，请在系统托盘中找到它。", "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("程序遇到错误：\n" + e.Exception.Message, "Clypsera", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            bool showAtStart = args != null && Array.IndexOf(args, "--show") >= 0;
            Application.Run(new MainForm(showAtStart, headless));
            }
        }
    }
}
