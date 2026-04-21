using NLog;
using System;
using System.Windows.Forms;
using HIDFader.Core;
using HIDFader.UI;

namespace HIDFader
{
    static class Program
    {
        private static readonly Logger log = LogManager.GetCurrentClassLogger();

        [MTAThread]
        private static void Main()
        {
            Log.Initialize();

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new frmApplicationSelector());
            }
            catch (Exception ex)
            {
                log.Error(ex, "Unhandled exception at application root");
                throw;
            }
            finally
            {
                Log.Shutdown();
            }
        }
    }
}