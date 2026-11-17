using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KebuzLoncher
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
            {
                LauncherData.WriteCrash(e.Exception);
                MessageBox.Show(ModMessages.Get("UnexpectedError", e.Exception.Message), ModMessages.Get("StartupErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception error) LauncherData.WriteCrash(error);
            };
            try { Application.Run(new Form1()); }
            catch (Exception error)
            {
                LauncherData.WriteCrash(error);
                MessageBox.Show(ModMessages.Get("StartupFailed", error.Message), ModMessages.Get("StartupErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
