using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TweakWise.Services
{
    public sealed class SystemCleanupService
    {
        private const int ShEmptyRecycleBinNoConfirmation = 0x00000001;
        private const int ShEmptyRecycleBinNoProgressUi = 0x00000002;
        private const int ShEmptyRecycleBinNoSound = 0x00000004;

        public IReadOnlyList<SystemCleanupTarget> BuildTargets()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            return new List<SystemCleanupTarget>
            {
                new SystemCleanupTarget(
                    "user-temp",
                    "Временные файлы пользователя",
                    "Очищает файлы из папки %TEMP%, которые уже не используются приложениями.",
                    true,
                    new[] { Path.GetTempPath() },
                    minAge: TimeSpan.FromMinutes(10)),

                new SystemCleanupTarget(
                    "windows-temp",
                    "Временные файлы Windows",
                    "Очищает безопасные остатки из Windows\\Temp. Занятые системой файлы будут пропущены.",
                    true,
                    new[] { Path.Combine(windows, "Temp") },
                    minAge: TimeSpan.FromHours(1)),

                new SystemCleanupTarget(
                    "thumb-cache",
                    "Кэш миниатюр Проводника",
                    "Удаляет базы thumbcache. Windows создаст их заново при открытии папок с изображениями и видео.",
                    true,
                    new[] { Path.Combine(localAppData, "Microsoft", "Windows", "Explorer") },
                    filePatterns: new[] { "thumbcache_*.db", "iconcache_*.db" },
                    minAge: TimeSpan.FromMinutes(10)),

                new SystemCleanupTarget(
                    "directx-cache",
                    "Кэш шейдеров DirectX",
                    "Удаляет временный кэш графических шейдеров. После очистки первые запуски игр могут немного дольше компилировать кэш.",
                    false,
                    new[]
                    {
                        Path.Combine(localAppData, "D3DSCache"),
                        Path.Combine(localAppData, "NVIDIA", "DXCache"),
                        Path.Combine(localAppData, "AMD", "DxCache")
                    },
                    minAge: TimeSpan.FromHours(1)),

                new SystemCleanupTarget(
                    "wer",
                    "Отчёты ошибок Windows",
                    "Удаляет старые локальные отчёты об ошибках, которые уже не нужны для текущей работы программ.",
                    true,
                    new[]
                    {
                        Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"),
                        Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"),
                        Path.Combine(localAppData, "Microsoft", "Windows", "WER")
                    },
                    minAge: TimeSpan.FromHours(1)),

                new SystemCleanupTarget(
                    "recycle-bin",
                    "Корзина",
                    "Очищает корзину Windows для доступных дисков.",
                    false,
                    Array.Empty<string>(),
                    isRecycleBin: true)
            };
        }

        public Task<IReadOnlyList<SystemCleanupTargetState>> AnalyzeAsync(
            IEnumerable<SystemCleanupTarget> targets,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                IReadOnlyList<SystemCleanupTargetState> result = (targets ?? Array.Empty<SystemCleanupTarget>())
                    .Select(target => AnalyzeTarget(target, cancellationToken))
                    .ToList()
                    .AsReadOnly();
                return result;
            }, cancellationToken);
        }

        public Task<SystemCleanupRunResult> CleanAsync(
            IEnumerable<SystemCleanupTarget> targets,
            IProgress<string> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                long freedBytes = 0;
                int deletedFiles = 0;
                int skippedFiles = 0;

                foreach (var target in targets ?? Array.Empty<SystemCleanupTarget>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(target.Title);

                    if (target.IsRecycleBin)
                    {
                        var before = EstimateRecycleBin(cancellationToken);
                        int result = SHEmptyRecycleBin(IntPtr.Zero, null,
                            ShEmptyRecycleBinNoConfirmation |
                            ShEmptyRecycleBinNoProgressUi |
                            ShEmptyRecycleBinNoSound);

                        if (result == 0)
                        {
                            freedBytes += before.Bytes;
                            deletedFiles += before.FileCount;
                        }
                        else
                        {
                            skippedFiles += before.FileCount;
                        }

                        continue;
                    }

                    foreach (var file in EnumerateTargetFiles(target, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            long length = SafeFileLength(file);
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                            freedBytes += length;
                            deletedFiles++;
                        }
                        catch
                        {
                            skippedFiles++;
                        }
                    }

                    foreach (var root in target.Paths.Where(Directory.Exists))
                        RemoveEmptyDirectories(root, cancellationToken);
                }

                return new SystemCleanupRunResult(freedBytes, deletedFiles, skippedFiles);
            }, cancellationToken);
        }

        private static SystemCleanupTargetState AnalyzeTarget(SystemCleanupTarget target, CancellationToken cancellationToken)
        {
            if (target == null)
                return new SystemCleanupTargetState(string.Empty, 0, 0, false, "Пункт очистки не определён.");

            if (target.IsRecycleBin)
            {
                var recycle = EstimateRecycleBin(cancellationToken);
                return new SystemCleanupTargetState(target.Id, recycle.Bytes, recycle.FileCount, recycle.FileCount > 0, recycle.Message);
            }

            long bytes = 0;
            int count = 0;
            foreach (var file in EnumerateTargetFiles(target, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bytes += SafeFileLength(file);
                count++;
            }

            string message = count == 0
                ? "Подходящих файлов сейчас нет."
                : $"{FormatBytes(bytes)} · файлов: {count}";

            return new SystemCleanupTargetState(target.Id, bytes, count, true, message);
        }

        private static IEnumerable<string> EnumerateTargetFiles(SystemCleanupTarget target, CancellationToken cancellationToken)
        {
            var cutoff = DateTime.Now - target.MinAge;
            foreach (var root in target.Paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var file in EnumerateFilesSafe(root, target.FilePatterns, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsSafeCleanupFile(file, cutoff))
                        continue;

                    yield return file;
                }
            }
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root, IReadOnlyList<string> patterns, CancellationToken cancellationToken)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = stack.Pop();

                IEnumerable<string> files = Array.Empty<string>();
                try
                {
                    files = (patterns == null || patterns.Count == 0)
                        ? Directory.EnumerateFiles(current)
                        : patterns.SelectMany(pattern => Directory.EnumerateFiles(current, pattern));
                }
                catch
                {
                }

                foreach (var file in files)
                    yield return file;

                IEnumerable<string> directories = Array.Empty<string>();
                try
                {
                    directories = Directory.EnumerateDirectories(current)
                        .Where(path => !IsReparsePoint(path));
                }
                catch
                {
                }

                foreach (var directory in directories)
                    stack.Push(directory);
            }
        }

        private static bool IsSafeCleanupFile(string path, DateTime cutoff)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return false;

                if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    return false;

                return info.LastWriteTime < cutoff;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return true;
            }
        }

        private static long SafeFileLength(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static void RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root))
                return;

            List<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root).ToList();
            }
            catch
            {
                return;
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(directory))
                    continue;

                RemoveEmptyDirectories(directory, cancellationToken);
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(root).Any())
                    Directory.Delete(root, false);
            }
            catch
            {
            }
        }

        private static RecycleBinEstimate EstimateRecycleBin(CancellationToken cancellationToken)
        {
            long bytes = 0;
            int count = 0;
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string recycleRoot = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (!Directory.Exists(recycleRoot))
                    continue;

                foreach (var file in EnumerateFilesSafe(recycleRoot, Array.Empty<string>(), cancellationToken))
                {
                    bytes += SafeFileLength(file);
                    count++;
                }
            }

            string message = count == 0
                ? "Корзина пуста или закрыта системой."
                : $"{FormatBytes(bytes)} · файлов: {count}";

            return new RecycleBinEstimate(bytes, count, message);
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, int dwFlags);

        private readonly struct RecycleBinEstimate
        {
            public RecycleBinEstimate(long bytes, int fileCount, string message)
            {
                Bytes = bytes;
                FileCount = fileCount;
                Message = message ?? string.Empty;
            }

            public long Bytes { get; }
            public int FileCount { get; }
            public string Message { get; }
        }
    }

    public sealed class SystemCleanupTarget
    {
        public SystemCleanupTarget(
            string id,
            string title,
            string description,
            bool isQuickDefault,
            IReadOnlyList<string> paths,
            IReadOnlyList<string> filePatterns = null,
            TimeSpan? minAge = null,
            bool isRecycleBin = false)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            IsQuickDefault = isQuickDefault;
            Paths = paths ?? Array.Empty<string>();
            FilePatterns = filePatterns ?? Array.Empty<string>();
            MinAge = minAge ?? TimeSpan.Zero;
            IsRecycleBin = isRecycleBin;
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public bool IsQuickDefault { get; }
        public IReadOnlyList<string> Paths { get; }
        public IReadOnlyList<string> FilePatterns { get; }
        public TimeSpan MinAge { get; }
        public bool IsRecycleBin { get; }
    }

    public sealed class SystemCleanupTargetState
    {
        public SystemCleanupTargetState(string id, long estimatedBytes, int fileCount, bool isAvailable, string message)
        {
            Id = id ?? string.Empty;
            EstimatedBytes = estimatedBytes;
            FileCount = fileCount;
            IsAvailable = isAvailable;
            Message = message ?? string.Empty;
        }

        public string Id { get; }
        public long EstimatedBytes { get; }
        public int FileCount { get; }
        public bool IsAvailable { get; }
        public string Message { get; }
    }

    public sealed class SystemCleanupRunResult
    {
        public SystemCleanupRunResult(long freedBytes, int deletedFiles, int skippedFiles)
        {
            FreedBytes = freedBytes;
            DeletedFiles = deletedFiles;
            SkippedFiles = skippedFiles;
        }

        public long FreedBytes { get; }
        public int DeletedFiles { get; }
        public int SkippedFiles { get; }
    }
}
