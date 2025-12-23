using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace OneKey.GitTools
{
    internal class GitQuickCommitWindow : EditorWindow
    {
        private const string WindowTitle = "\u5feb\u6377Git\u63d0\u4ea4";
        private const string CommitHistoryFileName = "QuickGitCommitHistory.json";
        private const string LayoutAssetName = "GitQuickCommitWindow.uxml";
        private const int MaxCommitHistoryEntries = 20;
        private const string AddedColorHex = "#80D980";
        private const string ModifiedColorHex = "#F2BF66";
        private const string DeletedColorHex = "#E68080";
        private const string GitStatusFormat = "Git \u72b6\u6001\uff1a\u5f85\u63d0\u4ea4 {0}\uff0c\u672a\u6682\u5b58 {1}";
        private const string AddedSegmentFormat = "<color={0}>\u65b0\u589e {1}</color>";
        private const string ModifiedSegmentFormat = "<color={0}>\u4fee\u6539 {1}</color>";
        private const string DeletedSegmentFormat = "<color={0}>\u5220\u9664 {1}</color>";
        private static string cachedAssetFolderPath;

        private UnityEngine.Object targetAsset;
        private string targetAssetPath;
        private readonly List<GitAssetInfo> assetInfos = new List<GitAssetInfo>();
        private readonly HashSet<string> excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<GitChangeEntry> gitChanges = new List<GitChangeEntry>();
        private readonly HashSet<string> relevantPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> targetAssetPaths = new List<string>();
        private readonly List<string> targetFolderPrefixes = new List<string>();

        private Task<List<GitChangeEntry>> refreshTask;
        private bool refreshInProgress;
        private bool refreshQueued;
        private List<string> refreshInfoMessages;

        private enum GitOperationKind
        {
            None,
            Stage,
            Unstage,
            Commit,
            CommitAndPush
        }

        private readonly struct GitOperationResult
        {
            public readonly bool Success;
            public readonly string Summary;
            public readonly bool CommitSucceeded;
            public readonly string CommittedMessage;

            public GitOperationResult(bool success, string summary, bool commitSucceeded = false, string committedMessage = null)
            {
                Success = success;
                Summary = summary;
                CommitSucceeded = commitSucceeded;
                CommittedMessage = committedMessage;
            }
        }

        private Task<GitOperationResult> gitOperationTask;
        private bool gitOperationInProgress;
        private GitOperationKind gitOperationKind;

        // 选择状态：左侧未暂存 / 右侧已暂存
        private readonly HashSet<string> selectedUnstagedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> selectedStagedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> initialStagedPaths;
        private bool showAdded = true;
        private bool showModified = true;
        private bool showDeleted = true;
        private string startTimeInput = string.Empty;
        private string endTimeInput = string.Empty;
        private DateTime? startTimeFilter;
        private DateTime? endTimeFilter;
        private bool startTimeValid = true;
        private bool endTimeValid = true;
        private string searchQuery = string.Empty;
        private string statusMessage;
        private string commitMessage = string.Empty;
        private string repositoryStatusMessage = "\u6b63\u5728\u68c0\u6d4b Git \u72b6\u6001...";
        private List<string> commitHistory;
        private List<string> savedCommitHistory;
        private List<string> fallbackCommitHistory;
        private bool historyDropdownVisible;

        // UI Toolkit elements
        private ObjectField targetField;
        private Label pathLabel;
        private Label statusLabel;
        private Toggle toggleAdded;
        private Toggle toggleModified;
        private Toggle toggleDeleted;
        private TextField startTimeField;
        private TextField endTimeField;
        private DropdownField timePresetField;
        private TextField searchField;
        private Label unstagedHeaderLabel;
        private Label stagedHeaderLabel;
        private ScrollView unstagedScrollView;
        private ScrollView stagedScrollView;
        private TextField commitMessageField;
        private Button commitButton;
        private Button commitAndPushButton;
        private Button historyButton;
        private Button resetTimeButton;
        private Button refreshButton;
        private Button toStagedButton;
        private Button toUnstagedButton;
        private Button clearExcludeButton;
        private VisualElement historyDropdown;
        private ListView historyListView;
        private Label repositoryStatusLabel;
        private Toggle unstagedSelectAllToggle;
        private Toggle stagedSelectAllToggle;

        // Notification
        private double notificationEndTime;

        [MenuItem("Assets/\u5feb\u6377Git\u63d0\u4ea4", false, 2000)]
        private static void OpenFromContext()
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
            {
                return;
            }

            var assets = new List<UnityEngine.Object>();
            foreach (var obj in objects)
            {
                if (obj == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                assets.Add(obj);
            }

            if (assets.Count == 0)
            {
                return;
            }

            var window = GetWindow<GitQuickCommitWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(1040, 600);
            window.maxSize = new Vector2(1040, 1080);
            window.Initialize(assets);
            window.Show();
        }

        [MenuItem("Assets/\u5feb\u6377Git\u63d0\u4ea4", true)]
        private static bool ValidateOpenFromContext()
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
            {
                return false;
            }

            foreach (var obj in objects)
            {
                if (obj == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    return true;
                }
            }

            return false;
        }

        [MenuItem("GameObject/\u5feb\u6377Git\u63d0\u4ea4", false, 2000)]
        private static void OpenFromGameObject(MenuCommand command)
        {
            var gameObjects = Selection.gameObjects;
            if (gameObjects == null || gameObjects.Length == 0)
            {
                return;
            }

            var assets = new List<UnityEngine.Object>();
            foreach (var go in gameObjects)
            {
                var asset = ResolvePrefabAssetFromGameObject(go);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            if (assets.Count == 0)
            {
                foreach (var go in gameObjects)
                {
                    var asset = ResolveSceneAssetFromGameObject(go);
                    if (asset != null)
                    {
                        assets.Add(asset);
                    }
                }

                if (assets.Count == 0)
                {
                    return;
                }
            }

            var window = GetWindow<GitQuickCommitWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(1040, 600);
            window.maxSize = new Vector2(1040, 1080);
            window.Initialize(assets);
            window.Show();
        }

        [MenuItem("GameObject/\u5feb\u6377Git\u63d0\u4ea4", true)]
        private static bool ValidateOpenFromGameObject(MenuCommand command)
        {
            var gameObjects = Selection.gameObjects;
            if (gameObjects == null || gameObjects.Length == 0)
            {
                return false;
            }

            foreach (var go in gameObjects)
            {
                if (ResolvePrefabAssetFromGameObject(go) != null || ResolveSceneAssetFromGameObject(go) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static UnityEngine.Object ResolvePrefabAssetFromGameObject(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
            }

            return null;
        }

        private static UnityEngine.Object ResolveSceneAssetFromGameObject(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            var scenePath = go.scene.path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath);
            }

            return null;
        }

        private void Initialize(IEnumerable<UnityEngine.Object> assets)
        {
            excludedPaths.Clear();
            SetTargetAssets(assets);
            RefreshData();
        }

        private static List<UnityEngine.Object> GetTargetsFromCurrentSelection()
        {
            var targets = new List<UnityEngine.Object>();

            var objects = Selection.objects;
            if (objects != null && objects.Length > 0)
            {
                foreach (var obj in objects)
                {
                    if (obj == null)
                    {
                        continue;
                    }

                    var path = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    targets.Add(obj);
                }
            }

            var gameObjects = Selection.gameObjects;
            if (gameObjects != null && gameObjects.Length > 0)
            {
                foreach (var go in gameObjects)
                {
                    var prefab = ResolvePrefabAssetFromGameObject(go);
                    if (prefab != null)
                    {
                        targets.Add(prefab);
                    }
                }

                if (targets.Count == 0)
                {
                    foreach (var go in gameObjects)
                    {
                        var scene = ResolveSceneAssetFromGameObject(go);
                        if (scene != null)
                        {
                            targets.Add(scene);
                            break;
                        }
                    }
                }
            }

            return targets;
        }

        private void SetTargetAssets(IEnumerable<UnityEngine.Object> assets)
        {
            targetAssetPaths.Clear();
            targetFolderPrefixes.Clear();

            if (assets == null)
            {
                targetAsset = null;
                targetAssetPath = string.Empty;
                GitUtility.SetContextAssetPath(null);
                return;
            }

            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UnityEngine.Object singleAsset = null;

            foreach (var asset in assets)
            {
                if (asset == null)
                {
                    continue;
                }

                var path = GitUtility.NormalizeAssetPath(AssetDatabase.GetAssetPath(asset));
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (!uniquePaths.Add(path))
                {
                    continue;
                }

                targetAssetPaths.Add(path);
                if (singleAsset == null)
                {
                    singleAsset = asset;
                }
            }

            if (targetAssetPaths.Count == 1)
            {
                targetAsset = singleAsset;
                targetAssetPath = targetAssetPaths[0];
                GitUtility.SetContextAssetPath(targetAssetPath);
                return;
            }

            targetAsset = null;
            targetAssetPath = string.Empty;
            GitUtility.SetContextAssetPath(targetAssetPaths.Count == 0 ? null : GetCommonContextAssetPath(targetAssetPaths));
        }

        private static string GetCommonContextAssetPath(IReadOnlyList<string> unityAssetPaths)
        {
            if (unityAssetPaths == null || unityAssetPaths.Count == 0)
            {
                return null;
            }

            var folders = new List<string>(unityAssetPaths.Count);
            foreach (var path in unityAssetPaths)
            {
                var normalized = GitUtility.NormalizeAssetPath(path);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                if (AssetDatabase.IsValidFolder(normalized))
                {
                    folders.Add(normalized);
                    continue;
                }

                var dir = GetFolderPath(normalized);
                folders.Add(string.IsNullOrEmpty(dir) ? "Assets" : dir);
            }

            if (folders.Count == 0)
            {
                return "Assets";
            }

            var common = folders[0];
            for (var i = 1; i < folders.Count; i++)
            {
                common = GetCommonPathPrefix(common, folders[i]);
                if (string.IsNullOrEmpty(common) || common.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    return "Assets";
                }
            }

            return string.IsNullOrEmpty(common) ? "Assets" : common;
        }

        private static string GetCommonPathPrefix(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return "Assets";
            }

            left = GitUtility.NormalizeAssetPath(left);
            right = GitUtility.NormalizeAssetPath(right);

            var leftParts = left.Split('/');
            var rightParts = right.Split('/');

            var count = Math.Min(leftParts.Length, rightParts.Length);
            var matched = 0;
            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                matched++;
            }

            if (matched <= 0)
            {
                return "Assets";
            }

            var prefix = string.Join("/", leftParts.Take(matched));
            return string.IsNullOrEmpty(prefix) ? "Assets" : prefix;
        }

        private void OnEnable()
        {
            // For domain reload / layout reload, rebuild UI
            CreateGUI();

            // When the window is restored by Unity (layout/domain reload), re-initialize from current selection.
            // Never fall back to "show all repo changes" by default.
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }

                if (refreshInProgress || gitOperationInProgress)
                {
                    return;
                }

                if (targetAssetPaths.Count == 0)
                {
                    SetTargetAssets(GetTargetsFromCurrentSelection());
                }

                if (targetAssetPaths.Count > 0)
                {
                    RefreshData();
                }
                else
                {
                    assetInfos.Clear();
                    statusMessage = "请先在 Project 或 Hierarchy 选择目标资源，再打开窗口（或在顶部“目标资源”里选择）。";
                    UpdateHeaderLabels();
                    RefreshListViews();
                    ForceRepaintUI();
                }
            };
        }

        private void OnDisable()
        {
            refreshInProgress = false;
            refreshQueued = false;
            refreshInfoMessages = null;
            refreshTask = null;

            gitOperationInProgress = false;
            gitOperationKind = GitOperationKind.None;
            gitOperationTask = null;
        }

        private void Update()
        {
            if (notificationEndTime > 0 && EditorApplication.timeSinceStartup >= notificationEndTime)
            {
                RemoveNotification();
                notificationEndTime = 0;
            }

            PollRefreshTask();
            PollGitOperationTask();
        }

        private void OnFocus()
        {
        }

        private void OnGUI()
        {
            // UI 完全使用 UI Toolkit 构建，如需 IMGUI 可在此扩展。
        }

        private void ForceRepaintUI()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            rootVisualElement?.MarkDirtyRepaint();
            Repaint();
        }

        private void RequestAssetDatabaseRefreshAndRefreshData()
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }

                try
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GitQuickCommit] AssetDatabase.Refresh failed: {ex.Message}");
                }

                RefreshData();
                ForceRepaintUI();
            };
        }

        private void ShowTempNotification(string message, float seconds = 2f)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            ShowNotification(new GUIContent(message));
            notificationEndTime = EditorApplication.timeSinceStartup + seconds;
        }

        private void CreateGUI()
        {
            historyDropdownVisible = false;

            var root = rootVisualElement;
            root.Clear();

            if (!BuildLayoutFromUxml(root))
            {
                return;
            }

            if (targetField != null)
            {
                targetField.objectType = typeof(UnityEngine.Object);
                targetField.allowSceneObjects = false;
                targetField.value = targetAsset;
                targetField.RegisterValueChangedCallback(evt =>
                {
                    excludedPaths.Clear();
                    SetTargetAssets(evt.newValue != null ? new[] { evt.newValue } : null);
                    RefreshData();
                });
            }

            if (clearExcludeButton != null)
            {
                clearExcludeButton.clicked += () =>
                {
                    excludedPaths.Clear();
                    RefreshListViews();
                };
            }

            if (toggleAdded != null)
            {
                toggleAdded.SetValueWithoutNotify(showAdded);
                toggleAdded.RegisterValueChangedCallback(evt => { showAdded = evt.newValue; RefreshListViews(); });
            }

            if (toggleModified != null)
            {
                toggleModified.SetValueWithoutNotify(showModified);
                toggleModified.RegisterValueChangedCallback(evt => { showModified = evt.newValue; RefreshListViews(); });
            }

            if (toggleDeleted != null)
            {
                toggleDeleted.SetValueWithoutNotify(showDeleted);
                toggleDeleted.RegisterValueChangedCallback(evt => { showDeleted = evt.newValue; RefreshListViews(); });
            }
            var presetChoices = new List<string>
            {
                "\u4e0d\u9650",
                "1 \u5c0f\u65f6\u5185",
                "5 \u5c0f\u65f6\u5185",
                "1 \u5929\u5185",
                "2 \u5929\u5185",
                "5 \u5929\u5185"
            };
            if (timePresetField != null)
            {
                timePresetField.choices = presetChoices;
                timePresetField.index = 0;
                timePresetField.RegisterValueChangedCallback(evt =>
                {
                    switch (timePresetField.index)
                    {
                        case 1:
                            ApplyQuickRange(TimeSpan.FromHours(1));
                            break;
                        case 2:
                            ApplyQuickRange(TimeSpan.FromHours(5));
                            break;
                        case 3:
                            ApplyQuickRange(TimeSpan.FromDays(1));
                            break;
                        case 4:
                            ApplyQuickRange(TimeSpan.FromDays(2));
                            break;
                        case 5:
                            ApplyQuickRange(TimeSpan.FromDays(5));
                            break;
                        default:
                            startTimeInput = string.Empty;
                            endTimeInput = string.Empty;
                            startTimeFilter = null;
                            endTimeFilter = null;
                            startTimeValid = true;
                            endTimeValid = true;
                            if (startTimeField != null) startTimeField.value = string.Empty;
                            if (endTimeField != null) endTimeField.value = string.Empty;
                            RefreshListViews();
                            break;
                    }
                });
            }
            if (startTimeField != null)
            {
                startTimeField.value = startTimeInput;
                startTimeField.RegisterValueChangedCallback(evt =>
                {
                    startTimeInput = evt.newValue;
                    startTimeFilter = TryParseDateTime(startTimeInput, out startTimeValid);
                    RefreshListViews();
                });
            }
            if (endTimeField != null)
            {
                endTimeField.value = endTimeInput;
                endTimeField.RegisterValueChangedCallback(evt =>
                {
                    endTimeInput = evt.newValue;
                    endTimeFilter = TryParseDateTime(endTimeInput, out endTimeValid);
                    RefreshListViews();
                });
            }
            if (resetTimeButton != null)
            {
                resetTimeButton.clicked += () =>
                {
                    startTimeInput = string.Empty;
                    endTimeInput = string.Empty;
                    startTimeFilter = null;
                    endTimeFilter = null;
                    startTimeValid = true;
                    endTimeValid = true;
                    if (startTimeField != null) startTimeField.value = string.Empty;
                    if (endTimeField != null) endTimeField.value = string.Empty;
                    if (timePresetField != null) timePresetField.index = 0;
                    RefreshListViews();
                };
            }
            if (searchField != null)
            {
                searchField.value = searchQuery;
                searchField.RegisterValueChangedCallback(evt =>
                {
                    searchQuery = evt.newValue ?? string.Empty;
                    RefreshListViews();
                });
            }
            if (refreshButton != null)
            {
                refreshButton.clicked += () => { RefreshData(); };
            }
            if (unstagedSelectAllToggle != null)
            {
                unstagedSelectAllToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        selectedUnstagedPaths.Clear();
                        foreach (var info in EnumerateFilteredAssets(false))
                        {
                            selectedUnstagedPaths.Add(info.AssetPath);
                        }
                    }
                    else
                    {
                        selectedUnstagedPaths.Clear();
                    }
                    RefreshListViews();
                });
            }
            if (stagedSelectAllToggle != null)
            {
                stagedSelectAllToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        selectedStagedPaths.Clear();
                        foreach (var info in EnumerateFilteredAssets(true))
                        {
                            selectedStagedPaths.Add(info.AssetPath);
                        }
                    }
                    else
                    {
                        selectedStagedPaths.Clear();
                    }
                    RefreshListViews();
                });
            }
            if (toStagedButton != null)
            {
                toStagedButton.clicked += () => { StageSelectedUnstaged(); };
            }
            if (toUnstagedButton != null)
            {
                toUnstagedButton.clicked += () => { UnstageSelectedStaged(); };
            }
            if (commitMessageField != null)
            {
                commitMessageField.value = commitMessage;
                commitMessageField.RegisterValueChangedCallback(evt =>
                {
                    commitMessage = evt.newValue;
                    UpdateCommitButtonsEnabled();
                });
            }
            if (historyButton != null)
            {
                historyButton.clicked += ToggleHistoryDropdown;
            }
            if (historyListView != null)
            {
                historyListView.selectionType = SelectionType.Single;
                historyListView.makeItem = () =>
                {
                    return new Label();
                };
                historyListView.bindItem = (element, i) =>
                {
                    if (element is Label label)
                    {
                        var hasEntries = commitHistory != null && i >= 0 && i < commitHistory.Count;
                        label.text = hasEntries ? commitHistory[i] : string.Empty;
                    }
                };
                historyListView.onSelectionChange += OnHistoryListSelectionChanged;
            }
            root.RegisterCallback<MouseDownEvent>(OnRootMouseDown, TrickleDown.TrickleDown);
            UpdateHistoryButtonState();
            if (commitButton != null)
            {
                commitButton.clicked += () => { PerformCommit(false); };
            }
            if (commitAndPushButton != null)
            {
                commitAndPushButton.clicked += () => { PerformCommit(true); };
            }
            if (repositoryStatusLabel != null)
            {
                repositoryStatusLabel.text = repositoryStatusMessage;
            }
            UpdateHeaderLabels();
            UpdateCommitButtonsEnabled();
            RefreshListViews();
        }

        private void PollRefreshTask()
        {
            if (refreshTask == null || !refreshTask.IsCompleted)
            {
                return;
            }

            var completedTask = refreshTask;
            var completedInfoMessages = refreshInfoMessages ?? new List<string>();
            refreshTask = null;
            refreshInfoMessages = null;

            refreshInProgress = false;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            if (refreshQueued)
            {
                refreshQueued = false;
                RefreshData();
                return;
            }

            if (completedTask.IsCanceled)
            {
                statusMessage = "刷新已取消。";
                UpdateHeaderLabels();
                UpdateCommitButtonsEnabled();
                ForceRepaintUI();
                return;
            }

            if (completedTask.IsFaulted)
            {
                var error = completedTask.Exception?.GetBaseException().Message ?? "未知错误";
                statusMessage = $"刷新失败：{error}";
                UpdateHeaderLabels();
                UpdateCommitButtonsEnabled();
                ForceRepaintUI();
                return;
            }

            ApplyGitChanges(completedTask.Result ?? new List<GitChangeEntry>(), completedInfoMessages);
        }

        private void PollGitOperationTask()
        {
            if (gitOperationTask == null || !gitOperationTask.IsCompleted)
            {
                return;
            }

            var completedTask = gitOperationTask;
            var completedKind = gitOperationKind;

            gitOperationTask = null;
            gitOperationKind = GitOperationKind.None;

            gitOperationInProgress = false;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            if (refreshQueued)
            {
                refreshQueued = false;
                RefreshData();
                return;
            }

            if (completedTask.IsCanceled)
            {
                ShowTempNotification("操作已取消。");
                return;
            }

            if (completedTask.IsFaulted)
            {
                var error = completedTask.Exception?.GetBaseException().Message ?? "未知错误";
                ShowTempNotification($"操作失败：{error}");
                return;
            }

            var result = completedTask.Result;
            if (completedKind == GitOperationKind.Commit || completedKind == GitOperationKind.CommitAndPush)
            {
                EditorUtility.DisplayDialog("提交", string.IsNullOrEmpty(result.Summary) ? "操作完成。" : result.Summary, "确定");

                if (result.CommitSucceeded)
                {
                    if (!string.IsNullOrWhiteSpace(result.CommittedMessage))
                    {
                        AddCommitMessageToHistory(result.CommittedMessage);
                    }

                    commitMessage = string.Empty;
                    if (commitMessageField != null)
                    {
                        commitMessageField.value = string.Empty;
                    }

                    HideHistoryDropdown();
                }
            }
            else if (!string.IsNullOrEmpty(result.Summary))
            {
                ShowTempNotification(result.Summary);
            }

            if (completedKind == GitOperationKind.Stage)
            {
                selectedUnstagedPaths.Clear();
            }
            else if (completedKind == GitOperationKind.Unstage)
            {
                selectedStagedPaths.Clear();
            }

            RefreshData();
        }

        private bool BuildLayoutFromUxml(VisualElement root)
        {
            var uxmlPath = Path.Combine(GetAssetFolderPath(), LayoutAssetName).Replace("\\", "/");
            var layoutAsset = LoadAssetAtPathOrByName<VisualTreeAsset>(uxmlPath, Path.GetFileNameWithoutExtension(LayoutAssetName));
            if (layoutAsset == null)
            {
                ShowLayoutLoadError(root, $"无法加载布局文件: {uxmlPath}\n请确认该文件已导入为 VisualTreeAsset。");
                return false;
            }

            var layoutInstance = layoutAsset.CloneTree();
            root.Add(layoutInstance);

            CacheUIElements(root);
            return true;
        }

        private static T LoadAssetAtPathOrByName<T>(string assetPath, string nameWithoutExtension) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            var typeName = typeof(T).Name;
            var guids = AssetDatabase.FindAssets($"{nameWithoutExtension} t:{typeName}");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            foreach (var guid in guids)
            {
                var candidatePath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(candidatePath))
                {
                    continue;
                }

                var candidate = AssetDatabase.LoadAssetAtPath<T>(candidatePath);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void ShowLayoutLoadError(VisualElement root, string message)
        {
            if (root == null)
            {
                return;
            }

            var label = new Label(message);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.paddingLeft = 8;
            label.style.paddingRight = 8;
            label.style.paddingTop = 8;
            label.style.paddingBottom = 8;
            root.Add(label);
        }

        private void CacheUIElements(VisualElement root)
        {
            targetField = root.Q<ObjectField>("targetField");
            pathLabel = root.Q<Label>("pathLabel");
            statusLabel = root.Q<Label>("statusLabel");
            toggleAdded = root.Q<Toggle>("toggleAdded");
            toggleModified = root.Q<Toggle>("toggleModified");
            toggleDeleted = root.Q<Toggle>("toggleDeleted");
            startTimeField = root.Q<TextField>("startTimeField");
            endTimeField = root.Q<TextField>("endTimeField");
            timePresetField = root.Q<DropdownField>("timePresetField");
            searchField = root.Q<TextField>("searchField");
            unstagedHeaderLabel = root.Q<Label>("unstagedHeaderLabel");
            stagedHeaderLabel = root.Q<Label>("stagedHeaderLabel");
            unstagedScrollView = root.Q<ScrollView>("unstagedScrollView");
            stagedScrollView = root.Q<ScrollView>("stagedScrollView");
            commitMessageField = root.Q<TextField>("commitMessageField");
            commitButton = root.Q<Button>("commitButton");
            commitAndPushButton = root.Q<Button>("commitAndPushButton");
            historyButton = root.Q<Button>("historyButton");
            resetTimeButton = root.Q<Button>("resetTimeButton");
            refreshButton = root.Q<Button>("refreshButton");
            toStagedButton = root.Q<Button>("toStagedButton");
            toUnstagedButton = root.Q<Button>("toUnstagedButton");
            clearExcludeButton = root.Q<Button>("clearExcludeButton");
            historyDropdown = root.Q<VisualElement>("historyDropdown");
            historyListView = root.Q<ListView>("historyListView");
            repositoryStatusLabel = root.Q<Label>("repositoryStatusLabel");
            unstagedSelectAllToggle = root.Q<Toggle>("unstagedSelectAllToggle");
            stagedSelectAllToggle = root.Q<Toggle>("stagedSelectAllToggle");
        }

        private void StageSelectedUnstaged()
        {
            var paths = selectedUnstagedPaths.ToList();
            if (paths.Count == 0)
            {
                ShowTempNotification("请先在左侧勾选要发送的变更。");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification("正在执行其他操作，请稍候。");
                return;
            }

            var toStage = assetInfos
                .Where(a => !a.IsStaged && a.IsUnstaged && selectedUnstagedPaths.Contains(a.AssetPath))
                .ToList();

            if (toStage.Count == 0)
            {
                ShowTempNotification("当前没有可发送的变更。");
                return;
            }

            var gitRoot = GitUtility.ProjectRoot;
            _ = GitUtility.UnityProjectFolder;
            if (string.IsNullOrEmpty(gitRoot))
            {
                ShowTempNotification("未找到 Git 根目录。");
                return;
            }

            var requests = new List<GitUtility.GitStageRequest>(toStage.Count);
            foreach (var info in toStage)
            {
                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var gitPath))
                {
                    requests.Add(new GitUtility.GitStageRequest(gitPath, info.ChangeType));
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalGitPath))
                {
                    requests.Add(new GitUtility.GitStageRequest(originalGitPath, GitChangeType.Deleted));
                }
            }

            if (requests.Count == 0)
            {
                ShowTempNotification("当前没有可发送的变更。");
                return;
            }

            gitOperationInProgress = true;
            gitOperationKind = GitOperationKind.Stage;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = "正在发送至待提交...";
            UpdateHeaderLabels();
            ForceRepaintUI();

            gitOperationTask = Task.Run(() =>
            {
                var success = GitUtility.StageGitPaths(gitRoot, requests, out var summary);
                return new GitOperationResult(success, summary);
            });
            gitOperationTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void UnstageSelectedStaged()
        {
            var paths = selectedStagedPaths.ToList();
            if (paths.Count == 0)
            {
                ShowTempNotification("请先在右侧勾选要移出的变更。");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification("正在执行其他操作，请稍候。");
                return;
            }

            var toUnstage = assetInfos
                .Where(a => a.IsStaged && selectedStagedPaths.Contains(a.AssetPath))
                .ToList();

            if (toUnstage.Count == 0)
            {
                ShowTempNotification("当前没有可移出的待提交项。");
                return;
            }

            var gitRoot = GitUtility.ProjectRoot;
            _ = GitUtility.UnityProjectFolder;
            if (string.IsNullOrEmpty(gitRoot))
            {
                ShowTempNotification("未找到 Git 根目录。");
                return;
            }

            var requests = new List<string>(toUnstage.Count);
            foreach (var info in toUnstage)
            {
                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var gitPath))
                {
                    requests.Add(gitPath);
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalGitPath))
                {
                    requests.Add(originalGitPath);
                }
            }

            if (requests.Count == 0)
            {
                ShowTempNotification("当前没有可移出的待提交项。");
                return;
            }

            gitOperationInProgress = true;
            gitOperationKind = GitOperationKind.Unstage;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = "正在从待提交移出...";
            UpdateHeaderLabels();
            ForceRepaintUI();

            gitOperationTask = Task.Run(() =>
            {
                var success = GitUtility.UnstageGitPaths(gitRoot, requests, out var summary);
                return new GitOperationResult(success, summary);
            });
            gitOperationTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private IEnumerable<GitAssetInfo> EnumerateFilteredAssets()
        {
            return EnumerateFilteredAssets(false);
        }

        private IEnumerable<GitAssetInfo> EnumerateFilteredAssets(bool stagedView)
        {
            foreach (var info in assetInfos)
            {
                if (stagedView)
                {
                    if (!info.IsStaged)
                    {
                        continue;
                    }
                }
                else
                {
                    if (!info.IsUnstaged)
                    {
                        continue;
                    }

                    if (!IsRelevantForCurrentTarget(info))
                    {
                        continue;
                    }
                }

                if (excludedPaths.Contains(info.AssetPath) ||
                    (!string.IsNullOrEmpty(info.OriginalPath) && excludedPaths.Contains(info.OriginalPath)))
                {
                    continue;
                }

                if (!IsChangeTypeVisible(info.ChangeType))
                {
                    continue;
                }

                if (!IsWithinTimeRange(info.WorkingTreeTime))
                {
                    continue;
                }

                if (!IsMatchSearchQuery(info))
                {
                    continue;
                }

                yield return info;
            }
        }

        private bool IsMatchSearchQuery(GitAssetInfo info)
        {
            if (info == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                return true;
            }

            var query = searchQuery.Trim();
            if (query.Length == 0)
            {
                return true;
            }

            var name = info.FileName ?? string.Empty;
            return name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsChangeTypeVisible(GitChangeType type)
        {
            return type switch
            {
                GitChangeType.Added => showAdded,
                GitChangeType.Deleted => showDeleted,
                GitChangeType.Modified => showModified,
                GitChangeType.Renamed => showModified,
                _ => true
            };
        }

        private bool IsWithinTimeRange(DateTime? time)
        {
            if (!startTimeFilter.HasValue && !endTimeFilter.HasValue)
            {
                return true;
            }

            if (!time.HasValue)
            {
                return true;
            }

            if (startTimeFilter.HasValue && time.Value < startTimeFilter.Value)
            {
                return false;
            }

            if (endTimeFilter.HasValue && time.Value > endTimeFilter.Value)
            {
                return false;
            }

            return true;
        }

        private void RefreshData()
        {
            if (refreshInProgress || gitOperationInProgress)
            {
                refreshQueued = true;
                return;
            }

            assetInfos.Clear();
            statusMessage = string.Empty;
            selectedUnstagedPaths.Clear();
            selectedStagedPaths.Clear();
            HideHistoryDropdown();
            relevantPaths.Clear();
            targetFolderPrefixes.Clear();

            var infoMessages = new List<string>();

            if (targetAssetPaths.Count == 0)
            {
                targetAssetPath = string.Empty;
                GitUtility.SetContextAssetPath(null);
                statusMessage = "请先在 Project 或 Hierarchy 选择目标资源，再打开窗口（或在顶部“目标资源”里选择）。";
                UpdateHeaderLabels();
                RefreshListViews();
                ForceRepaintUI();
                return;
            }
            else
            {
                GitUtility.SetContextAssetPath(GetCommonContextAssetPath(targetAssetPaths));
                targetAssetPath = targetAssetPaths.Count == 1 ? targetAssetPaths[0] : string.Empty;

                if (targetAssetPaths.Count == 1 && string.IsNullOrEmpty(targetAssetPath))
                {
                    infoMessages.Add("\u65e0\u6cd5\u89e3\u6790\u8d44\u6e90\u8def\u5f84\u3002");
                }
            }

            refreshInProgress = true;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            var gitRoot = GitUtility.ProjectRoot;
            if (string.IsNullOrEmpty(gitRoot))
            {
                refreshInProgress = false;
                UpdateActionButtonsEnabled();
                UpdateCommitButtonsEnabled();

                infoMessages.Add("未找到 Git 根目录。");
                statusMessage = infoMessages.Count > 0 ? string.Join("\n", infoMessages) : string.Empty;
                UpdateHeaderLabels();
                RefreshListViews();
                ForceRepaintUI();
                return;
            }

            _ = GitUtility.UnityProjectFolder;
            refreshInfoMessages = infoMessages;

            statusMessage = "正在刷新 Git 状态...";
            UpdateHeaderLabels();
            RefreshListViews();
            ForceRepaintUI();

            refreshTask = Task.Run(() => GitUtility.GetWorkingTreeChanges() ?? new List<GitChangeEntry>());
            refreshTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void ApplyGitChanges(List<GitChangeEntry> changes, List<string> infoMessages)
        {
            gitChanges = changes ?? new List<GitChangeEntry>();

            if (gitChanges.Count == 0)
            {
                infoMessages.Add("Git 未检测到任何变更。");
            }
            else if (targetAssetPaths.Count > 0)
            {
                UpdateTargetSelectionInfo(gitChanges);
            }

            assetInfos.Clear();
            foreach (var change in gitChanges)
            {
                var historyPath = string.IsNullOrEmpty(change.OriginalPath) ? change.Path : change.OriginalPath;
                var lastTime = GitUtility.GetLastKnownChangeTime(historyPath);
                assetInfos.Add(new GitAssetInfo(change.Path, change.OriginalPath, change.ChangeType, lastTime, change.WorkingTreeTime, change.IsStaged, change.IsUnstaged));
            }

            assetInfos.Sort((a, b) => string.Compare(a.AssetPath, b.AssetPath, StringComparison.OrdinalIgnoreCase));
            CaptureInitialStagedSnapshotIfNeeded();

            if (targetField != null)
            {
                targetField.SetValueWithoutNotify(targetAsset);
            }

            UpdateRepositoryStatusInfo();

            if (targetAssetPaths.Count > 0)
            {
                var hasRelevantUnstaged = EnumerateFilteredAssets(false).Any();
                if (!hasRelevantUnstaged && gitChanges.Count > 0)
                {
                    infoMessages.Add("当前资源暂无相关变更。");
                }
            }

            statusMessage = infoMessages.Count > 0 ? string.Join("\n", infoMessages) : string.Empty;
            UpdateHeaderLabels();
            UpdateCommitButtonsEnabled();
            RefreshListViews();
            ForceRepaintUI();
        }

        private void CaptureInitialStagedSnapshotIfNeeded()
        {
            if (initialStagedPaths != null)
            {
                return;
            }

            initialStagedPaths = new HashSet<string>(assetInfos.Where(a => a.IsStaged).Select(a => a.AssetPath), StringComparer.OrdinalIgnoreCase);
        }

        private void SendVisibleChangesToStage()
        {
            var toStage = EnumerateFilteredAssets(false).ToList();
            if (toStage.Count == 0)
            {
                EditorUtility.DisplayDialog("发送至待提交", "当前没有可发送的变更。", "确定");
                return;
            }

            var success = GitUtility.StageAssets(toStage, out var summary);
            EditorUtility.DisplayDialog("发送至待提交", summary, "确定");

            if (success)
            {
                RefreshData();
            }
        }

        private void UpdateHistoryButtonState()
        {
            if (historyButton == null)
            {
                return;
            }

            EnsureCommitHistoryLoaded();
            var hasHistory = HasCommitHistory();
            historyButton.SetEnabled(hasHistory);
            if (!hasHistory)
            {
                HideHistoryDropdown();
            }
        }

        private void ToggleHistoryDropdown()
        {
            if (historyDropdown == null || historyListView == null)
            {
                return;
            }

            EnsureCommitHistoryLoaded();
            RefreshFallbackCommitHistory();
            RebuildCommitHistoryDisplay();
            if (!HasCommitHistory())
            {
                EditorUtility.DisplayDialog("提交记录", "暂无提交记录", "确定");
                return;
            }

            if (historyDropdownVisible)
            {
                HideHistoryDropdown();
                return;
            }

            historyDropdownVisible = true;
            historyDropdown.style.display = DisplayStyle.Flex;
            historyListView.itemsSource = commitHistory;
            historyListView.RefreshItems();
            historyListView.ClearSelection();
        }

        private void HideHistoryDropdown()
        {
            if (historyDropdown == null)
            {
                return;
            }

            historyDropdownVisible = false;
            historyDropdown.style.display = DisplayStyle.None;
            historyListView?.ClearSelection();
        }

        private void OnHistoryListSelectionChanged(IEnumerable<object> items)
        {
            var selected = items?.FirstOrDefault();
            if (selected == null)
            {
                return;
            }

            ApplyCommitHistory(selected.ToString());
            HideHistoryDropdown();
        }

        private void OnRootMouseDown(MouseDownEvent evt)
        {
            if (!historyDropdownVisible || historyDropdown == null || historyButton == null)
            {
                return;
            }

            if (IsDescendantOf(evt.target as VisualElement, historyDropdown) ||
                IsDescendantOf(evt.target as VisualElement, historyButton))
            {
                return;
            }

            HideHistoryDropdown();
        }

        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            while (element != null)
            {
                if (element == ancestor)
                {
                    return true;
                }

                element = element.parent;
            }

            return false;
        }

        private bool HasCommitHistory()
        {
            return commitHistory != null && commitHistory.Count > 0;
        }

        private void ApplyCommitHistory(string entry)
        {
            if (string.IsNullOrEmpty(entry))
            {
                return;
            }

            commitMessage = entry;
            if (commitMessageField != null)
            {
                commitMessageField.SetValueWithoutNotify(entry);
            }
            UpdateCommitButtonsEnabled();
            HideHistoryDropdown();
            ForceRepaintUI();
        }

        private void EnsureCommitHistoryLoaded()
        {
            if (commitHistory != null)
            {
                return;
            }

            commitHistory = new List<string>();
            var path = GetCommitHistoryFilePath();
            savedCommitHistory = new List<string>();
            fallbackCommitHistory = new List<string>();

            if (File.Exists(path))
            {
                string json;
                try
                {
                    json = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"GitQuickCommit: 读取提交记录失败: {ex.Message}");
                    json = null;
                }

                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        var data = JsonUtility.FromJson<CommitHistoryData>(json);
                        if (data?.entries != null)
                        {
                            foreach (var entry in data.entries)
                            {
                                if (!string.IsNullOrWhiteSpace(entry))
                                {
                                    savedCommitHistory.Add(entry.Trim());
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"GitQuickCommit: 解析提交记录失败: {ex.Message}");
                        savedCommitHistory.Clear();
                    }
                }
            }

            RefreshFallbackCommitHistory();
            FilterSavedCommitHistoryToCurrentUser();
            RebuildCommitHistoryDisplay();
        }

        private void RefreshFallbackCommitHistory()
        {
            try
            {
                fallbackCommitHistory = GitUtility.GetRecentCommitMessagesForCurrentUser(MaxCommitHistoryEntries) ?? new List<string>();
            }
            catch (Exception ex)
            {
                fallbackCommitHistory = new List<string>();
                Debug.LogWarning($"GitQuickCommit: 读取Git提交记录失败: {ex.Message}");
            }
        }

        private void FilterSavedCommitHistoryToCurrentUser()
        {
            if (savedCommitHistory == null || savedCommitHistory.Count == 0)
            {
                return;
            }

            List<string> myMessages;
            try
            {
                myMessages = GitUtility.GetRecentCommitMessagesForCurrentUser(500);
            }
            catch
            {
                myMessages = null;
            }

            if (myMessages == null || myMessages.Count == 0)
            {
                Debug.LogWarning("GitQuickCommit: 未找到当前用户的提交记录（可能未配置 git user.email/user.name），无法过滤本地历史文件。");
                return;
            }

            var mine = new HashSet<string>(myMessages, StringComparer.Ordinal);
            savedCommitHistory.RemoveAll(entry => !mine.Contains(entry));
            if (savedCommitHistory.Count > MaxCommitHistoryEntries)
            {
                savedCommitHistory.RemoveRange(MaxCommitHistoryEntries, savedCommitHistory.Count - MaxCommitHistoryEntries);
            }
        }

        private void RebuildCommitHistoryDisplay()
        {
            if (commitHistory == null)
            {
                return;
            }

            commitHistory.Clear();

            if (savedCommitHistory != null)
            {
                foreach (var entry in savedCommitHistory)
                {
                    if (string.IsNullOrWhiteSpace(entry))
                    {
                        continue;
                    }

                    if (commitHistory.Count >= MaxCommitHistoryEntries)
                    {
                        return;
                    }

                    commitHistory.Add(entry.Trim());
                }
            }

            if (fallbackCommitHistory == null || fallbackCommitHistory.Count == 0)
            {
                return;
            }

            var existing = new HashSet<string>(commitHistory, StringComparer.Ordinal);
            foreach (var entry in fallbackCommitHistory)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                var trimmed = entry.Trim();
                if (existing.Contains(trimmed))
                {
                    continue;
                }

                commitHistory.Add(trimmed);
                if (commitHistory.Count >= MaxCommitHistoryEntries)
                {
                    return;
                }
            }
        }

        private void SaveCommitHistory()
        {
            if (savedCommitHistory == null)
            {
                return;
            }

            var path = GetCommitHistoryFilePath();
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (savedCommitHistory.Count == 0)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    return;
                }

                var data = new CommitHistoryData { entries = savedCommitHistory };
                var json = JsonUtility.ToJson(data);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GitQuickCommit: 保存提交记录失败: {ex.Message}");
            }
        }

        private void AddCommitMessageToHistory(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            EnsureCommitHistoryLoaded();
            var trimmed = message.Trim();
            savedCommitHistory ??= new List<string>();
            savedCommitHistory.RemoveAll(entry => string.Equals(entry, trimmed, StringComparison.Ordinal));
            savedCommitHistory.Insert(0, trimmed);
            if (savedCommitHistory.Count > MaxCommitHistoryEntries)
            {
                savedCommitHistory.RemoveRange(MaxCommitHistoryEntries, savedCommitHistory.Count - MaxCommitHistoryEntries);
            }

            SaveCommitHistory();
            RebuildCommitHistoryDisplay();
            UpdateHistoryButtonState();
            if (historyDropdownVisible && historyListView != null)
            {
                historyListView.itemsSource = commitHistory;
                historyListView.RefreshItems();
            }
        }

        private void PerformCommit(bool pushAfter)
        {
            var message = commitMessage;
            if (string.IsNullOrWhiteSpace(message))
            {
                EditorUtility.DisplayDialog("提交", "请先填写提交说明。", "确定");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification("正在执行其他操作，请稍候。");
                return;
            }

            var normalizedMessage = message.Trim();
            var gitRoot = GitUtility.ProjectRoot;
            _ = GitUtility.UnityProjectFolder;

            if (string.IsNullOrEmpty(gitRoot))
            {
                EditorUtility.DisplayDialog("提交", "未找到 Git 根目录。", "确定");
                return;
            }

            CaptureInitialStagedSnapshotIfNeeded();
            var stagedPathsNow = assetInfos.Where(a => a.IsStaged).Select(a => a.AssetPath).ToList();
            var preexistingStagedCount = 0;
            if (initialStagedPaths != null && stagedPathsNow.Count > 0)
            {
                foreach (var path in stagedPathsNow)
                {
                    if (initialStagedPaths.Contains(path))
                    {
                        preexistingStagedCount++;
                    }
                }
            }

            if (preexistingStagedCount > 0)
            {
                var confirmed = EditorUtility.DisplayDialog(
                    "安全确认",
                    $"将提交 {stagedPathsNow.Count} 个待提交条目，其中 {preexistingStagedCount} 个在打开窗口前就已暂存。\n\n是否继续提交？",
                    "继续提交",
                    "取消");

                if (!confirmed)
                {
                    return;
                }
            }

            gitOperationInProgress = true;
            gitOperationKind = pushAfter ? GitOperationKind.CommitAndPush : GitOperationKind.Commit;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = pushAfter ? "正在提交并推送..." : "正在提交...";
            UpdateHeaderLabels();
            ForceRepaintUI();

            gitOperationTask = Task.Run(() =>
            {
                if (!GitUtility.CommitGit(gitRoot, normalizedMessage, out var commitSummary))
                {
                    return new GitOperationResult(false, commitSummary, false, null);
                }

                var finalSummary = commitSummary;
                if (pushAfter)
                {
                    if (GitUtility.PushGit(gitRoot, out var pushSummary))
                    {
                        finalSummary = commitSummary + "\\n" + pushSummary;
                    }
                    else
                    {
                        var pushFailMessage = "\\n推送失败：远程可能已有新的提交，请在 UGit 中先拉取更新并解决冲突后再推送。";
                        var extraInfo = string.IsNullOrEmpty(pushSummary) ? string.Empty : "\\n" + pushSummary;
                        finalSummary = commitSummary + pushFailMessage + extraInfo;
                    }
                }

                return new GitOperationResult(true, finalSummary, true, normalizedMessage);
            });
            gitOperationTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void UpdateHeaderLabels()
        {
            if (pathLabel != null)
            {
                if (targetAssetPaths.Count == 0)
                {
                    pathLabel.text = "路径：未选择（不显示变更）";
                }
                else if (targetAssetPaths.Count == 1)
                {
                    pathLabel.text = $"路径：{targetAssetPaths[0]}";
                }
                else
                {
                    pathLabel.text = $"路径：已选择 {targetAssetPaths.Count} 个目标（显示关联变更）";
                }
            }

            if (statusLabel != null)
            {
                statusLabel.text = string.IsNullOrEmpty(statusMessage) ? string.Empty : statusMessage;
            }
        }

        private void UpdateCommitButtonsEnabled()
        {
            var hasMessage = !string.IsNullOrWhiteSpace(commitMessage);
            if (commitButton != null)
            {
                commitButton.SetEnabled(hasMessage && !refreshInProgress && !gitOperationInProgress);
            }

            if (commitAndPushButton != null)
            {
                commitAndPushButton.SetEnabled(hasMessage && !refreshInProgress && !gitOperationInProgress);
            }
        }

        private void UpdateActionButtonsEnabled()
        {
            var enabled = !refreshInProgress && !gitOperationInProgress;

            if (refreshButton != null)
            {
                refreshButton.SetEnabled(enabled);
            }

            if (toStagedButton != null)
            {
                toStagedButton.SetEnabled(enabled);
            }

            if (toUnstagedButton != null)
            {
                toUnstagedButton.SetEnabled(enabled);
            }

            if (resetTimeButton != null)
            {
                resetTimeButton.SetEnabled(enabled);
            }
        }

        private void RefreshListViews()
        {
            if (unstagedScrollView == null || stagedScrollView == null)
            {
                return;
            }

            var validUnstaged = new HashSet<string>(
                assetInfos.Where(i => i.IsUnstaged).Select(i => i.AssetPath),
                StringComparer.OrdinalIgnoreCase);
            foreach (var path in selectedUnstagedPaths.ToList())
            {
                if (!validUnstaged.Contains(path))
                {
                    selectedUnstagedPaths.Remove(path);
                }
            }

            var validStaged = new HashSet<string>(
                assetInfos.Where(i => i.IsStaged).Select(i => i.AssetPath),
                StringComparer.OrdinalIgnoreCase);
            foreach (var path in selectedStagedPaths.ToList())
            {
                if (!validStaged.Contains(path))
                {
                    selectedStagedPaths.Remove(path);
                }
            }

            var unstaged = EnumerateFilteredAssets(false).ToList();
            var staged = EnumerateFilteredAssets(true).ToList();

            if (unstagedHeaderLabel != null)
            {
                unstagedHeaderLabel.text = $"\u5de5\u4f5c\u533a\u53d8\u66f4\uff08\u672a\u6682\u5b58\uff09\uff1a{unstaged.Count} \u9879";
            }

            if (stagedHeaderLabel != null)
            {
                stagedHeaderLabel.text = $"\u5f85\u63d0\u4ea4\uff08\u5df2\u6682\u5b58\uff09\uff1a{staged.Count} \u9879";
            }

            if (unstagedSelectAllToggle != null)
            {
                var allSelected = unstaged.Count > 0 && unstaged.All(i => selectedUnstagedPaths.Contains(i.AssetPath));
                unstagedSelectAllToggle.SetValueWithoutNotify(allSelected);
            }

            if (stagedSelectAllToggle != null)
            {
                var allSelected = staged.Count > 0 && staged.All(i => selectedStagedPaths.Contains(i.AssetPath));
                stagedSelectAllToggle.SetValueWithoutNotify(allSelected);
            }

            unstagedScrollView.Clear();
            stagedScrollView.Clear();

            foreach (var info in unstaged)
            {
                unstagedScrollView.Add(CreateAssetRow(info, false));
            }

            foreach (var info in staged)
            {
                stagedScrollView.Add(CreateAssetRow(info, true));
            }

            if (unstaged.Count == 0 && staged.Count == 0)
            {
                var message = string.IsNullOrEmpty(statusMessage)
                    ? "Git 未检测到可显示的变更。"
                    : statusMessage;
                AddEmptyPlaceholderLabel(unstagedScrollView, message);
            }
        }

        private void AddEmptyPlaceholderLabel(ScrollView target, string message)
        {
            if (target == null)
            {
                return;
            }

            var container = new VisualElement();
            container.style.flexGrow = 1f;
            container.style.flexDirection = FlexDirection.Column;
            container.style.justifyContent = Justify.Center;

            var label = new Label(message);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.color = new Color(1f, 1f, 1f, 0.6f);
            label.style.alignSelf = Align.Center;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;

            container.Add(label);
            target.Add(container);
        }

        private VisualElement CreateAssetRow(GitAssetInfo info, bool stagedView)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom = 2;
            container.style.paddingLeft = 4;
            container.style.paddingRight = 4;
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;
            container.style.borderBottomWidth = 1;
            container.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 1f);

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            var selectToggle = new Toggle();
            selectToggle.style.width = 16;
            selectToggle.style.marginRight = 4;
            selectToggle.SetValueWithoutNotify(stagedView
                ? selectedStagedPaths.Contains(info.AssetPath)
                : selectedUnstagedPaths.Contains(info.AssetPath));
            selectToggle.RegisterValueChangedCallback(evt =>
            {
                var targetSet = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
                if (evt.newValue)
                {
                    targetSet.Add(info.AssetPath);
                }
                else
                {
                    targetSet.Remove(info.AssetPath);
                }
            });
            headerRow.Add(selectToggle);

            var nameLabel = new Label(info.FileName);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            Color nameColor;
            switch (info.ChangeType)
            {
                case GitChangeType.Added:
                    nameColor = new Color(0.5f, 0.85f, 0.5f);
                    break;
                case GitChangeType.Deleted:
                    nameColor = new Color(0.9f, 0.5f, 0.5f);
                    break;
                case GitChangeType.Modified:
                    nameColor = new Color(0.95f, 0.75f, 0.4f);
                    break;
                case GitChangeType.Renamed:
                    nameColor = new Color(0.6f, 0.75f, 1.0f);
                    break;
                default:
                    nameColor = Color.white;
                    break;
            }
            nameLabel.style.color = nameColor;
            headerRow.Add(nameLabel);

            nameLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                {
                    CopyToClipboard(info.FileName);
                    ShowTempNotification($"已复制名称：{info.FileName}");
                }
                else if (evt.clickCount == 1)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.AssetPath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                        Selection.activeObject = asset;
                    }
                }
            });

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            headerRow.Add(spacer);

            var timeText = info.WorkingTreeTime.HasValue
                ? info.WorkingTreeTime.Value.ToString("yyyy-MM-dd HH:mm")
                : "未知时间";
            var timeLabel = new Label(timeText);
            timeLabel.style.marginLeft = 8;
            timeLabel.style.fontSize = 10;
            timeLabel.style.color = new Color(1f, 1f, 1f, 0.6f);
            headerRow.Add(timeLabel);

            container.Add(headerRow);

            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.alignItems = Align.Center;

            var displayPath = GetFolderPath(info.AssetPath);
            if (!string.IsNullOrEmpty(info.OriginalPath))
            {
                var originalFolder = GetFolderPath(info.OriginalPath);
                if (!string.IsNullOrEmpty(originalFolder) &&
                    !string.Equals(originalFolder, displayPath, StringComparison.OrdinalIgnoreCase))
                {
                    displayPath = $"{originalFolder} -> {displayPath}";
                }
            }

            var pathInfoLabel = new Label(displayPath);
            pathInfoLabel.style.fontSize = 10;
            pathInfoLabel.style.color = new Color(1f, 1f, 1f, 0.6f);
            pathInfoLabel.style.flexGrow = 1f;
            pathRow.Add(pathInfoLabel);

            pathInfoLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                {
                    CopyToClipboard(info.AssetPath);
                    ShowTempNotification($"已复制路径：{info.AssetPath}");
                }
            });

            if (!stagedView)
            {
                var excludeButton = new Button(() =>
                {
                    excludedPaths.Add(info.AssetPath);
                    RefreshListViews();
                })
                {
                    text = "排除"
                };
                excludeButton.style.marginLeft = 4;
                pathRow.Add(excludeButton);

                var discardButton = new Button(() => ConfirmDiscardChange(info))
                {
                    text = "放弃更改"
                };
                discardButton.style.marginLeft = 4;
                pathRow.Add(discardButton);
            }

            container.Add(pathRow);

            return container;
        }

        private void ConfirmDiscardChange(GitAssetInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.AssetPath))
            {
                return;
            }

            var confirmed = EditorUtility.DisplayDialog(
                "\u653e\u5f03\u66f4\u6539",
                $"\u786e\u5b9a\u653e\u5f03\u4ee5\u4e0b\u66f4\u6539\uff1f\n{info.AssetPath}\n\n\u6b64\u64cd\u4f5c\u4e0d\u53ef\u64a4\u9500\u3002",
                "\u653e\u5f03",
                "\u53d6\u6d88");
            if (!confirmed)
            {
                return;
            }

            var success = GitUtility.DiscardChanges(new[] { info }, out var summary);
            if (success)
            {
                RequestAssetDatabaseRefreshAndRefreshData();
                ShowTempNotification(string.IsNullOrEmpty(summary) ? "\u5df2\u653e\u5f03\u66f4\u6539\u3002" : summary);
            }
            else
            {
                ShowTempNotification(string.IsNullOrEmpty(summary) ? "\u653e\u5f03\u66f4\u6539\u5931\u8d25\u3002" : summary);
            }
        }

        private void UpdateRepositoryStatusInfo()
        {
            var stagedCount = assetInfos.Count(info => info.IsStaged);
            var unstagedCount = assetInfos.Count(info => info.IsUnstaged);
            var addedCount = assetInfos.Count(info => info.ChangeType == GitChangeType.Added);
            var modifiedCount = assetInfos.Count(info =>
                info.ChangeType == GitChangeType.Modified || info.ChangeType == GitChangeType.Renamed);
            var deletedCount = assetInfos.Count(info => info.ChangeType == GitChangeType.Deleted);

            var statusText = string.Format(GitStatusFormat, stagedCount, unstagedCount);
            var addedText = string.Format(AddedSegmentFormat, AddedColorHex, addedCount);
            var modifiedText = string.Format(ModifiedSegmentFormat, ModifiedColorHex, modifiedCount);
            var deletedText = string.Format(DeletedSegmentFormat, DeletedColorHex, deletedCount);

            repositoryStatusMessage = $"{statusText}  |  {addedText}  {modifiedText}  {deletedText}";

            if (repositoryStatusLabel != null)
            {
                repositoryStatusLabel.text = repositoryStatusMessage;
            }
        }

        private bool IsRelevantForCurrentTarget(GitAssetInfo info)
        {
            if (info == null)
            {
                return false;
            }

            if (targetAssetPaths.Count == 0)
            {
                return true;
            }

            var assetPath = info.AssetPath ?? string.Empty;
            var originalPath = info.OriginalPath ?? string.Empty;

            if (targetFolderPrefixes.Count > 0)
            {
                foreach (var prefix in targetFolderPrefixes)
                {
                    if (string.IsNullOrEmpty(prefix))
                    {
                        continue;
                    }

                    if (assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                        originalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (relevantPaths.Count == 0)
            {
                foreach (var target in targetAssetPaths)
                {
                    if (string.Equals(assetPath, target, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(originalPath, target, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            return relevantPaths.Contains(assetPath) || relevantPaths.Contains(originalPath);
        }

        private void UpdateTargetSelectionInfo(IReadOnlyList<GitChangeEntry> currentChanges)
        {
            relevantPaths.Clear();
            targetFolderPrefixes.Clear();

            var normalizedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawTarget in targetAssetPaths)
            {
                var normalized = GitUtility.NormalizeAssetPath(rawTarget);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                if (AssetDatabase.IsValidFolder(normalized))
                {
                    var prefix = normalized.EndsWith("/")
                        ? normalized
                        : $"{normalized}/";
                    targetFolderPrefixes.Add(prefix);
                    AddRelevantPath(normalized);
                    continue;
                }

                normalizedTargets.Add(normalized);
                AddRelevantPath(normalized);

                string[] dependencies;
                try
                {
                    dependencies = AssetDatabase.GetDependencies(normalized, true);
                }
                catch
                {
                    dependencies = null;
                }

                if (dependencies == null || dependencies.Length == 0)
                {
                    AddAncestorFolderMetas(normalized);
                    continue;
                }

                foreach (var dep in dependencies)
                {
                    AddRelevantPath(dep);
                }
            }

            AddAncestorFolderMetasForCurrentRelevantPaths();
            AddReverseDependenciesFromChanges(currentChanges, normalizedTargets);
        }

        private void AddRelevantPath(string path)
        {
            var normalized = GitUtility.NormalizeAssetPath(path);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            relevantPaths.Add(normalized);
            if (!normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                relevantPaths.Add($"{normalized}.meta");
            }
        }

        private void AddAncestorFolderMetasForCurrentRelevantPaths()
        {
            foreach (var path in relevantPaths.ToList())
            {
                if (string.IsNullOrEmpty(path) || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddAncestorFolderMetas(path);
            }
        }

        private void AddAncestorFolderMetas(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            var normalized = GitUtility.NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            var lastSlash = normalized.LastIndexOf('/');
            while (lastSlash > 0)
            {
                var folder = normalized.Substring(0, lastSlash);
                if (!string.IsNullOrEmpty(folder) && !folder.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    relevantPaths.Add($"{folder}.meta");
                }

                lastSlash = folder.LastIndexOf('/');
            }
        }

        private void AddReverseDependenciesFromChanges(IReadOnlyList<GitChangeEntry> currentChanges, HashSet<string> normalizedTargets)
        {
            if (currentChanges == null || currentChanges.Count == 0)
            {
                return;
            }

            if (normalizedTargets == null || normalizedTargets.Count == 0)
            {
                return;
            }

            foreach (var entry in currentChanges)
            {
                var changedPath = GitUtility.NormalizeAssetPath(entry.Path);
                if (string.IsNullOrEmpty(changedPath))
                {
                    continue;
                }

                if (changedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!changedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (AssetDatabase.IsValidFolder(changedPath))
                {
                    continue;
                }

                string[] deps;
                try
                {
                    deps = AssetDatabase.GetDependencies(changedPath, true);
                }
                catch
                {
                    continue;
                }

                if (deps == null || deps.Length == 0)
                {
                    continue;
                }

                var referencesTarget = false;
                foreach (var dep in deps)
                {
                    var normalizedDep = GitUtility.NormalizeAssetPath(dep);
                    if (string.IsNullOrEmpty(normalizedDep))
                    {
                        continue;
                    }

                    if (normalizedTargets.Contains(normalizedDep))
                    {
                        referencesTarget = true;
                        break;
                    }
                }

                if (referencesTarget)
                {
                    AddRelevantPath(changedPath);
                    AddAncestorFolderMetas(changedPath);
                }
            }
        }

        private void ApplyQuickRange(TimeSpan duration)
        {
            var now = DateTime.Now;
            var start = now - duration;

            startTimeFilter = start;
            endTimeFilter = now;
            startTimeInput = FormatDateTime(start);
            endTimeInput = FormatDateTime(now);
            startTimeValid = true;
            endTimeValid = true;

            if (startTimeField != null) startTimeField.value = startTimeInput;
            if (endTimeField != null) endTimeField.value = endTimeInput;

            RefreshListViews();
        }

        private string GetAssetFolderPath()
        {
            if (!string.IsNullOrEmpty(cachedAssetFolderPath))
            {
                return cachedAssetFolderPath;
            }

            var script = MonoScript.FromScriptableObject(this);
            var scriptPath = AssetDatabase.GetAssetPath(script);
            var directory = string.IsNullOrEmpty(scriptPath) ? "Assets" : Path.GetDirectoryName(scriptPath);
            cachedAssetFolderPath = string.IsNullOrEmpty(directory) ? "Assets" : directory.Replace("\\", "/");
            return cachedAssetFolderPath;
        }

        private static DateTime? TryParseDateTime(string input, out bool valid)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                valid = true;
                return null;
            }

            if (DateTime.TryParse(input, out var result))
            {
                valid = true;
                return result;
            }

            valid = false;
            return null;
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm");
        }

        private static string GetFolderPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            var lastSlash = assetPath.LastIndexOf('/');
            return lastSlash > 0 ? assetPath.Substring(0, lastSlash) : assetPath;
        }

        private static string GetCommitHistoryFilePath()
        {
            var projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var libraryFolder = Path.Combine(projectFolder, "Library");
            return Path.Combine(libraryFolder, CommitHistoryFileName);
        }

        [Serializable]
        private class CommitHistoryData
        {
            public List<string> entries = new List<string>();
        }

        private static void CopyToClipboard(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer = content;
        }
    }
}
