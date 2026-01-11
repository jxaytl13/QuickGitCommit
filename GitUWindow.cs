using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace TLNexus.GitU
{
    internal partial class GitUWindow : EditorWindow
    {
        private const string WindowTitle = "GitU";
        private const string MenuPath = "Window/T·L Nexus/GitU";
        private const string CommitHistoryFileName = "GitUCommitHistory.json";
        private const string LegacyCommitHistoryFileName = "QuickGitCommitHistory.json";
        private const int MaxCommitHistoryEntries = 20;
        private const string StagedAllowListFileName = "GitUStagedAllowList.json";
        private const string LegacyStagedAllowListFileName = "QuickGitCommitStagedAllowList.json";
        private const string AssetTypeFilterPrefsKeyPrefix = "TLNexus.GitU.AssetTypeFilters:";
        private const string LegacyAssetTypeFilterPrefsKeyPrefix = "OneKey.GitTools.QuickGitCommit.AssetTypeFilters:";
        private const string AddedColorHex = "#80D980";
        private const string ModifiedColorHex = "#F2BF66";
        private const string DeletedColorHex = "#E68080";
        private const string GitStatusFormat = "Git \u72b6\u6001\uff1a\u5f85\u63d0\u4ea4 {0}\uff0c\u672a\u6682\u5b58 {1}";
        private const string AddedSegmentFormat = "<color={0}>\u65b0\u589e {1}</color>";
        private const string ModifiedSegmentFormat = "<color={0}>\u4fee\u6539 {1}</color>";
        private const string DeletedSegmentFormat = "<color={0}>\u5220\u9664 {1}</color>";
        private const string SearchPlaceholderText = "\u641c\u7d22\u6587\u4ef6 / \u8def\u5f84 / \u53d8\u66f4\u2026";
        private const string CommitMessagePlaceholderText = "\u5728\u8fd9\u91cc\u586b\u5199\u63d0\u4ea4\u4fe1\u606f\u2026";
        private static readonly Color SearchFieldTextColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color SearchFieldPlaceholderTextColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color CommitFieldTextColor = Color.white;
        private static readonly Color CommitFieldPlaceholderTextColor = new Color(1f, 1f, 1f, 0.3f);

        private UnityEngine.Object targetAsset;
        private string targetAssetPath;
        private readonly List<GitAssetInfo> assetInfos = new List<GitAssetInfo>();
        private List<GitChangeEntry> gitChanges = new List<GitChangeEntry>();
        private readonly HashSet<string> relevantPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> targetAssetPaths = new List<string>();
        private readonly List<string> targetFolderPrefixes = new List<string>();

        private Task<List<GitChangeEntry>> refreshTask;
        private bool refreshInProgress;
        private bool refreshQueued;
        private bool refreshQueuedClearUi;
        private List<string> refreshInfoMessages;
        private bool refreshDebouncePending;
        private bool refreshDebounceClearUi;
        private double refreshDebounceDeadline;
        private bool listViewRefreshDebouncePending;
        private double listViewRefreshDebounceDeadline;

        [MenuItem(MenuPath, false, 1000)]
        private static void OpenFromWindowMenu()
        {
            var window = GetWindow<GitUWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(1040, 600);
            window.maxSize = new Vector2(1040, 1080);
            window.Initialize(GetTargetsFromCurrentSelection());
            window.Show();
        }

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
        private bool hasShownPreexistingStagedHint;
        private readonly HashSet<string> stagedAllowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool autoCleanExternalStagedOnOpen;
        private bool hasAutoCleanedExternalStagedOnOpen;
        private bool recaptureInitialStagedSnapshotAfterAutoClean;
        private List<string> pendingStageGitPaths;
        private List<string> pendingUnstageGitPaths;
        private bool showAdded = true;
        private bool showModified = true;
        private bool showDeleted = true;
        private static readonly UnityAssetTypeFilter[] defaultSelectedAssetTypeFilters =
        {
            UnityAssetTypeFilter.AnimationClip,
            UnityAssetTypeFilter.Font,
            UnityAssetTypeFilter.Material,
            UnityAssetTypeFilter.Mesh,
            UnityAssetTypeFilter.Prefab,
            UnityAssetTypeFilter.Scene,
            UnityAssetTypeFilter.Shader,
            UnityAssetTypeFilter.Sprite,
            UnityAssetTypeFilter.Texture
        };

        private readonly HashSet<UnityAssetTypeFilter> assetTypeFilters = new HashSet<UnityAssetTypeFilter>(defaultSelectedAssetTypeFilters);
        private string searchQuery = string.Empty;
        private string statusMessage;
        private string commitMessage = string.Empty;
        private string repositoryStatusMessage = "\u6b63\u5728\u68c0\u6d4b Git \u72b6\u6001...";
        private bool searchPlaceholderActive;
        private bool commitMessagePlaceholderActive;
        private List<string> commitHistory;
        private List<string> savedCommitHistory;
        private List<string> fallbackCommitHistory;
        private bool historyDropdownVisible;
        private int unstagedSelectionAnchorIndex = -1;
        private int stagedSelectionAnchorIndex = -1;
        private bool? lastActiveStagedView;

        // UI Toolkit elements
        private ObjectField targetField;
        private Label pathLabel;
        private Label statusLabel;
        private Label saveToDiskHintLabel;
        private Button addedButton;
        private Button modifiedButton;
        private Button deletedButton;
        private ToolbarMenu assetTypeMenu;
        private TextField searchField;
        private Label unstagedHeaderLabel;
        private Label stagedHeaderLabel;
        private ListView unstagedScrollView;
        private ListView stagedScrollView;
        private TextField commitMessageField;
        private Button commitButton;
        private Button commitAndPushButton;
        private Button historyButton;
        private Button refreshButton;
        private VisualElement historyDropdown;
        private ListView historyListView;
        private Label repositoryStatusLabel;
        private Label toastLabel;

        private VisualElement leftColumn;
        private Label emptyPlaceholderLabel;

        private readonly List<GitAssetInfo> visibleUnstagedItems = new List<GitAssetInfo>();
        private readonly List<GitAssetInfo> visibleStagedItems = new List<GitAssetInfo>();
        private readonly Dictionary<string, UnityAssetTypeFilter> assetTypeCache = new Dictionary<string, UnityAssetTypeFilter>(StringComparer.OrdinalIgnoreCase);
        private int lastUnstagedVisibleCount = -1;
        private int lastStagedVisibleCount = -1;
        private bool assetListViewsConfigured;
        private bool visibleListsInitialized;

        private static string AssetTypeFilterPrefsKey => $"{AssetTypeFilterPrefsKeyPrefix}{Application.dataPath}";
        private static string LegacyAssetTypeFilterPrefsKey => $"{LegacyAssetTypeFilterPrefsKeyPrefix}{Application.dataPath}";

        private sealed class AssetRowRefs
        {
            public bool StagedView;
            public Image IconImage;
            public Label NameLabel;
            public Label PathLabel;
            public VisualElement ChangeBadgeContainer;
            public Label ChangeBadgeLabel;
            public GitAssetInfo Info;
            public int BoundIndex;

            public bool DragArmed;
            public Vector3 DragStartPosition;
            public int DragPointerId;
        }

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

            var window = GetWindow<GitUWindow>();
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

            var window = GetWindow<GitUWindow>();
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
            refreshQueuedClearUi = false;
            refreshInfoMessages = null;
            refreshTask = null;
            refreshDebouncePending = false;
            refreshDebounceClearUi = false;
            refreshDebounceDeadline = 0;
            listViewRefreshDebouncePending = false;
            listViewRefreshDebounceDeadline = 0;

            gitOperationInProgress = false;
            gitOperationKind = GitOperationKind.None;
            gitOperationTask = null;

            hasAutoCleanedExternalStagedOnOpen = false;
            recaptureInitialStagedSnapshotAfterAutoClean = false;
            pendingStageGitPaths = null;
            pendingUnstageGitPaths = null;
        }

        private void Update()
        {
            if (notificationEndTime > 0 && EditorApplication.timeSinceStartup >= notificationEndTime)
            {
                if (toastLabel != null)
                {
                    toastLabel.style.display = DisplayStyle.None;
                    toastLabel.text = string.Empty;
                }

                notificationEndTime = 0;
            }

            PollRefreshTask();
            PollGitOperationTask();
            PollDebouncedRefresh();
            PollDebouncedListViewRefresh();
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

            if (toastLabel != null)
            {
                toastLabel.text = message;
                toastLabel.style.display = DisplayStyle.Flex;
                notificationEndTime = EditorApplication.timeSinceStartup + seconds;
            }
        }

        private void CreateGUI()
        {
            historyDropdownVisible = false;
            autoCleanExternalStagedOnOpen = true;
            LoadStagedAllowList();
            LoadAssetTypeFilters();
            assetListViewsConfigured = false;
            lastUnstagedVisibleCount = -1;
            lastStagedVisibleCount = -1;
            visibleUnstagedItems.Clear();
            visibleStagedItems.Clear();
            visibleListsInitialized = false;

            var root = rootVisualElement;
            root.Clear();

            if (!BuildLayoutFromCode(root))
            {
                return;
            }

            EnsureAssetListViewsConfigured();

            if (saveToDiskHintLabel != null)
            {
                saveToDiskHintLabel.text = "重要提示：改材质/动画/Prefab/场景后，请先 Ctrl+S（或 Save Assets）写盘，再打开/刷新；否则可能漏显示。";
                saveToDiskHintLabel.style.whiteSpace = WhiteSpace.Normal;
            }

            if (targetField != null)
            {
                targetField.objectType = typeof(UnityEngine.Object);
                targetField.allowSceneObjects = false;
                targetField.value = targetAsset;
                targetField.RegisterValueChangedCallback(evt =>
                {
                    SetTargetAssets(evt.newValue != null ? new[] { evt.newValue } : null);
                    RefreshData();
                });
            }

            ConfigureChangeTypeButtons();

            if (assetTypeMenu != null)
            {
                ConfigureAssetTypeMenu();
            }
            if (searchField != null)
            {
                ConfigureSearchPlaceholder();
                searchField.RegisterValueChangedCallback(evt =>
                {
                    if (searchPlaceholderActive)
                    {
                        return;
                    }

                    searchQuery = evt.newValue ?? string.Empty;
                    RequestRefreshListViewsDebounced(0.18);
                });
                searchField.RegisterCallback<FocusInEvent>(_ => ClearSearchPlaceholderIfNeeded());
                searchField.RegisterCallback<FocusOutEvent>(_ => ApplySearchPlaceholderIfNeeded());
            }
            if (refreshButton != null)
            {
                refreshButton.clicked += () => { RequestRefreshData(false); };
            }
            if (commitMessageField != null)
            {
                ConfigureCommitMessagePlaceholder();
                commitMessageField.RegisterValueChangedCallback(evt =>
                {
                    if (commitMessagePlaceholderActive)
                    {
                        return;
                    }

                    commitMessage = evt.newValue;
                    UpdateCommitButtonsEnabled();
                });
                commitMessageField.RegisterCallback<FocusInEvent>(_ => ClearCommitMessagePlaceholderIfNeeded());
                commitMessageField.RegisterCallback<FocusOutEvent>(_ => ApplyCommitMessagePlaceholderIfNeeded());
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
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
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

        private void ConfigureSearchPlaceholder()
        {
            if (searchField == null)
            {
                return;
            }

            if (IsTextFieldFocused(searchField))
            {
                searchPlaceholderActive = false;
                searchField.SetValueWithoutNotify(searchQuery ?? string.Empty);
                SetTextFieldTextColor(searchField, SearchFieldTextColor);
                return;
            }

            if (string.IsNullOrEmpty(searchQuery))
            {
                searchField.SetValueWithoutNotify(SearchPlaceholderText);
                searchPlaceholderActive = true;
                SetTextFieldTextColor(searchField, SearchFieldPlaceholderTextColor);
            }
            else
            {
                searchField.SetValueWithoutNotify(searchQuery);
                searchPlaceholderActive = false;
                SetTextFieldTextColor(searchField, SearchFieldTextColor);
            }
        }

        private void ClearSearchPlaceholderIfNeeded()
        {
            if (searchField == null || !searchPlaceholderActive)
            {
                return;
            }

            searchPlaceholderActive = false;
            searchField.SetValueWithoutNotify(string.Empty);
            SetTextFieldTextColor(searchField, SearchFieldTextColor);
        }

        private void ApplySearchPlaceholderIfNeeded()
        {
            if (searchField == null || searchPlaceholderActive)
            {
                return;
            }

            if (!string.IsNullOrEmpty(searchField.value))
            {
                return;
            }

            searchPlaceholderActive = true;
            searchField.SetValueWithoutNotify(SearchPlaceholderText);
            SetTextFieldTextColor(searchField, SearchFieldPlaceholderTextColor);
        }

        private void ConfigureCommitMessagePlaceholder()
        {
            if (commitMessageField == null)
            {
                return;
            }

            if (IsTextFieldFocused(commitMessageField))
            {
                commitMessagePlaceholderActive = false;
                commitMessageField.SetValueWithoutNotify(commitMessage ?? string.Empty);
                SetTextFieldTextColor(commitMessageField, CommitFieldTextColor);
                UpdateCommitButtonsEnabled();
                return;
            }

            if (string.IsNullOrEmpty(commitMessage))
            {
                commitMessageField.SetValueWithoutNotify(CommitMessagePlaceholderText);
                commitMessagePlaceholderActive = true;
                SetTextFieldTextColor(commitMessageField, CommitFieldPlaceholderTextColor);
            }
            else
            {
                commitMessageField.SetValueWithoutNotify(commitMessage);
                commitMessagePlaceholderActive = false;
                SetTextFieldTextColor(commitMessageField, CommitFieldTextColor);
            }

            UpdateCommitButtonsEnabled();
        }

        private void ClearCommitMessagePlaceholderIfNeeded()
        {
            if (commitMessageField == null || !commitMessagePlaceholderActive)
            {
                return;
            }

            commitMessagePlaceholderActive = false;
            commitMessageField.SetValueWithoutNotify(string.Empty);
            SetTextFieldTextColor(commitMessageField, CommitFieldTextColor);
        }

        private void ApplyCommitMessagePlaceholderIfNeeded()
        {
            if (commitMessageField == null || commitMessagePlaceholderActive)
            {
                return;
            }

            if (!string.IsNullOrEmpty(commitMessageField.value))
            {
                return;
            }

            commitMessage = string.Empty;
            commitMessagePlaceholderActive = true;
            commitMessageField.SetValueWithoutNotify(CommitMessagePlaceholderText);
            SetTextFieldTextColor(commitMessageField, CommitFieldPlaceholderTextColor);
            UpdateCommitButtonsEnabled();
        }

        private static void SetTextFieldTextColor(TextField field, Color color)
        {
            if (field == null)
            {
                return;
            }

            var input = field.Q<VisualElement>(className: "unity-text-field__input");
            if (input != null)
            {
                input.Query<TextElement>().ForEach(e => e.style.color = color);
                input.style.color = color;
            }

            var fallback = field.Q<TextElement>(className: "unity-text-element");
            if (fallback != null)
            {
                fallback.style.color = color;
            }
        }

        private static bool IsTextFieldFocused(TextField field)
        {
            if (field == null)
            {
                return false;
            }

            var focused = field.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && IsDescendantOf(focused, field);
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
                var clearUi = refreshQueuedClearUi;
                refreshQueued = false;
                refreshQueuedClearUi = false;
                RequestRefreshData(clearUi);
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
            var runQueuedRefresh = false;
            var queuedClearUi = false;

            gitOperationTask = null;
            gitOperationKind = GitOperationKind.None;

            gitOperationInProgress = false;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            if (refreshQueued)
            {
                queuedClearUi = refreshQueuedClearUi;
                runQueuedRefresh = true;
                refreshQueued = false;
                refreshQueuedClearUi = false;
            }

            if (completedTask.IsCanceled)
            {
                ShowTempNotification("操作已取消。");
                if (runQueuedRefresh)
                {
                    RequestRefreshData(queuedClearUi);
                }
                else if (completedKind == GitOperationKind.Stage || completedKind == GitOperationKind.Unstage)
                {
                    RequestRefreshDataDebounced(false);
                }
                return;
            }

            if (completedTask.IsFaulted)
            {
                var error = completedTask.Exception?.GetBaseException().Message ?? "未知错误";
                ShowTempNotification($"操作失败：{error}");
                if (runQueuedRefresh)
                {
                    RequestRefreshData(queuedClearUi);
                }
                else if (completedKind == GitOperationKind.Stage || completedKind == GitOperationKind.Unstage)
                {
                    RequestRefreshDataDebounced(false);
                }
                return;
            }

            var result = completedTask.Result;

            if (completedKind == GitOperationKind.Stage)
            {
                if (result.Success && pendingStageGitPaths != null && pendingStageGitPaths.Count > 0)
                {
                    foreach (var p in pendingStageGitPaths)
                    {
                        stagedAllowList.Add(p);
                    }

                    SaveStagedAllowList();
                }

                pendingStageGitPaths = null;
            }
            else if (completedKind == GitOperationKind.Unstage)
            {
                if (result.Success && pendingUnstageGitPaths != null && pendingUnstageGitPaths.Count > 0)
                {
                    foreach (var p in pendingUnstageGitPaths)
                    {
                        stagedAllowList.Remove(p);
                    }

                    SaveStagedAllowList();
                }

                pendingUnstageGitPaths = null;
            }

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
                    ConfigureCommitMessagePlaceholder();

                    HideHistoryDropdown();
                }
            }
            else if (!string.IsNullOrEmpty(result.Summary))
            {
                ShowTempNotification(result.Summary);
            }

            if (recaptureInitialStagedSnapshotAfterAutoClean)
            {
                recaptureInitialStagedSnapshotAfterAutoClean = false;
                initialStagedPaths = null;
            }

            if (completedKind == GitOperationKind.Stage)
            {
                selectedUnstagedPaths.Clear();
            }
            else if (completedKind == GitOperationKind.Unstage)
            {
                selectedStagedPaths.Clear();
            }

            if (runQueuedRefresh)
            {
                RequestRefreshData(queuedClearUi);
            }
            else
            {
                RequestRefreshDataDebounced(false);
            }
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
            saveToDiskHintLabel = root.Q<Label>("saveToDiskHintLabel");
            addedButton = root.Q<Button>("addedButton");
            modifiedButton = root.Q<Button>("modifiedButton");
            deletedButton = root.Q<Button>("deletedButton");
            assetTypeMenu = root.Q<ToolbarMenu>("assetTypeMenu");
            searchField = root.Q<TextField>("searchField");
            unstagedHeaderLabel = root.Q<Label>("unstagedHeaderLabel");
            stagedHeaderLabel = root.Q<Label>("stagedHeaderLabel");
            unstagedScrollView = root.Q<ListView>("unstagedScrollView");
            stagedScrollView = root.Q<ListView>("stagedScrollView");
            commitMessageField = root.Q<TextField>("commitMessageField");
            commitButton = root.Q<Button>("commitButton");
            commitAndPushButton = root.Q<Button>("commitAndPushButton");
            historyButton = root.Q<Button>("historyButton");
            refreshButton = root.Q<Button>("refreshButton");
            historyDropdown = root.Q<VisualElement>("historyDropdown");
            historyListView = root.Q<ListView>("historyListView");
            repositoryStatusLabel = root.Q<Label>("repositoryStatusLabel");
            leftColumn = root.Q<VisualElement>("leftColumn");
            toastLabel = root.Q<Label>("toastLabel");
        }

        private void ConfigureChangeTypeButtons()
        {
            if (addedButton != null)
            {
                addedButton.clicked += () =>
                {
                    showAdded = !showAdded;
                    UpdateChangeTypeButtonsVisuals();
                    RequestRefreshListViewsDebounced();
                };
            }

            if (modifiedButton != null)
            {
                modifiedButton.clicked += () =>
                {
                    showModified = !showModified;
                    UpdateChangeTypeButtonsVisuals();
                    RequestRefreshListViewsDebounced();
                };
            }

            if (deletedButton != null)
            {
                deletedButton.clicked += () =>
                {
                    showDeleted = !showDeleted;
                    UpdateChangeTypeButtonsVisuals();
                    RequestRefreshListViewsDebounced();
                };
            }

            UpdateChangeTypeButtonsVisuals();
        }

        private void UpdateChangeTypeButtonsVisuals()
        {
            UpdateChangeTypeButtonVisual(addedButton, showAdded, new Color(0.5f, 0.85f, 0.5f));
            UpdateChangeTypeButtonVisual(modifiedButton, showModified, new Color(0.95f, 0.75f, 0.4f));
            UpdateChangeTypeButtonVisual(deletedButton, showDeleted, new Color(0.9f, 0.5f, 0.5f));
        }

        private static void UpdateChangeTypeButtonVisual(Button button, bool enabled, Color accentColor)
        {
            if (button == null)
            {
                return;
            }

            button.style.opacity = enabled ? 1f : 0.35f;
            button.style.color = accentColor;
            button.style.backgroundColor = enabled
                ? new Color(accentColor.r, accentColor.g, accentColor.b, 0.10f)
                : new Color(0.12f, 0.12f, 0.12f, 0.35f);
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            var borderColor = enabled
                ? new Color(accentColor.r, accentColor.g, accentColor.b, 0.75f)
                : new Color(1f, 1f, 1f, 0.08f);
            button.style.borderTopColor = borderColor;
            button.style.borderRightColor = borderColor;
            button.style.borderBottomColor = borderColor;
            button.style.borderLeftColor = borderColor;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        private void StageSelectedUnstaged()
        {
            if (selectedUnstagedPaths.Count == 0)
            {
                ShowTempNotification("请先在左侧勾选要发送的变更。");
                return;
            }

            StageAssetPaths(selectedUnstagedPaths.ToList());
        }

        private void StageAssetPaths(List<string> assetPaths)
        {
            assetPaths ??= new List<string>();
            assetPaths.RemoveAll(string.IsNullOrWhiteSpace);

            if (assetPaths.Count == 0)
            {
                ShowTempNotification("当前没有可发送的变更。");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification("正在执行其他操作，请稍候。");
                return;
            }

            var pathSet = new HashSet<string>(assetPaths, StringComparer.OrdinalIgnoreCase);
            var toStage = assetInfos
                .Where(a => !a.IsStaged && a.IsUnstaged && pathSet.Contains(a.AssetPath))
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

                // If this is an explicit "Deleted (from move)" entry, stage the destination path as well
                // so the move is staged as a whole.
                if (info.ChangeType == GitChangeType.Deleted &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var movedToGitPath))
                {
                    requests.Add(new GitUtility.GitStageRequest(movedToGitPath, GitChangeType.Added));
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

            foreach (var info in toStage)
            {
                info.IsStaged = true;
                info.IsUnstaged = false;
            }

            selectedUnstagedPaths.Clear();
            ApplyIncrementalMoveBetweenLists(toStage, toStaged: true);

            pendingStageGitPaths = requests.Select(r => r.GitRelativePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
            if (selectedStagedPaths.Count == 0)
            {
                ShowTempNotification("请先在右侧勾选要移出的变更。");
                return;
            }

            UnstageAssetPaths(selectedStagedPaths.ToList());
        }

        private void UnstageAssetPaths(List<string> assetPaths)
        {
            assetPaths ??= new List<string>();
            assetPaths.RemoveAll(string.IsNullOrWhiteSpace);

            if (assetPaths.Count == 0)
            {
                ShowTempNotification("当前没有可移出的待提交项。");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification("正在执行其他操作，请稍候。");
                return;
            }

            var pathSet = new HashSet<string>(assetPaths, StringComparer.OrdinalIgnoreCase);
            var toUnstage = assetInfos
                .Where(a => a.IsStaged && pathSet.Contains(a.AssetPath))
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

                // If this is an explicit "Deleted (from move)" entry, unstage the destination path as well
                // so the move is unstaged as a whole.
                if (info.ChangeType == GitChangeType.Deleted &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var movedToGitPath))
                {
                    requests.Add(movedToGitPath);
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

            foreach (var info in toUnstage)
            {
                info.IsStaged = false;
                info.IsUnstaged = true;
            }

            selectedStagedPaths.Clear();
            ApplyIncrementalMoveBetweenLists(toUnstage, toStaged: false);

            pendingUnstageGitPaths = requests
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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

        private void ApplyIncrementalMoveBetweenLists(List<GitAssetInfo> moved, bool toStaged)
        {
            if (!assetListViewsConfigured || !visibleListsInitialized || unstagedScrollView == null || stagedScrollView == null)
            {
                RefreshListViews();
                return;
            }

            var touchedUnstaged = false;
            var touchedStaged = false;

            foreach (var info in moved)
            {
                if (info == null || string.IsNullOrEmpty(info.AssetPath))
                {
                    continue;
                }

                if (toStaged)
                {
                    touchedUnstaged |= RemoveByPath(visibleUnstagedItems, info.AssetPath);

                    if (IsItemVisibleInList(info, stagedView: true) && !ContainsByPath(visibleStagedItems, info.AssetPath))
                    {
                        InsertSortedByPath(visibleStagedItems, info);
                        touchedStaged = true;
                    }
                }
                else
                {
                    touchedStaged |= RemoveByPath(visibleStagedItems, info.AssetPath);

                    if (IsItemVisibleInList(info, stagedView: false) && !ContainsByPath(visibleUnstagedItems, info.AssetPath))
                    {
                        InsertSortedByPath(visibleUnstagedItems, info);
                        touchedUnstaged = true;
                    }
                }
            }

            var isEmpty = visibleUnstagedItems.Count == 0 && visibleStagedItems.Count == 0;
            UpdateEmptyPlaceholder(isEmpty);

            if (unstagedHeaderLabel != null)
            {
                unstagedHeaderLabel.text = $"\u5de5\u4f5c\u533a\u53d8\u66f4\uff08\u672a\u6682\u5b58\uff09\uff1a{visibleUnstagedItems.Count} \u9879";
            }

            if (stagedHeaderLabel != null)
            {
                stagedHeaderLabel.text = $"\u5f85\u63d0\u4ea4\uff08\u5df2\u6682\u5b58\uff09\uff1a{visibleStagedItems.Count} \u9879";
            }

            if (touchedUnstaged)
            {
                UpdateListView(unstagedScrollView, visibleUnstagedItems, ref lastUnstagedVisibleCount);
            }
            else
            {
                unstagedScrollView.RefreshItems();
            }

            if (touchedStaged)
            {
                UpdateListView(stagedScrollView, visibleStagedItems, ref lastStagedVisibleCount);
            }
            else
            {
                stagedScrollView.RefreshItems();
            }
        }

        private bool IsItemVisibleInList(GitAssetInfo info, bool stagedView)
        {
            if (info == null)
            {
                return false;
            }

            if (stagedView)
            {
                if (!info.IsStaged)
                {
                    return false;
                }
            }
            else
            {
                if (!info.IsUnstaged)
                {
                    return false;
                }

                if (!IsRelevantForCurrentTarget(info))
                {
                    return false;
                }
            }

            if (!IsChangeTypeVisible(info.ChangeType))
            {
                return false;
            }

            if (!IsMatchSearchQuery(info))
            {
                return false;
            }

            if (!IsMatchAssetTypeFilter(info))
            {
                return false;
            }

            return true;
        }

        private static bool RemoveByPath(List<GitAssetInfo> list, string assetPath)
        {
            if (list == null || list.Count == 0 || string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item != null && string.Equals(item.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsByPath(List<GitAssetInfo> list, string assetPath)
        {
            if (list == null || list.Count == 0 || string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            foreach (var item in list)
            {
                if (item != null && string.Equals(item.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void InsertSortedByPath(List<GitAssetInfo> list, GitAssetInfo item)
        {
            if (list == null || item == null)
            {
                return;
            }

            var key = item.AssetPath ?? string.Empty;
            var lo = 0;
            var hi = list.Count;
            while (lo < hi)
            {
                var mid = lo + ((hi - lo) / 2);
                var midKey = list[mid]?.AssetPath ?? string.Empty;
                var cmp = string.Compare(midKey, key, StringComparison.OrdinalIgnoreCase);
                if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            list.Insert(lo, item);
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

                if (!IsChangeTypeVisible(info.ChangeType))
                {
                    continue;
                }

                if (!IsMatchSearchQuery(info))
                {
                    continue;
                }

                if (!IsMatchAssetTypeFilter(info))
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

        private bool IsMatchAssetTypeFilter(GitAssetInfo info)
        {
            if (assetTypeFilters.Contains(UnityAssetTypeFilter.All))
            {
                return true;
            }

            if (info == null)
            {
                return false;
            }

            var path = info.AssetPath;
            if (string.IsNullOrEmpty(path))
            {
                path = info.OriginalPath;
            }

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!assetTypeCache.TryGetValue(path, out var cached))
            {
                cached = GitUtility.DetectAssetTypeFilter(path);
                assetTypeCache[path] = cached;
            }

            return assetTypeFilters.Contains(cached);
        }

        private static readonly UnityAssetTypeFilter[] assetTypeOptions =
        {
            UnityAssetTypeFilter.All,
            UnityAssetTypeFilter.AnimationClip,
            UnityAssetTypeFilter.AudioClip,
            UnityAssetTypeFilter.AudioMixer,
            UnityAssetTypeFilter.ComputeShader,
            UnityAssetTypeFilter.Font,
            UnityAssetTypeFilter.GUISkin,
            UnityAssetTypeFilter.Material,
            UnityAssetTypeFilter.Mesh,
            UnityAssetTypeFilter.Model,
            UnityAssetTypeFilter.PhysicMaterial,
            UnityAssetTypeFilter.Prefab,
            UnityAssetTypeFilter.Scene,
            UnityAssetTypeFilter.Script,
            UnityAssetTypeFilter.Shader,
            UnityAssetTypeFilter.Sprite,
            UnityAssetTypeFilter.Texture,
            UnityAssetTypeFilter.VideoClip,
            UnityAssetTypeFilter.VisualEffectAsset
        };

        private void ConfigureAssetTypeMenu()
        {
            if (assetTypeMenu == null)
            {
                return;
            }

            assetTypeMenu.text = GetAssetTypeMenuText();

            assetTypeMenu.menu.AppendAction(
                UnityAssetTypeFilter.All.ToDisplayName(),
                _ => SetAssetTypeFilter(UnityAssetTypeFilter.All),
                _ => assetTypeFilters.Contains(UnityAssetTypeFilter.All) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);

            assetTypeMenu.menu.AppendSeparator();

            foreach (var option in assetTypeOptions)
            {
                if (option == UnityAssetTypeFilter.All)
                {
                    continue;
                }

                var captured = option;
                assetTypeMenu.menu.AppendAction(
                    captured.ToDisplayName(),
                    _ => ToggleAssetTypeFilter(captured),
                    _ => assetTypeFilters.Contains(captured) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
        }

        private void SetAssetTypeFilter(UnityAssetTypeFilter type)
        {
            assetTypeFilters.Clear();
            assetTypeFilters.Add(type);
            assetTypeCache.Clear();
            SaveAssetTypeFilters();
            if (assetTypeMenu != null) assetTypeMenu.text = GetAssetTypeMenuText();
            RequestRefreshListViewsDebounced();
        }

        private void ToggleAssetTypeFilter(UnityAssetTypeFilter type)
        {
            if (type == UnityAssetTypeFilter.All)
            {
                SetAssetTypeFilter(UnityAssetTypeFilter.All);
                return;
            }

            assetTypeFilters.Remove(UnityAssetTypeFilter.All);

            if (!assetTypeFilters.Add(type))
            {
                assetTypeFilters.Remove(type);
            }

            if (assetTypeFilters.Count == 0)
            {
                assetTypeFilters.Add(UnityAssetTypeFilter.All);
            }

            assetTypeCache.Clear();
            SaveAssetTypeFilters();
            if (assetTypeMenu != null) assetTypeMenu.text = GetAssetTypeMenuText();
            RequestRefreshListViewsDebounced();
        }

        private void LoadAssetTypeFilters()
        {
            assetTypeFilters.Clear();

            var raw = EditorPrefs.GetString(AssetTypeFilterPrefsKey, string.Empty);
            var loadedFromLegacy = false;
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = EditorPrefs.GetString(LegacyAssetTypeFilterPrefsKey, string.Empty);
                loadedFromLegacy = !string.IsNullOrWhiteSpace(raw);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                foreach (var type in defaultSelectedAssetTypeFilters)
                {
                    assetTypeFilters.Add(type);
                }

                return;
            }

            var parts = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (Enum.TryParse<UnityAssetTypeFilter>(part, out var value))
                {
                    assetTypeFilters.Add(value);
                }
            }

            if (assetTypeFilters.Contains(UnityAssetTypeFilter.All))
            {
                assetTypeFilters.Clear();
                assetTypeFilters.Add(UnityAssetTypeFilter.All);
            }

            if (assetTypeFilters.Count == 0)
            {
                foreach (var type in defaultSelectedAssetTypeFilters)
                {
                    assetTypeFilters.Add(type);
                }
            }

            if (loadedFromLegacy)
            {
                SaveAssetTypeFilters();
            }
        }

        private void SaveAssetTypeFilters()
        {
            var normalized = assetTypeFilters.Contains(UnityAssetTypeFilter.All) || assetTypeFilters.Count == 0
                ? new[] { UnityAssetTypeFilter.All }
                : assetTypeFilters.Where(t => t != UnityAssetTypeFilter.All).ToArray();

            var serialized = string.Join("|", normalized.Select(t => t.ToString()));
            EditorPrefs.SetString(AssetTypeFilterPrefsKey, serialized);
        }

        private string GetAssetTypeMenuText()
        {
            if (assetTypeFilters.Contains(UnityAssetTypeFilter.All) || assetTypeFilters.Count == 0)
            {
                return UnityAssetTypeFilter.All.ToDisplayName();
            }

            if (assetTypeFilters.Count == 1)
            {
                return assetTypeFilters.First().ToDisplayName();
            }

            return $"已选 {assetTypeFilters.Count} 项";
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

        private void RequestRefreshData(bool clearUi)
        {
            if (refreshInProgress || gitOperationInProgress)
            {
                refreshQueued = true;
                refreshQueuedClearUi = refreshQueuedClearUi || clearUi;
                return;
            }

            StartRefresh(clearUi);
        }

        private void RequestRefreshDataDebounced(bool clearUi, double delaySeconds = 0.15)
        {
            refreshDebouncePending = true;
            refreshDebounceClearUi = refreshDebounceClearUi || clearUi;
            refreshDebounceDeadline = EditorApplication.timeSinceStartup + Math.Max(0.01, delaySeconds);
        }

        private void PollDebouncedRefresh()
        {
            if (!refreshDebouncePending)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < refreshDebounceDeadline)
            {
                return;
            }

            var clearUi = refreshDebounceClearUi;
            refreshDebouncePending = false;
            refreshDebounceClearUi = false;
            RequestRefreshData(clearUi);
        }

        private void RequestRefreshListViewsDebounced(double delaySeconds = 0.12)
        {
            listViewRefreshDebouncePending = true;
            listViewRefreshDebounceDeadline = EditorApplication.timeSinceStartup + Math.Max(0.01, delaySeconds);
        }

        private void PollDebouncedListViewRefresh()
        {
            if (!listViewRefreshDebouncePending)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < listViewRefreshDebounceDeadline)
            {
                return;
            }

            listViewRefreshDebouncePending = false;
            RefreshListViews();
        }

        private void RefreshData()
        {
            RequestRefreshData(true);
        }

        private void StartRefresh(bool clearUi)
        {
            if (clearUi)
            {
                assetInfos.Clear();
                assetTypeCache.Clear();
                statusMessage = string.Empty;
                selectedUnstagedPaths.Clear();
                selectedStagedPaths.Clear();
                HideHistoryDropdown();
                relevantPaths.Clear();
                targetFolderPrefixes.Clear();
            }
            else
            {
                HideHistoryDropdown();
            }

            var infoMessages = new List<string>();

            if (targetAssetPaths.Count == 0)
            {
                targetAssetPath = string.Empty;
                GitUtility.SetContextAssetPath(null);
                assetInfos.Clear();
                assetTypeCache.Clear();
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
                assetInfos.Clear();
                assetTypeCache.Clear();
                selectedUnstagedPaths.Clear();
                selectedStagedPaths.Clear();
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

            UpdateAssetInfosIncrementally(gitChanges);
            CaptureInitialStagedSnapshotIfNeeded();

            if (!hasShownPreexistingStagedHint && initialStagedPaths != null && initialStagedPaths.Count > 0)
            {
                hasShownPreexistingStagedHint = true;
                infoMessages.Add("提示：右侧“待提交/已暂存”读取的是 Git 暂存区（index）。窗口会自动清理“非本工具暂存”的条目；如需手动清空可执行：git restore --staged .");
            }

            PruneStagedAllowListToCurrentIndex();
            if (TryStartAutoCleanExternalStagedOnOpen(infoMessages))
            {
                return;
            }

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

        private void UpdateAssetInfosIncrementally(List<GitChangeEntry> changes)
        {
            var existingByPath = new Dictionary<string, GitAssetInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in assetInfos)
            {
                if (info == null || string.IsNullOrEmpty(info.AssetPath))
                {
                    continue;
                }

                existingByPath[info.AssetPath] = info;
            }

            var updated = new List<GitAssetInfo>(changes?.Count ?? 0);
            if (changes != null)
            {
                foreach (var change in changes)
                {
                    if (string.IsNullOrEmpty(change.Path))
                    {
                        continue;
                    }

                    existingByPath.TryGetValue(change.Path, out var info);

                    var historyPath = string.IsNullOrEmpty(change.OriginalPath) ? change.Path : change.OriginalPath;
                    var lastTime = GitUtility.GetLastKnownChangeTime(historyPath);

                    if (info == null)
                    {
                        info = new GitAssetInfo(change.Path, change.OriginalPath, change.ChangeType, lastTime, change.WorkingTreeTime, change.IsStaged, change.IsUnstaged);
                    }
                    else
                    {
                        info.AssetPath = change.Path;
                        info.OriginalPath = change.OriginalPath;
                        info.ChangeType = change.ChangeType;
                        info.LastCommitTime = lastTime;
                        info.WorkingTreeTime = change.WorkingTreeTime;
                        info.IsStaged = change.IsStaged;
                        info.IsUnstaged = change.IsUnstaged;
                    }

                    updated.Add(info);
                }
            }

            updated.Sort((a, b) => string.Compare(a.AssetPath, b.AssetPath, StringComparison.OrdinalIgnoreCase));

            assetInfos.Clear();
            assetInfos.AddRange(updated);
            assetTypeCache.Clear();
        }

        private void CaptureInitialStagedSnapshotIfNeeded()
        {
            if (initialStagedPaths != null)
            {
                return;
            }

            initialStagedPaths = new HashSet<string>(assetInfos.Where(a => a.IsStaged).Select(a => a.AssetPath), StringComparer.OrdinalIgnoreCase);
        }

        private void PruneStagedAllowListToCurrentIndex()
        {
            if (stagedAllowList.Count == 0)
            {
                return;
            }

            var stagedNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in assetInfos)
            {
                if (!info.IsStaged)
                {
                    continue;
                }

                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var gitPath))
                {
                    stagedNow.Add(gitPath);
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalGitPath))
                {
                    stagedNow.Add(originalGitPath);
                }
            }

            var removed = stagedAllowList.RemoveWhere(p => !stagedNow.Contains(p));
            if (removed > 0)
            {
                SaveStagedAllowList();
            }
        }

        private bool TryStartAutoCleanExternalStagedOnOpen(List<string> infoMessages)
        {
            if (!autoCleanExternalStagedOnOpen || hasAutoCleanedExternalStagedOnOpen)
            {
                return false;
            }

            hasAutoCleanedExternalStagedOnOpen = true;

            if (refreshInProgress || gitOperationInProgress)
            {
                return false;
            }

            var gitRoot = GitUtility.ProjectRoot;
            _ = GitUtility.UnityProjectFolder;
            if (string.IsNullOrEmpty(gitRoot))
            {
                return false;
            }

            var toUnstage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in assetInfos)
            {
                if (!info.IsStaged)
                {
                    continue;
                }

                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var gitPath) && !stagedAllowList.Contains(gitPath))
                {
                    toUnstage.Add(gitPath);
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalGitPath) &&
                    !stagedAllowList.Contains(originalGitPath))
                {
                    toUnstage.Add(originalGitPath);
                }
            }

            if (toUnstage.Count == 0)
            {
                return false;
            }

            var pathsToUnstage = toUnstage.ToList();
            pendingUnstageGitPaths = pathsToUnstage;
            recaptureInitialStagedSnapshotAfterAutoClean = true;

            gitOperationInProgress = true;
            gitOperationKind = GitOperationKind.Unstage;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = "正在清理外部暂存...";
            UpdateHeaderLabels();
            RefreshListViews();
            ForceRepaintUI();

            gitOperationTask = Task.Run(() =>
            {
                var success = GitUtility.UnstageGitPaths(gitRoot, pathsToUnstage, out var summary);
                if (success)
                {
                    summary = $"已自动清理外部暂存：{summary}";
                }

                return new GitOperationResult(success, summary);
            });
            gitOperationTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return true;
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

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (!evt.actionKey || evt.keyCode != KeyCode.A)
            {
                return;
            }

            var focused = rootVisualElement?.focusController?.focusedElement as VisualElement;
            if (focused != null)
            {
                if (IsDescendantOf(focused, searchField) ||
                    IsDescendantOf(focused, commitMessageField) ||
                    IsDescendantOf(focused, targetField))
                {
                    return;
                }
            }

            bool? targetView = lastActiveStagedView;

            if (!targetView.HasValue && focused != null)
            {
                if (IsDescendantOf(focused, stagedScrollView))
                {
                    targetView = true;
                }
                else if (IsDescendantOf(focused, unstagedScrollView))
                {
                    targetView = false;
                }
            }

            if (!targetView.HasValue)
            {
                if (selectedStagedPaths.Count > 0)
                {
                    targetView = true;
                }
                else if (selectedUnstagedPaths.Count > 0)
                {
                    targetView = false;
                }
            }

            if (!targetView.HasValue)
            {
                return;
            }

            SelectAllInView(targetView.Value);
            evt.StopPropagation();
            evt.PreventDefault();
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
            commitMessagePlaceholderActive = false;
            if (commitMessageField != null)
            {
                commitMessageField.SetValueWithoutNotify(entry);
                SetTextFieldTextColor(commitMessageField, CommitFieldTextColor);
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

            if (!File.Exists(path))
            {
                var legacyPath = GetCommitHistoryFilePath(LegacyCommitHistoryFileName);
                if (File.Exists(legacyPath))
                {
                    path = legacyPath;
                }
            }

            if (File.Exists(path))
            {
                string json;
                try
                {
                    json = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"GitU: 读取提交记录失败: {ex.Message}");
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
                        Debug.LogWarning($"GitU: 解析提交记录失败: {ex.Message}");
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
                Debug.LogWarning($"GitU: 读取Git提交记录失败: {ex.Message}");
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
                Debug.LogWarning("GitU: 未找到当前用户的提交记录（可能未配置 git user.email/user.name），无法过滤本地历史文件。");
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
                Debug.LogWarning($"GitU: 保存提交记录失败: {ex.Message}");
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
            var newlineIndex = trimmed.IndexOfAny(new[] { '\r', '\n' });
            var historyEntry = newlineIndex >= 0 ? trimmed.Substring(0, newlineIndex).Trim() : trimmed;
            if (string.IsNullOrWhiteSpace(historyEntry))
            {
                return;
            }

            savedCommitHistory ??= new List<string>();
            savedCommitHistory.RemoveAll(entry => string.Equals(entry, historyEntry, StringComparison.Ordinal));
            savedCommitHistory.Insert(0, historyEntry);
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
            var busy = refreshInProgress || gitOperationInProgress;

            // 这两个按钮不应随 Git 操作频繁禁用/启用，否则视觉上会“闪一下”
            //（看起来像被程序点击/操控）；它们的动作要么只是本地筛选，要么会自动排队刷新。
            if (refreshButton != null)
            {
                refreshButton.SetEnabled(true);
            }
        }

        private void RefreshListViews()
        {
            if (unstagedScrollView == null || stagedScrollView == null)
            {
                return;
            }

            EnsureAssetListViewsConfigured();

            var unstagedScroll = unstagedScrollView.Q<ScrollView>();
            var stagedScroll = stagedScrollView.Q<ScrollView>();
            var unstagedScrollOffset = unstagedScroll?.scrollOffset ?? Vector2.zero;
            var stagedScrollOffset = stagedScroll?.scrollOffset ?? Vector2.zero;

            var unstaged = EnumerateFilteredAssets(false).ToList();
            var staged = EnumerateFilteredAssets(true).ToList();

            var validUnstaged = new HashSet<string>(unstaged.Select(i => i.AssetPath), StringComparer.OrdinalIgnoreCase);
            foreach (var path in selectedUnstagedPaths.ToList())
            {
                if (!validUnstaged.Contains(path))
                {
                    selectedUnstagedPaths.Remove(path);
                }
            }

            var validStaged = new HashSet<string>(staged.Select(i => i.AssetPath), StringComparer.OrdinalIgnoreCase);
            foreach (var path in selectedStagedPaths.ToList())
            {
                if (!validStaged.Contains(path))
                {
                    selectedStagedPaths.Remove(path);
                }
            }

            if (unstagedHeaderLabel != null)
            {
                unstagedHeaderLabel.text = $"\u5de5\u4f5c\u533a\u53d8\u66f4\uff08\u672a\u6682\u5b58\uff09\uff1a{unstaged.Count} \u9879";
            }

            if (stagedHeaderLabel != null)
            {
                stagedHeaderLabel.text = $"\u5f85\u63d0\u4ea4\uff08\u5df2\u6682\u5b58\uff09\uff1a{staged.Count} \u9879";
            }

            var isEmpty = unstaged.Count == 0 && staged.Count == 0;
            UpdateEmptyPlaceholder(isEmpty);

            visibleUnstagedItems.Clear();
            visibleUnstagedItems.AddRange(unstaged);
            visibleStagedItems.Clear();
            visibleStagedItems.AddRange(staged);

            UpdateListView(unstagedScrollView, visibleUnstagedItems, ref lastUnstagedVisibleCount);
            UpdateListView(stagedScrollView, visibleStagedItems, ref lastStagedVisibleCount);
            visibleListsInitialized = true;

            if (unstagedScroll != null)
            {
                unstagedScroll.schedule.Execute(() => { unstagedScroll.scrollOffset = unstagedScrollOffset; });
            }

            if (stagedScroll != null)
            {
                stagedScroll.schedule.Execute(() => { stagedScroll.scrollOffset = stagedScrollOffset; });
            }
        }

        private void AddEmptyPlaceholderLabel(VisualElement target, string message)
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

        private void EnsureAssetListViewsConfigured()
        {
            if (assetListViewsConfigured)
            {
                return;
            }

            if (unstagedScrollView == null || stagedScrollView == null)
            {
                return;
            }

            EnsureEmptyPlaceholderCreated();

            ConfigureAssetListView(unstagedScrollView, stagedView: false);
            ConfigureAssetListView(stagedScrollView, stagedView: true);

            unstagedScrollView.itemsSource = visibleUnstagedItems;
            stagedScrollView.itemsSource = visibleStagedItems;

            assetListViewsConfigured = true;
        }

        private void EnsureEmptyPlaceholderCreated()
        {
            if (emptyPlaceholderLabel != null || leftColumn == null)
            {
                return;
            }

            emptyPlaceholderLabel = new Label();
            emptyPlaceholderLabel.style.flexGrow = 1f;
            emptyPlaceholderLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            emptyPlaceholderLabel.style.color = new Color(1f, 1f, 1f, 0.6f);
            emptyPlaceholderLabel.style.display = DisplayStyle.None;

            var insertIndex = 1;
            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > leftColumn.childCount) insertIndex = leftColumn.childCount;
            leftColumn.Insert(insertIndex, emptyPlaceholderLabel);
        }

        private void UpdateEmptyPlaceholder(bool isEmpty)
        {
            if (emptyPlaceholderLabel == null)
            {
                return;
            }

            if (!isEmpty)
            {
                emptyPlaceholderLabel.style.display = DisplayStyle.None;
                if (unstagedScrollView != null) unstagedScrollView.style.display = DisplayStyle.Flex;
                if (stagedScrollView != null) stagedScrollView.style.display = DisplayStyle.Flex;
                return;
            }

            var message = string.IsNullOrEmpty(statusMessage)
                ? "Git 未检测到可显示的变更。"
                : statusMessage;

            emptyPlaceholderLabel.text = message;
            emptyPlaceholderLabel.style.display = DisplayStyle.Flex;
            if (unstagedScrollView != null) unstagedScrollView.style.display = DisplayStyle.None;
            if (stagedScrollView != null) stagedScrollView.style.display = DisplayStyle.None;
        }

        private void ConfigureAssetListView(ListView listView, bool stagedView)
        {
            listView.selectionType = SelectionType.None;
#if UNITY_2021_2_OR_NEWER
            listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            listView.fixedItemHeight = 38;
            listView.reorderable = false;
#else
            listView.itemHeight = 38;
#endif

            listView.makeItem = () => CreateAssetRowTemplate(stagedView);
            listView.bindItem = (e, i) => BindAssetRow(e, i, stagedView);

            listView.RegisterCallback<DragUpdatedEvent>(evt => OnListDragUpdated(evt, stagedView));
            listView.RegisterCallback<DragPerformEvent>(evt => OnListDragPerform(evt, stagedView));
        }

        private void UpdateListView(ListView listView, List<GitAssetInfo> items, ref int lastCount)
        {
            if (listView == null)
            {
                return;
            }

            listView.itemsSource = items;

            if (lastCount != items.Count)
            {
                lastCount = items.Count;
                listView.Rebuild();
                return;
            }

            listView.RefreshItems();
        }

        private const string DragPayloadKey = "TLNexus.GitU.DragPayload";

        private sealed class DragPayload
        {
            public bool SourceStaged;
            public List<string> AssetPaths;
        }

        private void OnListDragUpdated(DragUpdatedEvent evt, bool targetStaged)
        {
            var payload = DragAndDrop.GetGenericData(DragPayloadKey) as DragPayload;
            if (payload == null || payload.AssetPaths == null || payload.AssetPaths.Count == 0)
            {
                return;
            }

            DragAndDrop.visualMode = payload.SourceStaged == targetStaged
                ? DragAndDropVisualMode.Rejected
                : DragAndDropVisualMode.Move;

            evt.StopPropagation();
        }

        private void OnListDragPerform(DragPerformEvent evt, bool targetStaged)
        {
            var payload = DragAndDrop.GetGenericData(DragPayloadKey) as DragPayload;
            if (payload == null || payload.AssetPaths == null || payload.AssetPaths.Count == 0)
            {
                return;
            }

            if (payload.SourceStaged == targetStaged)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(DragPayloadKey, null);

            if (targetStaged)
            {
                StageAssetPaths(payload.AssetPaths);
            }
            else
            {
                UnstageAssetPaths(payload.AssetPaths);
            }

            evt.StopPropagation();
        }

        private VisualElement CreateAssetRowTemplate(bool stagedView)
        {
            var container = new VisualElement();
            container.AddToClassList("gitU-asset-item");
            container.style.flexDirection = FlexDirection.Column;
            container.style.flexGrow = 1f;
            container.style.flexShrink = 1f;
            container.style.alignSelf = Align.Stretch;
            container.style.minWidth = 0;
            container.style.borderTopLeftRadius = 0;
            container.style.borderTopRightRadius = 0;
            container.style.borderBottomRightRadius = 0;
            container.style.borderBottomLeftRadius = 0;
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.paddingTop = 0;
            container.style.paddingBottom = 0;
            container.style.height = 38;
            container.style.minHeight = 38;
            container.style.overflow = Overflow.Hidden;
            container.style.borderTopWidth = 0;
            container.style.borderRightWidth = 0;
            container.style.borderBottomWidth = 1;
            container.style.borderLeftWidth = 0;
            container.style.borderBottomColor = Rgb(20, 20, 20);

            var row = new VisualElement();
            row.AddToClassList("gitU-asset-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = Percent(100);

            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("gitU-asset-icon-container");
            iconContainer.style.width = 30;
            iconContainer.style.height = 30;
            iconContainer.style.minWidth = 30;
            iconContainer.style.minHeight = 30;
            iconContainer.style.borderTopLeftRadius = 8;
            iconContainer.style.borderTopRightRadius = 8;
            iconContainer.style.borderBottomRightRadius = 8;
            iconContainer.style.borderBottomLeftRadius = 8;
            iconContainer.style.borderTopWidth = 0;
            iconContainer.style.borderRightWidth = 0;
            iconContainer.style.borderBottomWidth = 0;
            iconContainer.style.borderLeftWidth = 0;
            var iconBorder = Rgba(255, 255, 255, 0.14f);
            iconContainer.style.borderTopColor = iconBorder;
            iconContainer.style.borderRightColor = iconBorder;
            iconContainer.style.borderBottomColor = iconBorder;
            iconContainer.style.borderLeftColor = iconBorder;
            iconContainer.style.backgroundColor = Color.clear;
            iconContainer.style.justifyContent = Justify.Center;
            iconContainer.style.alignItems = Align.Center;
            iconContainer.style.flexShrink = 0;
            iconContainer.style.marginRight = 8;
            row.Add(iconContainer);

            var iconImage = new Image();
            iconImage.AddToClassList("gitU-asset-icon");
            iconImage.scaleMode = ScaleMode.ScaleToFit;
            iconImage.style.width = 18;
            iconImage.style.height = 18;
            iconContainer.Add(iconImage);

            var namePathColumn = new VisualElement();
            namePathColumn.AddToClassList("gitU-asset-text");
            namePathColumn.style.flexDirection = FlexDirection.Column;
            namePathColumn.style.flexGrow = 1f;
            namePathColumn.style.flexShrink = 1f;
            namePathColumn.style.minWidth = 0;
            namePathColumn.style.justifyContent = Justify.Center;
            row.Add(namePathColumn);

            var nameLabel = new Label();
            nameLabel.AddToClassList("gitU-asset-name");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            namePathColumn.Add(nameLabel);

            var pathInfoLabel = new Label();
            pathInfoLabel.AddToClassList("gitU-asset-path");
            pathInfoLabel.style.whiteSpace = WhiteSpace.NoWrap;
            pathInfoLabel.style.overflow = Overflow.Hidden;
            pathInfoLabel.style.textOverflow = TextOverflow.Ellipsis;
            pathInfoLabel.style.marginTop = 1;
            pathInfoLabel.style.fontSize = 10;
            pathInfoLabel.style.color = Rgba(255, 255, 255, 0.55f);
            namePathColumn.Add(pathInfoLabel);

            var changeBadgeContainer = new VisualElement();
            changeBadgeContainer.AddToClassList("gitU-change-badge");
            changeBadgeContainer.style.width = 20;
            changeBadgeContainer.style.height = 20;
            changeBadgeContainer.style.minWidth = 20;
            changeBadgeContainer.style.minHeight = 20;
            changeBadgeContainer.style.marginLeft = 8;
            changeBadgeContainer.style.borderTopLeftRadius = 4;
            changeBadgeContainer.style.borderTopRightRadius = 4;
            changeBadgeContainer.style.borderBottomRightRadius = 4;
            changeBadgeContainer.style.borderBottomLeftRadius = 4;
            changeBadgeContainer.style.borderTopWidth = 1;
            changeBadgeContainer.style.borderRightWidth = 1;
            changeBadgeContainer.style.borderBottomWidth = 1;
            changeBadgeContainer.style.borderLeftWidth = 1;
            var badgeBorder = Rgba(255, 255, 255, 0.16f);
            changeBadgeContainer.style.borderTopColor = badgeBorder;
            changeBadgeContainer.style.borderRightColor = badgeBorder;
            changeBadgeContainer.style.borderBottomColor = badgeBorder;
            changeBadgeContainer.style.borderLeftColor = badgeBorder;
            changeBadgeContainer.style.backgroundColor = Rgba(255, 255, 255, 0.02f);
            changeBadgeContainer.style.justifyContent = Justify.Center;
            changeBadgeContainer.style.alignItems = Align.Center;
            changeBadgeContainer.style.flexShrink = 0;
            row.Add(changeBadgeContainer);

            var changeBadgeLabel = new Label();
            changeBadgeLabel.AddToClassList("gitU-change-badge-label");
            changeBadgeLabel.style.fontSize = 10;
            changeBadgeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            changeBadgeLabel.style.color = Rgba(255, 255, 255, 0.85f);
            changeBadgeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            changeBadgeLabel.style.width = 20;
            changeBadgeLabel.style.height = 20;
            changeBadgeLabel.style.marginTop = 0;
            changeBadgeLabel.style.marginRight = 0;
            changeBadgeLabel.style.marginBottom = 0;
            changeBadgeLabel.style.marginLeft = 0;
            changeBadgeLabel.style.paddingTop = 0;
            changeBadgeLabel.style.paddingRight = 0;
            changeBadgeLabel.style.paddingBottom = 0;
            changeBadgeLabel.style.paddingLeft = 0;
            changeBadgeContainer.Add(changeBadgeLabel);

            container.Add(row);

            var refs = new AssetRowRefs
            {
                StagedView = stagedView,
                IconImage = iconImage,
                NameLabel = nameLabel,
                PathLabel = pathInfoLabel,
                ChangeBadgeContainer = changeBadgeContainer,
                ChangeBadgeLabel = changeBadgeLabel
            };
            container.userData = refs;

            container.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (container.ClassListContains("gitU-asset-item--selected"))
                {
                    return;
                }

                container.style.backgroundColor = Rgba(255, 255, 255, 0.04f);
            });
            container.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (container.ClassListContains("gitU-asset-item--selected"))
                {
                    return;
                }

                container.style.backgroundColor = Color.clear;
            });

            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    var info = refs.Info;
                    if (info == null || string.IsNullOrEmpty(info.AssetPath))
                    {
                        return;
                    }

                    var set = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
                    if (!set.Contains(info.AssetPath))
                    {
                        HandleRowSelection(stagedView, info, refs.BoundIndex, shift: false, actionKey: false);
                    }

                    return;
                }

                if (evt.button != 0)
                {
                    return;
                }

                HandleRowSelection(stagedView, refs.Info, refs.BoundIndex, evt.shiftKey, evt.actionKey);

                refs.DragArmed = true;
                refs.DragStartPosition = new Vector3(evt.mousePosition.x, evt.mousePosition.y, 0f);
                refs.DragPointerId = 0;
            });

            row.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!refs.DragArmed || (evt.pressedButtons & 1) == 0)
                {
                    return;
                }

                var position = new Vector3(evt.mousePosition.x, evt.mousePosition.y, 0f);
                var delta = position - refs.DragStartPosition;
                if (delta.sqrMagnitude < 64f)
                {
                    return;
                }

                refs.DragArmed = false;

                TryStartDrag(stagedView, refs.Info, refs.BoundIndex);
            });

            row.RegisterCallback<MouseUpEvent>(_ =>
            {
                refs.DragArmed = false;
            });

            row.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                var info = refs.Info;
                if (info == null || string.IsNullOrEmpty(info.AssetPath))
                {
                    return;
                }

                var set = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
                if (!set.Contains(info.AssetPath))
                {
                    HandleRowSelection(stagedView, info, refs.BoundIndex, shift: false, actionKey: false);
                }

                evt.menu.AppendAction(
                    "\u653e\u5f03\u66f4\u6539",
                    _ => ConfirmDiscardSelected(stagedView),
                    _ => (stagedView ? selectedStagedPaths.Count : selectedUnstagedPaths.Count) > 0
                        ? DropdownMenuAction.Status.Normal
                        : DropdownMenuAction.Status.Disabled);
            });

            nameLabel.RegisterCallback<ClickEvent>(evt =>
            {
                var info = refs.Info;
                if (info == null)
                {
                    return;
                }

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

            pathInfoLabel.RegisterCallback<ClickEvent>(evt =>
            {
                var info = refs.Info;
                if (info == null)
                {
                    return;
                }

                if (evt.clickCount >= 2)
                {
                    CopyToClipboard(info.AssetPath);
                    ShowTempNotification($"已复制路径：{info.AssetPath}");
                }
            });

            return container;
        }

        private void ConfirmDiscardSelected(bool stagedView)
        {
            var set = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
            if (set.Count == 0)
            {
                return;
            }

            var selectedInfos = assetInfos
                .Where(i => i != null && !string.IsNullOrEmpty(i.AssetPath) && set.Contains(i.AssetPath))
                .ToList();

            if (selectedInfos.Count == 0)
            {
                return;
            }

            if (selectedInfos.Count == 1)
            {
                ConfirmDiscardChange(selectedInfos[0]);
                return;
            }

            const int maxPreviewLines = 12;
            var previewLines = selectedInfos
                .Select(i => i.AssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Take(maxPreviewLines)
                .ToList();
            var overflow = selectedInfos.Count - previewLines.Count;
            var preview = string.Join("\n", previewLines);
            if (overflow > 0)
            {
                preview += $"\n... 以及 {overflow} 项";
            }

            var confirmed = EditorUtility.DisplayDialog(
                "\u653e\u5f03\u66f4\u6539",
                $"\u786e\u5b9a\u653e\u5f03\u9009\u4e2d\u7684 {selectedInfos.Count} \u9879\u66f4\u6539\uff1f\n\n{preview}\n\n\u6b64\u64cd\u4f5c\u4e0d\u53ef\u64a4\u9500\u3002",
                "\u653e\u5f03",
                "\u53d6\u6d88");
            if (!confirmed)
            {
                return;
            }

            var success = GitUtility.DiscardChanges(selectedInfos, out var summary);
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

        private void HandleRowSelection(bool stagedView, GitAssetInfo info, int index, bool shift, bool actionKey)
        {
            if (info == null || string.IsNullOrEmpty(info.AssetPath))
            {
                return;
            }

            if (index < 0)
            {
                shift = false;
            }

            var thisSet = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
            var otherSet = stagedView ? selectedUnstagedPaths : selectedStagedPaths;
            if (otherSet.Count > 0)
            {
                otherSet.Clear();
            }

            if (shift)
            {
                var list = stagedView ? visibleStagedItems : visibleUnstagedItems;
                if (list == null || list.Count == 0)
                {
                    return;
                }

                var anchor = stagedView ? stagedSelectionAnchorIndex : unstagedSelectionAnchorIndex;
                if (anchor < 0 || anchor >= list.Count)
                {
                    anchor = index;
                }

                var min = Math.Min(anchor, index);
                var max = Math.Max(anchor, index);

                if (!actionKey)
                {
                    thisSet.Clear();
                }

                for (var i = min; i <= max; i++)
                {
                    var item = list[i];
                    if (item != null && !string.IsNullOrEmpty(item.AssetPath))
                    {
                        thisSet.Add(item.AssetPath);
                    }
                }
            }
            else if (actionKey)
            {
                if (!thisSet.Add(info.AssetPath))
                {
                    thisSet.Remove(info.AssetPath);
                }

                if (thisSet.Count == 0)
                {
                    if (stagedView)
                    {
                        stagedSelectionAnchorIndex = -1;
                    }
                    else
                    {
                        unstagedSelectionAnchorIndex = -1;
                    }
                }
            }
            else
            {
                thisSet.Clear();
                thisSet.Add(info.AssetPath);
            }

            if (stagedView)
            {
                stagedSelectionAnchorIndex = index;
            }
            else
            {
                unstagedSelectionAnchorIndex = index;
            }

            lastActiveStagedView = stagedView;
            unstagedScrollView?.RefreshItems();
            stagedScrollView?.RefreshItems();
        }

        private void SelectAllInView(bool stagedView)
        {
            var list = stagedView ? visibleStagedItems : visibleUnstagedItems;
            if (list == null || list.Count == 0)
            {
                return;
            }

            var thisSet = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
            var otherSet = stagedView ? selectedUnstagedPaths : selectedStagedPaths;

            if (otherSet.Count > 0)
            {
                otherSet.Clear();
            }

            thisSet.Clear();
            foreach (var item in list)
            {
                if (item != null && !string.IsNullOrEmpty(item.AssetPath))
                {
                    thisSet.Add(item.AssetPath);
                }
            }

            if (stagedView)
            {
                stagedSelectionAnchorIndex = list.Count - 1;
            }
            else
            {
                unstagedSelectionAnchorIndex = list.Count - 1;
            }

            lastActiveStagedView = stagedView;
            unstagedScrollView?.RefreshItems();
            stagedScrollView?.RefreshItems();
        }

        private void TryStartDrag(bool sourceStaged, GitAssetInfo info, int index)
        {
            if (info == null || string.IsNullOrEmpty(info.AssetPath))
            {
                return;
            }

            var currentEvent = Event.current;
            if (currentEvent == null || (currentEvent.type != EventType.MouseDown && currentEvent.type != EventType.MouseDrag))
            {
                return;
            }

            var sourceSet = sourceStaged ? selectedStagedPaths : selectedUnstagedPaths;
            if (!sourceSet.Contains(info.AssetPath))
            {
                HandleRowSelection(sourceStaged, info, index, shift: false, actionKey: false);
            }

            var selectedPaths = (sourceStaged ? selectedStagedPaths : selectedUnstagedPaths).ToList();
            if (selectedPaths.Count == 0)
            {
                selectedPaths.Add(info.AssetPath);
            }

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragPayloadKey, new DragPayload
            {
                SourceStaged = sourceStaged,
                AssetPaths = selectedPaths
            });
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            DragAndDrop.StartDrag(string.IsNullOrEmpty(info.FileName) ? "GitU" : info.FileName);
        }

        private void BindAssetRow(VisualElement element, int index, bool stagedView)
        {
            if (element == null)
            {
                return;
            }

            if (!(element.userData is AssetRowRefs refs))
            {
                return;
            }

            var list = stagedView ? visibleStagedItems : visibleUnstagedItems;
            if (index < 0 || index >= list.Count)
            {
                refs.Info = null;
                return;
            }

            var info = list[index];
            refs.Info = info;
            refs.BoundIndex = index;

            var isSelected = stagedView
                ? selectedStagedPaths.Contains(info.AssetPath)
                : selectedUnstagedPaths.Contains(info.AssetPath);
            element.EnableInClassList("gitU-asset-item--selected", isSelected);
            element.parent?.EnableInClassList("gitU-list-item--selected", isSelected);

            ApplyAssetItemSelectionVisual(element, isSelected);
            ApplyListItemWrapperBaseVisual(element.parent, isSelected);

            if (refs.IconImage != null)
            {
                var icon = string.IsNullOrEmpty(info.AssetPath) ? null : AssetDatabase.GetCachedIcon(info.AssetPath);
                refs.IconImage.image = icon != null ? icon : EditorGUIUtility.IconContent("DefaultAsset Icon").image as Texture2D;
            }

            if (refs.NameLabel != null)
            {
                refs.NameLabel.text = info.FileName;

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

                refs.NameLabel.style.color = nameColor;
            }

            if (refs.ChangeBadgeContainer != null && refs.ChangeBadgeLabel != null)
            {
                var badgeText = info.ChangeType switch
                {
                    GitChangeType.Added => "A",
                    GitChangeType.Deleted => "D",
                    GitChangeType.Modified => "M",
                    GitChangeType.Renamed => "M",
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(badgeText))
                {
                    refs.ChangeBadgeContainer.style.display = DisplayStyle.None;
                }
                else
                {
                    refs.ChangeBadgeContainer.style.display = DisplayStyle.Flex;
                    refs.ChangeBadgeLabel.text = badgeText;

                    Color badgeColor;
                    switch (info.ChangeType)
                    {
                        case GitChangeType.Added:
                            badgeColor = new Color(0.5f, 0.85f, 0.5f);
                            break;
                        case GitChangeType.Deleted:
                            badgeColor = new Color(0.9f, 0.5f, 0.5f);
                            break;
                        case GitChangeType.Modified:
                        case GitChangeType.Renamed:
                            badgeColor = new Color(0.95f, 0.75f, 0.4f);
                            break;
                        default:
                            badgeColor = Color.white;
                            break;
                    }

                    refs.ChangeBadgeLabel.style.color = badgeColor;
                    var borderColor = new Color(badgeColor.r, badgeColor.g, badgeColor.b, 0.75f);
                    refs.ChangeBadgeContainer.style.borderLeftColor = borderColor;
                    refs.ChangeBadgeContainer.style.borderRightColor = borderColor;
                    refs.ChangeBadgeContainer.style.borderTopColor = borderColor;
                    refs.ChangeBadgeContainer.style.borderBottomColor = borderColor;
                    refs.ChangeBadgeContainer.style.backgroundColor = new Color(badgeColor.r, badgeColor.g, badgeColor.b, 0.10f);
                }
            }

            if (refs.PathLabel != null)
            {
                var timeText = info.WorkingTreeTime.HasValue
                    ? info.WorkingTreeTime.Value.ToString("yyyy-MM-dd HH:mm")
                    : "未知时间";

                var displayPath = GetFolderPath(info.AssetPath);
                if (!string.IsNullOrEmpty(info.OriginalPath))
                {
                    var originalFolder = GetFolderPath(info.OriginalPath);
                    if (!string.IsNullOrEmpty(originalFolder))
                    {
                        if (info.ChangeType == GitChangeType.Renamed)
                        {
                            displayPath = $"{originalFolder}（删除） -> {displayPath}";
                        }
                        else if (info.ChangeType == GitChangeType.Deleted)
                        {
                            displayPath = $"{displayPath}（删除） -> {originalFolder}";
                        }
                        else if (!string.Equals(originalFolder, displayPath, StringComparison.OrdinalIgnoreCase))
                        {
                            displayPath = $"{originalFolder} -> {displayPath}";
                        }
                    }
                }

                refs.PathLabel.text = $"{displayPath} ｜ {timeText}";
            }
        }

        private static void ApplyAssetItemSelectionVisual(VisualElement item, bool selected)
        {
            if (item == null)
            {
                return;
            }

            item.style.borderTopWidth = 0;
            item.style.borderRightWidth = 0;
            item.style.borderBottomWidth = 1;
            item.style.borderLeftWidth = 0;
            item.style.borderTopColor = Color.clear;
            item.style.borderRightColor = Color.clear;
            item.style.borderBottomColor = Rgb(20, 20, 20);
            item.style.borderLeftColor = Color.clear;
            item.style.backgroundColor = selected ? Rgba(139, 92, 246, 0.26f) : Color.clear;
        }

        private static void ApplyListItemWrapperBaseVisual(VisualElement wrapper, bool selected)
        {
            if (wrapper == null)
            {
                return;
            }

            wrapper.style.marginTop = 0;
            wrapper.style.marginBottom = 0;
            wrapper.style.marginLeft = 0;
            wrapper.style.marginRight = 0;
            wrapper.style.height = 38;
            wrapper.style.minHeight = 38;
            wrapper.style.borderTopLeftRadius = 0;
            wrapper.style.borderTopRightRadius = 0;
            wrapper.style.borderBottomRightRadius = 8;
            wrapper.style.borderBottomLeftRadius = 8;
            wrapper.style.borderTopWidth = 0;
            wrapper.style.borderRightWidth = 0;
            wrapper.style.borderBottomWidth = 0;
            wrapper.style.borderLeftWidth = 0;
            wrapper.style.borderTopColor = Color.clear;
            wrapper.style.borderRightColor = Color.clear;
            wrapper.style.borderBottomColor = Color.clear;
            wrapper.style.borderLeftColor = Color.clear;

            wrapper.style.backgroundColor = Rgba(255, 255, 255, 0.02f);
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
            var relevantFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var relevantMetaGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                AddRelevantFileNamesForMoveDetection(relevantFileNames, normalized);

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
                    AddRelevantFileNamesForMoveDetection(relevantFileNames, dep);
                }
            }

            AddAncestorFolderMetasForCurrentRelevantPaths();
            AddReverseDependenciesFromChanges(currentChanges, normalizedTargets);
            CollectRelevantMetaGuidsFromChanges(currentChanges, relevantMetaGuids);
            AddPossiblyMovedDeletionsFromChanges(currentChanges, relevantFileNames, relevantMetaGuids);
        }

        private void CollectRelevantMetaGuidsFromChanges(IReadOnlyList<GitChangeEntry> currentChanges, HashSet<string> relevantMetaGuids)
        {
            if (currentChanges == null || currentChanges.Count == 0)
            {
                return;
            }

            if (relevantMetaGuids == null)
            {
                return;
            }

            foreach (var entry in currentChanges)
            {
                if (entry.ChangeType == GitChangeType.Deleted)
                {
                    continue;
                }

                var path = GitUtility.NormalizeAssetPath(entry.Path);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (relevantPaths.Count > 0 && !relevantPaths.Contains(path))
                {
                    continue;
                }

                var metaPath = path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? path : $"{path}.meta";
                if (GitUtility.TryGetMetaGuidFromDisk(metaPath, out var guid) && !string.IsNullOrEmpty(guid))
                {
                    relevantMetaGuids.Add(guid);
                }
            }
        }

        private static void AddRelevantFileNamesForMoveDetection(HashSet<string> fileNames, string unityPath)
        {
            if (fileNames == null || string.IsNullOrEmpty(unityPath))
            {
                return;
            }

            var normalized = GitUtility.NormalizeAssetPath(unityPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            var fileName = Path.GetFileName(normalized);
            if (!string.IsNullOrEmpty(fileName))
            {
                fileNames.Add(fileName);

                // Also track the corresponding .meta file name so we can surface deleted metas from moves.
                if (!fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    fileNames.Add($"{fileName}.meta");
                }
            }
        }

        private void AddPossiblyMovedDeletionsFromChanges(
            IReadOnlyList<GitChangeEntry> currentChanges,
            HashSet<string> relevantFileNames,
            HashSet<string> relevantMetaGuids)
        {
            if (currentChanges == null || currentChanges.Count == 0)
            {
                return;
            }

            if (relevantFileNames == null || relevantFileNames.Count == 0)
            {
                relevantFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            relevantMetaGuids ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var gitRoot = GitUtility.ProjectRoot;
            _ = GitUtility.UnityProjectFolder;

            // If Git does not detect a rename (and outputs "A + D" instead),
            // the old path deletion may no longer be in the dependency graph (only the new path is referenced).
            // Heuristic: include deleted entries whose file name matches any target/dependency file name.
            foreach (var entry in currentChanges)
            {
                if (entry.ChangeType != GitChangeType.Deleted)
                {
                    continue;
                }

                var deletedPath = GitUtility.NormalizeAssetPath(entry.Path);
                if (string.IsNullOrEmpty(deletedPath))
                {
                    continue;
                }

                var deletedName = Path.GetFileName(deletedPath);
                if (string.IsNullOrEmpty(deletedName))
                {
                    continue;
                }

                var matched = relevantFileNames.Contains(deletedName);
                if (!matched && relevantMetaGuids.Count > 0 &&
                    !string.IsNullOrEmpty(gitRoot))
                {
                    var deletedMetaUnityPath = deletedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                        ? deletedPath
                        : $"{deletedPath}.meta";

                    if (GitUtility.TryGetGitRelativePath(deletedMetaUnityPath, out var gitRelativeMetaPath) &&
                        GitUtility.TryGetMetaGuidFromHead(gitRoot, gitRelativeMetaPath, out var deletedGuid) &&
                        !string.IsNullOrEmpty(deletedGuid) &&
                        relevantMetaGuids.Contains(deletedGuid))
                    {
                        matched = true;
                    }
                }

                if (!matched)
                {
                    continue;
                }

                AddRelevantPath(deletedPath);
                AddAncestorFolderMetas(deletedPath);
            }
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

        private static string GetFolderPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            var lastSlash = assetPath.LastIndexOf('/');
            return lastSlash > 0 ? assetPath.Substring(0, lastSlash) : assetPath;
        }

        private static string GetCommitHistoryFilePath(string fileName = CommitHistoryFileName)
        {
            var projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var libraryFolder = Path.Combine(projectFolder, "Library");
            return Path.Combine(libraryFolder, fileName);
        }

        private static string GetStagedAllowListFilePath(string fileName = StagedAllowListFileName)
        {
            var projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var libraryFolder = Path.Combine(projectFolder, "Library");
            return Path.Combine(libraryFolder, fileName);
        }

        private void LoadStagedAllowList()
        {
            stagedAllowList.Clear();

            var path = GetStagedAllowListFilePath();
            if (!File.Exists(path))
            {
                var legacyPath = GetStagedAllowListFilePath(LegacyStagedAllowListFileName);
                if (File.Exists(legacyPath))
                {
                    path = legacyPath;
                }
                else
                {
                    return;
                }
            }

            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var data = JsonUtility.FromJson<StagedAllowListData>(json);
                if (data?.gitRelativePaths == null)
                {
                    return;
                }

                foreach (var p in data.gitRelativePaths)
                {
                    if (string.IsNullOrWhiteSpace(p))
                    {
                        continue;
                    }

                    stagedAllowList.Add(GitUtility.NormalizeAssetPath(p.Trim()));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GitU: 读取暂存白名单失败: {ex.Message}");
                stagedAllowList.Clear();
            }
        }

        private void SaveStagedAllowList()
        {
            var path = GetStagedAllowListFilePath();
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (stagedAllowList.Count == 0)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    return;
                }

                var data = new StagedAllowListData
                {
                    gitRelativePaths = stagedAllowList.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
                };
                File.WriteAllText(path, JsonUtility.ToJson(data));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GitU: 保存暂存白名单失败: {ex.Message}");
            }
        }

        [Serializable]
        private class CommitHistoryData
        {
            public List<string> entries = new List<string>();
        }

        [Serializable]
        private class StagedAllowListData
        {
            public List<string> gitRelativePaths = new List<string>();
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
