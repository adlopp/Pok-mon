using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace UltraYeaLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Si una actualización dejó preparado un launcher nuevo (Launcher.exe.new),
            // se intercambia AHORA, al principio del proceso, cuando todavía no hay
            // código pendiente de leer del propio .exe. Si se hace más tarde, un .exe
            // "single-file" renombrado revienta con FileNotFoundException.
            try { ApplyPendingLauncherUpdate(); }
            catch (Exception ex) { TryLog("intercambio del launcher nuevo", ex); }

            // Restos de un intercambio anterior.
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

        private static void ApplyPendingLauncherUpdate()
        {
            string? self = Environment.ProcessPath;
            if (string.IsNullOrEmpty(self)) return;

            string pending = self + ".new";
            if (!File.Exists(pending)) return;

            // ¿ya somos ese binario? (intercambio ya hecho) -> solo limpiar.
            if (FilesEqual(pending, self))
            {
                TryDelete(pending);
                return;
            }

            // Forzar la carga de System.Diagnostics.Process con el bundle intacto,
            // para que el relanzamiento tras renombrar el .exe no tenga que leerlo.
            using (Process cur = Process.GetCurrentProcess()) { _ = cur.Id; }

            string old = self + ".old";
            TryDelete(old);
            File.Move(self, old);        // Windows permite renombrar un .exe en ejecución
            File.Move(pending, self);    // el nuevo pasa a ser Launcher.exe

            Process.Start(new ProcessStartInfo(self) { UseShellExecute = true });
            Environment.Exit(0);         // salir YA, sin ejecutar más código del bundle
        }

        private static bool FilesEqual(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
                using FileStream sa = File.OpenRead(a);
                using FileStream sb = File.OpenRead(b);
                return Convert.ToHexString(SHA256.HashData(sa)) == Convert.ToHexString(SHA256.HashData(sb));
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* se reintenta luego */ }
        }

        private static void TryLog(string ctx, Exception ex)
        {
            try { Log.Exception(ctx, ex); } catch { /* nada que hacer */ }
        }
    }
}
