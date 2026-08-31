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
                Console.Error.WriteLine(
                    $"Update check failed. You have version {CurrentReleaseTag}. " +
                    $"Check https://github.com/FunOrange/circle-tracker/releases/latest\n{e.Message}");
                return;
            }

            if (latestRelease == null) return;
            if (latestRelease.TagName == CurrentReleaseTag) return;

            Console.WriteLine(
                $"Update available: {latestRelease.TagName}\n" +
                $"Release notes: {latestRelease.Body}\n" +
                $"Download: {latestRelease.HtmlUrl}");
        }
    }
}

