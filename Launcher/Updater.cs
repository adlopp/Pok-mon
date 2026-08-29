using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UltraYeaLauncher
{
    internal sealed class Updater
    {
        public readonly struct Progress
        {
            public Progress(string phase, long done, long total)
            {
                Phase = phase;
                Done = done;
                Total = total;
            }

            public string Phase { get; }
            public long Done { get; }
            public long Total { get; }

            public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
        }

        private readonly HttpClient _http;
        private readonly string _gameDir;
        private readonly string _workDir;

        public Updater(HttpClient http, string gameDir)
        {
            _http = http;
            _gameDir = Path.GetFullPath(gameDir);
            _workDir = Path.Combine(Path.GetTempPath(), "UltraYeaLauncher", "update");
        }

        // ---------------------------------------------------------------- plan

        public static UpdatePlan ResolvePlan(LauncherConfig cfg, GhRelease rel, UpdateManifest? m)
        {
            if (m != null)
            {
                GhAsset? asset = rel.Assets.FirstOrDefault(
                    a => a.Name.Equals(m.Package.Asset, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                    throw new InvalidOperationException(
                        $"manifest.json apunta al asset '{m.Package.Asset}', que no está en la Release.");

                return new UpdatePlan
                {
                    Version = string.IsNullOrWhiteSpace(m.Version) ? VersionUtil.Normalize(rel.TagName) : m.Version.Trim(),
                    Notes = m.Notes ?? rel.Body ?? "",
                    Mandatory = m.Mandatory,
                    DownloadUrl = asset.Url,
                    Size = m.Package.Size > 0 ? m.Package.Size : asset.Size,
                    Sha256 = m.Package.Sha256,
                    Delete = m.Delete ?? (IReadOnlyList<string>)Array.Empty<string>(),
                };
            }

            // Sin manifest: usamos la etiqueta de la Release y el primer .zip que encaje.
            GhAsset? pkg =
                rel.Assets.FirstOrDefault(a => GlobMatch(a.Name, cfg.AssetPattern)) ??
                rel.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (pkg == null)
                throw new InvalidOperationException("La Release no contiene ningún archivo .zip descargable.");

            return new UpdatePlan
            {
                Version = VersionUtil.Normalize(rel.TagName),
                Notes = rel.Body ?? "",
                Mandatory = false,
                DownloadUrl = pkg.Url,
                Size = pkg.Size,
                Sha256 = null,
                Delete = Array.Empty<string>(),
            };
        }

        private static bool GlobMatch(string name, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            string rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase);
        }

        // ------------------------------------------------------------- pre-check

        public void EnsureGameDirWritable()
        {
            try
            {
                string probe = Path.Combine(_gameDir, ".write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "No tengo permiso de escritura en la carpeta del juego:" + Environment.NewLine +
                    _gameDir + Environment.NewLine + Environment.NewLine +
                    "Mueve el juego a una carpeta personal (Escritorio, Descargas, Documentos) " +
                    "y no lo ejecutes desde 'Archivos de programa'.", ex);
            }
        }

        // ------------------------------------------------------------------ run

        public async Task RunAsync(UpdatePlan plan, IProgress<Progress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(_workDir);
            string zip = Path.Combine(_workDir, "package.zip");
            string staged = Path.Combine(_workDir, "staged");

            await DownloadAsync(plan.DownloadUrl, zip, plan.Size, progress, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(plan.Sha256))
            {
                progress.Report(new Progress("Verificando la descarga…", 0, 0));
                string actual = await Sha256HexAsync(zip, ct).ConfigureAwait(false);
                if (!actual.Equals(plan.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "La verificación SHA-256 ha fallado (descarga corrupta)." + Environment.NewLine +
                        "esperado: " + plan.Sha256 + Environment.NewLine +
                        "obtenido: " + actual + Environment.NewLine +
                        "Vuelve a intentarlo.");
            }

            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            Directory.CreateDirectory(staged);

            progress.Report(new Progress("Extrayendo archivos…", 0, 0));
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, staged, overwriteFiles: true), ct).ConfigureAwait(false);

            string root = ResolveStagedRoot(staged);

            progress.Report(new Progress("Aplicando la actualización…", 0, 0));
            await Task.Run(() => Apply(root, plan), ct).ConfigureAwait(false);

            try { Directory.Delete(_workDir, true); } catch { /* limpieza best-effort */ }
        }

        private async Task DownloadAsync(string url, string dest, long expected, IProgress<Progress> progress, CancellationToken ct)
        {
            using HttpResponseMessage resp = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? expected;

            await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);

            byte[] buffer = new byte[1 << 20];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                progress.Report(new Progress("Descargando la actualización…", done, total));
            }
        }

        private static string ResolveStagedRoot(string staged)
        {
            string[] entries = Directory.GetFileSystemEntries(staged);
            if (entries.Length == 1 && Directory.Exists(entries[0]))
                return entries[0]; // el .zip traía una única carpeta raíz
            return staged;
        }

        private void Apply(string stagedRoot, UpdatePlan plan)
        {
            string selfPath = Environment.ProcessPath ?? "";
            string selfName = Path.GetFileName(selfPath);

            // Archivos que NO se sobrescriben: los gestiona el propio launcher / el jugador.
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "version.txt",
                "launcher_config.json",
                "launcher.log",
            };

            int copied = 0, skipped = 0;

            foreach (string srcFile in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(stagedRoot, srcFile);
                if (keep.Contains(rel)) { skipped++; continue; }

                string dstFile = Path.Combine(_gameDir, rel);

                bool isSelf =
                    (selfName.Length > 0 && rel.Equals(selfName, StringComparison.OrdinalIgnoreCase)) ||
                    (selfPath.Length > 0 &&
                     Path.GetFullPath(dstFile).Equals(Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase)) ||
                    // Nunca intentamos sobrescribir directamente un exe de launcher en ejecución.
                    Path.GetFileName(rel).Equals("Launcher.exe", StringComparison.OrdinalIgnoreCase);

                if (isSelf)
                {
                    SelfUpdate(srcFile, selfPath);
                    continue;
                }

                if (File.Exists(dstFile) && SameFile(srcFile, dstFile)) { skipped++; continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                File.Copy(srcFile, dstFile, overwrite: true);
                copied++;
            }

            foreach (string relPath in plan.Delete ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(relPath)) continue;

                string full = Path.GetFullPath(Path.Combine(_gameDir, relPath));
                if (!full.StartsWith(_gameDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue; // impide rutas que se salgan de la carpeta del juego
                if (Path.GetFileName(full).Equals(selfName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (File.Exists(full)) File.Delete(full);
                }
                catch (Exception ex)
                {
                    Log.Exception("borrado de " + relPath, ex);
                }
            }

            Log.Write($"Aplicada actualización: {copied} archivo(s) actualizado(s), {skipped} sin cambios.");
        }

        private static void SelfUpdate(string newExe, string selfPath)
        {
            if (string.IsNullOrEmpty(selfPath) || !File.Exists(selfPath))
            {
                // No sabemos con seguridad cuál es el exe en ejecución: mejor no tocarlo.
                Log.Write("Se omite la auto-actualización del launcher (ruta propia desconocida).");
                return;
            }

            try
            {
                if (SameFile(newExe, selfPath)) return;

                string old = selfPath + ".old";
                try { if (File.Exists(old)) File.Delete(old); } catch { /* se reintenta al arrancar */ }

                File.Move(selfPath, old);          // Windows permite renombrar un .exe en ejecución
                File.Copy(newExe, selfPath);
                Log.Write("Launcher actualizado; la nueva versión se usará la próxima vez que se abra.");
            }
            catch (Exception ex)
            {
                Log.Exception("auto-actualización del launcher", ex);
            }
        }

        // --------------------------------------------------------------- hashing

        private static bool SameFile(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
            return Sha256Hex(a) == Sha256Hex(b);
        }

        private static string Sha256Hex(string path)
        {
            using FileStream s = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }

        private static async Task<string> Sha256HexAsync(string path, CancellationToken ct)
        {
            await using FileStream s = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(s, ct).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
