using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public class Release
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
    }

    public class Updater
    {
        private static readonly ILogger<Updater> _log = AppLogger.For<Updater>();

        public const string DefaultRepository = "lacedawn/osu-tracker-linux";
        private static readonly HttpClient client;

        public static Version CurrentVersion =>
            Assembly.GetEntryAssembly()?.GetName().Version ??
            Assembly.GetExecutingAssembly().GetName().Version ??
            new Version(1, 0, 0);

        public static string CurrentReleaseTag => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

        static Updater()
        {
            client = new HttpClient();
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.DefaultRequestHeaders.Add("User-Agent", "Circle-Tracker");
        }

        public static Version? ParseSemanticVersion(string? versionStr)
        {
            if (string.IsNullOrWhiteSpace(versionStr))
                return null;

            string cleaned = versionStr.Trim();
            if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[1..].Trim();
            }

            int separatorIdx = cleaned.IndexOfAny(new[] { '-', '+' });
            if (separatorIdx >= 0)
            {
                cleaned = cleaned[..separatorIdx].Trim();
            }

            var parts = cleaned.Split('.');
            int major = 0;
            int minor = 0;
            int patch = 0;

            if (parts.Length >= 1 && int.TryParse(parts[0], out int parsedMajor))
            {
                major = parsedMajor;
            }
            else
            {
                return null;
            }

            if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedMinor))
            {
                minor = parsedMinor;
            }

            if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedPatch))
            {
                patch = parsedPatch;
            }

            return new Version(major, minor, patch);
        }

        public static bool IsUpdateAvailable(string? currentVersion, string? remoteVersion)
        {
            if (string.IsNullOrWhiteSpace(remoteVersion))
                return false;

            if (string.IsNullOrWhiteSpace(currentVersion))
                return true;

            var cur = ParseSemanticVersion(currentVersion);
            var rem = ParseSemanticVersion(remoteVersion);

            if (cur == null || rem == null)
                return false;

            return rem > cur;
        }

        public static bool IsUpdateAvailable(string? remoteVersion)
        {
            return IsUpdateAvailable(CurrentVersion.ToString(), remoteVersion);
        }

        public static async Task<bool> CheckForUpdates(string? repository = null, HttpClient? httpClient = null, CancellationToken ct = default)
        {
            Release? latestRelease = null;
            string repo = repository ?? DefaultRepository;
            var http = httpClient ?? client;

            try
            {
                using var response = await http.GetAsync($"/repos/{repo}/releases/latest", ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    latestRelease = JsonSerializer.Deserialize<Release>(responseJson, options);
                }
                else
                {
                    _log.LogWarning("Update check returned non-success status {StatusCode} for repository {Repository}. Current version: {CurrentVersion}", (int)response.StatusCode, repo, CurrentVersion);
                    return false;
                }
            }
            catch (OperationCanceledException e)
            {
                _log.LogWarning(e, "Update check cancelled for repository {Repository}. Current version: {CurrentVersion}", repo, CurrentVersion);
                return false;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Update check failed for repository {Repository}. Current version: {CurrentVersion}", repo, CurrentVersion);
                return false;
            }

            if (string.IsNullOrEmpty(latestRelease?.TagName))
                return false;

            if (IsUpdateAvailable(CurrentVersion.ToString(), latestRelease.TagName))
            {
                _log.LogInformation("Update available: {TagName}\nRelease notes: {Body}\nDownload: {HtmlUrl}",
                    latestRelease.TagName, latestRelease.Body, latestRelease.HtmlUrl);
                return true;
            }

            return false;
        }
    }
}
