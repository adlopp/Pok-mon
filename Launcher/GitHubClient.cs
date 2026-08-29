using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UltraYeaLauncher
{
    internal sealed class GitHubClient : IDisposable
    {
        private readonly HttpClient _http;

        public GitHubClient()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("UltraYeaLauncher/1.0");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        /// <summary>HttpClient compartido para que Updater reutilice la misma conexión.</summary>
        public HttpClient Raw => _http;

        public async Task<GhRelease> GetLatestReleaseAsync(LauncherConfig cfg, CancellationToken ct)
        {
            string baseUrl = $"https://api.github.com/repos/{cfg.RepoOwner}/{cfg.RepoName}/releases";

            try
            {
                if (cfg.IncludePrereleases)
                {
                    List<GhRelease>? list = await _http
                        .GetFromJsonAsync<List<GhRelease>>(baseUrl + "?per_page=15", ct)
                        .ConfigureAwait(false);

                    GhRelease? pick = list?.FirstOrDefault(r => !r.Draft);
                    return pick ?? throw new InvalidOperationException(
                        "El repositorio todavía no tiene ninguna Release publicada.");
                }

                GhRelease? latest = await _http
                    .GetFromJsonAsync<GhRelease>(baseUrl + "/latest", ct)
                    .ConfigureAwait(false);

                return latest ?? throw new InvalidOperationException(
                    "No hay ninguna Release marcada como 'latest' en el repositorio.");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(
                    "El repositorio no existe, es privado, o aún no tiene ninguna Release publicada.", ex);
            }
        }

        public async Task<UpdateManifest?> TryGetManifestAsync(GhRelease rel, string manifestAssetName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(manifestAssetName)) return null;

            GhAsset? asset = rel.Assets.FirstOrDefault(
                a => a.Name.Equals(manifestAssetName, StringComparison.OrdinalIgnoreCase));
            if (asset == null) return null;

            try
            {
                string json = await _http.GetStringAsync(asset.Url, ct).ConfigureAwait(false);
                json = json.TrimStart('﻿', '​').Trim(); // por si el archivo llega con BOM

                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                };
                return JsonSerializer.Deserialize<UpdateManifest>(json, opts);
            }
            catch (Exception ex)
            {
                Log.Exception("descarga de manifest.json", ex);
                return null; // seguimos con los datos de la propia Release
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
