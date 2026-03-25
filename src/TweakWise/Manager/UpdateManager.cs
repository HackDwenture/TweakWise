using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
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
        private static readonly string BranchApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/branches/{ReleaseBranchName}";
        private static readonly string CommitsApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/commits?sha={ReleaseBranchName}&per_page=8";
        private static readonly string BranchPageUrl = $"https://github.com/{RepoOwner}/{RepoName}/tree/{ReleaseBranchName}";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string lastKnownReleaseCommit = null)
        {
            try
            {
                using var branchResponse = await HttpClient.GetAsync(BranchApiUrl);
                if (branchResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Error,
                        ErrorMessage = "Ветка release не найдена на GitHub."
                    };
                }

                if (!branchResponse.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Error,
                        ErrorMessage = $"GitHub вернул код {(int)branchResponse.StatusCode}."
                    };
                }

                await using var branchStream = await branchResponse.Content.ReadAsStreamAsync();
                var branch = await JsonSerializer.DeserializeAsync<GitHubBranchDto>(branchStream);
                if (branch?.Commit?.Sha == null)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Error,
                        ErrorMessage = "Не удалось определить последний коммит ветки release."
                    };
                }

                string latestCommitSha = branch.Commit.Sha;
                string releaseNotes = await LoadReleaseBranchNotesAsync();
                bool hasNewCommit = !string.Equals(latestCommitSha, lastKnownReleaseCommit, StringComparison.OrdinalIgnoreCase);

                return new UpdateCheckResult
                {
                    Status = hasNewCommit ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
                    CurrentVersionText = string.IsNullOrWhiteSpace(lastKnownReleaseCommit)
                        ? "Локальная версия ещё не синхронизировалась с веткой release"
                        : $"Последний просмотренный коммит: {ShortSha(lastKnownReleaseCommit)}",
                    LatestVersionText = $"release / {ShortSha(latestCommitSha)}",
                    ReleaseCommitSha = latestCommitSha,
                    ReleaseUrl = BranchPageUrl,
                    DownloadUrl = BranchPageUrl,
                    ReleaseNotes = releaseNotes
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
            OpenUrl(BranchPageUrl);
        }

        private async Task<string> LoadReleaseBranchNotesAsync()
        {
            using var commitsResponse = await HttpClient.GetAsync(CommitsApiUrl);
            if (!commitsResponse.IsSuccessStatusCode)
                return "Не удалось загрузить changelog из ветки release.";

            await using var commitsStream = await commitsResponse.Content.ReadAsStreamAsync();
            var commits = await JsonSerializer.DeserializeAsync<GitHubCommitDto[]>(commitsStream);
            if (commits == null || commits.Length == 0)
                return "В ветке release пока нет коммитов.";

            var builder = new StringBuilder();
            builder.AppendLine("Последние изменения в ветке release:");
            builder.AppendLine();

            foreach (var commit in commits)
            {
                string shortSha = ShortSha(commit.Sha);
                string author = commit.Commit?.Author?.Name ?? "Unknown";
                string date = commit.Commit?.Author?.Date?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
                string message = commit.Commit?.Message?.Trim() ?? "(без описания)";

                builder.Append("• ");
                builder.Append(shortSha);

                if (!string.IsNullOrWhiteSpace(date))
                {
                    builder.Append("  ");
                    builder.Append(date);
                }

                if (!string.IsNullOrWhiteSpace(author))
                {
                    builder.Append("  ");
                    builder.Append(author);
                }

                builder.AppendLine();
                builder.AppendLine(message);
                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }

        private static string ShortSha(string sha)
        {
            if (string.IsNullOrWhiteSpace(sha))
                return "unknown";

            return sha.Length <= 7 ? sha : sha.Substring(0, 7);
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

        private class GitHubBranchDto
        {
            [JsonPropertyName("commit")]
            public GitHubBranchCommitDto Commit { get; set; }
        }

        private class GitHubBranchCommitDto
        {
            [JsonPropertyName("sha")]
            public string Sha { get; set; }
        }

        private class GitHubCommitDto
        {
            [JsonPropertyName("sha")]
            public string Sha { get; set; }

            [JsonPropertyName("commit")]
            public GitHubCommitDetailsDto Commit { get; set; }
        }

        private class GitHubCommitDetailsDto
        {
            [JsonPropertyName("message")]
            public string Message { get; set; }

            [JsonPropertyName("author")]
            public GitHubCommitAuthorDto Author { get; set; }
        }

        private class GitHubCommitAuthorDto
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("date")]
            public DateTimeOffset? Date { get; set; }
        }
    }

    public class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; set; }
        public string CurrentVersionText { get; set; } = string.Empty;
        public string LatestVersionText { get; set; } = string.Empty;
        public string ReleaseCommitSha { get; set; } = string.Empty;
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
