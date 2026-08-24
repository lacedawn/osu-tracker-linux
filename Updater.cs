using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;

namespace Circle_Tracker
{
    public class Author
    {
        public string? login { get; set; }
        public int id { get; set; }
        public string? node_id { get; set; }
        public string? avatar_url { get; set; }
        public string? gravatar_id { get; set; }
        public string? url { get; set; }
        public string? html_url { get; set; }
        public string? followers_url { get; set; }
        public string? following_url { get; set; }
        public string? gists_url { get; set; }
        public string? starred_url { get; set; }
        public string? subscriptions_url { get; set; }
        public string? organizations_url { get; set; }
        public string? repos_url { get; set; }
        public string? events_url { get; set; }
        public string? received_events_url { get; set; }
        public string? type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Uploader
    {
        public string? login { get; set; }
        public int id { get; set; }
        public string? node_id { get; set; }
        public string? avatar_url { get; set; }
        public string? gravatar_id { get; set; }
        public string? url { get; set; }
        public string? html_url { get; set; }
        public string? followers_url { get; set; }
        public string? following_url { get; set; }
        public string? gists_url { get; set; }
        public string? starred_url { get; set; }
        public string? subscriptions_url { get; set; }
        public string? organizations_url { get; set; }
        public string? repos_url { get; set; }
        public string? events_url { get; set; }
        public string? received_events_url { get; set; }
        public string? type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Asset
    {
        public string? url { get; set; }
        public int id { get; set; }
        public string? node_id { get; set; }
        public string? name { get; set; }
        public object? label { get; set; }
        public Uploader? uploader { get; set; }
        public string? content_type { get; set; }
        public string? state { get; set; }
        public int size { get; set; }
        public int download_count { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public string? browser_download_url { get; set; }
    }

    public class Release
    {
        public string? url { get; set; }
        public string? assets_url { get; set; }
        public string? upload_url { get; set; }
        public string? html_url { get; set; }
        public int id { get; set; }
        public Author? author { get; set; }
        public string? node_id { get; set; }
        public string? tag_name { get; set; }
        public string? target_commitish { get; set; }
        public string? name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public DateTime created_at { get; set; }
        public DateTime published_at { get; set; }
        public List<Asset>? assets { get; set; }
        public string? tarball_url { get; set; }
        public string? zipball_url { get; set; }
        public string? body { get; set; }
    }

    class Updater
    {
        static readonly string CURRENT_RELEASE_TAG = "v17-lazer-linux";
        static readonly HttpClient client;

        static Updater()
        {
            client = new HttpClient();
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.DefaultRequestHeaders.Add("User-Agent", "Circle-Tracker");
        }

        public static async void CheckForUpdates()
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
                    $"Update check failed. You have version {CURRENT_RELEASE_TAG}. " +
                    $"Check https://github.com/FunOrange/circle-tracker/releases/latest\n{e.Message}");
                return;
            }

            if (latestRelease == null) return;
            if (latestRelease.tag_name == CURRENT_RELEASE_TAG) return;

            Console.WriteLine(
                $"Update available: {latestRelease.tag_name}\n" +
                $"Release notes: {latestRelease.body}\n" +
                $"Download: {latestRelease.html_url}");

            try
            {
                Process.Start(new ProcessStartInfo(latestRelease.html_url ?? "")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
    }
}
