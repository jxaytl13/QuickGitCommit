using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TLNexus.GitU
{
    internal enum GitChangeType
    {
        Added,
        Modified,
        Deleted,
        Renamed,
        Unknown
    }

    internal static class GitChangeTypeExtensions
    {
        public static string ToDisplayName(this GitChangeType type)
        {
            return type switch
            {
                GitChangeType.Added => "新增",
                GitChangeType.Modified => "修改",
                GitChangeType.Deleted => "删除",
                GitChangeType.Renamed => "重命名",
                _ => "未知"
            };
        }
    }

    internal enum UnityAssetTypeFilter
    {
        All,
        AnimationClip,
        AudioClip,
        AudioMixer,
        ComputeShader,
        Font,
        GUISkin,
        Material,
        Mesh,
        Model,
        PhysicMaterial,
        Prefab,
        Scene,
        Script,
        Shader,
        Sprite,
        Texture,
        VideoClip,
        VisualEffectAsset,
        Unknown
    }

    internal static class UnityAssetTypeFilterExtensions
    {
        public static string ToDisplayName(this UnityAssetTypeFilter type)
        {
            return type switch
            {
                UnityAssetTypeFilter.All => "All",
                UnityAssetTypeFilter.AnimationClip => "Animation Clip",
                UnityAssetTypeFilter.AudioClip => "Audio Clip",
                UnityAssetTypeFilter.AudioMixer => "Audio Mixer",
                UnityAssetTypeFilter.ComputeShader => "Compute Shader",
                UnityAssetTypeFilter.Font => "Font",
                UnityAssetTypeFilter.GUISkin => "GUI Skin",
                UnityAssetTypeFilter.Material => "Material",
                UnityAssetTypeFilter.Mesh => "Mesh",
                UnityAssetTypeFilter.Model => "Model",
                UnityAssetTypeFilter.PhysicMaterial => "Physic Material",
                UnityAssetTypeFilter.Prefab => "Prefab",
                UnityAssetTypeFilter.Scene => "Scene",
                UnityAssetTypeFilter.Script => "Script",
                UnityAssetTypeFilter.Shader => "Shader",
                UnityAssetTypeFilter.Sprite => "Sprite",
                UnityAssetTypeFilter.Texture => "Texture",
                UnityAssetTypeFilter.VideoClip => "Video Clip",
                UnityAssetTypeFilter.VisualEffectAsset => "Visual Effect Asset",
                _ => "Unknown"
            };
        }
    }

    [Serializable]
    internal class GitAssetInfo
    {
        public string AssetPath;
        public string OriginalPath;
        public GitChangeType ChangeType;
        public DateTime? LastCommitTime;
        public DateTime? WorkingTreeTime;
        public bool IsStaged;
        public bool IsUnstaged;

        public GitAssetInfo(string path, string originalPath, GitChangeType type, DateTime? lastTime, DateTime? workingTime, bool isStaged, bool isUnstaged)
        {
            AssetPath = path;
            OriginalPath = originalPath;
            ChangeType = type;
            LastCommitTime = lastTime;
            WorkingTreeTime = workingTime;
            IsStaged = isStaged;
            IsUnstaged = isUnstaged;
        }

        public string FileName => Path.GetFileName(AssetPath);
    }

    internal readonly struct GitChangeEntry
    {
        public readonly string Path;
        public readonly string OriginalPath;
        public readonly GitChangeType ChangeType;
        public readonly DateTime? WorkingTreeTime;
        public readonly bool IsStaged;
        public readonly bool IsUnstaged;

        public GitChangeEntry(string path, string originalPath, GitChangeType type, DateTime? workingTreeTime, bool isStaged, bool isUnstaged)
        {
            Path = path;
            OriginalPath = originalPath;
            ChangeType = type;
            WorkingTreeTime = workingTreeTime;
            IsStaged = isStaged;
            IsUnstaged = isUnstaged;
        }
    }

    internal readonly struct GitRepositoryStatusInfo
    {
        public readonly int StagedCount;
        public readonly int UnstagedCount;

        public GitRepositoryStatusInfo(int stagedCount, int unstagedCount)
        {
            StagedCount = stagedCount;
            UnstagedCount = unstagedCount;
        }

        public bool HasChanges => StagedCount > 0 || UnstagedCount > 0;
    }

    internal static class GitUtility
    {
        private const int GitCommandTimeoutShortMs = 30_000;
        private const int GitCommandTimeoutMediumMs = 120_000;
        private const int GitCommandTimeoutLongMs = 300_000;

        internal static bool IsMatchAssetTypeFilter(string unityAssetPath, UnityAssetTypeFilter filter)
        {
            if (filter == UnityAssetTypeFilter.All)
            {
                return true;
            }

            return DetectAssetTypeFilter(unityAssetPath) == filter;
        }

        internal static UnityAssetTypeFilter DetectAssetTypeFilter(string unityAssetPath)
        {
            if (string.IsNullOrWhiteSpace(unityAssetPath))
            {
                return UnityAssetTypeFilter.Unknown;
            }

            var path = NormalizeAssetPath(unityAssetPath);
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - 5);
            }

            var extension = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
            switch (extension)
            {
                case ".anim":
                    return UnityAssetTypeFilter.AnimationClip;
                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".aiff":
                case ".aif":
                case ".mod":
                case ".it":
                case ".s3m":
                case ".xm":
                    return UnityAssetTypeFilter.AudioClip;
                case ".mixer":
                    return UnityAssetTypeFilter.AudioMixer;
                case ".compute":
                    return UnityAssetTypeFilter.ComputeShader;
                case ".ttf":
                case ".otf":
                case ".ttc":
                case ".dfont":
                case ".fnt":
                    return UnityAssetTypeFilter.Font;
                case ".guiskin":
                    return UnityAssetTypeFilter.GUISkin;
                case ".mat":
                    return UnityAssetTypeFilter.Material;
                case ".prefab":
                    return UnityAssetTypeFilter.Prefab;
                case ".unity":
                    return UnityAssetTypeFilter.Scene;
                case ".cs":
                case ".js":
                case ".boo":
                case ".asmdef":
                case ".asmref":
                    return UnityAssetTypeFilter.Script;
                case ".shader":
                case ".cginc":
                case ".hlsl":
                case ".glslinc":
                    return UnityAssetTypeFilter.Shader;
                case ".physicmaterial":
                    return UnityAssetTypeFilter.PhysicMaterial;
                case ".fbx":
                case ".obj":
                case ".dae":
                case ".3ds":
                case ".dxf":
                case ".blend":
                case ".max":
                case ".c4d":
                case ".mb":
                case ".ma":
                    return UnityAssetTypeFilter.Model;
                case ".mp4":
                case ".mov":
                case ".avi":
                case ".webm":
                case ".m4v":
                case ".ogv":
                case ".wmv":
                    return UnityAssetTypeFilter.VideoClip;
                case ".vfx":
                case ".vfxgraph":
                    return UnityAssetTypeFilter.VisualEffectAsset;
                case ".spriteatlas":
                    return UnityAssetTypeFilter.Sprite;
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".exr":
                case ".tif":
                case ".tiff":
                case ".bmp":
                case ".gif":
                case ".dds":
                case ".hdr":
                    return DetectTextureOrSprite(path);
            }

            var mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (mainType != null && typeof(Mesh).IsAssignableFrom(mainType))
            {
                return UnityAssetTypeFilter.Mesh;
            }

            return UnityAssetTypeFilter.Unknown;
        }

        private static UnityAssetTypeFilter DetectTextureOrSprite(string unityAssetPath)
        {
            try
            {
                var importer = AssetImporter.GetAtPath(unityAssetPath) as TextureImporter;
                if (importer != null && importer.textureType == TextureImporterType.Sprite)
                {
                    return UnityAssetTypeFilter.Sprite;
                }
            }
            catch
            {
                // ignored: deleted/missing asset or importer not available
            }

            return UnityAssetTypeFilter.Texture;
        }

        private static string cachedProjectRoot;
        private static string cachedUnityProjectFolder;
        private static string contextAssetAbsolutePath;
        private static bool gitRootNotFoundLogged;
        private static bool gitRevParseWarningLogged;

        internal static string ProjectRoot
        {
            get
            {
                if (string.IsNullOrEmpty(cachedProjectRoot) || !Directory.Exists(cachedProjectRoot))
                {
                    cachedProjectRoot = LocateGitRoot();
                    if (string.IsNullOrEmpty(cachedProjectRoot))
                    {
                        if (!gitRootNotFoundLogged)
                        {
                            Debug.LogWarning("GitU: 未找到Git根目录（.git）。请确认当前Unity工程在Git仓库内，且已安装并可在命令行调用 git。");
                            gitRootNotFoundLogged = true;
                        }
                    }
                    else
                    {
                        gitRootNotFoundLogged = false;
                    }
                }

                return cachedProjectRoot;
            }
        }

        internal static string UnityProjectFolder => GetUnityProjectFolder();

        internal readonly struct GitStageRequest
        {
            public readonly string GitRelativePath;
            public readonly GitChangeType ChangeType;

            public GitStageRequest(string gitRelativePath, GitChangeType changeType)
            {
                GitRelativePath = gitRelativePath;
                ChangeType = changeType;
            }
        }

        internal static bool TryGetGitRelativePath(string unityAssetPath, out string gitRelativePath)
        {
            gitRelativePath = ConvertUnityPathToGitPath(unityAssetPath);
            return !string.IsNullOrEmpty(gitRelativePath);
        }

        internal static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            return assetPath.Replace('\\', '/');
        }

        internal static void SetContextAssetPath(string assetRelativePath)
        {
            if (string.IsNullOrEmpty(assetRelativePath))
            {
                contextAssetAbsolutePath = null;
                cachedProjectRoot = null;
                return;
            }

            var absolutePath = ResolveAbsolutePath(assetRelativePath);
            if (string.IsNullOrEmpty(absolutePath))
            {
                contextAssetAbsolutePath = null;
                cachedProjectRoot = null;
                return;
            }

            if (AssetDatabase.IsValidFolder(assetRelativePath))
            {
                contextAssetAbsolutePath = absolutePath;
            }
            else
            {
                var directory = Path.GetDirectoryName(absolutePath);
                contextAssetAbsolutePath = string.IsNullOrEmpty(directory) ? absolutePath : directory;
            }

            cachedProjectRoot = null;
        }

        internal static List<GitChangeEntry> GetWorkingTreeChanges()
        {
            var result = new List<GitChangeEntry>();

            // Use --untracked-files=all so untracked directories are expanded into files;
            // otherwise Git may collapse them as "?? someDir/" which Unity can't stage/commit precisely.
            if (!TryRunGitCommandRaw("status --porcelain=v1 -z --untracked-files=all --find-renames", out var output, out _))
            {
                return result;
            }

            if (string.IsNullOrEmpty(output))
            {
                return result;
            }

            var records = output.Split('\0');
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (string.IsNullOrEmpty(record))
                {
                    continue;
                }

                record = record.TrimEnd('\r', '\n');
                if (record.Length < 4 || record[2] != ' ')
                {
                    continue;
                }

                var statusCode = record.Substring(0, 2);
                var pathSegment = record.Substring(3);
                if (string.IsNullOrEmpty(pathSegment))
                {
                    continue;
                }

                string originalPathSegment = null;

                // In porcelain v1 -z, rename/copy paths are emitted as two NUL-separated fields:
                // "XY old\0new\0". Keep both paths so filtering/staging can account for moves in/out.
                var statusPrimary = statusCode[0] != ' ' ? statusCode[0] : statusCode[1];
                if ((statusPrimary == 'R' || statusPrimary == 'C') && i + 1 < records.Length)
                {
                    var destination = records[i + 1];
                    if (!string.IsNullOrEmpty(destination))
                    {
                        originalPathSegment = pathSegment;
                        pathSegment = destination;
                        i++;
                    }
                }

                var changeType = ParseChangeType(statusCode);
                if (changeType == GitChangeType.Unknown)
                {
                    continue;
                }

                var indexStatus = statusCode[0];
                var workTreeStatus = statusCode[1];

                var isStaged = indexStatus != ' ' && indexStatus != '?';
                var isUnstaged = workTreeStatus != ' ' || indexStatus == '?';
                if (!isStaged && !isUnstaged)
                {
                    continue;
                }

                var unityRelativePath = ConvertGitPathToUnityPath(pathSegment);
                var unityOriginalPath = string.IsNullOrEmpty(originalPathSegment) ? null : ConvertGitPathToUnityPath(originalPathSegment);

                if (string.IsNullOrEmpty(unityRelativePath))
                {
                    if (!string.IsNullOrEmpty(unityOriginalPath) && changeType == GitChangeType.Renamed)
                    {
                        var workingTimeFallback = GetWorkingTreeTimestamp(unityOriginalPath, GitChangeType.Deleted);
                        result.Add(new GitChangeEntry(unityOriginalPath, null, GitChangeType.Deleted, workingTimeFallback, isStaged, isUnstaged));
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(unityOriginalPath) && changeType == GitChangeType.Renamed)
                {
                    changeType = GitChangeType.Added;
                }

                var workingTime = GetWorkingTreeTimestamp(unityRelativePath, changeType);
                result.Add(new GitChangeEntry(unityRelativePath, unityOriginalPath, changeType, workingTime, isStaged, isUnstaged));

                // For rename/move entries, also emit an explicit "Deleted" entry for the original path so UI can show it.
                if (changeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(unityOriginalPath) &&
                    !string.Equals(unityOriginalPath, unityRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    var deletedTime = GetWorkingTreeTimestamp(unityOriginalPath, GitChangeType.Deleted);
                    result.Add(new GitChangeEntry(unityOriginalPath, unityRelativePath, GitChangeType.Deleted, deletedTime, isStaged, isUnstaged));
                }
            }

            return result;
        }

        internal static IEnumerable<GitChangeEntry> GetAssetRelatedChanges(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                yield break;
            }

            assetPath = NormalizeAssetPath(assetPath);
            var allChanges = GetWorkingTreeChanges();
            if (allChanges.Count == 0)
            {
                yield break;
            }

            var dependencySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                AddPathAndMeta(dependencySet, assetPath);
            }
            else
            {
                foreach (var dep in AssetDatabase.GetDependencies(assetPath, true))
                {
                    var normalized = NormalizeAssetPath(dep);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        AddPathAndMeta(dependencySet, normalized);
                    }
                }

                AddPathAndMeta(dependencySet, assetPath);
            }

            foreach (var entry in allChanges)
            {
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    if (entry.Path.StartsWith(assetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return entry;
                    }

                    continue;
                }

                if (dependencySet.Contains(entry.Path))
                {
                    yield return entry;
                }
            }
        }

        internal static DateTime? GetLastKnownChangeTime(string relativePath)
        {
            return GetWorkingTreeTimestamp(relativePath, GitChangeType.Unknown);
        }

        private static DateTime? GetWorkingTreeTimestamp(string unityPath, GitChangeType type)
        {
            var fullPath = ResolveAbsolutePath(unityPath);
            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                return File.GetLastWriteTime(fullPath);
            }

            return DateTime.Now;
        }

        private static DateTime? GetLastCommitTime(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            var gitRelativePath = ConvertUnityPathToGitPath(relativePath);
            if (string.IsNullOrEmpty(gitRelativePath))
            {
                return null;
            }

            if (!TryRunGitCommand(BuildGitArgs("log -1 --format=%ct", gitRelativePath), out var output))
            {
                return null;
            }

            var trimmed = output.Trim();
            if (long.TryParse(trimmed, out var seconds))
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            return null;
        }

        private static string NormalizeStatusPath(string rawPath)
        {
            if (string.IsNullOrEmpty(rawPath))
            {
                return string.Empty;
            }

            var arrowIndex = rawPath.IndexOf("->", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                rawPath = rawPath.Substring(arrowIndex + 2).Trim();
            }

            return NormalizeAssetPath(rawPath);
        }

        private static GitChangeType ParseChangeType(string status)
        {
            if (string.IsNullOrEmpty(status) || status.Length < 2)
            {
                return GitChangeType.Unknown;
            }

            var primary = status[0] != ' ' ? status[0] : status[1];

            return primary switch
            {
                'A' => GitChangeType.Added,
                'M' => GitChangeType.Modified,
                'D' => GitChangeType.Deleted,
                'R' => GitChangeType.Renamed,
                'C' => GitChangeType.Added,
                '?' => GitChangeType.Added,
                _ => GitChangeType.Unknown
            };
        }

        private static bool TryRunGitCommand(string arguments, out string output, int timeoutMilliseconds = GitCommandTimeoutShortMs)
        {
            output = string.Empty;

            var gitRoot = ProjectRoot;
            if (string.IsNullOrEmpty(gitRoot))
            {
                return false;
            }

            if (!TryRunGitProcess(gitRoot, arguments, timeoutMilliseconds, out var standardOutput, out var standardError))
            {
                if (!string.IsNullOrEmpty(standardError))
                {
                    Debug.LogWarning($"Git命令执行失败: git {arguments}\n{standardError}");
                }
                else
                {
                    Debug.LogWarning($"Git命令执行失败: git {arguments}");
                }

                return false;
            }

            output = standardOutput;
            return true;
        }

        private static bool TryRunGitCommandRaw(string arguments, out string output, out string error, int timeoutMilliseconds = GitCommandTimeoutShortMs)
        {
            output = string.Empty;
            error = string.Empty;

            var gitRoot = ProjectRoot;
            if (string.IsNullOrEmpty(gitRoot))
            {
                return false;
            }

            if (!TryRunGitProcessRaw(gitRoot, arguments, timeoutMilliseconds, out output, out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"Git命令执行失败: git {arguments}\n{error}");
                }
                else
                {
                    Debug.LogWarning($"Git命令执行失败: git {arguments}");
                }

                return false;
            }

            return true;
        }

        private static bool TryRunGitCommandNoLog(string gitRoot, string arguments, int timeoutMilliseconds, out string standardOutput, out string standardError)
        {
            standardOutput = string.Empty;
            standardError = string.Empty;

            if (string.IsNullOrEmpty(gitRoot))
            {
                standardError = "未找到 Git 根目录。";
                return false;
            }

            return TryRunGitProcess(gitRoot, arguments, timeoutMilliseconds, out standardOutput, out standardError);
        }

        internal static bool StageGitPaths(string gitRoot, IReadOnlyList<GitStageRequest> requests, out string summary)
        {
            summary = string.Empty;

            if (requests == null || requests.Count == 0)
            {
                summary = "没有可发送的变更。";
                return false;
            }

            var addPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var updatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var request in requests)
            {
                var gitPath = request.GitRelativePath?.Trim();
                if (string.IsNullOrEmpty(gitPath))
                {
                    continue;
                }

                if (request.ChangeType == GitChangeType.Deleted)
                {
                    updatePaths.Add(gitPath);
                    addPaths.Remove(gitPath);
                }
                else if (!updatePaths.Contains(gitPath))
                {
                    addPaths.Add(gitPath);
                }
            }

            var total = addPaths.Count + updatePaths.Count;
            var succeeded = 0;
            var firstError = string.Empty;

            if (total == 0)
            {
                summary = "没有可发送的变更。";
                return false;
            }

            TryRunGitPathCommandsBatched(gitRoot, "add", addPaths, ref succeeded, ref firstError);
            TryRunGitPathCommandsBatched(gitRoot, "add -u", updatePaths, ref succeeded, ref firstError);

            summary = $"已发送至待提交: {succeeded}/{total} 个条目。";
            if (succeeded == 0 && !string.IsNullOrEmpty(firstError))
            {
                summary = $"{summary}\n{firstError}";
            }

            return succeeded > 0;
        }

        internal static bool UnstageGitPaths(string gitRoot, IReadOnlyList<string> gitRelativePaths, out string summary)
        {
            summary = string.Empty;

            if (gitRelativePaths == null || gitRelativePaths.Count == 0)
            {
                summary = "没有可移出的待提交项。";
                return false;
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var gitPath in gitRelativePaths)
            {
                var trimmed = gitPath?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                paths.Add(trimmed);
            }

            var total = paths.Count;
            var succeeded = 0;
            var firstError = string.Empty;

            if (total == 0)
            {
                summary = "没有可移出的待提交项。";
                return false;
            }

            TryRunGitPathCommandsBatched(gitRoot, "reset HEAD", paths, ref succeeded, ref firstError);

            summary = $"已从待提交移出: {succeeded}/{total} 个条目。";
            if (succeeded == 0 && !string.IsNullOrEmpty(firstError))
            {
                summary = $"{summary}\n{firstError}";
            }

            return succeeded > 0;
        }

        private const int GitMaxPathsPerCommand = 200;
        private const int GitMaxArgumentsLength = 30_000;

        private static void TryRunGitPathCommandsBatched(
            string gitRoot,
            string baseArguments,
            IEnumerable<string> gitRelativePaths,
            ref int succeeded,
            ref string firstError)
        {
            if (gitRelativePaths == null)
            {
                return;
            }

            var normalizedPaths = gitRelativePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedPaths.Count == 0)
            {
                return;
            }

            foreach (var chunk in ChunkGitPaths(baseArguments, normalizedPaths))
            {
                if (chunk.Count == 0)
                {
                    continue;
                }

                var arguments = BuildGitArgs(baseArguments, chunk.ToArray());
                var timeout = chunk.Count > 1 ? GitCommandTimeoutMediumMs : GitCommandTimeoutShortMs;

                if (TryRunGitCommandNoLog(gitRoot, arguments, timeout, out _, out var stderr))
                {
                    succeeded += chunk.Count;
                    continue;
                }

                if (string.IsNullOrEmpty(firstError) && !string.IsNullOrEmpty(stderr))
                {
                    firstError = stderr.Trim();
                }

                foreach (var path in chunk)
                {
                    var oneArguments = BuildGitArgs(baseArguments, path);
                    if (TryRunGitCommandNoLog(gitRoot, oneArguments, GitCommandTimeoutShortMs, out _, out var oneErr))
                    {
                        succeeded++;
                        continue;
                    }

                    if (string.IsNullOrEmpty(firstError) && !string.IsNullOrEmpty(oneErr))
                    {
                        firstError = oneErr.Trim();
                    }
                }
            }
        }

        private static IEnumerable<List<string>> ChunkGitPaths(string baseArguments, IReadOnlyList<string> gitRelativePaths)
        {
            var current = new List<string>();
            var currentLength = baseArguments.Length + 3; // includes " --"

            foreach (var path in gitRelativePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var trimmed = path.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var quoted = QuoteGitArgument(trimmed);
                var additionalLength = 1 + quoted.Length; // leading space

                if (current.Count > 0 &&
                    (current.Count >= GitMaxPathsPerCommand || currentLength + additionalLength > GitMaxArgumentsLength))
                {
                    yield return current;
                    current = new List<string>();
                    currentLength = baseArguments.Length + 3;
                }

                current.Add(trimmed);
                currentLength += additionalLength;
            }

            if (current.Count > 0)
            {
                yield return current;
            }
        }

        internal static bool CommitGit(string gitRoot, string message, bool isChinese, out string summary)
        {
            summary = string.Empty;

            if (string.IsNullOrWhiteSpace(message))
            {
                summary = isChinese ? "提交说明不能为空。" : "Commit message cannot be empty.";
                return false;
            }

            var trimmed = message.Trim();
            var containsLineBreak = trimmed.IndexOfAny(new[] { '\r', '\n' }) >= 0;

            if (!TryRunGitCommandNoLog(gitRoot, "diff --cached --name-only", GitCommandTimeoutShortMs, out var diffOutput, out var diffError))
            {
                var baseMsg = isChinese ? "无法检查待提交内容，请确认仓库状态。" : "Unable to check staged content. Please verify repository status.";
                summary = string.IsNullOrEmpty(diffError)
                    ? baseMsg
                    : $"{baseMsg}\n{diffError.Trim()}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(diffOutput))
            {
                summary = isChinese ? "当前没有已暂存的变更可提交。" : "No staged changes to commit.";
                return false;
            }

            string commitArgs;
            string tempMessageFilePath = null;
            try
            {
                if (!containsLineBreak)
                {
                    commitArgs = $"commit -m {QuoteGitArgument(trimmed)}";
                }
                else
                {
                    tempMessageFilePath = Path.Combine(Path.GetTempPath(), $"GitUCommitMessage_{Guid.NewGuid():N}.txt");
                    var normalized = trimmed.Replace("\r\n", "\n").Replace('\r', '\n');
                    File.WriteAllText(tempMessageFilePath, normalized, Encoding.UTF8);
                    commitArgs = $"commit -F {QuoteGitArgument(tempMessageFilePath)}";
                }

                if (!TryRunGitCommandNoLog(gitRoot, commitArgs, GitCommandTimeoutMediumMs, out _, out var commitError))
                {
                    var failMsg = isChinese ? "提交失败，请查看 Console 日志中的 Git 输出。" : "Commit failed. Please check the Git output in the Console.";
                    summary = string.IsNullOrEmpty(commitError)
                        ? failMsg
                        : (isChinese ? $"提交失败：{commitError.Trim()}" : $"Commit failed: {commitError.Trim()}");
                    return false;
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempMessageFilePath))
                {
                    try
                    {
                        if (File.Exists(tempMessageFilePath))
                        {
                            File.Delete(tempMessageFilePath);
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            summary = isChinese ? "提交成功。" : "Commit successful.";
            return true;
        }

        internal static bool PushGit(string gitRoot, bool isChinese, out string summary)
        {
            summary = string.Empty;

            if (!TryRunGitCommandNoLog(gitRoot, "push", GitCommandTimeoutLongMs, out _, out var pushError))
            {
                summary = string.IsNullOrEmpty(pushError)
                    ? (isChinese ? "推送失败，请查看 Console 日志中的 Git 输出。" : "Push failed. Please check the Git output in the Console.")
                    : pushError.Trim();
                return false;
            }

            summary = isChinese ? "推送成功。" : "Push successful.";
            return true;
        }

        private static string QuoteGitArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            var needsQuotes = argument.Length == 0
                              || argument.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) >= 0;

            if (!needsQuotes)
            {
                return argument;
            }

            var sb = new StringBuilder();
            sb.Append('"');

            var backslashCount = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashCount * 2 + 1);
                    sb.Append('"');
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    sb.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                sb.Append(c);
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount * 2);
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static readonly Regex UnityMetaGuidRegex = new Regex(@"^\s*guid:\s*([0-9a-fA-F]+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

        internal static bool TryGetMetaGuidFromDisk(string unityMetaPath, out string guid)
        {
            guid = null;

            if (string.IsNullOrEmpty(unityMetaPath))
            {
                return false;
            }

            var absolutePath = ResolveAbsolutePath(unityMetaPath);
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                var content = File.ReadAllText(absolutePath);
                return TryParseUnityMetaGuid(content, out guid);
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetMetaGuidFromHead(string gitRoot, string gitRelativeMetaPath, out string guid)
        {
            guid = null;

            if (string.IsNullOrEmpty(gitRoot) || string.IsNullOrEmpty(gitRelativeMetaPath))
            {
                return false;
            }

            var normalized = gitRelativeMetaPath.Trim().Replace('\\', '/');
            if (normalized.Length == 0)
            {
                return false;
            }

            var objectSpec = $"HEAD:{normalized}";
            var arguments = $"show {QuoteGitArgument(objectSpec)}";
            if (!TryRunGitCommandNoLog(gitRoot, arguments, GitCommandTimeoutShortMs, out var stdout, out _))
            {
                return false;
            }

            return TryParseUnityMetaGuid(stdout, out guid);
        }

        private static bool TryParseUnityMetaGuid(string content, out string guid)
        {
            guid = null;

            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            var match = UnityMetaGuidRegex.Match(content);
            if (!match.Success || match.Groups.Count < 2)
            {
                return false;
            }

            guid = match.Groups[1].Value?.Trim();
            return !string.IsNullOrEmpty(guid);
        }

        private static string BuildGitArgs(string baseArguments, params string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return baseArguments;
            }

            var sb = new StringBuilder(baseArguments);
            sb.Append(" --");

            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                sb.Append(' ');
                sb.Append(QuoteGitArgument(path));
            }

            return sb.ToString();
        }

        private static bool TryRunGitProcess(string workingDirectory, string arguments, int timeoutMilliseconds, out string standardOutput, out string standardError)
        {
            standardOutput = string.Empty;
            standardError = string.Empty;

            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                return false;
            }

            if (timeoutMilliseconds <= 0)
            {
                timeoutMilliseconds = GitCommandTimeoutShortMs;
            }

            try
            {
                using var process = new Process();
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                var outputLock = new object();
                var errorLock = new object();

                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                    {
                        return;
                    }

                    lock (outputLock)
                    {
                        outputBuilder.AppendLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                    {
                        return;
                    }

                    lock (errorLock)
                    {
                        errorBuilder.AppendLine(args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // ignored
                    }

                    standardError = $"Git命令超时（{timeoutMilliseconds}ms）: git {arguments}";
                    return false;
                }

                process.WaitForExit();

                lock (outputLock)
                {
                    standardOutput = outputBuilder.ToString();
                }

                lock (errorLock)
                {
                    standardError = errorBuilder.ToString();
                }

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                standardError = ex.Message;
                return false;
            }
        }

        private static bool TryRunGitProcessRaw(string workingDirectory, string arguments, int timeoutMilliseconds, out string standardOutput, out string standardError)
        {
            standardOutput = string.Empty;
            standardError = string.Empty;

            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                return false;
            }

            if (timeoutMilliseconds <= 0)
            {
                timeoutMilliseconds = GitCommandTimeoutShortMs;
            }

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // ignored
                    }

                    standardError = $"Git命令超时（{timeoutMilliseconds}ms）: git {arguments}";
                    return false;
                }

                process.WaitForExit();

                if (!Task.WaitAll(new Task[] { stdoutTask, stderrTask }, timeoutMilliseconds))
                {
                    standardError = $"Git输出读取超时（{timeoutMilliseconds}ms）: git {arguments}";
                    return false;
                }

                standardOutput = stdoutTask.Result;
                standardError = stderrTask.Result;
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                standardError = ex.Message;
                return false;
            }
        }

        private static string LocateGitRoot()
        {
            var traversalRoot = FindGitRootByTraversal(contextAssetAbsolutePath);
            if (!string.IsNullOrEmpty(traversalRoot))
            {
                return traversalRoot;
            }

            traversalRoot = FindGitRootByTraversal(GetUnityProjectFolder());
            if (!string.IsNullOrEmpty(traversalRoot))
            {
                return traversalRoot;
            }

            if (TryResolveGitRootViaGitCommand(contextAssetAbsolutePath, out var resolvedRoot))
            {
                return resolvedRoot;
            }

            if (TryResolveGitRootViaGitCommand(GetUnityProjectFolder(), out resolvedRoot))
            {
                return resolvedRoot;
            }

            return null;
        }

        private static string FindGitRootByTraversal(string startDirectory)
        {
            if (string.IsNullOrEmpty(startDirectory))
            {
                return null;
            }

            try
            {
                var directory = new DirectoryInfo(startDirectory);
                while (directory != null)
                {
                    var gitPath = Path.Combine(directory.FullName, ".git");
                    if (Directory.Exists(gitPath) || File.Exists(gitPath))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"定位Git根目录时发生错误: {ex.Message}");
            }

            return null;
        }

        private static bool TryResolveGitRootViaGitCommand(string workingDirectory, out string rootPath)
        {
            rootPath = null;

            if (string.IsNullOrEmpty(workingDirectory))
            {
                workingDirectory = GetUnityProjectFolder();
            }

            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                return false;
            }

            if (TryRunGitProcess(workingDirectory, "rev-parse --show-toplevel", GitCommandTimeoutShortMs, out var stdout, out var stderr))
            {
                var firstLine = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstLine))
                {
                    rootPath = Path.GetFullPath(firstLine.Trim());
                    return true;
                }

                return false;
            }

            if (!string.IsNullOrEmpty(stderr) && !gitRevParseWarningLogged)
            {
                if (stderr.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Debug.LogWarning($"GitU: rev-parse 失败: {stderr}");
                }

                gitRevParseWarningLogged = true;
            }

            return false;
        }

        private static string GetUnityProjectFolder()
        {
            if (string.IsNullOrEmpty(cachedUnityProjectFolder))
            {
                cachedUnityProjectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }

            return cachedUnityProjectFolder;
        }

        private static string ConvertGitPathToUnityPath(string gitRelativePath)
        {
            if (string.IsNullOrEmpty(gitRelativePath))
            {
                return null;
            }

            // 去除两端空白和可能的引号
            gitRelativePath = gitRelativePath.Trim().Trim('"');

            // 过滤掉包含非法路径字符的条目，避免 ArgumentException
            if (gitRelativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                Debug.LogWarning($"GitU: 跳过包含非法字符的Git路径: {gitRelativePath}");
                return null;
            }

            var gitRoot = ProjectRoot;
            var unityFolder = GetUnityProjectFolder();
            if (string.IsNullOrEmpty(gitRoot) || string.IsNullOrEmpty(unityFolder))
            {
                return null;
            }

            string absolutePath;
            try
            {
                absolutePath = Path.GetFullPath(Path.Combine(gitRoot, gitRelativePath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GitU: 无法组合Git路径 root={gitRoot}, rel={gitRelativePath}, error={ex.Message}");
                return null;
            }

            if (!absolutePath.StartsWith(unityFolder, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = absolutePath.Substring(unityFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            return NormalizeAssetPath(relative);
        }

        private static string ConvertUnityPathToGitPath(string unityRelativePath)
        {
            if (string.IsNullOrEmpty(unityRelativePath))
            {
                return null;
            }

            var gitRoot = ProjectRoot;
            if (string.IsNullOrEmpty(gitRoot))
            {
                return null;
            }

            var absolutePath = ResolveAbsolutePath(unityRelativePath);
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            if (!absolutePath.StartsWith(gitRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = absolutePath.Substring(gitRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            return NormalizeAssetPath(relative);
        }

        internal static bool StageAssets(IEnumerable<GitAssetInfo> assets, out string summary)
        {
            var list = assets?.ToList() ?? new List<GitAssetInfo>();
            if (list.Count == 0)
            {
                summary = "没有可发送的变更。";
                return false;
            }

            var total = 0;
            var succeeded = 0;

            foreach (var info in list)
            {
                if (string.IsNullOrEmpty(info?.AssetPath))
                {
                    continue;
                }

                total++;
                var stagedOk = StageSingleAssetPath(info, info.AssetPath, info.ChangeType);

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    stagedOk = stagedOk && StageSingleAssetPath(info, info.OriginalPath, GitChangeType.Deleted);
                }

                if (stagedOk)
                {
                    succeeded++;
                }
            }

            if (total == 0)
            {
                summary = "没有可发送的变更。";
                return false;
            }

            summary = $"已发送至待提交: {succeeded}/{total} 个条目。";
            return succeeded > 0;
        }

        private static bool StageSingleAssetPath(GitAssetInfo info, string unityPath, GitChangeType changeType)
        {
            if (string.IsNullOrEmpty(unityPath))
            {
                return false;
            }

            var gitPath = ConvertUnityPathToGitPath(unityPath);
            if (string.IsNullOrEmpty(gitPath))
            {
                return false;
            }

            var arguments = changeType == GitChangeType.Deleted
                ? BuildGitArgs("add -u", gitPath)
                : BuildGitArgs("add", gitPath);

            Debug.Log($"[GitQuickCommit] Stage: type={changeType}, unityPath={unityPath}, gitPath={gitPath}, args=git {arguments}");

            if (TryRunGitCommand(arguments, out _))
            {
                return true;
            }

            Debug.LogWarning($"[GitQuickCommit] Stage failed for {gitPath}");
            return false;
        }

        internal static bool DiscardChanges(IEnumerable<GitAssetInfo> assets, out string summary)
        {
            var list = assets?.Where(a => a != null && !string.IsNullOrEmpty(a.AssetPath)).ToList() ?? new List<GitAssetInfo>();
            if (list.Count == 0)
            {
                summary = "\u6ca1\u6709\u53ef\u653e\u5f03\u7684\u66f4\u6539\u3002";
                return false;
            }

            var total = 0;
            var succeeded = 0;

            foreach (var info in list)
            {
                total++;
                if (DiscardSinglePath(info, info.AssetPath))
                {
                    succeeded++;
                }

                if (!info.AssetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    DiscardSinglePath(info, $"{info.AssetPath}.meta");
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    DiscardSinglePath(info, info.OriginalPath);

                    if (!info.OriginalPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        DiscardSinglePath(info, $"{info.OriginalPath}.meta");
                    }
                }
            }

            summary = succeeded == total
                ? $"\u5df2\u653e\u5f03 {succeeded} \u9879\u66f4\u6539\u3002"
                : $"\u5df2\u653e\u5f03 {succeeded}/{total} \u9879\u66f4\u6539\uff08\u5176\u4f59\u8bf7\u67e5\u770b Console \u65e5\u5fd7\uff09\u3002";
            return succeeded > 0;
        }

        private static bool DiscardSinglePath(GitAssetInfo info, string unityPath)
        {
            if (info == null || string.IsNullOrEmpty(unityPath))
            {
                return false;
            }

            var gitPath = ConvertUnityPathToGitPath(unityPath);
            if (string.IsNullOrEmpty(gitPath))
            {
                return false;
            }

            var isFolder = unityPath.EndsWith("/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(unityPath);

            var resetOk = true;
            if (info.IsStaged)
            {
                resetOk = TryRunGitCommand(BuildGitArgs("reset -q HEAD", gitPath), out _);
            }

            var treatAsAdded = info.ChangeType == GitChangeType.Added ||
                               (info.ChangeType == GitChangeType.Renamed &&
                                string.Equals(unityPath, info.AssetPath, StringComparison.OrdinalIgnoreCase));

            if (treatAsAdded)
            {
                var cleanFlags = isFolder ? "-fd" : "-f";
                var cleanOk = TryRunGitCommand(BuildGitArgs($"clean {cleanFlags}", gitPath), out _);
                return resetOk && cleanOk;
            }

            var checkoutOk = TryRunGitCommand(BuildGitArgs("checkout", gitPath), out _);
            return resetOk && checkoutOk;
        }

        internal static bool UnstageAssets(IEnumerable<GitAssetInfo> assets, out string summary)
        {
            var list = assets?.ToList() ?? new List<GitAssetInfo>();
            list.RemoveAll(a => a == null || string.IsNullOrEmpty(a.AssetPath));

            if (list.Count == 0)
            {
                summary = "没有可移出的待提交项。";
                return false;
            }

            var total = 0;
            var succeeded = 0;

            foreach (var info in list)
            {
                total++;
                var gitPath = ConvertUnityPathToGitPath(info.AssetPath);
                if (string.IsNullOrEmpty(gitPath))
                {
                    continue;
                }

                var arguments = BuildGitArgs("reset HEAD", gitPath);
                Debug.Log($"[GitQuickCommit] Unstage: unityPath={info.AssetPath}, gitPath={gitPath}, args=git {arguments}");

                var unstagedOk = TryRunGitCommand(arguments, out _);
                if (unstagedOk &&
                    info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    var originalGitPath = ConvertUnityPathToGitPath(info.OriginalPath);
                    if (!string.IsNullOrEmpty(originalGitPath))
                    {
                        var originalArgs = BuildGitArgs("reset HEAD", originalGitPath);
                        Debug.Log($"[GitQuickCommit] Unstage: unityPath={info.OriginalPath}, gitPath={originalGitPath}, args=git {originalArgs}");
                        unstagedOk = TryRunGitCommand(originalArgs, out _);
                    }
                }

                if (unstagedOk)
                {
                    succeeded++;
                }
                else
                {
                    Debug.LogWarning($"[GitQuickCommit] Unstage failed for {gitPath}");
                }
            }

            if (total == 0)
            {
                summary = "没有可移出的待提交项。";
                return false;
            }

            summary = $"已从待提交移出: {succeeded}/{total} 个条目。";
            return succeeded > 0;
        }

        internal static bool Commit(string message, bool isChinese, out string summary)
        {
            summary = string.Empty;

            if (string.IsNullOrWhiteSpace(message))
            {
                summary = isChinese ? "提交说明不能为空。" : "Commit message cannot be empty.";
                return false;
            }

            var trimmed = message.Trim();
            var containsLineBreak = trimmed.IndexOfAny(new[] { '\r', '\n' }) >= 0;

            if (!TryRunGitCommand($"diff --cached --name-only", out var diffOutput))
            {
                summary = isChinese ? "无法检查待提交内容，请确认仓库状态。" : "Unable to check staged content. Please verify repository status.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(diffOutput))
            {
                summary = isChinese ? "当前没有已暂存的变更可提交。" : "No staged changes to commit.";
                return false;
            }

            string commitArgs;
            string tempMessageFilePath = null;
            try
            {
                if (!containsLineBreak)
                {
                    commitArgs = $"commit -m {QuoteGitArgument(trimmed)}";
                }
                else
                {
                    tempMessageFilePath = Path.Combine(Path.GetTempPath(), $"GitUCommitMessage_{Guid.NewGuid():N}.txt");
                    var normalized = trimmed.Replace("\r\n", "\n").Replace('\r', '\n');
                    File.WriteAllText(tempMessageFilePath, normalized, Encoding.UTF8);
                    commitArgs = $"commit -F {QuoteGitArgument(tempMessageFilePath)}";
                }

                if (!TryRunGitCommand(commitArgs, out var output, GitCommandTimeoutMediumMs))
                {
                    summary = isChinese ? "提交失败，请查看 Console 日志中的 Git 输出。" : "Commit failed. Please check the Git output in the Console.";
                    return false;
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempMessageFilePath))
                {
                    try
                    {
                        if (File.Exists(tempMessageFilePath))
                        {
                            File.Delete(tempMessageFilePath);
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            summary = isChinese ? "提交成功。" : "Commit successful.";
            return true;
        }

        internal static bool Push(bool isChinese, out string summary)
        {
            summary = string.Empty;

            if (!TryRunGitCommand("push", out var output, GitCommandTimeoutLongMs))
            {
                summary = isChinese ? "推送失败，请查看 Console 日志中的 Git 输出。" : "Push failed. Please check the Git output in the Console.";
                return false;
            }

            summary = isChinese ? "推送成功。" : "Push successful.";
            return true;
        }

        internal static List<string> GetRecentCommitMessages(int maxCount, string authorPattern = null, bool excludeMerges = false)
        {
            var results = new List<string>();
            if (maxCount <= 0)
            {
                return results;
            }

            var args = $"log -n {maxCount} --format=%s";
            if (excludeMerges)
            {
                args += " --no-merges";
            }
            if (!string.IsNullOrWhiteSpace(authorPattern))
            {
                args += $" --author={QuoteGitArgument(authorPattern.Trim())}";
            }

            if (!TryRunGitCommand(args, out var output))
            {
                return results;
            }

            var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    results.Add(trimmed);
                }
            }

            return results;
        }

        internal static List<string> GetRecentCommitMessagesForCurrentUser(int maxCount)
        {
            var authorPattern = GetCurrentUserAuthorPattern();
            if (string.IsNullOrWhiteSpace(authorPattern))
            {
                return new List<string>();
            }

            return GetRecentCommitMessages(maxCount, authorPattern, excludeMerges: true);
        }

        private static string GetCurrentUserAuthorPattern()
        {
            if (TryGetGitConfigValue("user.email", out var email) && !string.IsNullOrWhiteSpace(email))
            {
                return EscapeGitAuthorPattern(email.Trim());
            }

            if (TryGetGitConfigValue("user.name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return EscapeGitAuthorPattern(name.Trim());
            }

            return null;
        }

        private static bool TryGetGitConfigValue(string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!TryRunGitCommand($"config --get {key}", out var output))
            {
                return false;
            }

            var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(line => line.Trim())
                                  .FirstOrDefault(line => !string.IsNullOrEmpty(line));
            if (string.IsNullOrEmpty(firstLine))
            {
                return false;
            }

            value = firstLine;
            return true;
        }

        private static string EscapeGitAuthorPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return pattern;
            }

            return Regex.Escape(pattern);
        }

        private static string ResolveAbsolutePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            if (Path.IsPathRooted(relativePath))
            {
                return Path.GetFullPath(relativePath);
            }

            var projectFolder = GetUnityProjectFolder();
            return Path.GetFullPath(Path.Combine(projectFolder, relativePath));
        }

        private static void AddPathAndMeta(HashSet<string> set, string path)
        {
            if (set == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            set.Add(path);
            if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                set.Add($"{path}.meta");
            }
        }

        internal static GitRepositoryStatusInfo GetRepositoryStatusInfo()
        {
            var changes = GetWorkingTreeChanges();
            if (changes == null || changes.Count == 0)
            {
                return new GitRepositoryStatusInfo(0, 0);
            }

            var staged = 0;
            var unstaged = 0;
            foreach (var entry in changes)
            {
                if (entry.IsStaged)
                {
                    staged++;
                }

                if (entry.IsUnstaged)
                {
                    unstaged++;
                }
            }

            return new GitRepositoryStatusInfo(staged, unstaged);
        }
    }
}
