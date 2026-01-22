using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace TLNexus.GitU
{
    internal partial class GitUWindow : EditorWindow
    {
        private static readonly Dictionary<Type, Texture2D> TypeIconCache = new Dictionary<Type, Texture2D>();
        private static Texture2D cachedFolderIcon;
        private static Texture2D cachedDefaultAssetIcon;
        private static Texture2D cachedUIImageIcon;

        private const string WindowTitle = "GitU";
        private const string MenuPath = "Window/T·L NEXUS/GitU";
        private const string CommitHistoryFileName = "GitUCommitHistory.json";
        private const string LegacyCommitHistoryFileName = "QuickGitCommitHistory.json";
        private const int MaxCommitHistoryEntries = 8100;
        private const int MaxCommitHistoryDisplayEntries = 100;
        private const string SortKeyPrefsKeyPrefix = "TLNexus.GitU.SortKey:";
        private const string SortOrderPrefsKeyPrefix = "TLNexus.GitU.SortOrder:";
        private const string LanguagePrefKey = "TLNexus.GitU.IsChinese";
        private const string AutoSaveBeforeOpenPrefsKeyPrefix = "TLNexus.GitU.AutoSaveBeforeOpen:";
        private const string AutoOpenGitClientAfterCommitPrefsKeyPrefix = "TLNexus.GitU.AutoOpenGitClientAfterCommit:";
        private const string GitClientPathPrefsKeyPrefix = "TLNexus.GitU.GitClientPath:";
        private const string StagedAllowListFileName = "GitUStagedAllowList.json";
        private const string AssetTypeFilterPrefsKeyPrefix = "TLNexus.GitU.AssetTypeFilters:";
        private const string LegacyAssetTypeFilterPrefsKeyPrefix = "OneKey.GitTools.QuickGitCommit.AssetTypeFilters:";
        private const string AddedColorHex = "#80D980";
        private const string ModifiedColorHex = "#F2BF66";
        private const string DeletedColorHex = "#E68080";
        private const string GitStatusFormatZh = "Git \u72b6\u6001\uff1a\u5f85\u63d0\u4ea4 {0}\uff0c\u672a\u6682\u5b58 {1}";
        private const string GitStatusFormatEn = "Git Status: Staged {0}, Unstaged {1}";
        private const string AddedSegmentFormatZh = "<color={0}>\u65b0\u589e {1}</color>";
        private const string AddedSegmentFormatEn = "<color={0}>Added {1}</color>";
        private const string ModifiedSegmentFormatZh = "<color={0}>\u4fee\u6539 {1}</color>";
        private const string ModifiedSegmentFormatEn = "<color={0}>Modified {1}</color>";
        private const string DeletedSegmentFormatZh = "<color={0}>\u5220\u9664 {1}</color>";
        private const string DeletedSegmentFormatEn = "<color={0}>Deleted {1}</color>";
        private const string SearchPlaceholderTextZh = "\u641c\u7d22\u6587\u4ef6 / \u8def\u5f84 / \u53d8\u66f4\u2026";
        private const string SearchPlaceholderTextEn = "Search files / paths / changes...";
        private const string CommitMessagePlaceholderTextZh = "\u5728\u8fd9\u91cc\u586b\u5199\u63d0\u4ea4\u4fe1\u606f\u2026";
        private const string CommitMessagePlaceholderTextEn = "Write commit message here...";
        private const string SearchTooltipZh = "\u641c\u7d22\u6587\u4ef6\u3001\u8def\u5f84\u6216\u53d8\u66f4\u2026";
        private const string SearchTooltipEn = "Search files, paths, or changes...";
        private const string SaveToDiskHintZh = "\u91cd\u8981\u63d0\u793a\uff1a\u6539\u6750\u8d28/\u52a8\u753b/Prefab/\u573a\u666f\u540e\uff0c\u8bf7\u5148 Ctrl+S\uff08\u6216 Save Assets\uff09\u5199\u76d8\uff0c\u518d\u6253\u5f00/\u5237\u65b0\uff1b\u5426\u5219\u53ef\u80fd\u6f0f\u663e\u793a\u3002";
        private const string SaveToDiskHintEn = "Important: After editing materials/animations/prefabs/scenes, press Ctrl+S (or Save Assets) before opening/refreshing, otherwise changes may not show up.";
        private static readonly Color SearchFieldTextColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color SearchFieldPlaceholderTextColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color CommitFieldTextColor = Color.white;
        private static readonly Color CommitFieldPlaceholderTextColor = new Color(1f, 1f, 1f, 0.3f);

        private UnityEngine.Object targetAsset;
        private string targetAssetPath;
        private readonly List<GitAssetInfo> assetInfos = new List<GitAssetInfo>();
        private List<GitChangeEntry> gitChanges = new List<GitChangeEntry>();
        private List<string> activeRepositoryRoots = new List<string>();
        private bool hasMultipleRepositories;
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
            window.minSize = new Vector2(1040, 900);
            window.maxSize = new Vector2(1040, 900);
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
        private bool hasAutoStagedModifiedFiles;
        private bool skipRefreshUiDuringAutoStage;
        private readonly Dictionary<string, HashSet<string>> stagedAllowListByRoot =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool autoCleanExternalStagedOnOpen;
        private bool hasAutoCleanedExternalStagedOnOpen;
        private bool recaptureInitialStagedSnapshotAfterAutoClean;
        private Dictionary<string, List<string>> pendingStageGitPathsByRoot;
        private Dictionary<string, List<string>> pendingUnstageGitPathsByRoot;
        private bool showAdded = true;
        private bool showModified = true;
        private bool showDeleted = true;
        private static readonly UnityAssetTypeFilter[] defaultSelectedAssetTypeFilters =
        {
            UnityAssetTypeFilter.All
        };

        private readonly HashSet<UnityAssetTypeFilter> assetTypeFilters = new HashSet<UnityAssetTypeFilter>(defaultSelectedAssetTypeFilters);
        private string searchQuery = string.Empty;
        private string statusMessage;
        private string commitMessage = string.Empty;
        private string repositoryStatusMessage = "\u6b63\u5728\u68c0\u6d4b Git \u72b6\u6001...";
        private bool searchPlaceholderActive;
        private bool commitMessagePlaceholderActive;
        private bool isChineseUi;
        private bool autoSaveBeforeOpen;
        private bool autoOpenGitClientAfterCommit;
        private string gitClientPath;
        private List<string> commitHistory;
        private List<string> savedCommitHistory;
        private List<string> fallbackCommitHistory;
        private bool hasAttemptedLoadGitHistory;
        private bool historyDropdownVisible;
        private int unstagedSelectionAnchorIndex = -1;
        private int stagedSelectionAnchorIndex = -1;
        private bool? lastActiveStagedView;
        private bool autoSaveAttemptedThisOpen;

        private enum AssetSortKey
        {
            GitStatus,
            AssetType,
            Name,
            Time,
            Path
        }

        private enum SortOrder
        {
            Ascending,
            Descending
        }

        private AssetSortKey assetSortKey = AssetSortKey.Path;
        private SortOrder assetSortOrder = SortOrder.Ascending;

        // UI Toolkit elements
        private ObjectField targetField;
        private Label multiSelectLabel;
        private Label pathLabel;
        private Label statusLabel;
        private Label saveToDiskHintLabel;
        private Button addedButton;
        private Button modifiedButton;
        private Button deletedButton;
        private ToolbarMenu assetTypeMenu;
        private TextField searchField;
        private Label unstagedTitleLabel;
        private Label unstagedHeaderLabel;
        private Label stagedTitleLabel;
        private Label stagedHeaderLabel;
        private ListView unstagedScrollView;
        private ListView stagedScrollView;
        private Label commitMessageTitleLabel;
        private Label commitMessageHintLabel;
        private TextField commitMessageField;
        private Button commitButton;
        private Button commitAndPushButton;
        private VisualElement historyButton;
        private Label historyButtonLabel;
        private Button refreshButton;
        private Button repositoryStatusUpButton;
        private Label sortInfoLabel;
        private Button settingsButton;
        private VisualElement historyDropdown;
        private ListView historyListView;
        private Label repositoryStatusLabel;
        private Label toastLabel;
        private VisualElement dragBadge;
        private Label dragBadgeLabel;
        private bool dragBadgeCleanupQueued;
        private double lastDragBadgeUpdateTime;

        private VisualElement leftColumn;
        private VisualElement unstagedListContainer;
        private VisualElement stagedListContainer;
        private VisualElement unstagedEmptyHintOverlay;
        private VisualElement stagedEmptyHintOverlay;
        private Label unstagedEmptyHintLabel;
        private Label stagedEmptyHintLabel;
        private Label historyTitleLabel;
        private Label historyHintLabel;

        private readonly List<GitAssetInfo> visibleUnstagedItems = new List<GitAssetInfo>();
        private readonly List<GitAssetInfo> visibleStagedItems = new List<GitAssetInfo>();
        private readonly Dictionary<string, UnityAssetTypeFilter> assetTypeCache = new Dictionary<string, UnityAssetTypeFilter>(StringComparer.OrdinalIgnoreCase);
        private int lastUnstagedVisibleCount = -1;
        private int lastStagedVisibleCount = -1;
        private bool assetListViewsConfigured;
        private bool visibleListsInitialized;

        private VisualElement sortMenuOverlay;
        private VisualElement sortMenuPanel;
        private int sortMenuPositionRequestId;
        private int sortMenuPendingPositionAttempts;

        private GitUSettingsOverlay settingsOverlay;

        private static string AssetTypeFilterPrefsKey => $"{AssetTypeFilterPrefsKeyPrefix}{Application.dataPath}";
        private static string LegacyAssetTypeFilterPrefsKey => $"{LegacyAssetTypeFilterPrefsKeyPrefix}{Application.dataPath}";
        private static string SortKeyPrefsKey => $"{SortKeyPrefsKeyPrefix}{Application.dataPath}";
        private static string SortOrderPrefsKey => $"{SortOrderPrefsKeyPrefix}{Application.dataPath}";
        private static string AutoSaveBeforeOpenPrefsKey => $"{AutoSaveBeforeOpenPrefsKeyPrefix}{Application.dataPath}";
        private static string AutoOpenGitClientAfterCommitPrefsKey => $"{AutoOpenGitClientAfterCommitPrefsKeyPrefix}{Application.dataPath}";
        private static string GitClientPathPrefsKey => $"{GitClientPathPrefsKeyPrefix}{Application.dataPath}";

        private sealed class AssetRowRefs
        {
            public bool StagedView;
            public VisualElement IconContainer;
            public Image IconImage;
            public Label NameLabel;
            public Label PathLabel;
            public VisualElement ChangeBadgeContainer;
            public Label ChangeBadgeLabel;
            public GitAssetInfo Info;
            public int BoundIndex;

            public bool DragArmed;
            public Vector3 DragStartPosition;
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
            window.minSize = new Vector2(1040, 900);
            window.maxSize = new Vector2(1040, 900);
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
            window.minSize = new Vector2(1040, 900);
            window.maxSize = new Vector2(1040, 900);
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
            autoSaveAttemptedThisOpen = false;
            autoSaveBeforeOpen = EditorPrefs.GetBool(AutoSaveBeforeOpenPrefsKey, false);
            if (autoSaveBeforeOpen)
            {
                TryAutoSaveBeforeOpen();
            }

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
            minSize = new Vector2(1040, 900);
            maxSize = new Vector2(1040, 900);

            // For domain reload / layout reload, rebuild UI
            CreateGUI();
            autoSaveAttemptedThisOpen = false;

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

                if (autoSaveBeforeOpen && !autoSaveAttemptedThisOpen)
                {
                    TryAutoSaveBeforeOpen();
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
                    statusMessage = isChineseUi
                        ? "请先在 Project 或 Hierarchy 选择目标资源，再打开窗口（或在顶部“目标资源”里选择）。"
                        : "Select a target in Project or Hierarchy before opening the window (or choose one in the Target field above).";
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
            pendingStageGitPathsByRoot = null;
            pendingUnstageGitPathsByRoot = null;
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

            // DragAndDrop 的结束事件在 UI Toolkit 下并不总能可靠回调（例如拖拽过程中回到源区域松开）。
            // 兜底：如果拖拽徽章还在显示、鼠标已松开且一段时间未收到 DragUpdated，则认为拖拽已结束并清理。
            if (dragBadge != null && dragBadge.style.display.value != DisplayStyle.None)
            {
                var anyMouseDown = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
                if (!anyMouseDown &&
                    lastDragBadgeUpdateTime > 0 &&
                    EditorApplication.timeSinceStartup - lastDragBadgeUpdateTime > 0.05)
                {
                    DragAndDrop.SetGenericData(DragPayloadKey, null);
                    HideDragBadge();
                }
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

        private void TryAutoSaveBeforeOpen()
        {
            autoSaveAttemptedThisOpen = true;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[GitU] Auto-save on open skipped (play mode).");
                return;
            }

            var isChinese = EditorPrefs.GetInt(LanguagePrefKey, 1) != 0;
            var dirtyScenePathsBefore = GetDirtyOpenScenePaths(out var untitledDirtyScenesBefore);
            TryEndEditingActiveTextField();

            var prefabStageSaved = TrySaveDirtyPrefabStage(out var prefabStageSummary);
            if (!prefabStageSaved && !string.IsNullOrEmpty(prefabStageSummary))
            {
                Debug.LogWarning($"[GitU] {prefabStageSummary}");
            }

            var assetsOk = true;
            try
            {
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                assetsOk = false;
                Debug.LogWarning($"[GitU] Auto SaveAssets failed: {ex.Message}");
            }

            var scenesOk = true;
            try
            {
                scenesOk = EditorSceneManager.SaveOpenScenes();
            }
            catch (Exception ex)
            {
                scenesOk = false;
                Debug.LogWarning($"[GitU] Auto SaveOpenScenes failed: {ex.Message}");
            }

            var dirtyScenePathsAfter = GetDirtyOpenScenePaths(out var untitledDirtyScenesAfter);
            if (dirtyScenePathsAfter.Count > 0 || untitledDirtyScenesAfter > 0)
            {
                var remaining = string.Join(", ", dirtyScenePathsAfter.Select(Path.GetFileNameWithoutExtension));
                var extra = untitledDirtyScenesAfter > 0 ? $" + Untitled({untitledDirtyScenesAfter})" : string.Empty;
                Debug.LogWarning($"[GitU] Auto-save on open: scenes still dirty after save ({dirtyScenePathsAfter.Count}{extra}): {remaining}");
                scenesOk = false;
            }

            var prefabSegment = string.Empty;
            if (!string.IsNullOrEmpty(prefabStageSummary))
            {
                prefabSegment = $" / Prefab:{(prefabStageSaved ? "OK" : "Fail")}";
            }

            var message = isChinese
                ? $"已自动保存（Assets:{(assetsOk ? "OK" : "Fail")} / Scenes:{(scenesOk ? "OK" : "Fail")}{prefabSegment} / Dirty:{dirtyScenePathsBefore.Count}->{dirtyScenePathsAfter.Count}）"
                : $"Auto-saved (Assets:{(assetsOk ? "OK" : "Fail")} / Scenes:{(scenesOk ? "OK" : "Fail")}{prefabSegment} / Dirty:{dirtyScenePathsBefore.Count}->{dirtyScenePathsAfter.Count})";

            Debug.Log($"[GitU] {message}");
            ShowTempNotification(message, 2.2f);
        }

        private static List<string> GetDirtyOpenScenePaths(out int untitledDirtyScenes)
        {
            untitledDirtyScenes = 0;
            var paths = new List<string>();

            int sceneCount;
            try
            {
                sceneCount = EditorSceneManager.sceneCount;
            }
            catch
            {
                return paths;
            }

            for (var i = 0; i < sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene;
                try
                {
                    scene = EditorSceneManager.GetSceneAt(i);
                }
                catch
                {
                    continue;
                }

                if (!scene.IsValid() || !scene.isDirty)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path))
                {
                    untitledDirtyScenes++;
                    continue;
                }

                paths.Add(scene.path);
            }

            return paths;
        }

        private static bool TryEndEditingActiveTextField()
        {
            try
            {
                var method = typeof(EditorGUI).GetMethod("EndEditingActiveTextField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method != null)
                {
                    method.Invoke(null, null);
                    return true;
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                GUI.FocusControl(null);
            }
            catch
            {
                // ignored
            }

            try
            {
                GUIUtility.keyboardControl = 0;
            }
            catch
            {
                // ignored
            }

            try
            {
                var prop = typeof(EditorGUIUtility).GetProperty("editingTextField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(null, false, null);
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        private static bool TrySaveDirtyPrefabStage(out string summary)
        {
            summary = null;

            object prefabStage = null;
            try
            {
                var utility = Type.GetType("UnityEditor.SceneManagement.PrefabStageUtility, UnityEditor", throwOnError: false)
                              ?? Type.GetType("UnityEditor.Experimental.SceneManagement.PrefabStageUtility, UnityEditor", throwOnError: false);
                var method = utility?.GetMethod("GetCurrentPrefabStage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                prefabStage = method?.Invoke(null, null);
            }
            catch
            {
                prefabStage = null;
            }

            if (prefabStage == null)
            {
                return true;
            }

            try
            {
                var stageType = prefabStage.GetType();

                string assetPath = null;
                var assetPathProp = stageType.GetProperty("assetPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                  ?? stageType.GetProperty("prefabAssetPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (assetPathProp != null)
                {
                    assetPath = assetPathProp.GetValue(prefabStage) as string;
                }

                var sceneProp = stageType.GetProperty("scene", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var stageScene = sceneProp != null
                    ? (UnityEngine.SceneManagement.Scene)sceneProp.GetValue(prefabStage)
                    : default;

                var wasDirty = stageScene.IsValid() && stageScene.isDirty;
                if (!wasDirty)
                {
                    return true;
                }

                var saveMethod = stageType.GetMethod("SavePrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (saveMethod != null)
                {
                    saveMethod.Invoke(prefabStage, null);
                }
                else
                {
                    EditorApplication.ExecuteMenuItem("File/Save");
                }

                stageScene = sceneProp != null
                    ? (UnityEngine.SceneManagement.Scene)sceneProp.GetValue(prefabStage)
                    : default;
                var stillDirty = stageScene.IsValid() && stageScene.isDirty;

                if (stillDirty)
                {
                    summary = string.IsNullOrEmpty(assetPath)
                        ? "PrefabStage: save attempted but still dirty."
                        : $"PrefabStage: save attempted but still dirty ({Path.GetFileNameWithoutExtension(assetPath)}).";
                    return false;
                }

                summary = string.IsNullOrEmpty(assetPath)
                    ? "PrefabStage: saved."
                    : $"PrefabStage: saved ({Path.GetFileNameWithoutExtension(assetPath)}).";
                return true;
            }
            catch (Exception ex)
            {
                summary = $"PrefabStage: save failed ({ex.Message}).";
                return false;
            }
        }

        private bool TryOpenExternalGitClient(out string error)
        {
            error = null;

            var path = gitClientPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = isChineseUi ? "未设置 Git 客户端路径。" : "Git client path is not set.";
                return false;
            }

            if (!File.Exists(path))
            {
                error = isChineseUi ? "Git 客户端路径无效（文件不存在）。" : "Git client path is invalid (file not found).";
                return false;
            }

            var workingDir = GitUtility.ProjectRoot;
            if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
            {
                workingDir = Application.dataPath;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = workingDir,
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                error = (isChineseUi ? "打开 Git 客户端失败：" : "Failed to open Git client: ") + ex.Message;
                return false;
            }
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

        private void HideTempNotification()
        {
            if (toastLabel == null)
            {
                return;
            }

            toastLabel.style.display = DisplayStyle.None;
            toastLabel.text = string.Empty;
            notificationEndTime = 0;
        }

        private void CreateGUI()
        {
            historyDropdownVisible = false;
            autoCleanExternalStagedOnOpen = true;
            LoadStagedAllowList();
            LoadAssetTypeFilters();
            LoadSortSettings();
            isChineseUi = EditorPrefs.GetInt(LanguagePrefKey, 1) != 0;
            GitUtility.SetLanguage(isChineseUi);
            autoSaveBeforeOpen = EditorPrefs.GetBool(AutoSaveBeforeOpenPrefsKey, false);
            autoOpenGitClientAfterCommit = EditorPrefs.GetBool(AutoOpenGitClientAfterCommitPrefsKey, false);
            gitClientPath = EditorPrefs.GetString(GitClientPathPrefsKey, string.Empty);
            assetListViewsConfigured = false;
            lastUnstagedVisibleCount = -1;
            lastStagedVisibleCount = -1;
            visibleUnstagedItems.Clear();
            visibleStagedItems.Clear();
            visibleListsInitialized = false;

            var root = rootVisualElement;
            root.Clear();
            sortMenuOverlay = null;
            sortMenuPanel = null;
            sortMenuPositionRequestId = 0;
            settingsOverlay = null;

            if (!BuildLayoutFromCode(root))
            {
                return;
            }

            UpdateSortInfoLabel();
            ApplyLanguageToUI();

            root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (historyDropdownVisible)
                {
                    UpdateHistoryDropdownLayout();
                }
            });

            EnsureAssetListViewsConfigured();
            UpdateListEmptyHints();
            EnsureSettingsOverlay();

            if (saveToDiskHintLabel != null)
            {
                saveToDiskHintLabel.style.whiteSpace = WhiteSpace.Normal;
            }
            UpdateSaveToDiskHintVisibility();

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
                historyButton.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!historyButton.enabledSelf)
                    {
                        return;
                    }

                    ToggleHistoryDropdown();
                });
            }
            if (settingsButton != null)
            {
                settingsButton.tooltip = isChineseUi ? "设置" : "Settings";
                settingsButton.clicked += ToggleSettingsOverlay;
            }
            if (repositoryStatusUpButton != null)
            {
                repositoryStatusUpButton.tooltip = isChineseUi ? "排序" : "Sort";
                repositoryStatusUpButton.clicked += ToggleSortMenu;
            }
            if (historyListView != null)
            {
                historyListView.selectionType = SelectionType.Single;
                historyListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
                historyListView.makeItem = () =>
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.height = StyleKeyword.Auto;
                    row.style.minHeight = 30;
                    row.style.paddingBottom = 6;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(1f, 1f, 1f, 0.05f);

                    var icon = new Image { name = "historyItemIcon" };
                    icon.image = EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D;
                    icon.scaleMode = ScaleMode.ScaleToFit;
                    icon.tintColor = new Color(1f, 1f, 1f, 0.5f);
                    icon.style.width = 14;
                    icon.style.height = 14;
                    icon.style.marginLeft = 10;
                    icon.style.marginRight = 6;
                    icon.style.marginTop = 0;
                    icon.style.marginBottom = 0;
                    row.Add(icon);

                    var label = new Label { name = "historyItemLabel" };
                    label.style.flexGrow = 1;
                    label.style.flexShrink = 1;
                    label.style.unityTextAlign = TextAnchor.UpperLeft;
                    label.style.fontSize = 11;
                    label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.marginTop = 0;
                    label.style.marginRight = 0;
                    label.style.marginBottom = 0;
                    label.style.marginLeft = 0;
                    row.Add(label);

                    return row;
                };
                historyListView.bindItem = (element, i) =>
                {
                    var label = element.Q<Label>("historyItemLabel");
                    if (label != null)
                    {
                        var hasEntries = commitHistory != null && i >= 0 && i < commitHistory.Count;
                        label.text = hasEntries ? commitHistory[i] : string.Empty;
                    }
                };
                historyListView.onSelectionChange += OnHistoryListSelectionChanged;
            }
            root.RegisterCallback<MouseDownEvent>(OnRootMouseDown, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);
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

        private void UpdateHistoryDropdownLayout()
        {
            if (historyDropdown == null)
            {
                return;
            }

            var host = historyDropdown.parent ?? rootVisualElement;
            if (host == null)
            {
                return;
            }

            var hostWidth = host.resolvedStyle.width;
            var hostHeight = host.resolvedStyle.height;
            if (hostWidth <= 0f || hostHeight <= 0f)
            {
                return;
            }

            const float widthOverHeight = 3f / 4f;
            var maxWidth = hostWidth * 0.92f;
            var maxHeight = hostHeight * 0.92f;

            var desiredHeight = Mathf.Clamp(hostHeight * 0.75f, 360f, 720f);
            var desiredWidth = desiredHeight * widthOverHeight;

            if (desiredWidth > maxWidth)
            {
                desiredWidth = maxWidth;
                desiredHeight = desiredWidth / widthOverHeight;
            }
            if (desiredHeight > maxHeight)
            {
                desiredHeight = maxHeight;
                desiredWidth = desiredHeight * widthOverHeight;
            }

            historyDropdown.style.width = desiredWidth;
            historyDropdown.style.height = desiredHeight;
            historyDropdown.style.left = (hostWidth - desiredWidth) * 0.5f;
            historyDropdown.style.top = (hostHeight - desiredHeight) * 0.5f;
        }

        private void EnsureSettingsOverlay()
        {
            if (settingsOverlay != null)
            {
                return;
            }

            var version = typeof(GitUWindow).Assembly.GetName().Version?.ToString();
            if (string.IsNullOrWhiteSpace(version))
            {
                version = Application.unityVersion;
            }

            var initialChinese = EditorPrefs.GetInt(LanguagePrefKey, 1) != 0;
            var initialAutoSaveBeforeOpen = EditorPrefs.GetBool(AutoSaveBeforeOpenPrefsKey, false);
            var initialAutoOpenGitClientAfterCommit = EditorPrefs.GetBool(AutoOpenGitClientAfterCommitPrefsKey, false);
            var initialGitClientPath = EditorPrefs.GetString(GitClientPathPrefsKey, string.Empty);

            settingsOverlay = new GitUSettingsOverlay(
                aboutVersion: "v1.5.0",
                aboutAuthor: "T·L",
                aboutAuthorLink: "https://jxaytl13.github.io",
                aboutDocumentLink: null,
                aboutDocumentLinkChinese: "https://my.feishu.cn/wiki/IngGwL2hviirYgkDkTzcpLR1n8e?from=from_copylink",
                aboutDocumentLinkEnglish: "https://my.feishu.cn/wiki/UNSVw7dAZiHSaBkM3encs7ybnne?from=from_copylink",
                toolIntroductionZh: "GitU 特色：\n- Unity 编辑器内的轻量 Git 提交面板，专注“变更 -> 待提交 -> 提交/推送”流程\n- 变更区 / 待提交区双列表，支持拖拽与批量选择，快速整理待提交内容\n- 提交信息支持多行（Enter 换行），提交记录可查看并复用\n- 保持纯 Git 工作流：提交就是标准 Git commit，可用任意 Git 客户端继续 pull/rebase/merge\n- “提交并推送”只执行 push（不自动拉取），避免在 Unity 内引入不可控的合并/冲突流程",
                toolIntroductionEn: "GitU Highlights:\n- A lightweight in-Editor Git commit panel for Unity, focused on the “changes -> staged -> commit/push” flow\n- Dual lists (Unstaged / Staged) with drag & batch selection to curate staged content fast\n- Multi-line commit messages (Enter for new line); commit history can be viewed and reused\n- Pure Git workflow: commits are standard Git commits; continue work in any Git client (pull/rebase/merge)\n- “Commit & Push” only runs push (no auto pull) to avoid uncontrolled merges/conflicts inside Unity",
                getConfigPath: () =>
                {
                    var commitHistoryPath = GetCommitHistoryFilePath();
                    var allowListPath = GetStagedAllowListFilePath();
                    return $"{commitHistoryPath}\n{allowListPath}";
                },
                initialChinese: initialChinese,
                initialAutoSaveBeforeOpen: initialAutoSaveBeforeOpen,
                initialAutoOpenGitClientAfterCommit: initialAutoOpenGitClientAfterCommit,
                initialGitClientPath: initialGitClientPath,
                onLanguageChanged: OnSettingsLanguageChanged,
                onAutoSaveBeforeOpenChanged: OnAutoSaveBeforeOpenChanged,
                onAutoOpenGitClientAfterCommitChanged: OnAutoOpenGitClientAfterCommitChanged,
                onGitClientPathChanged: OnGitClientPathChanged);

            rootVisualElement.Add(settingsOverlay.Root);
        }

        private void OnAutoSaveBeforeOpenChanged(bool enabled)
        {
            EditorPrefs.SetBool(AutoSaveBeforeOpenPrefsKey, enabled);
            autoSaveBeforeOpen = enabled;
            UpdateSaveToDiskHintVisibility();
        }

        private void OnAutoOpenGitClientAfterCommitChanged(bool enabled)
        {
            EditorPrefs.SetBool(AutoOpenGitClientAfterCommitPrefsKey, enabled);
            autoOpenGitClientAfterCommit = enabled;
        }

        private void OnGitClientPathChanged(string path)
        {
            path ??= string.Empty;
            EditorPrefs.SetString(GitClientPathPrefsKey, path);
            gitClientPath = path;
        }

        private void OnSettingsLanguageChanged(bool isChinese)
        {
            EditorPrefs.SetInt(LanguagePrefKey, isChinese ? 1 : 0);

            if (isChineseUi == isChinese)
            {
                return;
            }

            isChineseUi = isChinese;
            GitUtility.SetLanguage(isChineseUi);
            ApplyLanguageToUI();

            if (sortMenuOverlay != null && sortMenuOverlay.resolvedStyle.display == DisplayStyle.Flex)
            {
                RebuildSortMenuContents();
            }
        }

        private void ToggleSettingsOverlay()
        {
            if (settingsOverlay == null)
            {
                EnsureSettingsOverlay();
            }

            if (settingsOverlay == null)
            {
                return;
            }

            if (settingsOverlay.Root.resolvedStyle.display == DisplayStyle.Flex)
            {
                settingsOverlay.Hide();
            }
            else
            {
                settingsOverlay.Show();
            }
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
                searchField.SetValueWithoutNotify(isChineseUi ? SearchPlaceholderTextZh : SearchPlaceholderTextEn);
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
            searchField.SetValueWithoutNotify(isChineseUi ? SearchPlaceholderTextZh : SearchPlaceholderTextEn);
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
                commitMessageField.SetValueWithoutNotify(isChineseUi ? CommitMessagePlaceholderTextZh : CommitMessagePlaceholderTextEn);
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
            commitMessageField.SetValueWithoutNotify(isChineseUi ? CommitMessagePlaceholderTextZh : CommitMessagePlaceholderTextEn);
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
                statusMessage = isChineseUi ? "刷新已取消。" : "Refresh canceled.";
                UpdateHeaderLabels();
                UpdateCommitButtonsEnabled();
                ForceRepaintUI();
                return;
            }

            if (completedTask.IsFaulted)
            {
                var error = completedTask.Exception?.GetBaseException().Message ?? (isChineseUi ? "未知错误" : "Unknown error");
                statusMessage = isChineseUi ? $"刷新失败：{error}" : $"Refresh failed: {error}";
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
                ShowTempNotification(isChineseUi ? "操作已取消。" : "Operation canceled.");
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
                var error = completedTask.Exception?.GetBaseException().Message ?? (isChineseUi ? "未知错误" : "Unknown error");
                ShowTempNotification(isChineseUi ? $"操作失败：{error}" : $"Operation failed: {error}");
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
                if (result.Success && pendingStageGitPathsByRoot != null && pendingStageGitPathsByRoot.Count > 0)
                {
                    AddAllowListPaths(pendingStageGitPathsByRoot);
                }

                pendingStageGitPathsByRoot = null;
            }
            else if (completedKind == GitOperationKind.Unstage)
            {
                if (result.Success && pendingUnstageGitPathsByRoot != null && pendingUnstageGitPathsByRoot.Count > 0)
                {
                    RemoveAllowListPaths(pendingUnstageGitPathsByRoot);
                }

                pendingUnstageGitPathsByRoot = null;
            }

            if (completedKind == GitOperationKind.Commit || completedKind == GitOperationKind.CommitAndPush)
            {
                var dialogTitle = isChineseUi ? "提交" : "Commit";
                var dialogOk = isChineseUi ? "确定" : "OK";
                var dialogCancel = isChineseUi ? "取消" : "Cancel";
                var dialogSummary = string.IsNullOrEmpty(result.Summary)
                    ? (isChineseUi ? "操作完成。" : "Done.")
                    : result.Summary;

                if (result.CommitSucceeded && autoOpenGitClientAfterCommit)
                {
                    var dialogMessage = dialogSummary + "\n\n" + (isChineseUi ? "点击“确定”打开 Git 客户端。" : "Click OK to open the Git client.");
                    var confirmedOpen = EditorUtility.DisplayDialog(dialogTitle, dialogMessage, dialogOk, dialogCancel);
                    if (confirmedOpen)
                    {
                        if (!TryOpenExternalGitClient(out var error))
                        {
                            EditorUtility.DisplayDialog(dialogTitle, error ?? (isChineseUi ? "打开 Git 客户端失败。" : "Failed to open Git client."), dialogOk);
                        }
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog(dialogTitle, dialogSummary, dialogOk);
                }

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
            multiSelectLabel = root.Q<Label>("multiSelectLabel");
            pathLabel = root.Q<Label>("pathLabel");
            statusLabel = root.Q<Label>("statusLabel");
            saveToDiskHintLabel = root.Q<Label>("saveToDiskHintLabel");
            addedButton = root.Q<Button>("addedButton");
            modifiedButton = root.Q<Button>("modifiedButton");
            deletedButton = root.Q<Button>("deletedButton");
            assetTypeMenu = root.Q<ToolbarMenu>("assetTypeMenu");
            searchField = root.Q<TextField>("searchField");
            unstagedTitleLabel = root.Q<Label>("unstagedTitleLabel");
            unstagedHeaderLabel = root.Q<Label>("unstagedHeaderLabel");
            stagedTitleLabel = root.Q<Label>("stagedTitleLabel");
            stagedHeaderLabel = root.Q<Label>("stagedHeaderLabel");
            unstagedScrollView = root.Q<ListView>("unstagedScrollView");
            stagedScrollView = root.Q<ListView>("stagedScrollView");
            commitMessageTitleLabel = root.Q<Label>("commitMessageTitleLabel");
            commitMessageHintLabel = root.Q<Label>("commitMessageHintLabel");
            commitMessageField = root.Q<TextField>("commitMessageField");
            commitButton = root.Q<Button>("commitButton");
            commitAndPushButton = root.Q<Button>("commitAndPushButton");
            historyButton = root.Q<VisualElement>("historyButton");
            historyButtonLabel = root.Q<Label>("historyButtonLabel");
            refreshButton = root.Q<Button>("refreshButton");
            repositoryStatusUpButton = root.Q<Button>("repositoryStatusUpButton");
            sortInfoLabel = root.Q<Label>("sortInfoLabel");
            settingsButton = root.Q<Button>("Setting");
            historyDropdown = root.Q<VisualElement>("historyDropdown");
            historyListView = root.Q<ListView>("historyListView");
            historyTitleLabel = root.Q<Label>("historyTitleLabel");
            historyHintLabel = root.Q<Label>("historyHintLabel");
            repositoryStatusLabel = root.Q<Label>("repositoryStatusLabel");
            leftColumn = root.Q<VisualElement>("leftColumn");
            toastLabel = root.Q<Label>("toastLabel");
            dragBadge = root.Q<VisualElement>("dragBadge");
            dragBadgeLabel = root.Q<Label>("dragBadgeLabel");
            unstagedListContainer = root.Q<VisualElement>("unstagedListContainer");
            stagedListContainer = root.Q<VisualElement>("stagedListContainer");
            unstagedEmptyHintOverlay = root.Q<VisualElement>("unstagedEmptyHintOverlay");
            stagedEmptyHintOverlay = root.Q<VisualElement>("stagedEmptyHintOverlay");
            unstagedEmptyHintLabel = root.Q<Label>("unstagedEmptyHintLabel");
            stagedEmptyHintLabel = root.Q<Label>("stagedEmptyHintLabel");
        }

        private void ShowDragBadge(Vector2 panelMousePosition, int count)
        {
            if (dragBadge == null || dragBadgeLabel == null)
            {
                return;
            }

            if (count <= 0)
            {
                HideDragBadge();
                return;
            }

            var host = dragBadge.parent ?? rootVisualElement;
            if (host != null)
            {
                var local = host.WorldToLocal(panelMousePosition);
                dragBadge.style.left = local.x + DragBadgeOffset.x;
                dragBadge.style.top = local.y + DragBadgeOffset.y;
            }

            dragBadgeLabel.text = count.ToString();
            dragBadgeLabel.style.fontSize = count >= 100 ? 9 : (count >= 10 ? 10 : 11);
            dragBadge.style.display = DisplayStyle.Flex;
            lastDragBadgeUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void HideDragBadge()
        {
            if (dragBadge == null)
            {
                return;
            }

            dragBadge.style.display = DisplayStyle.None;
            lastDragBadgeUpdateTime = 0;
        }

        private void LoadSortSettings()
        {
            var key = EditorPrefs.GetInt(SortKeyPrefsKey, (int)AssetSortKey.Path);
            if (Enum.IsDefined(typeof(AssetSortKey), key))
            {
                assetSortKey = (AssetSortKey)key;
            }
            else
            {
                assetSortKey = AssetSortKey.Path;
            }

            var order = EditorPrefs.GetInt(SortOrderPrefsKey, (int)SortOrder.Ascending);
            if (Enum.IsDefined(typeof(SortOrder), order))
            {
                assetSortOrder = (SortOrder)order;
            }
            else
            {
                assetSortOrder = SortOrder.Ascending;
            }

            UpdateSortInfoLabel();
        }

        private void SaveSortSettings()
        {
            EditorPrefs.SetInt(SortKeyPrefsKey, (int)assetSortKey);
            EditorPrefs.SetInt(SortOrderPrefsKey, (int)assetSortOrder);
            UpdateSortInfoLabel();
        }

        private void UpdateSortInfoLabel()
        {
            if (sortInfoLabel == null)
            {
                return;
            }

            sortInfoLabel.text = $"{GetSortKeyLabel(assetSortKey)}｜{GetSortOrderLabel(assetSortOrder)}";
        }

        private string GetSortKeyLabel(AssetSortKey key)
        {
            switch (key)
            {
                case AssetSortKey.GitStatus:
                    return isChineseUi ? "Git 状态" : "Git Status";
                case AssetSortKey.AssetType:
                    return isChineseUi ? "资源类型" : "Type";
                case AssetSortKey.Name:
                    return isChineseUi ? "名称" : "Name";
                case AssetSortKey.Time:
                    return isChineseUi ? "时间" : "Time";
                case AssetSortKey.Path:
                    return isChineseUi ? "路径" : "Path";
                default:
                    return isChineseUi ? "路径" : "Path";
            }
        }

        private string GetSortOrderLabel(SortOrder order)
        {
            switch (order)
            {
                case SortOrder.Ascending:
                    return isChineseUi ? "升序" : "Ascending";
                case SortOrder.Descending:
                    return isChineseUi ? "降序" : "Descending";
                default:
                    return isChineseUi ? "升序" : "Ascending";
            }
        }

        private void ApplyLanguageToUI()
        {
            if (addedButton != null) addedButton.text = isChineseUi ? "新增 (A)" : "Added (A)";
            if (modifiedButton != null) modifiedButton.text = isChineseUi ? "修改 (M)" : "Modified (M)";
            if (deletedButton != null) deletedButton.text = isChineseUi ? "删除 (D)" : "Deleted (D)";

            if (refreshButton != null)
            {
                refreshButton.text = string.Empty;
                refreshButton.tooltip = isChineseUi ? "刷新" : "Refresh";
            }
            if (settingsButton != null) settingsButton.text = string.Empty;
            if (repositoryStatusUpButton != null) repositoryStatusUpButton.text = string.Empty;
            if (historyButtonLabel != null) historyButtonLabel.text = isChineseUi ? "记录" : "History";
            if (commitButton != null) commitButton.text = isChineseUi ? "提交到本地" : "Commit";
            if (commitAndPushButton != null) commitAndPushButton.text = isChineseUi ? "提交并推送" : "Commit & Push";

            if (unstagedTitleLabel != null) unstagedTitleLabel.text = isChineseUi ? "变更区" : "UNSTAGED CHANGES";
            if (stagedTitleLabel != null) stagedTitleLabel.text = isChineseUi ? "待提交" : "STAGED CHANGES";

            if (commitMessageTitleLabel != null) commitMessageTitleLabel.text = isChineseUi ? "提交信息" : "Commit Message";
            if (commitMessageHintLabel != null) commitMessageHintLabel.text = isChineseUi ? "按Enter可换行" : "Enter to add a new line";

            if (historyTitleLabel != null) historyTitleLabel.text = isChineseUi ? "提交记录" : "Commit History";
            if (historyHintLabel != null) historyHintLabel.text = isChineseUi ? "提示：最多显示前100条记录" : "Hint: up to 100 entries";

            if (settingsButton != null) settingsButton.tooltip = isChineseUi ? "设置" : "Settings";
            if (repositoryStatusUpButton != null) repositoryStatusUpButton.tooltip = isChineseUi ? "排序" : "Sort";
            if (searchField != null) searchField.tooltip = isChineseUi ? SearchTooltipZh : SearchTooltipEn;
            if (assetTypeMenu != null) assetTypeMenu.text = GetAssetTypeMenuText();

            if (saveToDiskHintLabel != null)
            {
                saveToDiskHintLabel.text = isChineseUi ? SaveToDiskHintZh : SaveToDiskHintEn;
            }

            if (unstagedHeaderLabel != null) unstagedHeaderLabel.text = GetUnstagedHeaderText(GetVisibleAssetCount(visibleUnstagedItems));
            if (stagedHeaderLabel != null) stagedHeaderLabel.text = GetStagedHeaderText(GetVisibleAssetCount(visibleStagedItems));

            if (unstagedEmptyHintLabel != null)
            {
                unstagedEmptyHintLabel.text = isChineseUi
                    ? "暂无变更\n\n提示：\n拖拽条目到“待提交”可加入待提交\nCtrl：多选\nShift：连续选择\nCtrl+A：全选"
                    : "No changes.\n\nTips:\nDrag items to “Staged” to include them\nCtrl: multi-select\nShift: range select\nCtrl+A: select all";
            }

            if (stagedEmptyHintLabel != null)
            {
                stagedEmptyHintLabel.text = isChineseUi
                    ? "暂无待提交条目\n\n提示：\n拖拽条目到“变更区”可取消待提交\nCtrl：多选\nShift：连续选择\nCtrl+A：全选"
                    : "No staged items.\n\nTips:\nDrag items to “Unstaged” to unstage\nCtrl: multi-select\nShift: range select\nCtrl+A: select all";
            }

            UpdateSaveToDiskHintVisibility();
            UpdateHeaderLabels();

            UpdateSortInfoLabel();
            ConfigureSearchPlaceholder();
            ConfigureCommitMessagePlaceholder();
        }

        private void UpdateSaveToDiskHintVisibility()
        {
            if (saveToDiskHintLabel == null)
            {
                return;
            }

            saveToDiskHintLabel.style.display = autoSaveBeforeOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private string GetUnstagedHeaderText(int count)
        {
            return isChineseUi ? $"工作区变更（未暂存）：{count} 项" : $"Unstaged changes: {count} items";
        }

        private string GetStagedHeaderText(int count)
        {
            return isChineseUi ? $"待提交（已暂存）：{count} 项" : $"Staged changes: {count} items";
        }

        private static int GetVisibleAssetCount(List<GitAssetInfo> items)
        {
            if (items == null || items.Count == 0)
            {
                return 0;
            }

            var count = 0;
            foreach (var item in items)
            {
                if (item == null || item.IsHeader || string.IsNullOrEmpty(item.AssetPath))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private void ToggleSortMenu()
        {
            if (sortMenuOverlay != null && sortMenuOverlay.resolvedStyle.display == DisplayStyle.Flex)
            {
                HideSortMenu();
                return;
            }

            ShowSortMenu();
        }

        private void ShowSortMenu()
        {
            if (repositoryStatusUpButton == null)
            {
                return;
            }

            EnsureSortMenuOverlay();
            RebuildSortMenuContents();

            sortMenuOverlay.style.display = DisplayStyle.Flex;
            sortMenuOverlay.BringToFront();
            sortMenuOverlay.Focus();

            sortMenuPositionRequestId++;
            sortMenuPendingPositionAttempts = 0;
            sortMenuPanel.style.visibility = Visibility.Hidden;
            sortMenuPanel.style.left = -10000;
            sortMenuPanel.style.top = -10000;
            PositionSortMenuPanel();
        }

        private void HideSortMenu()
        {
            if (sortMenuOverlay == null)
            {
                return;
            }

            sortMenuPositionRequestId++;
            sortMenuPendingPositionAttempts = 0;
            sortMenuOverlay.style.display = DisplayStyle.None;
            if (sortMenuPanel != null)
            {
                sortMenuPanel.style.visibility = Visibility.Hidden;
                sortMenuPanel.style.left = -10000;
                sortMenuPanel.style.top = -10000;
            }
        }

        private void EnsureSortMenuOverlay()
        {
            if (sortMenuOverlay != null)
            {
                return;
            }

            sortMenuOverlay = new VisualElement { name = "gitU-sort-menu-overlay" };
            sortMenuOverlay.style.display = DisplayStyle.None;
            sortMenuOverlay.style.position = Position.Absolute;
            sortMenuOverlay.style.left = 0;
            sortMenuOverlay.style.top = 0;
            sortMenuOverlay.style.right = 0;
            sortMenuOverlay.style.bottom = 0;
            sortMenuOverlay.style.backgroundColor = Color.clear;

            sortMenuOverlay.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (sortMenuPanel == null)
                {
                    return;
                }

                if (IsDescendantOf(evt.target as VisualElement, sortMenuPanel))
                {
                    return;
                }

                HideSortMenu();
                evt.StopPropagation();
            });
            sortMenuOverlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    HideSortMenu();
                    evt.StopPropagation();
                    evt.PreventDefault();
                }
            });

            sortMenuPanel = new VisualElement { name = "gitU-sort-menu-panel" };
            sortMenuPanel.style.position = Position.Absolute;
            sortMenuPanel.style.width = 220;
            sortMenuPanel.style.backgroundColor = Html("#111115");
            sortMenuPanel.style.borderTopWidth = 1;
            sortMenuPanel.style.borderRightWidth = 1;
            sortMenuPanel.style.borderBottomWidth = 1;
            sortMenuPanel.style.borderLeftWidth = 1;
            var border = Rgba(255, 255, 255, 0.12f);
            sortMenuPanel.style.borderTopColor = border;
            sortMenuPanel.style.borderRightColor = border;
            sortMenuPanel.style.borderBottomColor = border;
            sortMenuPanel.style.borderLeftColor = border;
            sortMenuPanel.style.borderTopLeftRadius = 8;
            sortMenuPanel.style.borderTopRightRadius = 8;
            sortMenuPanel.style.borderBottomLeftRadius = 8;
            sortMenuPanel.style.borderBottomRightRadius = 8;
            sortMenuPanel.style.paddingTop = 6;
            sortMenuPanel.style.paddingBottom = 6;
            sortMenuPanel.style.paddingLeft = 6;
            sortMenuPanel.style.paddingRight = 6;
            sortMenuPanel.style.visibility = Visibility.Hidden;
            sortMenuPanel.style.left = -10000;
            sortMenuPanel.style.top = -10000;

            sortMenuOverlay.Add(sortMenuPanel);
            rootVisualElement.Add(sortMenuOverlay);
        }

        private void PositionSortMenuPanel()
        {
            if (repositoryStatusUpButton == null || sortMenuPanel == null || sortMenuOverlay == null)
            {
                return;
            }

            var requestId = sortMenuPositionRequestId;
            sortMenuPanel.schedule.Execute(() =>
            {
                if (sortMenuPanel == null || sortMenuOverlay == null || repositoryStatusUpButton == null)
                {
                    return;
                }

                if (requestId != sortMenuPositionRequestId)
                {
                    return;
                }

                var root = sortMenuOverlay.parent ?? rootVisualElement;
                if (root == null)
                {
                    return;
                }

                var rootWidth = root.resolvedStyle.width;
                var rootHeight = root.resolvedStyle.height;
                if (rootWidth <= 0f || rootHeight <= 0f)
                {
                    if (sortMenuPendingPositionAttempts++ < 20)
                    {
                        PositionSortMenuPanel();
                    }
                    return;
                }

                var anchor = repositoryStatusUpButton.worldBound;
                if (anchor.width <= 0f || anchor.height <= 0f)
                {
                    if (sortMenuPendingPositionAttempts++ < 20)
                    {
                        PositionSortMenuPanel();
                    }
                    return;
                }

                // First-open layout pass may report a valid size but still sit at (0,0).
                if (anchor.xMin <= 0.01f && anchor.yMin <= 0.01f)
                {
                    if (sortMenuPendingPositionAttempts++ < 20)
                    {
                        PositionSortMenuPanel();
                    }
                    return;
                }

                var panelWidth = sortMenuPanel.resolvedStyle.width;
                var panelHeight = sortMenuPanel.resolvedStyle.height;
                if (panelWidth <= 0f) panelWidth = 220f;
                if (panelHeight <= 0f) panelHeight = 220f;

                const float gap = 6f;

                // Prefer: panel bottom-right aligns to the trigger button (above it).
                var left = anchor.xMax - panelWidth;
                var top = (anchor.yMin - gap) - panelHeight;

                // If above would go out of view, fall back to below the button.
                if (top < 0f)
                {
                    top = anchor.yMax + gap;
                }

                left = Mathf.Clamp(left, 0f, Mathf.Max(0f, rootWidth - panelWidth));
                top = Mathf.Clamp(top, 0f, Mathf.Max(0f, rootHeight - panelHeight));

                sortMenuPanel.style.left = left;
                sortMenuPanel.style.top = top;
                sortMenuPanel.style.visibility = Visibility.Visible;
                sortMenuPanel.BringToFront();
            });
        }

        private void RebuildSortMenuContents()
        {
            if (sortMenuPanel == null)
            {
                return;
            }

            sortMenuPanel.Clear();

            sortMenuPanel.Add(CreateSortMenuItem(GetSortKeyLabel(AssetSortKey.GitStatus), assetSortKey == AssetSortKey.GitStatus, () =>
            {
                assetSortKey = AssetSortKey.GitStatus;
                SaveSortSettings();
                HideSortMenu();
                RefreshListViews();
            }));
            sortMenuPanel.Add(CreateSortMenuItem(GetSortKeyLabel(AssetSortKey.AssetType), assetSortKey == AssetSortKey.AssetType, () =>
            {
                assetSortKey = AssetSortKey.AssetType;
                SaveSortSettings();
                HideSortMenu();
                RefreshListViews();
            }));
            sortMenuPanel.Add(CreateSortMenuItem(GetSortKeyLabel(AssetSortKey.Name), assetSortKey == AssetSortKey.Name, () =>
            {
                assetSortKey = AssetSortKey.Name;
                SaveSortSettings();
                HideSortMenu();
                RefreshListViews();
            }));
            sortMenuPanel.Add(CreateSortMenuItem(GetSortKeyLabel(AssetSortKey.Time), assetSortKey == AssetSortKey.Time, () =>
            {
                assetSortKey = AssetSortKey.Time;
                SaveSortSettings();
                HideSortMenu();
                RefreshListViews();
            }));
            sortMenuPanel.Add(CreateSortMenuItem(GetSortKeyLabel(AssetSortKey.Path), assetSortKey == AssetSortKey.Path, () =>
            {
                assetSortKey = AssetSortKey.Path;
                SaveSortSettings();
                HideSortMenu();
                RefreshListViews();
            }));

            sortMenuPanel.Add(CreateSortMenuSeparator());

            sortMenuPanel.Add(CreateSortMenuItem(GetSortOrderLabel(SortOrder.Ascending), assetSortOrder == SortOrder.Ascending, () =>
            {
                assetSortOrder = SortOrder.Ascending;
                SaveSortSettings();
                HideSortMenu();
                RefreshListViews();
            }));
            sortMenuPanel.Add(CreateSortMenuItem(GetSortOrderLabel(SortOrder.Descending), assetSortOrder == SortOrder.Descending, () =>
            {
                assetSortOrder = SortOrder.Descending;
                SaveSortSettings();
                HideSortMenu();
                RefreshListViews();
            }));
        }

        private VisualElement CreateSortMenuSeparator()
        {
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.marginTop = 4;
            sep.style.marginBottom = 4;
            sep.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            sep.pickingMode = PickingMode.Ignore;
            return sep;
        }

        private VisualElement CreateSortMenuItem(string label, bool checkedState, Action onClick)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 22;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;
            row.pickingMode = PickingMode.Position;

            var check = new Label(checkedState ? "✓" : string.Empty);
            check.style.width = 16;
            check.style.unityTextAlign = TextAnchor.MiddleCenter;
            check.style.color = Rgb(139, 92, 246);
            row.Add(check);

            var text = new Label(label);
            text.style.marginLeft = 8;
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            text.style.unityTextAlign = TextAnchor.MiddleLeft;
            text.style.color = new Color(1f, 1f, 1f, 0.85f);
            row.Add(text);

            row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f));
            row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = Color.clear);
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                onClick?.Invoke();
                evt.StopPropagation();
            });

            return row;
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

            button.style.opacity = 1f;
            button.style.color = enabled ? accentColor : new Color(1f, 1f, 1f, 0.6f);
            button.style.backgroundColor = enabled
                ? new Color(accentColor.r, accentColor.g, accentColor.b, 0.10f)
                : new Color(1f, 1f, 1f, 0.06f);
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            var borderColor = enabled
                ? accentColor
                : new Color(1f, 1f, 1f, 0.12f);
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
                ShowTempNotification(isChineseUi ? "请先在左侧勾选要发送的变更。" : "Select changes on the left to stage.");
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
                ShowTempNotification(isChineseUi ? "当前没有可发送的变更。" : "No changes to stage.");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification(isChineseUi ? "正在执行其他操作，请稍候。" : "Another operation is in progress. Please wait.");
                return;
            }

            var pathSet = new HashSet<string>(assetPaths, StringComparer.OrdinalIgnoreCase);
            var toStage = assetInfos
                .Where(a => !a.IsStaged && a.IsUnstaged && pathSet.Contains(a.AssetPath))
                .ToList();

            if (toStage.Count == 0)
            {
                ShowTempNotification(isChineseUi ? "当前没有可发送的变更。" : "No changes to stage.");
                return;
            }

            _ = GitUtility.UnityProjectFolder;

            var requestsByRoot = new Dictionary<string, List<GitUtility.GitStageRequest>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in toStage)
            {
                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var root, out var gitPath))
                {
                    if (!requestsByRoot.TryGetValue(root, out var list))
                    {
                        list = new List<GitUtility.GitStageRequest>();
                        requestsByRoot[root] = list;
                    }

                    list.Add(new GitUtility.GitStageRequest(gitPath, info.ChangeType));
                }

                // If this is an explicit "Deleted (from move)" entry, stage the destination path as well
                // so the move is staged as a whole.
                if (info.ChangeType == GitChangeType.Deleted &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var movedToRoot, out var movedToGitPath))
                {
                    if (!requestsByRoot.TryGetValue(movedToRoot, out var list))
                    {
                        list = new List<GitUtility.GitStageRequest>();
                        requestsByRoot[movedToRoot] = list;
                    }

                    list.Add(new GitUtility.GitStageRequest(movedToGitPath, GitChangeType.Added));
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalRoot, out var originalGitPath))
                {
                    if (!requestsByRoot.TryGetValue(originalRoot, out var list))
                    {
                        list = new List<GitUtility.GitStageRequest>();
                        requestsByRoot[originalRoot] = list;
                    }

                    list.Add(new GitUtility.GitStageRequest(originalGitPath, GitChangeType.Deleted));
                }
            }

            var requestGroups = requestsByRoot
                .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                .ToList();

            if (requestGroups.Count == 0)
            {
                ShowTempNotification(isChineseUi ? "当前没有可发送的变更。" : "No changes to stage.");
                return;
            }

            foreach (var info in toStage)
            {
                info.IsStaged = true;
                info.IsUnstaged = false;
            }

            selectedUnstagedPaths.Clear();
            ApplyIncrementalMoveBetweenLists(toStage, toStaged: true);

            pendingStageGitPathsByRoot = requestGroups
                .Select(kvp => new KeyValuePair<string, List<string>>(kvp.Key, kvp.Value
                    .Select(r => r.GitRelativePath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
                .Where(kvp => kvp.Value.Count > 0)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            gitOperationInProgress = true;
            gitOperationKind = GitOperationKind.Stage;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = isChineseUi ? "正在发送至待提交..." : "Sending to staged...";
            UpdateHeaderLabels();
            ForceRepaintUI();

            gitOperationTask = Task.Run(() =>
            {
                var summaries = new List<string>();
                var anySucceeded = false;
                var anyFailed = false;

                foreach (var group in requestGroups)
                {
                    var success = GitUtility.StageGitPaths(group.Key, group.Value, out var summary);
                    anySucceeded |= success;
                    anyFailed |= !success;

                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        summaries.Add(requestGroups.Count == 1 ? summary : $"{GitUtility.GetRepositoryDisplayName(group.Key)}: {summary}");
                    }
                }

                return new GitOperationResult(anySucceeded && !anyFailed ? true : anySucceeded, string.Join("\n", summaries));
            });
            gitOperationTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void UnstageSelectedStaged()
        {
            if (selectedStagedPaths.Count == 0)
            {
                ShowTempNotification(isChineseUi ? "请先在右侧勾选要移出的变更。" : "Select changes on the right to unstage.");
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
                ShowTempNotification(isChineseUi ? "当前没有可移出的待提交项。" : "No staged items to remove.");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification(isChineseUi ? "正在执行其他操作，请稍候。" : "Another operation is in progress. Please wait.");
                return;
            }

            var pathSet = new HashSet<string>(assetPaths, StringComparer.OrdinalIgnoreCase);
            var toUnstage = assetInfos
                .Where(a => a.IsStaged && pathSet.Contains(a.AssetPath))
                .ToList();

            if (toUnstage.Count == 0)
            {
                ShowTempNotification(isChineseUi ? "当前没有可移出的待提交项。" : "No staged items to remove.");
                return;
            }

            _ = GitUtility.UnityProjectFolder;

            var requestsByRoot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in toUnstage)
            {
                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var root, out var gitPath))
                {
                    if (!requestsByRoot.TryGetValue(root, out var list))
                    {
                        list = new List<string>();
                        requestsByRoot[root] = list;
                    }

                    list.Add(gitPath);
                }

                // If this is an explicit "Deleted (from move)" entry, unstage the destination path as well
                // so the move is unstaged as a whole.
                if (info.ChangeType == GitChangeType.Deleted &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var movedToRoot, out var movedToGitPath))
                {
                    if (!requestsByRoot.TryGetValue(movedToRoot, out var list))
                    {
                        list = new List<string>();
                        requestsByRoot[movedToRoot] = list;
                    }

                    list.Add(movedToGitPath);
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalRoot, out var originalGitPath))
                {
                    if (!requestsByRoot.TryGetValue(originalRoot, out var list))
                    {
                        list = new List<string>();
                        requestsByRoot[originalRoot] = list;
                    }

                    list.Add(originalGitPath);
                }
            }

            var requestGroups = requestsByRoot
                .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                .Select(kvp => new KeyValuePair<string, List<string>>(kvp.Key, kvp.Value
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
                .Where(kvp => kvp.Value.Count > 0)
                .ToList();

            if (requestGroups.Count == 0)
            {
                ShowTempNotification(isChineseUi ? "当前没有可移出的待提交项。" : "No staged items to remove.");
                return;
            }

            foreach (var info in toUnstage)
            {
                info.IsStaged = false;
                info.IsUnstaged = true;
            }

            selectedStagedPaths.Clear();
            ApplyIncrementalMoveBetweenLists(toUnstage, toStaged: false);

            pendingUnstageGitPathsByRoot = requestGroups
                .Select(kvp => new KeyValuePair<string, List<string>>(kvp.Key, kvp.Value
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
                .Where(kvp => kvp.Value.Count > 0)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            gitOperationInProgress = true;
            gitOperationKind = GitOperationKind.Unstage;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = isChineseUi ? "正在从待提交移出..." : "Removing from staged...";
            UpdateHeaderLabels();
            ForceRepaintUI();

            gitOperationTask = Task.Run(() =>
            {
                var summaries = new List<string>();
                var anySucceeded = false;
                var anyFailed = false;

                foreach (var group in requestGroups)
                {
                    var success = GitUtility.UnstageGitPaths(group.Key, group.Value, out var summary);
                    anySucceeded |= success;
                    anyFailed |= !success;

                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        summaries.Add(requestGroups.Count == 1 ? summary : $"{GitUtility.GetRepositoryDisplayName(group.Key)}: {summary}");
                    }
                }

                return new GitOperationResult(anySucceeded && !anyFailed ? true : anySucceeded, string.Join("\n", summaries));
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

            if (hasMultipleRepositories)
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
                        InsertSortedByCurrentSort(visibleStagedItems, info);
                        touchedStaged = true;
                    }
                }
                else
                {
                    touchedStaged |= RemoveByPath(visibleStagedItems, info.AssetPath);

                    if (IsItemVisibleInList(info, stagedView: false) && !ContainsByPath(visibleUnstagedItems, info.AssetPath))
                    {
                        InsertSortedByCurrentSort(visibleUnstagedItems, info);
                        touchedUnstaged = true;
                    }
                }
            }

            UpdateListEmptyHints();

            if (unstagedHeaderLabel != null)
            {
                unstagedHeaderLabel.text = GetUnstagedHeaderText(GetVisibleAssetCount(visibleUnstagedItems));
            }

            if (stagedHeaderLabel != null)
            {
                stagedHeaderLabel.text = GetStagedHeaderText(GetVisibleAssetCount(visibleStagedItems));
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

        private List<GitAssetInfo> ApplySortToAssetList(List<GitAssetInfo> list)
        {
            if (list == null || list.Count <= 1)
            {
                return list;
            }

            var indexed = list.Select((item, index) => (item, index));
            IOrderedEnumerable<(GitAssetInfo item, int index)> ordered;

            switch (assetSortKey)
            {
                case AssetSortKey.GitStatus:
                    ordered = assetSortOrder == SortOrder.Ascending
                        ? indexed.OrderBy(t => GetGitStatusSortIndex(t.item)).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase)
                        : indexed.OrderByDescending(t => GetGitStatusSortIndex(t.item)).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase);
                    break;
                case AssetSortKey.AssetType:
                    ordered = assetSortOrder == SortOrder.Ascending
                        ? indexed.OrderBy(t => (int)GetOrDetectAssetType(t.item)).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase)
                        : indexed.OrderByDescending(t => (int)GetOrDetectAssetType(t.item)).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase);
                    break;
                case AssetSortKey.Name:
                    ordered = assetSortOrder == SortOrder.Ascending
                        ? indexed.OrderBy(t => GetSortName(t.item), StringComparer.OrdinalIgnoreCase).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase)
                        : indexed.OrderByDescending(t => GetSortName(t.item), StringComparer.OrdinalIgnoreCase).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase);
                    break;
                case AssetSortKey.Time:
                    ordered = assetSortOrder == SortOrder.Ascending
                        ? indexed.OrderBy(t => GetSortTimeTicks(t.item)).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase)
                        : indexed.OrderByDescending(t => GetSortTimeTicks(t.item)).ThenBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase);
                    break;
                case AssetSortKey.Path:
                default:
                    ordered = assetSortOrder == SortOrder.Ascending
                        ? indexed.OrderBy(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase)
                        : indexed.OrderByDescending(t => GetSortPath(t.item), StringComparer.OrdinalIgnoreCase);
                    break;
            }

            // Ensure stability when keys are identical.
            var sorted = ordered.ThenBy(t => t.index).Select(t => t.item).ToList();
            return sorted;
        }

        private void InsertSortedByCurrentSort(List<GitAssetInfo> list, GitAssetInfo item)
        {
            if (list == null || item == null)
            {
                return;
            }

            if (assetSortKey == AssetSortKey.Path && assetSortOrder == SortOrder.Ascending)
            {
                InsertSortedByPath(list, item);
                return;
            }

            var insertIndex = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                if (CompareAssetInfosForSort(item, list[i]) < 0)
                {
                    insertIndex = i;
                    break;
                }
            }

            list.Insert(insertIndex, item);
        }

        private int CompareAssetInfosForSort(GitAssetInfo a, GitAssetInfo b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            var cmp = 0;
            switch (assetSortKey)
            {
                case AssetSortKey.GitStatus:
                    cmp = GetGitStatusSortIndex(a).CompareTo(GetGitStatusSortIndex(b));
                    break;
                case AssetSortKey.AssetType:
                    cmp = ((int)GetOrDetectAssetType(a)).CompareTo((int)GetOrDetectAssetType(b));
                    break;
                case AssetSortKey.Name:
                    cmp = string.Compare(GetSortName(a), GetSortName(b), StringComparison.OrdinalIgnoreCase);
                    break;
                case AssetSortKey.Time:
                    cmp = GetSortTimeTicks(a).CompareTo(GetSortTimeTicks(b));
                    break;
                case AssetSortKey.Path:
                default:
                    cmp = string.Compare(GetSortPath(a), GetSortPath(b), StringComparison.OrdinalIgnoreCase);
                    break;
            }

            if (cmp == 0)
            {
                cmp = string.Compare(GetSortPath(a), GetSortPath(b), StringComparison.OrdinalIgnoreCase);
            }

            if (cmp == 0)
            {
                cmp = string.Compare(a.OriginalPath ?? string.Empty, b.OriginalPath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (assetSortOrder == SortOrder.Descending)
            {
                cmp = -cmp;
            }

            return cmp;
        }

        private static int GetGitStatusSortIndex(GitAssetInfo info)
        {
            if (info == null)
            {
                return int.MaxValue;
            }

            return info.ChangeType switch
            {
                GitChangeType.Added => 0,
                GitChangeType.Modified => 1,
                GitChangeType.Deleted => 2,
                GitChangeType.Renamed => 3,
                _ => 4
            };
        }

        private UnityAssetTypeFilter GetOrDetectAssetType(GitAssetInfo info)
        {
            if (info == null)
            {
                return UnityAssetTypeFilter.Unknown;
            }

            var path = GetSortPath(info);
            if (string.IsNullOrEmpty(path))
            {
                return UnityAssetTypeFilter.Unknown;
            }

            if (!assetTypeCache.TryGetValue(path, out var cached))
            {
                cached = GitUtility.DetectAssetTypeFilter(path);
                assetTypeCache[path] = cached;
            }

            return cached;
        }

        private static string GetSortPath(GitAssetInfo info)
        {
            if (info == null)
            {
                return string.Empty;
            }

            return info.AssetPath ?? info.OriginalPath ?? string.Empty;
        }

        private static string GetSortName(GitAssetInfo info)
        {
            if (info == null)
            {
                return string.Empty;
            }

            return info.FileName ?? string.Empty;
        }

        private static long GetSortTimeTicks(GitAssetInfo info)
        {
            if (info == null)
            {
                return long.MinValue;
            }

            var time = info.WorkingTreeTime ?? info.LastCommitTime ?? DateTime.MinValue;
            return time.Ticks;
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

            return isChineseUi
                ? $"已选 {assetTypeFilters.Count} 项"
                : $"{assetTypeFilters.Count} selected";
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
                statusMessage = isChineseUi
                    ? "请先在 Project 或 Hierarchy 选择目标资源，再打开窗口（或在顶部“目标资源”里选择）。"
                    : "Select a target in Project or Hierarchy before opening the window (or choose one in the Target field above).";
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
                    infoMessages.Add(isChineseUi ? "无法解析资源路径。" : "Failed to resolve the asset path.");
                }
            }

            refreshInProgress = true;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            var repoRoots = GitUtility.GetRepositoryRootsForUnityProject();
            if (repoRoots == null || repoRoots.Count == 0)
            {
                refreshInProgress = false;
                UpdateActionButtonsEnabled();
                UpdateCommitButtonsEnabled();

                infoMessages.Add(isChineseUi ? "未找到任何 Git 仓库。" : "No Git repository found.");
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

            // 自动暂存刷新时跳过UI更新，避免闪烁
            if (!skipRefreshUiDuringAutoStage)
            {
                statusMessage = isChineseUi ? "正在刷新 Git 状态..." : "Refreshing Git status...";
                UpdateHeaderLabels();
                RefreshListViews();
                ForceRepaintUI();
            }

            refreshTask = Task.Run(() => GitUtility.GetWorkingTreeChanges() ?? new List<GitChangeEntry>());
            refreshTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void ApplyGitChanges(List<GitChangeEntry> changes, List<string> infoMessages)
        {
            // 重置自动暂存UI跳过标志
            skipRefreshUiDuringAutoStage = false;

            gitChanges = changes ?? new List<GitChangeEntry>();

            activeRepositoryRoots = gitChanges
                .Select(c => c.RepoRoot)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            hasMultipleRepositories = activeRepositoryRoots.Count > 1;

            // 检测同时是 staged 和 unstaged 的文件（已暂存但又被修改），自动将新修改也暂存
            // 只在第一次刷新时执行，防止无限循环
            if (!hasAutoStagedModifiedFiles)
            {
                var modifiedStagedPaths = gitChanges
                    .Where(c => c.IsStaged && c.IsUnstaged)
                    .Select(c => c.Path)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (modifiedStagedPaths.Count > 0)
                {
                    hasAutoStagedModifiedFiles = true;
                    var gitRoot = GitUtility.ProjectRoot;
                    if (!string.IsNullOrEmpty(gitRoot))
                    {
                        GitUtility.AutoStageModifiedStagedFiles(gitRoot, modifiedStagedPaths);
                        // 重新刷新以获取最新状态，但跳过UI更新避免闪烁
                        skipRefreshUiDuringAutoStage = true;
                        RequestRefreshData(false);
                        return;
                    }
                }
            }

            if (gitChanges.Count == 0)
            {
                infoMessages.Add(isChineseUi ? "Git 未检测到任何变更。" : "Git detected no changes.");
            }
            else if (targetAssetPaths.Count > 0)
            {
                UpdateTargetSelectionInfo(gitChanges);
            }

            UpdateAssetInfosIncrementally(gitChanges);
            CaptureInitialStagedSnapshotIfNeeded();

            if (hasMultipleRepositories)
            {
                infoMessages.Add(isChineseUi
                    ? $"检测到多个 Git 仓库（{activeRepositoryRoots.Count} 个）。GitU 将按仓库分别执行暂存/提交与外部暂存清理。"
                    : $"Multiple Git repositories detected ({activeRepositoryRoots.Count}). GitU will stage/commit and auto-clean external staged items per repo.");
            }

            if (!hasShownPreexistingStagedHint && initialStagedPaths != null && initialStagedPaths.Count > 0)
            {
                hasShownPreexistingStagedHint = true;
                infoMessages.Add(isChineseUi
                    ? "提示：右侧“待提交/已暂存”读取的是 Git 暂存区（index）。窗口会自动清理“非本工具暂存”的条目；如需手动清空可执行：git restore --staged ."
                    : "Hint: The right \"Staged\" list reflects the Git index. GitU auto-cleans items staged outside this tool; to clear manually, run: git restore --staged .");
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
                    var noRelevantMessage = isChineseUi
                        ? "当前资源暂无相关变更。"
                        : "No changes related to the current selection.";
                    if (infoMessages.Count > 0)
                    {
                        infoMessages[infoMessages.Count - 1] = $"{infoMessages[infoMessages.Count - 1]} {noRelevantMessage}";
                    }
                    else
                    {
                        infoMessages.Add(noRelevantMessage);
                    }
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
                        info = new GitAssetInfo(change.Path, change.OriginalPath, change.ChangeType, lastTime, change.WorkingTreeTime, change.IsStaged, change.IsUnstaged, change.RepoRoot);
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
                        info.RepoRoot = change.RepoRoot;
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

        private static string NormalizeGitRoot(string gitRoot)
        {
            if (string.IsNullOrWhiteSpace(gitRoot))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(gitRoot.Trim());
            }
            catch
            {
                return gitRoot.Trim();
            }
        }

        private static string NormalizeGitPath(string gitRelativePath)
        {
            if (string.IsNullOrWhiteSpace(gitRelativePath))
            {
                return null;
            }

            return GitUtility.NormalizeAssetPath(gitRelativePath.Trim());
        }

        private HashSet<string> GetAllowListForRoot(string gitRoot, bool createIfMissing)
        {
            var normalizedRoot = NormalizeGitRoot(gitRoot);
            if (string.IsNullOrEmpty(normalizedRoot))
            {
                return null;
            }

            if (!stagedAllowListByRoot.TryGetValue(normalizedRoot, out var set) && createIfMissing)
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                stagedAllowListByRoot[normalizedRoot] = set;
            }

            return set;
        }

        private bool IsAllowListed(string gitRoot, string gitRelativePath)
        {
            var set = GetAllowListForRoot(gitRoot, false);
            if (set == null)
            {
                return false;
            }

            var normalizedPath = NormalizeGitPath(gitRelativePath);
            return !string.IsNullOrEmpty(normalizedPath) && set.Contains(normalizedPath);
        }

        private void AddAllowListPaths(Dictionary<string, List<string>> byRoot)
        {
            if (byRoot == null || byRoot.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var kvp in byRoot)
            {
                var set = GetAllowListForRoot(kvp.Key, true);
                if (set == null)
                {
                    continue;
                }

                foreach (var path in kvp.Value)
                {
                    var normalized = NormalizeGitPath(path);
                    if (string.IsNullOrEmpty(normalized))
                    {
                        continue;
                    }

                    if (set.Add(normalized))
                    {
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SaveStagedAllowList();
            }
        }

        private void RemoveAllowListPaths(Dictionary<string, List<string>> byRoot)
        {
            if (byRoot == null || byRoot.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var kvp in byRoot)
            {
                var set = GetAllowListForRoot(kvp.Key, false);
                if (set == null)
                {
                    continue;
                }

                foreach (var path in kvp.Value)
                {
                    var normalized = NormalizeGitPath(path);
                    if (string.IsNullOrEmpty(normalized))
                    {
                        continue;
                    }

                    if (set.Remove(normalized))
                    {
                        changed = true;
                    }
                }

                if (set.Count == 0)
                {
                    stagedAllowListByRoot.Remove(NormalizeGitRoot(kvp.Key));
                    changed = true;
                }
            }

            if (changed)
            {
                SaveStagedAllowList();
            }
        }

        private void PruneStagedAllowListToCurrentIndex()
        {
            if (stagedAllowListByRoot.Count == 0)
            {
                return;
            }

            var stagedNowByRoot = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in assetInfos)
            {
                if (!info.IsStaged)
                {
                    continue;
                }

                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var root, out var gitPath))
                {
                    var normalizedRoot = NormalizeGitRoot(root);
                    if (!stagedNowByRoot.TryGetValue(normalizedRoot, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        stagedNowByRoot[normalizedRoot] = set;
                    }

                    set.Add(gitPath);
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalRoot, out var originalGitPath))
                {
                    var normalizedRoot = NormalizeGitRoot(originalRoot);
                    if (!stagedNowByRoot.TryGetValue(normalizedRoot, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        stagedNowByRoot[normalizedRoot] = set;
                    }

                    set.Add(originalGitPath);
                }
            }

            var changed = false;
            var roots = stagedAllowListByRoot.Keys.ToList();
            foreach (var root in roots)
            {
                if (!stagedNowByRoot.TryGetValue(root, out var stagedNow))
                {
                    stagedAllowListByRoot.Remove(root);
                    changed = true;
                    continue;
                }

                var set = stagedAllowListByRoot[root];
                var removed = set.RemoveWhere(p => !stagedNow.Contains(p));
                if (removed > 0)
                {
                    changed = true;
                }

                if (set.Count == 0)
                {
                    stagedAllowListByRoot.Remove(root);
                    changed = true;
                }
            }

            if (changed)
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

            _ = GitUtility.UnityProjectFolder;
            var toUnstageByRoot = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void AddToUnstage(string root, string gitPath)
            {
                var normalizedRoot = NormalizeGitRoot(root);
                if (string.IsNullOrWhiteSpace(normalizedRoot) || string.IsNullOrWhiteSpace(gitPath))
                {
                    return;
                }

                if (!toUnstageByRoot.TryGetValue(normalizedRoot, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    toUnstageByRoot[normalizedRoot] = set;
                }

                set.Add(gitPath);
            }

            foreach (var info in assetInfos)
            {
                if (!info.IsStaged)
                {
                    continue;
                }

                if (GitUtility.TryGetGitRelativePath(info.AssetPath, out var root, out var gitPath) &&
                    !IsAllowListed(root, gitPath))
                {
                    AddToUnstage(root, gitPath);
                }

                if (info.ChangeType == GitChangeType.Renamed &&
                    !string.IsNullOrEmpty(info.OriginalPath) &&
                    !string.Equals(info.OriginalPath, info.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                    GitUtility.TryGetGitRelativePath(info.OriginalPath, out var originalRoot, out var originalGitPath) &&
                    !IsAllowListed(originalRoot, originalGitPath))
                {
                    AddToUnstage(originalRoot, originalGitPath);
                }
            }

            if (toUnstageByRoot.Count == 0)
            {
                return false;
            }

            var requestGroups = toUnstageByRoot
                .Select(kvp => new KeyValuePair<string, List<string>>(kvp.Key, kvp.Value
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
                .Where(kvp => kvp.Value.Count > 0)
                .ToList();

            if (requestGroups.Count == 0)
            {
                return false;
            }

            pendingUnstageGitPathsByRoot = requestGroups
                .Select(kvp => new KeyValuePair<string, List<string>>(kvp.Key, kvp.Value
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
                .Where(kvp => kvp.Value.Count > 0)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            recaptureInitialStagedSnapshotAfterAutoClean = true;

            gitOperationInProgress = true;
            gitOperationKind = GitOperationKind.Unstage;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = isChineseUi ? "正在清理外部暂存..." : "Cleaning external staged items...";
            UpdateHeaderLabels();
            RefreshListViews();
            ForceRepaintUI();

            var isChinese = isChineseUi;
            gitOperationTask = Task.Run(() =>
            {
                var summaries = new List<string>();
                var anyFailed = false;
                var anySucceeded = false;

                foreach (var group in requestGroups)
                {
                    var success = GitUtility.UnstageGitPaths(group.Key, group.Value, out var summary);
                    anySucceeded |= success;
                    anyFailed |= !success;

                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        summaries.Add(requestGroups.Count == 1
                            ? summary
                            : $"{GitUtility.GetRepositoryDisplayName(group.Key)}: {summary}");
                    }
                }

                var summaryText = string.Join("\n", summaries);
                if (anySucceeded)
                {
                    summaryText = string.IsNullOrWhiteSpace(summaryText)
                        ? (isChinese ? "已自动清理外部暂存。" : "Auto-cleaned external staged items.")
                        : (isChinese ? $"已自动清理外部暂存：{summaryText}" : $"Auto-cleaned external staged items: {summaryText}");
                }

                return new GitOperationResult(anySucceeded && !anyFailed ? true : anySucceeded, summaryText);
            });
            gitOperationTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return true;
        }

        private void SendVisibleChangesToStage()
        {
            var toStage = EnumerateFilteredAssets(false).ToList();
            if (toStage.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    isChineseUi ? "发送至待提交" : "Send to Staged",
                    isChineseUi ? "当前没有可发送的变更。" : "No changes to stage.",
                    isChineseUi ? "确定" : "OK");
                return;
            }

            var success = GitUtility.StageAssets(toStage, out var summary);
            EditorUtility.DisplayDialog(
                isChineseUi ? "发送至待提交" : "Send to Staged",
                summary,
                isChineseUi ? "确定" : "OK");

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

            EnsureCommitHistoryLoaded(loadGitHistory: false);
            var hasHistory = HasCommitHistory() || !hasAttemptedLoadGitHistory;
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

            EnsureCommitHistoryLoaded(loadGitHistory: true);
            if (!HasCommitHistory())
            {
                EditorUtility.DisplayDialog(
                    isChineseUi ? "提交记录" : "Commit History",
                    isChineseUi ? "暂无提交记录" : "No commit history.",
                    isChineseUi ? "确定" : "OK");
                UpdateHistoryButtonState();
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
            UpdateHistoryDropdownLayout();
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
            if (toastLabel != null && toastLabel.style.display.value != DisplayStyle.None)
            {
                HideTempNotification();
            }

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

        private void OnRootMouseUp(MouseUpEvent _)
        {
            if (dragBadge == null || dragBadge.style.display.value == DisplayStyle.None)
            {
                return;
            }

            QueueDragBadgeCleanup();
        }

        private void QueueDragBadgeCleanup()
        {
            if (dragBadgeCleanupQueued)
            {
                return;
            }

            dragBadgeCleanupQueued = true;
            EditorApplication.delayCall += () =>
            {
                dragBadgeCleanupQueued = false;
                DragAndDrop.SetGenericData(DragPayloadKey, null);
                HideDragBadge();
            };
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                if (dragBadge != null && dragBadge.style.display.value != DisplayStyle.None)
                {
                    QueueDragBadgeCleanup();
                }

                return;
            }

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

        private void EnsureCommitHistoryLoaded(bool loadGitHistory)
        {
            if (commitHistory != null)
            {
                if (loadGitHistory)
                {
                    RefreshFallbackCommitHistory();
                    FilterSavedCommitHistoryToCurrentUser();
                    RebuildCommitHistoryDisplay();
                }
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
                    Debug.LogWarning(isChineseUi
                        ? $"GitU: 读取提交记录失败: {ex.Message}"
                        : $"GitU: Failed to read commit history: {ex.Message}");
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
                        Debug.LogWarning(isChineseUi
                            ? $"GitU: 解析提交记录失败: {ex.Message}"
                            : $"GitU: Failed to parse commit history: {ex.Message}");
                        savedCommitHistory.Clear();
                    }
                }
            }

            if (loadGitHistory)
            {
                RefreshFallbackCommitHistory();
                FilterSavedCommitHistoryToCurrentUser();
            }
            RebuildCommitHistoryDisplay();
        }

        private void RefreshFallbackCommitHistory()
        {
            hasAttemptedLoadGitHistory = true;
            try
            {
                fallbackCommitHistory = GitUtility.GetRecentCommitMessagesForCurrentUser(MaxCommitHistoryDisplayEntries) ?? new List<string>();
            }
            catch (Exception ex)
            {
                fallbackCommitHistory = new List<string>();
                Debug.LogWarning(isChineseUi
                    ? $"GitU: 读取 Git 提交记录失败: {ex.Message}"
                    : $"GitU: Failed to read Git commit history: {ex.Message}");
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
                myMessages = GitUtility.GetRecentCommitMessagesForCurrentUser(Mathf.Max(500, MaxCommitHistoryDisplayEntries));
            }
            catch
            {
                myMessages = null;
            }

            if (myMessages == null || myMessages.Count == 0)
            {
                Debug.LogWarning(isChineseUi
                    ? "GitU: 未找到当前用户的提交记录（可能未配置 git user.email/user.name），无法过滤本地历史文件。"
                    : "GitU: No commit history found for current user (git user.email/name may be missing); local history was not filtered.");
                return;
            }

            var mine = new HashSet<string>(myMessages, StringComparer.Ordinal);
            savedCommitHistory.RemoveAll(entry => !mine.Contains(GetCommitSubject(entry)));
            if (savedCommitHistory.Count > MaxCommitHistoryEntries)
            {
                savedCommitHistory.RemoveRange(MaxCommitHistoryEntries, savedCommitHistory.Count - MaxCommitHistoryEntries);
            }
        }

        private static string GetCommitSubject(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            var trimmed = message.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            var newlineIndex = trimmed.IndexOfAny(new[] { '\r', '\n' });
            var subject = newlineIndex >= 0 ? trimmed.Substring(0, newlineIndex) : trimmed;
            return subject.Trim();
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
                var existingSubjects = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in savedCommitHistory)
                {
                    if (string.IsNullOrWhiteSpace(entry))
                    {
                        continue;
                    }

                    var full = entry.Trim();
                    var subject = GetCommitSubject(full);
                    if (subject.Length == 0 || existingSubjects.Contains(subject))
                    {
                        continue;
                    }

                    if (commitHistory.Count >= MaxCommitHistoryDisplayEntries)
                    {
                        return;
                    }

                    existingSubjects.Add(subject);
                    commitHistory.Add(full);
                }

                if (fallbackCommitHistory == null || fallbackCommitHistory.Count == 0)
                {
                    return;
                }

                foreach (var entry in fallbackCommitHistory)
                {
                    if (string.IsNullOrWhiteSpace(entry))
                    {
                        continue;
                    }

                    var trimmed = entry.Trim();
                    var subject = GetCommitSubject(trimmed);
                    if (subject.Length == 0 || existingSubjects.Contains(subject))
                    {
                        continue;
                    }

                    commitHistory.Add(trimmed);
                    existingSubjects.Add(subject);
                    if (commitHistory.Count >= MaxCommitHistoryDisplayEntries)
                    {
                        return;
                    }
                }

                return;
            }

            if (fallbackCommitHistory == null || fallbackCommitHistory.Count == 0)
            {
                return;
            }

            var existing = new HashSet<string>(commitHistory.Select(GetCommitSubject), StringComparer.Ordinal);
            foreach (var entry in fallbackCommitHistory)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                var trimmed = entry.Trim();
                var subject = GetCommitSubject(trimmed);
                if (subject.Length == 0 || existing.Contains(subject))
                {
                    continue;
                }

                commitHistory.Add(trimmed);
                existing.Add(subject);
                if (commitHistory.Count >= MaxCommitHistoryDisplayEntries)
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
                Debug.LogWarning(isChineseUi
                    ? $"GitU: 保存提交记录失败: {ex.Message}"
                    : $"GitU: Failed to save commit history: {ex.Message}");
            }
        }

        private void AddCommitMessageToHistory(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            EnsureCommitHistoryLoaded(loadGitHistory: false);
            var trimmed = message.Trim();
            var subject = GetCommitSubject(trimmed);
            if (string.IsNullOrWhiteSpace(subject))
            {
                return;
            }

            savedCommitHistory ??= new List<string>();
            savedCommitHistory.RemoveAll(entry => string.Equals(GetCommitSubject(entry), subject, StringComparison.Ordinal));
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
                EditorUtility.DisplayDialog(
                    isChineseUi ? "提交" : "Commit",
                    isChineseUi ? "请先填写提交说明。" : "Please enter a commit message.",
                    isChineseUi ? "确定" : "OK");
                return;
            }

            if (refreshInProgress || gitOperationInProgress)
            {
                ShowTempNotification(isChineseUi ? "正在执行其他操作，请稍候。" : "Another operation is in progress. Please wait.");
                return;
            }

            var normalizedMessage = message.Trim();
            _ = GitUtility.UnityProjectFolder;
            var repositoryRoots = GitUtility.GetRepositoryRootsForUnityProject().ToList();

            if (repositoryRoots.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    isChineseUi ? "提交" : "Commit",
                    isChineseUi ? "未找到任何 Git 仓库。" : "No Git repository found.",
                    isChineseUi ? "确定" : "OK");
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
                    var breakdown = string.Empty;
                    if (hasMultipleRepositories)
                    {
                        var unknownRepoName = isChineseUi ? "未知" : "Unknown";
                        var stagedByRepo = assetInfos
                            .Where(a => a != null && a.IsStaged)
                            .GroupBy(a =>
                            {
                                var name = GitUtility.GetRepositoryDisplayName(a.RepoRoot);
                                return string.IsNullOrWhiteSpace(name) ? unknownRepoName : name;
                            })
                            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(g => $"- {g.Key}: {g.Count()}")
                            .ToList();

                        if (stagedByRepo.Count > 0)
                        {
                            breakdown = "\n\n" + string.Join("\n", stagedByRepo);
                        }
                    }

                    var confirmed = EditorUtility.DisplayDialog(
                        isChineseUi ? "安全确认" : "Confirm",
                        isChineseUi
                            ? $"将提交 {stagedPathsNow.Count} 个待提交条目。{breakdown}\n\n是否继续提交？"
                            : $"You are about to commit {stagedPathsNow.Count} staged items.{breakdown}\n\nContinue?",
                        isChineseUi ? "继续提交" : "Commit",
                        isChineseUi ? "取消" : "Cancel");

                    if (!confirmed)
                    {
                    return;
                }
            }

            gitOperationInProgress = true;
            gitOperationKind = pushAfter ? GitOperationKind.CommitAndPush : GitOperationKind.Commit;
            UpdateActionButtonsEnabled();
            UpdateCommitButtonsEnabled();

            statusMessage = pushAfter
                ? (isChineseUi ? "正在提交并推送..." : "Committing & pushing...")
                : (isChineseUi ? "正在提交..." : "Committing...");
            UpdateHeaderLabels();
            ForceRepaintUI();

            var isChinese = isChineseUi;
            gitOperationTask = Task.Run(() =>
            {
                var rootsToCommit = new List<string>();
                foreach (var root in repositoryRoots)
                {
                    if (GitUtility.HasStagedChanges(root))
                    {
                        rootsToCommit.Add(root);
                    }
                }

                if (rootsToCommit.Count == 0)
                {
                    var noStaged = isChinese ? "当前没有已暂存的变更可提交。" : "No staged changes to commit.";
                    return new GitOperationResult(false, noStaged, false, null);
                }

                var summaries = new List<string>();
                foreach (var root in rootsToCommit)
                {
                    var repoName = GitUtility.GetRepositoryDisplayName(root);
                    var prefix = rootsToCommit.Count == 1 || string.IsNullOrWhiteSpace(repoName) ? string.Empty : $"{repoName}: ";

                    if (!GitUtility.CommitGit(root, normalizedMessage, isChinese, out var commitSummary))
                    {
                        var failureLines = new List<string>(summaries) { prefix + commitSummary };
                        return new GitOperationResult(false, string.Join("\n", failureLines), false, null);
                    }

                    summaries.Add(prefix + commitSummary);

                    if (pushAfter)
                    {
                        if (GitUtility.PushGit(root, isChinese, out var pushSummary))
                        {
                            summaries.Add(prefix + pushSummary);
                        }
                        else
                        {
                            var pushFailMessage = isChinese
                                ? "推送失败：远程可能已有新的提交，请在 Git 客户端中先拉取更新并解决冲突后再推送。"
                                : "Push failed: Remote may have new commits. Please pull updates and resolve conflicts in your Git client before pushing again.";
                            var extraInfo = string.IsNullOrEmpty(pushSummary) ? string.Empty : "\n" + pushSummary;
                            summaries.Add(prefix + pushFailMessage + extraInfo);
                        }
                    }
                }

                return new GitOperationResult(true, string.Join("\n", summaries), true, normalizedMessage);
            });
            gitOperationTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void UpdateHeaderLabels()
        {
            // 多选时隐藏目标槽，显示文本标签
            var isMultiSelect = targetAssetPaths.Count > 1;
            if (targetField != null)
            {
                targetField.style.display = isMultiSelect ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (multiSelectLabel != null)
            {
                multiSelectLabel.style.display = isMultiSelect ? DisplayStyle.Flex : DisplayStyle.None;
                if (isMultiSelect)
                {
                    multiSelectLabel.text = isChineseUi
                        ? $"已选择 {targetAssetPaths.Count} 个目标"
                        : $"{targetAssetPaths.Count} targets selected";
                }
            }

            if (pathLabel != null)
            {
                if (targetAssetPaths.Count == 0)
                {
                    pathLabel.text = isChineseUi
                        ? "路径：未选择（不显示变更）"
                        : "Path: none selected (no changes shown)";
                }
                else if (targetAssetPaths.Count == 1)
                {
                    pathLabel.text = isChineseUi
                        ? $"路径：{targetAssetPaths[0]}"
                        : $"Path: {targetAssetPaths[0]}";
                }
                else
                {
                    pathLabel.text = isChineseUi
                        ? $"路径：已选择 {targetAssetPaths.Count} 个目标（显示关联变更）"
                        : $"Path: {targetAssetPaths.Count} targets selected (showing related changes)";
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
                var enabled = hasMessage && !refreshInProgress && !gitOperationInProgress;
                commitButton.SetEnabled(enabled);
                if (!enabled)
                {
                    var accentColor = AccentColor;
                    commitButton.style.backgroundColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0f);
                    commitButton.style.borderTopColor = accentColor;
                    commitButton.style.borderRightColor = accentColor;
                    commitButton.style.borderBottomColor = accentColor;
                    commitButton.style.borderLeftColor = accentColor;
                    commitButton.style.color = accentColor;
                }
            }

            if (commitAndPushButton != null)
            {
                var enabled = hasMessage && !refreshInProgress && !gitOperationInProgress;
                commitAndPushButton.SetEnabled(enabled);
                if (!enabled)
                {
                    var accentColor = AccentColor;
                    commitAndPushButton.style.backgroundColor = accentColor;
                    commitAndPushButton.style.borderTopColor = accentColor;
                    commitAndPushButton.style.borderRightColor = accentColor;
                    commitAndPushButton.style.borderBottomColor = accentColor;
                    commitAndPushButton.style.borderLeftColor = accentColor;
                    commitAndPushButton.style.color = Rgba(0, 0, 0, 0.95f);
                }
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

            var unstaged = ApplySortToAssetList(EnumerateFilteredAssets(false).ToList());
            var staged = ApplySortToAssetList(EnumerateFilteredAssets(true).ToList());

            var unstagedDisplayList = BuildDisplayListWithRepositoryHeaders(unstaged, stagedView: false);
            var stagedDisplayList = BuildDisplayListWithRepositoryHeaders(staged, stagedView: true);

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
                unstagedHeaderLabel.text = GetUnstagedHeaderText(unstaged.Count);
            }

            if (stagedHeaderLabel != null)
            {
                stagedHeaderLabel.text = GetStagedHeaderText(staged.Count);
            }

            visibleUnstagedItems.Clear();
            visibleUnstagedItems.AddRange(unstagedDisplayList);
            visibleStagedItems.Clear();
            visibleStagedItems.AddRange(stagedDisplayList);

            UpdateListEmptyHints();

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

        private List<GitAssetInfo> BuildDisplayListWithRepositoryHeaders(List<GitAssetInfo> items, bool stagedView)
        {
            items ??= new List<GitAssetInfo>();

            if (!hasMultipleRepositories || items.Count == 0)
            {
                return items;
            }

            var byRoot = new Dictionary<string, List<GitAssetInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in items)
            {
                if (info == null || info.IsHeader)
                {
                    continue;
                }

                var root = info.RepoRoot ?? string.Empty;
                if (!byRoot.TryGetValue(root, out var list))
                {
                    list = new List<GitAssetInfo>();
                    byRoot[root] = list;
                }

                list.Add(info);
            }

            var unknownRepoName = isChineseUi ? "未知" : "Unknown";
            var groups = byRoot
                .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                .Select(kvp =>
                {
                    var name = GitUtility.GetRepositoryDisplayName(kvp.Key);
                    return new
                    {
                        RepoRoot = kvp.Key,
                        RepoName = string.IsNullOrWhiteSpace(name) ? unknownRepoName : name,
                        Items = kvp.Value
                    };
                })
                .OrderBy(g => g.RepoName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new List<GitAssetInfo>(items.Count + groups.Count);
            foreach (var group in groups)
            {
                var title = isChineseUi
                    ? $"仓库：{group.RepoName}（{group.Items.Count}）"
                    : $"Repo: {group.RepoName} ({group.Items.Count})";
                result.Add(new GitAssetInfo(
                    path: string.Empty,
                    originalPath: null,
                    type: GitChangeType.Unknown,
                    lastTime: null,
                    workingTime: null,
                    isStaged: stagedView,
                    isUnstaged: !stagedView,
                    repoRoot: group.RepoRoot,
                    isHeader: true,
                    headerText: title));

                result.AddRange(group.Items);
            }

            return result;
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

            ConfigureAssetListView(unstagedScrollView, stagedView: false);
            ConfigureAssetListView(stagedScrollView, stagedView: true);

            RegisterListDropTarget(unstagedScrollView, unstagedListContainer, unstagedEmptyHintOverlay, targetStaged: false);
            RegisterListDropTarget(stagedScrollView, stagedListContainer, stagedEmptyHintOverlay, targetStaged: true);

            unstagedScrollView.itemsSource = visibleUnstagedItems;
            stagedScrollView.itemsSource = visibleStagedItems;

            assetListViewsConfigured = true;
        }

        private void RegisterListDropTarget(ListView listView, VisualElement container, VisualElement emptyOverlay, bool targetStaged)
        {
            if (listView != null)
            {
                listView.RegisterCallback<DragUpdatedEvent>(evt => OnListDragUpdated(evt, targetStaged), TrickleDown.TrickleDown);
                listView.RegisterCallback<DragPerformEvent>(evt => OnListDragPerform(evt, targetStaged), TrickleDown.TrickleDown);
                listView.RegisterCallback<DragLeaveEvent>(OnListDragLeave, TrickleDown.TrickleDown);
            }

            if (container != null)
            {
                container.RegisterCallback<DragUpdatedEvent>(evt => OnListDragUpdated(evt, targetStaged), TrickleDown.TrickleDown);
                container.RegisterCallback<DragPerformEvent>(evt => OnListDragPerform(evt, targetStaged), TrickleDown.TrickleDown);
                container.RegisterCallback<DragLeaveEvent>(OnListDragLeave, TrickleDown.TrickleDown);
            }

            if (emptyOverlay != null)
            {
                emptyOverlay.RegisterCallback<DragUpdatedEvent>(evt => OnListDragUpdated(evt, targetStaged), TrickleDown.TrickleDown);
                emptyOverlay.RegisterCallback<DragPerformEvent>(evt => OnListDragPerform(evt, targetStaged), TrickleDown.TrickleDown);
                emptyOverlay.RegisterCallback<DragLeaveEvent>(OnListDragLeave, TrickleDown.TrickleDown);
            }

            var scrollView = listView?.Q<ScrollView>();
            if (scrollView != null)
            {
                scrollView.RegisterCallback<DragUpdatedEvent>(evt => OnListDragUpdated(evt, targetStaged), TrickleDown.TrickleDown);
                scrollView.RegisterCallback<DragPerformEvent>(evt => OnListDragPerform(evt, targetStaged), TrickleDown.TrickleDown);
                scrollView.RegisterCallback<DragLeaveEvent>(OnListDragLeave, TrickleDown.TrickleDown);
            }
        }

        private void UpdateListEmptyHints()
        {
            var loading = refreshInProgress || (refreshTask != null && !refreshTask.IsCompleted);

            if (unstagedEmptyHintOverlay != null)
            {
                unstagedEmptyHintOverlay.style.display = visibleUnstagedItems.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (unstagedEmptyHintLabel != null && visibleUnstagedItems.Count == 0)
            {
                if (loading)
                {
                    unstagedEmptyHintLabel.text = isChineseUi ? "正在读取变更…" : "Loading changes…";
                }
                else
                {
                    unstagedEmptyHintLabel.text = isChineseUi
                        ? "暂无变更\n\n提示：\n拖拽条目到“待提交”可加入待提交\nCtrl：多选\nShift：连续选择\nCtrl+A：全选"
                        : "No changes.\n\nTips:\nDrag items to “Staged” to include them\nCtrl: multi-select\nShift: range select\nCtrl+A: select all";
                }
            }

            if (stagedEmptyHintOverlay != null)
            {
                stagedEmptyHintOverlay.style.display = visibleStagedItems.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (stagedEmptyHintLabel != null && visibleStagedItems.Count == 0)
            {
                if (loading)
                {
                    stagedEmptyHintLabel.text = isChineseUi ? "正在读取待提交…" : "Loading staged…";
                }
                else
                {
                    stagedEmptyHintLabel.text = isChineseUi
                        ? "暂无待提交条目\n\n提示：\n拖拽条目到“变更区”可取消待提交\nCtrl：多选\nShift：连续选择\nCtrl+A：全选"
                        : "No staged items.\n\nTips:\nDrag items to “Unstaged” to unstage\nCtrl: multi-select\nShift: range select\nCtrl+A: select all";
                }
            }
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

            // Drop targets are registered in EnsureAssetListViewsConfigured so they also work when the list is empty.
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
        private static readonly Vector2 DragBadgeOffset = new Vector2(12f, 12f);

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
                HideDragBadge();
                return;
            }

            ShowDragBadge(evt.mousePosition, payload.AssetPaths.Count);
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
                HideDragBadge();
                return;
            }

            if (payload.SourceStaged == targetStaged)
            {
                HideDragBadge();
                return;
            }

            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(DragPayloadKey, null);
            HideDragBadge();

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

        private void OnListDragLeave(DragLeaveEvent evt)
        {
            HideDragBadge();
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
                IconContainer = iconContainer,
                IconImage = iconImage,
                NameLabel = nameLabel,
                PathLabel = pathInfoLabel,
                ChangeBadgeContainer = changeBadgeContainer,
                ChangeBadgeLabel = changeBadgeLabel
            };
            container.userData = refs;

            container.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (refs.Info != null && refs.Info.IsHeader)
                {
                    return;
                }

                if (container.ClassListContains("gitU-asset-item--selected"))
                {
                    return;
                }

                container.style.backgroundColor = Rgba(255, 255, 255, 0.04f);
            });
            container.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (refs.Info != null && refs.Info.IsHeader)
                {
                    return;
                }

                if (container.ClassListContains("gitU-asset-item--selected"))
                {
                    return;
                }

                container.style.backgroundColor = Color.clear;
            });

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (refs.Info != null && refs.Info.IsHeader)
                {
                    return;
                }

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

                var pointerDownInfo = refs.Info;
                if (pointerDownInfo == null || string.IsNullOrEmpty(pointerDownInfo.AssetPath))
                {
                    return;
                }

                if (!evt.shiftKey && !evt.actionKey)
                {
                    var thisSet = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
                    var otherSet = stagedView ? selectedUnstagedPaths : selectedStagedPaths;
                    if (otherSet.Count > 0)
                    {
                        otherSet.Clear();
                    }

                    if (!thisSet.Contains(pointerDownInfo.AssetPath))
                    {
                        HandleRowSelection(stagedView, pointerDownInfo, refs.BoundIndex, shift: false, actionKey: false);
                    }
                    else
                    {
                        if (stagedView)
                        {
                            stagedSelectionAnchorIndex = refs.BoundIndex;
                        }
                        else
                        {
                            unstagedSelectionAnchorIndex = refs.BoundIndex;
                        }

                        lastActiveStagedView = stagedView;
                        unstagedScrollView?.RefreshItems();
                        stagedScrollView?.RefreshItems();
                    }
                }
                else
                {
                    HandleRowSelection(stagedView, pointerDownInfo, refs.BoundIndex, evt.shiftKey, evt.actionKey);
                }

                refs.DragArmed = true;
                refs.DragStartPosition = new Vector3(evt.position.x, evt.position.y, 0f);
            });

            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (refs.Info != null && refs.Info.IsHeader)
                {
                    return;
                }

                if (!refs.DragArmed || (evt.pressedButtons & 1) == 0)
                {
                    return;
                }

                var position = new Vector3(evt.position.x, evt.position.y, 0f);
                var delta = position - refs.DragStartPosition;
                if (delta.sqrMagnitude < 64f)
                {
                    return;
                }

                refs.DragArmed = false;

                var dragEvent = evt.imguiEvent;
                if (dragEvent == null || dragEvent.type != EventType.MouseDrag)
                {
                    dragEvent = new Event { type = EventType.MouseDrag };
                }

                var previousEvent = Event.current;
                try
                {
                    Event.current = dragEvent;
                    TryStartDrag(stagedView, refs.Info, refs.BoundIndex);
                }
                finally
                {
                    Event.current = previousEvent;
                }
            });

            row.RegisterCallback<MouseUpEvent>(_ =>
            {
                refs.DragArmed = false;
            });

            container.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                var info = refs.Info;
                if (info == null || info.IsHeader || string.IsNullOrEmpty(info.AssetPath))
                {
                    return;
                }

                var set = stagedView ? selectedStagedPaths : selectedUnstagedPaths;
                if (!set.Contains(info.AssetPath))
                {
                    HandleRowSelection(stagedView, info, refs.BoundIndex, shift: false, actionKey: false);
                }

                var unstageLabel = isChineseUi ? "从待提交移出" : "Remove from staged";
                var stageLabel = isChineseUi ? "发送至待提交" : "Send to staged";
                var discardLabel = isChineseUi ? "放弃更改" : "Discard changes";

                if (stagedView)
                {
                    evt.menu.AppendAction(
                        unstageLabel,
                        _ => UnstageSelectedStaged(),
                        _ => selectedStagedPaths.Count > 0
                            ? DropdownMenuAction.Status.Normal
                            : DropdownMenuAction.Status.Disabled);
                }
                else
                {
                    evt.menu.AppendAction(
                        stageLabel,
                        _ => StageSelectedUnstaged(),
                        _ => selectedUnstagedPaths.Count > 0
                            ? DropdownMenuAction.Status.Normal
                            : DropdownMenuAction.Status.Disabled);
                }

                evt.menu.AppendSeparator();

                evt.menu.AppendAction(
                    discardLabel,
                    _ => ConfirmDiscardSelected(stagedView),
                    _ => (stagedView ? selectedStagedPaths.Count : selectedUnstagedPaths.Count) > 0
                        ? DropdownMenuAction.Status.Normal
                        : DropdownMenuAction.Status.Disabled);
            }));

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
                    ShowTempNotification(isChineseUi ? $"已复制名称：{info.FileName}" : $"Copied name: {info.FileName}");
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
                    ShowTempNotification(isChineseUi ? $"已复制路径：{info.AssetPath}" : $"Copied path: {info.AssetPath}");
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
                preview += isChineseUi ? $"\n... 以及 {overflow} 项" : $"\n... and {overflow} more";
            }

            var confirmed = EditorUtility.DisplayDialog(
                isChineseUi ? "放弃更改" : "Discard Changes",
                isChineseUi
                    ? $"确定放弃选中的 {selectedInfos.Count} 项更改？\n\n{preview}\n\n此操作不可撤销。"
                    : $"Discard {selectedInfos.Count} selected changes?\n\n{preview}\n\nThis action cannot be undone.",
                isChineseUi ? "放弃" : "Discard",
                isChineseUi ? "取消" : "Cancel");
            if (!confirmed)
            {
                return;
            }

            var success = GitUtility.DiscardChanges(selectedInfos, out var summary);
            if (success)
            {
                RequestAssetDatabaseRefreshAndRefreshData();
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? (isChineseUi ? "已放弃更改。" : "Discarded changes.")
                    : summary);
            }
            else
            {
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? (isChineseUi ? "放弃更改失败。" : "Failed to discard changes.")
                    : summary);
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
            if (sourceSet.Count == 0)
            {
                sourceSet.Add(info.AssetPath);
            }
            else if (!sourceSet.Contains(info.AssetPath))
            {
                sourceSet.Add(info.AssetPath);
            }

            var selectedPaths = sourceSet.ToList();
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

            var draggedObjects = new List<UnityEngine.Object>(selectedPaths.Count);
            foreach (var path in selectedPaths)
            {
                var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null)
                {
                    draggedObjects.Add(asset);
                }
            }
            DragAndDrop.objectReferences = draggedObjects.Count > 0 ? draggedObjects.ToArray() : Array.Empty<UnityEngine.Object>();

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

            if (info != null && info.IsHeader)
            {
                element.EnableInClassList("gitU-asset-item--selected", false);
                element.parent?.EnableInClassList("gitU-list-item--selected", false);

                ApplyAssetItemSelectionVisual(element, selected: false);
                ApplyListItemWrapperBaseVisual(element.parent, selected: false);

                element.style.backgroundColor = Rgba(255, 255, 255, 0.06f);
                element.style.borderBottomColor = Rgb(20, 20, 20);

                if (refs.IconContainer != null)
                {
                    refs.IconContainer.style.display = DisplayStyle.None;
                }
                if (refs.IconImage != null)
                {
                    refs.IconImage.image = null;
                }

                if (refs.ChangeBadgeContainer != null)
                {
                    refs.ChangeBadgeContainer.style.display = DisplayStyle.None;
                }

                if (refs.NameLabel != null)
                {
                    refs.NameLabel.text = info.HeaderText ?? string.Empty;
                    refs.NameLabel.style.color = new Color(0.78f, 0.84f, 1.0f);
                }

                if (refs.PathLabel != null)
                {
                    refs.PathLabel.text = string.Empty;
                    refs.PathLabel.style.display = DisplayStyle.None;
                }

                return;
            }

            if (refs.IconContainer != null)
            {
                refs.IconContainer.style.display = DisplayStyle.Flex;
            }

            if (refs.PathLabel != null)
            {
                refs.PathLabel.style.display = DisplayStyle.Flex;
            }

            var isSelected = stagedView
                ? selectedStagedPaths.Contains(info.AssetPath)
                : selectedUnstagedPaths.Contains(info.AssetPath);
            element.EnableInClassList("gitU-asset-item--selected", isSelected);
            element.parent?.EnableInClassList("gitU-list-item--selected", isSelected);

            ApplyAssetItemSelectionVisual(element, isSelected);
            ApplyListItemWrapperBaseVisual(element.parent, isSelected);
            element.style.borderBottomColor = Rgb(20, 20, 20);

            if (refs.IconImage != null)
            {
                refs.IconImage.image = GetTypeIconForAssetPath(info.AssetPath);
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
                var unknownTimeText = isChineseUi ? "未知时间" : "Unknown time";
                var timeText = info.WorkingTreeTime.HasValue
                    ? info.WorkingTreeTime.Value.ToString("yyyy-MM-dd HH:mm")
                    : unknownTimeText;

                var displayPath = GetFolderPath(info.AssetPath);
                if (!string.IsNullOrEmpty(info.OriginalPath))
                {
                    var originalFolder = GetFolderPath(info.OriginalPath);
                    if (!string.IsNullOrEmpty(originalFolder))
                    {
                        var deletedTag = isChineseUi ? "（删除）" : " (deleted)";
                        if (info.ChangeType == GitChangeType.Renamed)
                        {
                            displayPath = $"{originalFolder}{deletedTag} -> {displayPath}";
                        }
                        else if (info.ChangeType == GitChangeType.Deleted)
                        {
                            displayPath = $"{displayPath}{deletedTag} -> {originalFolder}";
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

        private static Texture2D GetTypeIconForAssetPath(string assetPath)
        {
            if (cachedDefaultAssetIcon == null)
            {
                cachedDefaultAssetIcon = EditorGUIUtility.IconContent("DefaultAsset Icon")?.image as Texture2D;
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return cachedDefaultAssetIcon;
            }

            assetPath = GitUtility.NormalizeAssetPath(assetPath);

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                if (cachedFolderIcon == null)
                {
                    cachedFolderIcon = EditorGUIUtility.IconContent("Folder Icon")?.image as Texture2D ?? cachedDefaultAssetIcon;
                }

                return cachedFolderIcon;
            }

            var typePath = assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? assetPath.Substring(0, assetPath.Length - 5)
                : assetPath;

            Type mainType = null;
            try
            {
                mainType = AssetDatabase.GetMainAssetTypeAtPath(typePath);
            }
            catch
            {
                // ignored
            }

            if (mainType == null)
            {
                return cachedDefaultAssetIcon;
            }

            if ((mainType == typeof(Texture2D) || mainType == typeof(Sprite)) && TryGetUIImageIcon(out var uiImageIcon))
            {
                return uiImageIcon;
            }

            if (TypeIconCache.TryGetValue(mainType, out var cached) && cached != null)
            {
                return cached;
            }

            var icon = EditorGUIUtility.ObjectContent(null, mainType)?.image as Texture2D;
            icon ??= cachedDefaultAssetIcon;
            TypeIconCache[mainType] = icon;
            return icon;
        }

        private static bool TryGetUIImageIcon(out Texture2D icon)
        {
            if (cachedUIImageIcon != null)
            {
                icon = cachedUIImageIcon;
                return true;
            }

            try
            {
                // Avoid hard dependency on UnityEngine.UI to keep GitU usable in projects without UGUI.
                var uiImageType = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI", throwOnError: false);
                if (uiImageType != null)
                {
                    cachedUIImageIcon = EditorGUIUtility.ObjectContent(null, uiImageType)?.image as Texture2D;
                }

                cachedUIImageIcon ??= EditorGUIUtility.IconContent("Image Icon")?.image as Texture2D;
            }
            catch
            {
                // ignored
            }

            icon = cachedUIImageIcon;
            return icon != null;
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
            item.style.backgroundColor = selected
                ? new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.26f)
                : Color.clear;
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
                isChineseUi ? "放弃更改" : "Discard Changes",
                isChineseUi
                    ? $"确定放弃以下更改？\n{info.AssetPath}\n\n此操作不可撤销。"
                    : $"Discard the following change?\n{info.AssetPath}\n\nThis action cannot be undone.",
                isChineseUi ? "放弃" : "Discard",
                isChineseUi ? "取消" : "Cancel");
            if (!confirmed)
            {
                return;
            }

            var success = GitUtility.DiscardChanges(new[] { info }, out var summary);
            if (success)
            {
                RequestAssetDatabaseRefreshAndRefreshData();
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? (isChineseUi ? "已放弃更改。" : "Discarded changes.")
                    : summary);
            }
            else
            {
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? (isChineseUi ? "放弃更改失败。" : "Failed to discard changes.")
                    : summary);
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

            var statusText = string.Format(isChineseUi ? GitStatusFormatZh : GitStatusFormatEn, stagedCount, unstagedCount);
            var addedFormat = isChineseUi ? AddedSegmentFormatZh : AddedSegmentFormatEn;
            var modifiedFormat = isChineseUi ? ModifiedSegmentFormatZh : ModifiedSegmentFormatEn;
            var deletedFormat = isChineseUi ? DeletedSegmentFormatZh : DeletedSegmentFormatEn;
            var addedText = string.Format(addedFormat, AddedColorHex, addedCount);
            var modifiedText = string.Format(modifiedFormat, ModifiedColorHex, modifiedCount);
            var deletedText = string.Format(deletedFormat, DeletedColorHex, deletedCount);

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
                if (!matched && relevantMetaGuids.Count > 0)
                {
                    var deletedMetaUnityPath = deletedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                        ? deletedPath
                        : $"{deletedPath}.meta";

                    if (GitUtility.TryGetGitRelativePath(deletedMetaUnityPath, out var metaRepoRoot, out var gitRelativeMetaPath) &&
                        !string.IsNullOrEmpty(metaRepoRoot) &&
                        GitUtility.TryGetMetaGuidFromHead(metaRepoRoot, gitRelativeMetaPath, out var deletedGuid) &&
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
            stagedAllowListByRoot.Clear();

            var path = GetStagedAllowListFilePath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var data = JsonUtility.FromJson<StagedAllowListData>(json);
                if (data == null)
                {
                    return;
                }

                if (data.repositories == null || data.repositories.Count == 0)
                {
                    return;
                }

                foreach (var repo in data.repositories)
                {
                    if (repo == null || string.IsNullOrWhiteSpace(repo.gitRoot) || repo.gitRelativePaths == null)
                    {
                        continue;
                    }

                    var set = GetAllowListForRoot(repo.gitRoot, true);
                    if (set == null)
                    {
                        continue;
                    }

                    foreach (var p in repo.gitRelativePaths)
                    {
                        var normalized = NormalizeGitPath(p);
                        if (string.IsNullOrEmpty(normalized))
                        {
                            continue;
                        }

                        set.Add(normalized);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(isChineseUi
                    ? $"GitU: 读取暂存白名单失败: {ex.Message}"
                    : $"GitU: Failed to load staged allowlist: {ex.Message}");
                stagedAllowListByRoot.Clear();
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

                if (stagedAllowListByRoot.Count == 0 || stagedAllowListByRoot.Values.All(v => v == null || v.Count == 0))
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    return;
                }

                var data = new StagedAllowListData
                {
                    repositories = stagedAllowListByRoot
                        .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                        .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kvp => new StagedAllowListRepoEntry
                        {
                            gitRoot = NormalizeGitRoot(kvp.Key),
                            gitRelativePaths = kvp.Value
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .Select(p => NormalizeGitPath(p))
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                .ToList()
                        })
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.gitRoot) && entry.gitRelativePaths.Count > 0)
                        .ToList()
                };
                File.WriteAllText(path, JsonUtility.ToJson(data));
            }
            catch (Exception ex)
            {
                Debug.LogWarning(isChineseUi
                    ? $"GitU: 保存暂存白名单失败: {ex.Message}"
                    : $"GitU: Failed to save staged allowlist: {ex.Message}");
            }
        }

        [Serializable]
        private class CommitHistoryData
        {
            public List<string> entries = new List<string>();
        }

        [Serializable]
        private class StagedAllowListRepoEntry
        {
            public string gitRoot;
            public List<string> gitRelativePaths = new List<string>();
        }

        [Serializable]
        private class StagedAllowListData
        {
            public List<StagedAllowListRepoEntry> repositories = new List<StagedAllowListRepoEntry>();
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
