using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TweakWise.Managers
{
    public class UpdateManager
    {
        private const string RepoOwner = "HackDwenture";
        private const string RepoName = "TweakWise";
        private const string ReleaseBranchName = "release";
        private static readonly string VersionManifestUrl =
            $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{ReleaseBranchName}/src/TweakWise/version.json";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                using var response = await HttpClient.GetAsync(VersionManifestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Error,
                        ErrorMessage = $"Не удалось загрузить version.json. Код ответа: {(int)response.StatusCode}."
                    };
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                var manifest = await JsonSerializer.DeserializeAsync<VersionManifestDto>(stream);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Error,
                        ErrorMessage = "Файл version.json повреждён или не содержит версию."
                    };
                }

                var localVersion = AppInfo.VersionId;
                int compareResult = CompareVersions(manifest.Version, localVersion);

                return new UpdateCheckResult
                {
                    Status = compareResult > 0 ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
                    CurrentVersionText = $"Текущая версия: {AppInfo.DisplayVersion}",
                    LatestVersionText = string.IsNullOrWhiteSpace(manifest.DisplayVersion)
                        ? manifest.Version
                        : manifest.DisplayVersion,
                    LatestVersionId = manifest.Version,
                    DownloadUrl = string.IsNullOrWhiteSpace(manifest.DownloadUrl) ? manifest.DetailsUrl : manifest.DownloadUrl,
                    ReleaseUrl = string.IsNullOrWhiteSpace(manifest.DetailsUrl) ? VersionManifestUrl : manifest.DetailsUrl,
                    ReleaseNotes = BuildReleaseNotes(manifest)
                };
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    ErrorMessage = ex.Message
                };
            }
        }

        public void OpenDownload(string downloadUrl)
        {
            OpenUrl(downloadUrl);
        }

        public void OpenLatestReleasePage()
        {
            OpenUrl(VersionManifestUrl);
        }

        private static string BuildReleaseNotes(VersionManifestDto manifest)
        {
            if (manifest.Changelog == null || manifest.Changelog.Length == 0)
                return "Список изменений не указан.";

            return string.Join(Environment.NewLine + Environment.NewLine, manifest.Changelog.Select(item => $"• {item}"));
        }

        private static int CompareVersions(string left, string right)
        {
            var leftVersion = ParsedVersion.Parse(left);
            var rightVersion = ParsedVersion.Parse(right);
            return leftVersion.CompareTo(rightVersion);
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TweakWise-Updater");
            client.Timeout = TimeSpan.FromSeconds(10);
            return client;
        }

        private sealed class VersionManifestDto
        {
            [JsonPropertyName("version")]
            public string Version { get; set; }

            [JsonPropertyName("displayVersion")]
            public string DisplayVersion { get; set; }

            [JsonPropertyName("changelog")]
            public string[] Changelog { get; set; }

            [JsonPropertyName("downloadUrl")]
            public string DownloadUrl { get; set; }

            [JsonPropertyName("detailsUrl")]
            public string DetailsUrl { get; set; }
        }

        private readonly struct ParsedVersion : IComparable<ParsedVersion>
        {
            public ParsedVersion(int major, int minor, int patch, int channelRank, int channelNumber)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                ChannelRank = channelRank;
                ChannelNumber = channelNumber;
            }

            public int Major { get; }
            public int Minor { get; }
            public int Patch { get; }
            public int ChannelRank { get; }
            public int ChannelNumber { get; }

            public static ParsedVersion Parse(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return new ParsedVersion(0, 0, 0, 0, 0);

                string normalized = value.Trim().TrimStart('v', 'V');
                string[] versionAndSuffix = normalized.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
                string[] numberParts = versionAndSuffix[0].Split('.');

                int major = numberParts.Length > 0 && int.TryParse(numberParts[0], out var parsedMajor) ? parsedMajor : 0;
                int minor = numberParts.Length > 1 && int.TryParse(numberParts[1], out var parsedMinor) ? parsedMinor : 0;
                int patch = numberParts.Length > 2 && int.TryParse(numberParts[2], out var parsedPatch) ? parsedPatch : 0;

                int channelRank = 3;
                int channelNumber = 0;

                if (versionAndSuffix.Length > 1)
                {
                    string suffix = versionAndSuffix[1].Trim().ToLowerInvariant();
                    if (suffix.StartsWith("alpha"))
                        channelRank = 0;
                    else if (suffix.StartsWith("beta"))
                        channelRank = 1;
                    else if (suffix.StartsWith("rc"))
                        channelRank = 2;
                }

                return new ParsedVersion(major, minor, patch, channelRank, channelNumber);
            }

            public int CompareTo(ParsedVersion other)
            {
                int result = Major.CompareTo(other.Major);
                if (result != 0) return result;

                result = Minor.CompareTo(other.Minor);
                if (result != 0) return result;

                result = Patch.CompareTo(other.Patch);
                if (result != 0) return result;

                result = ChannelRank.CompareTo(other.ChannelRank);
                if (result != 0) return result;

                return ChannelNumber.CompareTo(other.ChannelNumber);
            }
        }
    }

    public class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; set; }
        public string CurrentVersionText { get; set; } = string.Empty;
        public string LatestVersionText { get; set; } = string.Empty;
        public string LatestVersionId { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        Error
    }
}
