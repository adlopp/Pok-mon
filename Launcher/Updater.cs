using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
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

                var plan = new UpdatePlan
                {
                    Version = string.IsNullOrWhiteSpace(m.Version) ? VersionUtil.Normalize(rel.TagName) : m.Version.Trim(),
                    Notes = m.Notes ?? rel.Body ?? "",
                    Mandatory = m.Mandatory,
                    DownloadUrl = asset.Url,
                    Size = m.Package.Size > 0 ? m.Package.Size : asset.Size,
                    Sha256 = m.Package.Sha256,
                    Delete = m.Delete ?? (IReadOnlyList<string>)Array.Empty<string>(),
                };

                // Diferencial: necesita el delta zip Y el files.json en la Release.
                if (m.Delta != null && !string.IsNullOrWhiteSpace(m.Delta.Asset) &&
                    m.Files != null && !string.IsNullOrWhiteSpace(m.Files.Asset))
                {
                    GhAsset? deltaAsset = rel.Assets.FirstOrDefault(
                        a => a.Name.Equals(m.Delta.Asset, StringComparison.OrdinalIgnoreCase));
                    GhAsset? filesAsset = rel.Assets.FirstOrDefault(
                        a => a.Name.Equals(m.Files.Asset, StringComparison.OrdinalIgnoreCase));

                    if (deltaAsset != null && filesAsset != null)
                    {
                        plan.DeltaFromVersion = VersionUtil.Normalize(m.Delta.FromVersion);
                        plan.DeltaUrl = deltaAsset.Url;
                        plan.DeltaSize = m.Delta.Size > 0 ? m.Delta.Size : deltaAsset.Size;
                        plan.DeltaSha256 = m.Delta.Sha256;
                        plan.FilesUrl = filesAsset.Url;
                        plan.FilesSha256 = m.Files.Sha256;
                    }
                }

                return plan;
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

        public async Task RunAsync(UpdatePlan plan, string localVersion, IProgress<Progress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(_workDir);

            // ----- ¿podemos hacer una actualización diferencial? -----
            // El delta se genera siempre DESDE una versión base (delta.fromVersion)
            // hasta la actual, acumulando todos los cambios. Por eso sirve para
            // cualquier jugador que tenga una versión IGUAL O POSTERIOR a esa base
            // (no solo exactamente la base): los archivos anteriores a la base ya
            // están en su disco tal y como los espera files.json.
            bool deltaApplies =
                plan.HasDelta &&
                VersionUtil.Compare(localVersion, plan.DeltaFromVersion!) >= 0;

            if (deltaApplies)
            {
                try
                {
                    await RunDeltaAsync(plan, progress, ct).ConfigureAwait(false);
                    try { Directory.Delete(_workDir, true); } catch { /* limpieza best-effort */ }
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (DeltaFallbackException ex)
                {
                    Log.Write("Actualización diferencial descartada (" + ex.Message + "); se descarga el paquete completo.");
                    progress.Report(new Progress("Descargando el paquete completo…", 0, 0));
                }
            }

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

        /// <summary>Archivos que NO se sobrescriben: los gestiona el propio launcher / el jugador.</summary>
        private static readonly HashSet<string> KeepFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "version.txt",
            "launcher_config.json",
            "launcher.log",
        };

        private static bool IsLauncherExe(string rel, string selfName)
            => Path.GetFileName(rel).Equals("Launcher.exe", StringComparison.OrdinalIgnoreCase) ||
               (selfName.Length > 0 && Path.GetFileName(rel).Equals(selfName, StringComparison.OrdinalIgnoreCase));

        private void Apply(string stagedRoot, UpdatePlan plan)
        {
            string selfPath = Environment.ProcessPath ?? "";
            string selfName = Path.GetFileName(selfPath);
            int copied = 0, skipped = 0;

            foreach (string srcFile in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(stagedRoot, srcFile);
                ApplyOneFile(srcFile, rel, selfPath, selfName, ref copied, ref skipped);
            }

            ProcessDeletes(plan.Delete, selfName);
            Log.Write($"Aplicada actualización: {copied} archivo(s) actualizado(s), {skipped} sin cambios.");
        }

        /// <summary>Copia un archivo del paquete a la carpeta del juego (o lo enruta a la auto-actualización).</summary>
        private void ApplyOneFile(string srcFile, string rel, string selfPath, string selfName, ref int copied, ref int skipped)
        {
            if (KeepFiles.Contains(rel)) { skipped++; return; }

            string dstFile = Path.Combine(_gameDir, rel);

            bool isSelf =
                IsLauncherExe(rel, selfName) ||
                (selfPath.Length > 0 &&
                 Path.GetFullPath(dstFile).Equals(Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase));

            if (isSelf)
            {
                SelfUpdate(srcFile, selfPath);
                return;
            }

            if (File.Exists(dstFile) && SameFile(srcFile, dstFile)) { skipped++; return; }

            Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
            CopyOverwrite(srcFile, dstFile);
            copied++;
        }

        /// <summary>Quita "solo lectura"/oculto/sistema del destino: <see cref="File.Copy(string,string,bool)"/>
        /// falla con "Access denied" si el archivo a sobrescribir tiene alguno de esos atributos.</summary>
        private static void ClearBlockingAttributes(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                FileAttributes a = File.GetAttributes(path);
                const FileAttributes bad = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
                if ((a & bad) != 0) File.SetAttributes(path, a & ~bad);
            }
            catch { /* best effort */ }
        }

        /// <summary>Copia sobrescribiendo, quitando atributos que bloquean y reintentando
        /// un par de veces por si otro proceso tiene el archivo abierto un instante.</summary>
        private static void CopyOverwrite(string src, string dst)
        {
            for (int attempt = 1; ; attempt++)
            {
                ClearBlockingAttributes(dst);
                try
                {
                    File.Copy(src, dst, overwrite: true);
                    return;
                }
                catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is IOException) && attempt < 3)
                {
                    Thread.Sleep(300);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new IOException(
                        "No se pudo escribir:" + Environment.NewLine + dst + Environment.NewLine + Environment.NewLine +
                        "Cierra cualquier programa que tenga ese archivo abierto (Bloc de notas, etc.), " +
                        "comprueba que no está marcado como \"solo lectura\" y vuelve a intentarlo. " +
                        "Si el problema persiste, borra ese archivo a mano y reintenta.", ex);
                }
            }
        }

        private void ProcessDeletes(IEnumerable<string>? relPaths, string selfName)
        {
            foreach (string relPath in relPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(relPath)) continue;

                string full = Path.GetFullPath(Path.Combine(_gameDir, relPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(_gameDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue; // impide rutas que se salgan de la carpeta del juego
                if (Path.GetFileName(full).Equals(selfName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (KeepFiles.Contains(Path.GetRelativePath(_gameDir, full)))
                    continue;

                try
                {
                    if (File.Exists(full))
                    {
                        ClearBlockingAttributes(full);
                        File.Delete(full);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("borrado de " + relPath, ex);
                }
            }
        }

        // ----------------------------------------------------- actualización delta

        private sealed class DeltaFallbackException : Exception
        {
            public DeltaFallbackException(string message) : base(message) { }
        }

        private const string FilesManifestName = "files.json";

        private async Task RunDeltaAsync(UpdatePlan plan, IProgress<Progress> progress, CancellationToken ct)
        {
            string deltaZip = Path.Combine(_workDir, "delta.zip");
            string staged = Path.Combine(_workDir, "delta");

            // 1) delta zip : solo los archivos que cambiaron desde la versión instalada
            //    (lleva files.json embebido en la raíz).
            progress.Report(new Progress("Descargando los cambios…", 0, plan.DeltaSize));
            try
            {
                await DownloadAsync(plan.DeltaUrl!, deltaZip, plan.DeltaSize, progress, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DeltaFallbackException("no se pudo descargar el delta: " + ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(plan.DeltaSha256))
            {
                progress.Report(new Progress("Verificando la descarga…", 0, 0));
                string got = await Sha256HexAsync(deltaZip, ct).ConfigureAwait(false);
                if (!got.Equals(plan.DeltaSha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new DeltaFallbackException("el SHA-256 del delta no coincide");
            }

            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            Directory.CreateDirectory(staged);

            progress.Report(new Progress("Extrayendo los cambios…", 0, 0));
            await Task.Run(() => ZipFile.ExtractToDirectory(deltaZip, staged, overwriteFiles: true), ct).ConfigureAwait(false);
            string root = ResolveStagedRoot(staged);

            // 2) files.json : hash + tamaño de cada archivo de la versión nueva.
            //    Normalmente viene dentro del delta; si no, se descarga aparte.
            FilesManifest fm = await LoadFilesManifestAsync(root, plan, ct).ConfigureAwait(false);

            progress.Report(new Progress("Aplicando la actualización…", 0, 0));
            await Task.Run(() => ApplyDelta(root, fm, plan, progress), ct).ConfigureAwait(false);
        }

        private async Task<FilesManifest> LoadFilesManifestAsync(string stagedRoot, UpdatePlan plan, CancellationToken ct)
        {
            string embedded = Path.Combine(stagedRoot, FilesManifestName);
            string path;

            if (File.Exists(embedded))
            {
                path = embedded;
            }
            else
            {
                path = Path.Combine(_workDir, "files.json");
                try
                {
                    using HttpResponseMessage resp = await _http.GetAsync(plan.FilesUrl!, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    await using (Stream s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        await s.CopyToAsync(f, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new DeltaFallbackException("no se pudo obtener files.json: " + ex.Message);
                }
            }

            if (!string.IsNullOrWhiteSpace(plan.FilesSha256))
            {
                string got = await Sha256HexAsync(path, ct).ConfigureAwait(false);
                if (!got.Equals(plan.FilesSha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new DeltaFallbackException("el SHA-256 de files.json no coincide");
            }

            try
            {
                string json = (await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)).TrimStart('﻿', '​').Trim();
                FilesManifest? fm = JsonSerializer.Deserialize<FilesManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                });
                if (fm == null || fm.Files.Count == 0)
                    throw new InvalidDataException("files.json vacío o sin lista de archivos");
                return fm;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DeltaFallbackException("files.json ilegible: " + ex.Message);
            }
        }

        private void ApplyDelta(string stagedRoot, FilesManifest fm, UpdatePlan plan, IProgress<Progress> progress)
        {
            string selfPath = Environment.ProcessPath ?? "";
            string selfName = Path.GetFileName(selfPath);
            int copied = 0, skipped = 0;

            // A) copiar lo que trae el delta zip (menos el propio files.json embebido)
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string srcFile in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
            {
                string rel = NormalizeRel(Path.GetRelativePath(stagedRoot, srcFile));
                if (rel.Equals(FilesManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                touched.Add(rel);
                ApplyOneFile(srcFile, rel.Replace('/', Path.DirectorySeparatorChar), selfPath, selfName, ref copied, ref skipped);
            }

            // B) comprobar que la instalación queda íntegra. Los archivos que trae el
            //    delta se verifican por hash (que se hayan escrito bien); el resto solo
            //    por existencia y tamaño (confiamos en la copia byte a byte de la versión
            //    instalada, igual que la actualización completa). Si algo falla ->
            //    se descarta el delta y se baja el zip completo.
            int chec0 = 0, checN = fm.Files.Count;
            progress.Report(new Progress("Comprobando los archivos…", 0, checN));
            foreach (KeyValuePair<string, FileEntry> kv in fm.Files)
            {
                string rel = NormalizeRel(kv.Key);
                chec0++;
                if ((chec0 & 1023) == 0)
                    progress.Report(new Progress("Comprobando los archivos…", chec0, checN));

                if (KeepFiles.Contains(rel)) continue;                 // los gestiona el jugador
                if (IsLauncherExe(rel, selfName)) continue;            // se auto-actualiza aparte

                string dst = Path.Combine(_gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(dst))
                    throw new DeltaFallbackException("falta " + rel);

                var fi = new FileInfo(dst);
                if (fi.Length != kv.Value.Size)
                    throw new DeltaFallbackException("tamaño distinto en " + rel);

                if (touched.Contains(rel) &&
                    !string.IsNullOrEmpty(kv.Value.Sha256) &&
                    !Sha256Hex(dst).Equals(kv.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new DeltaFallbackException("hash distinto en " + rel);
            }

            // C) borrados: lo que ya no existe en la versión nueva + lo que pida el manifest
            var toDelete = new List<string>();
            if (fm.Deleted != null) toDelete.AddRange(fm.Deleted);
            if (plan.Delete != null) toDelete.AddRange(plan.Delete);
            ProcessDeletes(toDelete, selfName);

            Log.Write($"Aplicada actualización diferencial: {copied} archivo(s) actualizado(s), {skipped} sin cambios, {checN} verificado(s).");
        }

        private static string NormalizeRel(string rel)
            => rel.Replace('\\', '/').TrimStart('/');

        private static void SelfUpdate(string newExe, string selfPath)
        {
            if (string.IsNullOrEmpty(selfPath))
            {
                // No sabemos con seguridad cuál es el exe en ejecución: mejor no tocarlo.
                Log.Write("Se omite la auto-actualización del launcher (ruta propia desconocida).");
                return;
            }

            try
            {
                if (File.Exists(selfPath) && SameFile(newExe, selfPath)) return;   // ya está al día

                // NO tocamos el .exe en ejecución: como es "single-file", si lo
                // renombramos, cualquier lectura posterior de su propio contenido
                // (JIT diferido) revienta con FileNotFoundException. En su lugar
                // dejamos Launcher.exe.new y Program.Main lo intercambia al arrancar,
                // que es el único momento sin código pendiente por leer del bundle.
                string pending = selfPath + ".new";
                ClearBlockingAttributes(pending);
                File.Copy(newExe, pending, overwrite: true);
                Log.Write("Launcher nuevo preparado; se aplicará la próxima vez que abras el launcher.");
            }
            catch (Exception ex)
            {
                Log.Exception("preparar la auto-actualización del launcher", ex);
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
