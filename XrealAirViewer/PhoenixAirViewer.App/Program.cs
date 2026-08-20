using System;
using System.Windows.Forms;

namespace PhoenixAirViewer.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool diagnosticMode = false;
            for (int index = 0; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--diagnostic", StringComparison.OrdinalIgnoreCase))
                {
                    diagnosticMode = true;
                }
            }

            Application.Run(new MainForm(diagnosticMode));
        }
    }
}
