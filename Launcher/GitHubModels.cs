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

        /// <summary>Paquete diferencial: solo los archivos que cambiaron respecto a <c>delta.fromVersion</c>.</summary>
        [JsonPropertyName("delta")] public DeltaInfo? Delta { get; set; }

        /// <summary>Asset con el hash SHA-256 y el tamaño de cada archivo de esta versión (files.json).</summary>
        [JsonPropertyName("files")] public AssetRef? Files { get; set; }
    }

    internal sealed class PackageInfo
    {
        /// <summary>Nombre del asset .zip en la Release.</summary>
        [JsonPropertyName("asset")] public string Asset { get; set; } = "";
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    internal sealed class DeltaInfo
    {
        /// <summary>Versión instalada para la que sirve este delta. Si no coincide, se baja el zip completo.</summary>
        [JsonPropertyName("fromVersion")] public string FromVersion { get; set; } = "";
        [JsonPropertyName("asset")] public string Asset { get; set; } = "";
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    internal sealed class AssetRef
    {
        [JsonPropertyName("asset")] public string Asset { get; set; } = "";
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    // ---- files.json : hash + tamaño de cada archivo de una versión ----

    internal sealed class FilesManifest
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("generated")] public string? Generated { get; set; }

        /// <summary>Ruta relativa (con '/') -&gt; hash y tamaño.</summary>
        [JsonPropertyName("files")] public Dictionary<string, FileEntry> Files { get; set; } =
            new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rutas que existían en la versión anterior y ya no.</summary>
        [JsonPropertyName("deleted")] public List<string> Deleted { get; set; } = new List<string>();
    }

    internal sealed class FileEntry
    {
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
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

        // ---- diferencial (opcional): solo se usa si DeltaFromVersion == versión local ----
        public string? DeltaFromVersion;
        public string? DeltaUrl;
        public long DeltaSize;
        public string? DeltaSha256;
        public string? FilesUrl;
        public string? FilesSha256;

        public bool HasDelta =>
            !string.IsNullOrWhiteSpace(DeltaUrl) &&
            !string.IsNullOrWhiteSpace(FilesUrl) &&
            !string.IsNullOrWhiteSpace(DeltaFromVersion);
    }
}
