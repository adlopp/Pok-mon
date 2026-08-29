using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UltraYeaLauncher
{
    // ---- Subconjunto de la respuesta "release" de la API de GitHub ----

    internal sealed class GhRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = new List<GhAsset>();
    }

    internal sealed class GhAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    // ---- manifest.json opcional (lo genera tools/build_release.ps1) ----

    internal sealed class UpdateManifest
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("released")] public string? Released { get; set; }
        [JsonPropertyName("mandatory")] public bool Mandatory { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("package")] public PackageInfo Package { get; set; } = new PackageInfo();

        /// <summary>Rutas relativas (respecto a la carpeta del juego) que hay que borrar al actualizar.</summary>
        [JsonPropertyName("delete")] public List<string> Delete { get; set; } = new List<string>();
    }

    internal sealed class PackageInfo
    {
        /// <summary>Nombre del asset .zip en la Release.</summary>
        [JsonPropertyName("asset")] public string Asset { get; set; } = "";
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    // ---- Plan de actualización ya resuelto (lo consume Updater) ----

    internal sealed class UpdatePlan
    {
        public string Version = "";
        public string Notes = "";
        public bool Mandatory;
        public string DownloadUrl = "";
        public long Size;
        public string? Sha256;
        public IReadOnlyList<string> Delete = Array.Empty<string>();
    }
}
