using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneKey.GitTools
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

    [Serializable]
    internal class GitAssetInfo
    {
        public string AssetPath;
        public GitChangeType ChangeType;
        public DateTime? LastCommitTime;
        public DateTime? WorkingTreeTime;
        public bool IsStaged;
        public bool IsUnstaged;

        public GitAssetInfo(string path, GitChangeType type, DateTime? lastTime, DateTime? workingTime, bool isStaged, bool isUnstaged)
        {
            AssetPath = path;
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
        public readonly GitChangeType ChangeType;
        public readonly DateTime? WorkingTreeTime;
        public readonly bool IsStaged;
        public readonly bool IsUnstaged;

        public GitChangeEntry(string path, GitChangeType type, DateTime? workingTreeTime, bool isStaged, bool isUnstaged)
        {
            Path = path;
            ChangeType = type;
            WorkingTreeTime = workingTreeTime;
            IsStaged = isStaged;
            IsUnstaged = isUnstaged;
        }
    }

    internal static class GitUtility
    {
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
                        gitRootNotFoundLogged = true;
                    }
                    else
                    {
                        gitRootNotFoundLogged = false;
                    }
                }

                return cachedProjectRoot;
            }
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

            if (!TryRunGitCommand("status --porcelain", out var output))
            {
                return result;
            }

            var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (line.Length < 4)
                {
                    continue;
                }

                var statusCode = line.Substring(0, 2);
                var pathSegment = line.Substring(3).Trim();
                if (string.IsNullOrEmpty(pathSegment))
                {
                    continue;
                }

                var normalizedPath = NormalizeStatusPath(pathSegment);
                if (string.IsNullOrEmpty(normalizedPath))
                {
                    continue;
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

                var unityRelativePath = ConvertGitPathToUnityPath(normalizedPath);
                if (string.IsNullOrEmpty(unityRelativePath))
                {
                    continue;
                }

                var workingTime = GetWorkingTreeTimestamp(unityRelativePath, changeType);
                result.Add(new GitChangeEntry(unityRelativePath, changeType, workingTime, isStaged, isUnstaged));
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
                dependencySet.Add(assetPath);
            }
            else
            {
                foreach (var dep in AssetDatabase.GetDependencies(assetPath, true))
                {
                    var normalized = NormalizeAssetPath(dep);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        dependencySet.Add(normalized);
                    }
                }

                dependencySet.Add(assetPath);
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

            if (!TryRunGitCommand($"log -1 --format=%ct -- \"{gitRelativePath}\"", out var output))
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

        private static bool TryRunGitCommand(string arguments, out string output)
        {
            output = string.Empty;

            var gitRoot = ProjectRoot;
            if (string.IsNullOrEmpty(gitRoot))
            {
                return false;
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = arguments,
                        WorkingDirectory = gitRoot,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Debug.LogWarning($"Git命令执行失败: git {arguments}\n{standardError}");
                    return false;
                }

                output = standardOutput;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"无法执行Git命令: {ex.Message}");
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

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "rev-parse --show-toplevel",
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    var firstLine = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstLine))
                    {
                        rootPath = Path.GetFullPath(firstLine.Trim());
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(stderr) && !gitRevParseWarningLogged)
                {
                    if (stderr.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Debug.LogWarning($"GitQuickCommit: rev-parse 失败: {stderr}");
                    }

                    gitRevParseWarningLogged = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GitQuickCommit: 调用 git rev-parse 失败: {ex.Message}");
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
                Debug.LogWarning($"GitQuickCommit: 跳过包含非法字符的Git路径: {gitRelativePath}");
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
                Debug.LogWarning($"GitQuickCommit: 无法组合Git路径 root={gitRoot}, rel={gitRelativePath}, error={ex.Message}");
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
                var gitPath = ConvertUnityPathToGitPath(info.AssetPath);
                if (string.IsNullOrEmpty(gitPath))
                {
                    continue;
                }

                string arguments;
                if (info.ChangeType == GitChangeType.Deleted)
                {
                    // 对已删除文件使用 -u 方式只更新已跟踪条目的删除状态
                    arguments = $"add -u -- \"{gitPath}\"";
                }
                else
                {
                    arguments = $"add -- \"{gitPath}\"";
                }

                Debug.Log($"[GitQuickCommit] Stage: type={info.ChangeType}, unityPath={info.AssetPath}, gitPath={gitPath}, args=git {arguments}");

                if (TryRunGitCommand(arguments, out _))
                {
                    succeeded++;
                }
                else
                {
                    Debug.LogWarning($"[GitQuickCommit] Stage failed for {gitPath}");
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

                var arguments = $"reset HEAD -- \"{gitPath}\"";
                Debug.Log($"[GitQuickCommit] Unstage: unityPath={info.AssetPath}, gitPath={gitPath}, args=git {arguments}");

                if (TryRunGitCommand(arguments, out _))
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

        internal static bool Commit(string message, out string summary)
        {
            summary = string.Empty;

            if (string.IsNullOrWhiteSpace(message))
            {
                summary = "提交说明不能为空。";
                return false;
            }

            var trimmed = message.Trim();
            // 避免双引号导致命令行解析问题，将其替换为单引号
            var safeMessage = trimmed.Replace("\"", "'");

            if (!TryRunGitCommand($"diff --cached --name-only", out var diffOutput))
            {
                summary = "无法检查待提交内容，请确认仓库状态。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(diffOutput))
            {
                summary = "当前没有已暂存的变更可提交。";
                return false;
            }

            if (!TryRunGitCommand($"commit -m \"{safeMessage}\"", out var output))
            {
                summary = "提交失败，请查看 Console 日志中的 Git 输出。";
                return false;
            }

            summary = "提交成功。";
            return true;
        }

        internal static bool Push(out string summary)
        {
            summary = string.Empty;

            if (!TryRunGitCommand("push", out var output))
            {
                summary = "推送失败，请查看 Console 日志中的 Git 输出。";
                return false;
            }

            summary = "推送成功。";
            return true;
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
    }
}
