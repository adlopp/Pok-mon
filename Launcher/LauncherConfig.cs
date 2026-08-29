using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraYeaLauncher
{
    /// <summary>
    /// Se lee de <c>launcher_config.json</c>, situado junto a Launcher.exe.
    /// Admite comentarios <c>//</c> y comas finales.
    /// </summary>
    internal sealed class LauncherConfig
    {
        [JsonPropertyName("repoOwner")] public string RepoOwner { get; set; } = "";
        [JsonPropertyName("repoName")] public string RepoName { get; set; } = "";
        [JsonPropertyName("gameExe")] public string GameExe { get; set; } = "Game.exe";
        [JsonPropertyName("assetPattern")] public string AssetPattern { get; set; } = "*.zip";
        [JsonPropertyName("manifestAsset")] public string ManifestAsset { get; set; } = "manifest.json";
        [JsonPropertyName("includePrereleases")] public bool IncludePrereleases { get; set; }
        [JsonPropertyName("autoLaunchWhenUpToDate")] public bool AutoLaunchWhenUpToDate { get; set; } = true;
        [JsonPropertyName("allowSkipUpdate")] public bool AllowSkipUpdate { get; set; } = true;

        public static string Dir => AppContext.BaseDirectory;
        public static string ConfigPath => Path.Combine(Dir, "launcher_config.json");
        public static string VersionPath => Path.Combine(Dir, "version.txt");

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static LauncherConfig Load()
        {
            if (!File.Exists(ConfigPath))
                throw new FileNotFoundException(
                    "No se encontró launcher_config.json junto al launcher:" + Environment.NewLine + ConfigPath);

            LauncherConfig? cfg;
            try
            {
                cfg = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath), Options);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("launcher_config.json tiene un error de formato: " + ex.Message, ex);
            }

            if (cfg == null)
                throw new InvalidDataException("launcher_config.json está vacío.");
            if (string.IsNullOrWhiteSpace(cfg.RepoOwner) || string.IsNullOrWhiteSpace(cfg.RepoName))
                throw new InvalidDataException(
                    "launcher_config.json: faltan \"repoOwner\" y/o \"repoName\" (usuario y repositorio de GitHub).");

            return cfg;
        }

        public string ReadLocalVersion()
        {
            try
            {
                return File.Exists(VersionPath)
                    ? File.ReadAllText(VersionPath).Trim()
                    : "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }

        public void WriteLocalVersion(string version)
            => File.WriteAllText(VersionPath, (version ?? "").Trim());
    }
}
