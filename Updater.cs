using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public class Release
    {
        [JsonProperty("tag_name")] public string? TagName { get; set; }
        [JsonProperty("html_url")] public string? HtmlUrl { get; set; }
        [JsonProperty("body")] public string? Body { get; set; }
    }

    class Updater
    {
        private static readonly ILogger<Updater> _log = AppLogger.For<Updater>();

        static readonly string CurrentReleaseTag = "v16";
        static readonly HttpClient client;

        static Updater()
        {
            client = new HttpClient();
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.DefaultRequestHeaders.Add("User-Agent", "Circle-Tracker");
        }

        public static async Task CheckForUpdates()
        {
            Release? latestRelease = null;
            try
            {
                var response = await client.GetAsync("/repos/FunOrange/circle-tracker/releases/latest");
                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    latestRelease = JsonConvert.DeserializeObject<Release>(responseJson);
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "Update check failed. You have version {CurrentReleaseTag}", CurrentReleaseTag);
                return;
            }

            if (latestRelease == null) return;
            if (latestRelease.TagName == CurrentReleaseTag) return;

            _log.LogInformation("Update available: {TagName}\nRelease notes: {Body}\nDownload: {HtmlUrl}",
                latestRelease.TagName, latestRelease.Body, latestRelease.HtmlUrl);
        }
    }
}

