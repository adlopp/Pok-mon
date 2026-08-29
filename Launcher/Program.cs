using System;
using System.IO;
using System.Windows.Forms;

namespace UltraYeaLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Si una actualización anterior dejó una copia vieja del launcher, bórrala ahora
            // (no se pudo borrar mientras el proceso anterior corría).
            try
            {
                string? self = Environment.ProcessPath;
                if (self != null && File.Exists(self + ".old"))
                    File.Delete(self + ".old");
            }
            catch
            {
                // sin importancia
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
