using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace OneKey.GitTools
{
    internal class GitQuickCommitWindow : EditorWindow
    {
        private const string WindowTitle = "快捷Git提交";

        private UnityEngine.Object targetAsset;
        private string targetAssetPath;
        private readonly List<GitAssetInfo> assetInfos = new List<GitAssetInfo>();
        private readonly HashSet<string> excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<GitChangeEntry> gitChanges = new List<GitChangeEntry>();

        // 旧 IMGUI 列表滚动位置（CreateGUI 不再使用，但保留避免编译错误）
        private Vector2 scrollPos;
        private Vector2 stagedScrollPos;

        // 选择状态：左侧未暂存 / 右侧已暂存
        private readonly HashSet<string> selectedUnstagedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> selectedStagedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool showAdded = true;
        private bool showModified = true;
        private bool showDeleted = true;
        private string startTimeInput = string.Empty;
        private string endTimeInput = string.Empty;
        private DateTime? startTimeFilter;
        private DateTime? endTimeFilter;
        private bool startTimeValid = true;
        private bool endTimeValid = true;
        private string statusMessage;
        private string commitMessage = string.Empty;

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
        private Label unstagedHeaderLabel;
        private Label stagedHeaderLabel;
        private ScrollView unstagedScrollView;
        private ScrollView stagedScrollView;
        private TextField commitMessageField;
        private Button commitButton;
        private Button commitAndPushButton;
        private Toggle unstagedSelectAllToggle;
        private Toggle stagedSelectAllToggle;

        // Notification
        private double notificationEndTime;

        [MenuItem("Assets/快捷Git提交", false, 2000)]
        private static void OpenFromContext()
        {
            var obj = Selection.activeObject;
            if (obj == null)
            {
                return;
            }

            var window = GetWindow<GitQuickCommitWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(600, 500);
            window.Initialize(obj);
            window.Show();
        }

        [MenuItem("Assets/快捷Git提交", true)]
        private static bool ValidateOpenFromContext()
        {
            var obj = Selection.activeObject;
            if (obj == null)
            {
                return false;
            }

            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return true;
        }

        [MenuItem("GameObject/快捷Git提交", false, 2000)]
        private static void OpenFromGameObject(MenuCommand command)
        {
            var go = Selection.activeGameObject;
            var asset = ResolveAssetFromGameObject(go);
            if (asset == null)
            {
                return;
            }

            var window = GetWindow<GitQuickCommitWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(600, 500);
            window.Initialize(asset);
            window.Show();
        }

        [MenuItem("GameObject/快捷Git提交", true)]
        private static bool ValidateOpenFromGameObject(MenuCommand command)
        {
            var go = Selection.activeGameObject;
            return ResolveAssetFromGameObject(go) != null;
        }

        private static UnityEngine.Object ResolveAssetFromGameObject(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            // 1. 如果这个对象本身就是一个资源（少数情况）
            var directPath = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(directPath))
            {
                return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(directPath);
            }

            // 2. Prefab 实例：取最近的 Prefab 资源路径
            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
            }

            // 3. 普通场景节点：退回到所在场景资源
            var scenePath = go.scene.path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath);
            }

            return null;
        }

        private void Initialize(UnityEngine.Object asset)
        {
            targetAsset = asset;
            excludedPaths.Clear();
            GitUtility.SetContextAssetPath(AssetDatabase.GetAssetPath(asset));
            RefreshData();
        }

        private void OnEnable()
        {
            // For domain reload / layout reload, rebuild UI
            CreateGUI();
        }

        private void Update()
        {
            if (notificationEndTime > 0 && EditorApplication.timeSinceStartup >= notificationEndTime)
            {
                RemoveNotification();
                notificationEndTime = 0;
            }
        }

        private void OnFocus()
        {
        }

        private void OnGUI()
        {
            // 使用 UI Toolkit，不再绘制 IMGUI
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
            var root = rootVisualElement;
            root.Clear();
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Editor/QuickGitCommit/GitQuickCommitWindow.uxml");
            if (visualTree != null)
            {
                visualTree.CloneTree(root);

                var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/QuickGitCommit/GitQuickCommitWindow.uss");
                if (styleSheet != null)
                {
                    root.styleSheets.Add(styleSheet);
                }

                targetField = root.Q<ObjectField>("targetField");
                if (targetField != null)
                {
                    targetField.objectType = typeof(UnityEngine.Object);
                    targetField.allowSceneObjects = false;
                    targetField.value = targetAsset;
                    targetField.RegisterValueChangedCallback(evt =>
                    {
                        targetAsset = evt.newValue;
                        excludedPaths.Clear();
                        GitUtility.SetContextAssetPath(targetAsset != null ? AssetDatabase.GetAssetPath(targetAsset) : null);
                        RefreshData();
                    });
                }

                pathLabel = root.Q<Label>("pathLabel");
                statusLabel = root.Q<Label>("statusLabel");

                toggleAdded = root.Q<Toggle>("toggleAdded");
                toggleModified = root.Q<Toggle>("toggleModified");
                toggleDeleted = root.Q<Toggle>("toggleDeleted");
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

                var presetChoicesUxml = new List<string>
                {
                    "无",
                    "1 小时内",
                    "5 小时内",
                    "1 天内",
                    "2 天内",
                    "5 天内"
                };

                timePresetField = root.Q<DropdownField>("timePresetField");
                if (timePresetField != null)
                {
                    timePresetField.choices = presetChoicesUxml;
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

                startTimeField = root.Q<TextField>("startTimeField");
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

                endTimeField = root.Q<TextField>("endTimeField");
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

                var resetTimeButtonUxml = root.Q<Button>("resetTimeButton");
                if (resetTimeButtonUxml != null)
                {
                    resetTimeButtonUxml.clicked += () =>
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

                var refreshButtonUxml = root.Q<Button>("refreshButton");
                if (refreshButtonUxml != null)
                {
                    refreshButtonUxml.clicked += () => { RefreshData(); };
                }

                unstagedHeaderLabel = root.Q<Label>("unstagedHeaderLabel");
                stagedHeaderLabel = root.Q<Label>("stagedHeaderLabel");
                unstagedScrollView = root.Q<ScrollView>("unstagedScrollView");
                stagedScrollView = root.Q<ScrollView>("stagedScrollView");

                unstagedSelectAllToggle = root.Q<Toggle>("unstagedSelectAllToggle");
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

                stagedSelectAllToggle = root.Q<Toggle>("stagedSelectAllToggle");
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

                var toStagedButtonUxml = root.Q<Button>("toStagedButton");
                if (toStagedButtonUxml != null)
                {
                    toStagedButtonUxml.clicked += () => { StageSelectedUnstaged(); };
                }

                var toUnstagedButtonUxml = root.Q<Button>("toUnstagedButton");
                if (toUnstagedButtonUxml != null)
                {
                    toUnstagedButtonUxml.clicked += () => { UnstageSelectedStaged(); };
                }

                commitMessageField = root.Q<TextField>("commitMessageField");
                if (commitMessageField != null)
                {
                    commitMessageField.value = commitMessage;
                    commitMessageField.RegisterValueChangedCallback(evt =>
                    {
                        commitMessage = evt.newValue;
                        UpdateCommitButtonsEnabled();
                    });
                }

                commitButton = root.Q<Button>("commitButton");
                if (commitButton != null)
                {
                    commitButton.clicked += () => { PerformCommit(false); };
                }

                commitAndPushButton = root.Q<Button>("commitAndPushButton");
                if (commitAndPushButton != null)
                {
                    commitAndPushButton.clicked += () => { PerformCommit(true); };
                }

                UpdateHeaderLabels();
                UpdateCommitButtonsEnabled();
                RefreshListViews();

                return;
            }

            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1f;
            // VS Code Dark 背景 #1E1E1E
            root.style.backgroundColor = new Color(0.117f, 0.117f, 0.117f, 1f);

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Column;
            header.style.marginBottom = 4;
            header.style.paddingLeft = 4;
            header.style.paddingRight = 4;
            header.style.paddingTop = 4;
            header.style.paddingBottom = 4;
            header.style.borderBottomWidth = 0;
            header.style.borderBottomColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            // 卡片背景 #252526
            header.style.backgroundColor = new Color(0.145f, 0.145f, 0.149f, 1f);

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            targetField = new ObjectField("目标资源")
            {
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = false
            };
            targetField.value = targetAsset;
            targetField.style.flexGrow = 1f;
            targetField.RegisterValueChangedCallback(evt =>
            {
                targetAsset = evt.newValue;
                excludedPaths.Clear();
                GitUtility.SetContextAssetPath(targetAsset != null ? AssetDatabase.GetAssetPath(targetAsset) : null);
                RefreshData();
            });
            headerRow.Add(targetField);

            var clearExcludeButton = new Button(() =>
            {
                excludedPaths.Clear();
                RefreshListViews();
            })
            { text = "清除排除" };
            clearExcludeButton.style.width = 80;
            headerRow.Add(clearExcludeButton);

            header.Add(headerRow);

            pathLabel = new Label();
            header.Add(pathLabel);

            var operationHintLabel = new Label("提示：单击文件名定位，快速双击文件名复制名称，快速双击路径复制路径。");
            operationHintLabel.style.fontSize = 10;
            operationHintLabel.style.color = new Color(1f, 1f, 1f, 0.7f);
            operationHintLabel.style.marginTop = 2;
            header.Add(operationHintLabel);

            statusLabel = new Label();
            header.Add(statusLabel);

            root.Add(header);

            // Filters
            var filterBox = new VisualElement();
            filterBox.style.flexDirection = FlexDirection.Column;
            filterBox.style.paddingLeft = 4;
            filterBox.style.paddingRight = 4;
            filterBox.style.paddingTop = 4;
            filterBox.style.paddingBottom = 4;
            filterBox.style.marginBottom = 4;
            filterBox.style.borderBottomWidth = 0;
            filterBox.style.borderBottomColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            filterBox.style.backgroundColor = new Color(0.145f, 0.145f, 0.149f, 1f);

            var filterTitle = new Label("条件筛选");
            filterTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            filterBox.Add(filterTitle);

            // 类型筛选：文字在左，勾选框在右
            var typeRow = new VisualElement();
            typeRow.style.flexDirection = FlexDirection.Row;

            VisualElement CreateTypeToggle(string label, Color textColor, ref Toggle toggle, bool initialValue)
            {
                var container = new VisualElement();
                container.style.flexDirection = FlexDirection.Row;
                container.style.alignItems = Align.Center;
                container.style.marginRight = 8;

                var text = new Label(label);
                text.style.marginRight = 2;
                text.style.color = textColor;

                toggle = new Toggle { value = initialValue };

                container.Add(text);
                container.Add(toggle);
                return container;
            }

            toggleAdded = null;
            toggleModified = null;
            toggleDeleted = null;

            var addedContainer = CreateTypeToggle("新增", new Color(0.5f, 0.85f, 0.5f), ref toggleAdded, showAdded);
            var modifiedContainer = CreateTypeToggle("修改", new Color(0.95f, 0.75f, 0.4f), ref toggleModified, showModified);
            var deletedContainer = CreateTypeToggle("删除", new Color(0.9f, 0.5f, 0.5f), ref toggleDeleted, showDeleted);

            toggleAdded.RegisterValueChangedCallback(evt => { showAdded = evt.newValue; RefreshListViews(); });
            toggleModified.RegisterValueChangedCallback(evt => { showModified = evt.newValue; RefreshListViews(); });
            toggleDeleted.RegisterValueChangedCallback(evt => { showDeleted = evt.newValue; RefreshListViews(); });

            typeRow.Add(addedContainer);
            typeRow.Add(modifiedContainer);
            typeRow.Add(deletedContainer);

            // 左右分布：左侧是类型勾选，右侧是时间枚举
            var typeRowSpacer = new VisualElement();
            typeRowSpacer.style.flexGrow = 1f;
            typeRow.Add(typeRowSpacer);

            // 时间枚举放在同一行
            var presetChoices = new List<string>
            {
                "无",
                "1 小时内",
                "5 小时内",
                "1 天内",
                "2 天内",
                "5 天内"
            };

            var presetContainer = new VisualElement();
            presetContainer.style.flexDirection = FlexDirection.Row;
            presetContainer.style.alignItems = Align.Center;
            presetContainer.style.marginLeft = 12;

            var presetLabel = new Label("时间");
            presetLabel.style.marginRight = 2;
            presetContainer.Add(presetLabel);

            timePresetField = new DropdownField(string.Empty, presetChoices, 0);
            timePresetField.style.width = 110;
            timePresetField.style.height = 20;
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
                        // 选择“无”：清空时间筛选
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
            presetContainer.Add(timePresetField);

            typeRow.Add(presetContainer);
            filterBox.Add(typeRow);

            // 时间范围：开始 / 结束 各占一行，左文字右输入框
            filterBox.Add(new Label("时间范围 (格式: yyyy-MM-dd HH:mm)"));

            var startRow = new VisualElement();
            startRow.style.flexDirection = FlexDirection.Row;
            startRow.style.alignItems = Align.Center;

            var startLabel = new Label("开始");
            startLabel.style.minWidth = 40;
            startLabel.style.marginRight = 4;
            startRow.Add(startLabel);

            startTimeField = new TextField { value = startTimeInput };
            startTimeField.style.flexGrow = 1f;
            startTimeField.RegisterValueChangedCallback(evt =>
            {
                startTimeInput = evt.newValue;
                startTimeFilter = TryParseDateTime(startTimeInput, out startTimeValid);
                RefreshListViews();
            });
            startRow.Add(startTimeField);
            filterBox.Add(startRow);

            var endRow = new VisualElement();
            endRow.style.flexDirection = FlexDirection.Row;
            endRow.style.alignItems = Align.Center;

            var endLabel = new Label("结束");
            endLabel.style.minWidth = 40;
            endLabel.style.marginRight = 4;
            endRow.Add(endLabel);

            endTimeField = new TextField { value = endTimeInput };
            endTimeField.style.flexGrow = 1f;
            endTimeField.RegisterValueChangedCallback(evt =>
            {
                endTimeInput = evt.newValue;
                endTimeFilter = TryParseDateTime(endTimeInput, out endTimeValid);
                RefreshListViews();
            });
            endRow.Add(endTimeField);
            filterBox.Add(endRow);

            var timeButtonRow = new VisualElement();
            timeButtonRow.style.flexDirection = FlexDirection.Row;
            timeButtonRow.style.marginTop = 2;

            var resetTimeButton = new Button(() =>
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
            })
            { text = "重置时间筛选" };
            resetTimeButton.style.flexGrow = 1f;
            resetTimeButton.style.height = 22;
            resetTimeButton.style.marginRight = 4;
            timeButtonRow.Add(resetTimeButton);

            var refreshButton = new Button(() => { RefreshData(); }) { text = "刷新" };
            refreshButton.style.flexGrow = 1f;
            refreshButton.style.height = 22;
            timeButtonRow.Add(refreshButton);

            filterBox.Add(timeButtonRow);

            root.Add(filterBox);

            // 中间区域：左（未暂存列表 45%）+ 中（箭头按钮 10%）+ 右（已暂存列表 45%）
            var middleRow = new VisualElement();
            middleRow.style.flexDirection = FlexDirection.Row;
            middleRow.style.flexGrow = 1f;

            // 左侧：未暂存列表（约 45% 宽度）
            var leftCol = new VisualElement();
            leftCol.style.flexDirection = FlexDirection.Column;
            leftCol.style.flexGrow = 45f;
            leftCol.style.flexShrink = 0f;
            leftCol.style.marginRight = 4;
            leftCol.style.borderTopWidth = 1;
            leftCol.style.borderBottomWidth = 1;
            leftCol.style.borderLeftWidth = 1;
            leftCol.style.borderRightWidth = 1;
            leftCol.style.borderTopColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            leftCol.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            leftCol.style.borderLeftColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            leftCol.style.borderRightColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            leftCol.style.backgroundColor = new Color(0.145f, 0.145f, 0.149f, 1f);

            var unstagedHeaderRow = new VisualElement();
            unstagedHeaderRow.style.flexDirection = FlexDirection.Row;
            unstagedHeaderRow.style.alignItems = Align.Center;

            var unstagedSelectAllContainer = new VisualElement();
            unstagedSelectAllContainer.style.flexDirection = FlexDirection.Row;
            unstagedSelectAllContainer.style.alignItems = Align.Center;

            unstagedSelectAllToggle = new Toggle();
            unstagedSelectAllToggle.style.marginRight = 2;
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

            var unstagedSelectAllLabel = new Label("全选");
            unstagedSelectAllContainer.Add(unstagedSelectAllToggle);
            unstagedSelectAllContainer.Add(unstagedSelectAllLabel);

            unstagedHeaderRow.Add(unstagedSelectAllContainer);

            var unstagedHeaderSpacer = new VisualElement();
            unstagedHeaderSpacer.style.flexGrow = 1f;
            unstagedHeaderRow.Add(unstagedHeaderSpacer);

            unstagedHeaderLabel = new Label();
            unstagedHeaderRow.Add(unstagedHeaderLabel);
            leftCol.Add(unstagedHeaderRow);
            unstagedScrollView = new ScrollView();
            unstagedScrollView.style.flexGrow = 1f;
            leftCol.Add(unstagedScrollView);

            // 中间箭头按钮列（约 10% 宽度）
            var middleCol = new VisualElement();
            middleCol.style.flexDirection = FlexDirection.Column;
            middleCol.style.alignItems = Align.Stretch;
            middleCol.style.justifyContent = Justify.Center;
            middleCol.style.flexGrow = 10f;
            middleCol.style.flexShrink = 0f;
            middleCol.style.marginLeft = 2;
            middleCol.style.marginRight = 2;

            var toStagedButton = new Button(() => { StageSelectedUnstaged(); }) { text = "→" };
            toStagedButton.style.marginBottom = 6;
            var toUnstagedButton = new Button(() => { UnstageSelectedStaged(); }) { text = "←" };

            middleCol.Add(toStagedButton);
            middleCol.Add(toUnstagedButton);

            // 右侧：已暂存列表（约 45% 宽度）
            var rightCol = new VisualElement();
            rightCol.style.flexDirection = FlexDirection.Column;
            rightCol.style.flexGrow = 45f;
            rightCol.style.flexShrink = 0f;
            rightCol.style.marginLeft = 4;
            rightCol.style.borderTopWidth = 1;
            rightCol.style.borderBottomWidth = 1;
            rightCol.style.borderLeftWidth = 1;
            rightCol.style.borderRightWidth = 1;
            rightCol.style.borderTopColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            rightCol.style.borderBottomColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            rightCol.style.borderLeftColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            rightCol.style.borderRightColor = new Color(0.235f, 0.235f, 0.235f, 1f);
            rightCol.style.backgroundColor = new Color(0.145f, 0.145f, 0.149f, 1f);

            var stagedHeaderRow = new VisualElement();
            stagedHeaderRow.style.flexDirection = FlexDirection.Row;
            stagedHeaderRow.style.alignItems = Align.Center;

            var stagedSelectAllContainer = new VisualElement();
            stagedSelectAllContainer.style.flexDirection = FlexDirection.Row;
            stagedSelectAllContainer.style.alignItems = Align.Center;

            stagedSelectAllToggle = new Toggle();
            stagedSelectAllToggle.style.marginRight = 2;
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

            var stagedSelectAllLabel = new Label("全选");
            stagedSelectAllContainer.Add(stagedSelectAllToggle);
            stagedSelectAllContainer.Add(stagedSelectAllLabel);

            stagedHeaderRow.Add(stagedSelectAllContainer);

            var stagedHeaderSpacer = new VisualElement();
            stagedHeaderSpacer.style.flexGrow = 1f;
            stagedHeaderRow.Add(stagedHeaderSpacer);

            stagedHeaderLabel = new Label();
            stagedHeaderRow.Add(stagedHeaderLabel);
            rightCol.Add(stagedHeaderRow);
            stagedScrollView = new ScrollView();
            stagedScrollView.style.flexGrow = 1f;
            rightCol.Add(stagedScrollView);

            middleRow.Add(leftCol);
            middleRow.Add(middleCol);
            middleRow.Add(rightCol);
            root.Add(middleRow);

            // Commit area
            var commitBox = new VisualElement();
            commitBox.style.flexDirection = FlexDirection.Column;
            commitBox.style.paddingLeft = 4;
            commitBox.style.paddingRight = 4;
            commitBox.style.paddingTop = 4;
            commitBox.style.paddingBottom = 4;
            commitBox.style.marginTop = 4;
            commitBox.style.borderTopWidth = 0;
            commitBox.style.borderTopColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            commitBox.style.backgroundColor = new Color(0.145f, 0.145f, 0.149f, 1f);
            commitBox.style.flexShrink = 0f; // 不被列表挤压高度

            var commitTitle = new Label("提交");
            commitTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            commitBox.Add(commitTitle);

            // 提交说明：占满一行宽度
            commitMessageField = new TextField("提交说明") { value = commitMessage };
            commitMessageField.style.flexGrow = 1f;
            commitMessageField.style.marginTop = 2;
            commitMessageField.RegisterValueChangedCallback(evt =>
            {
                commitMessage = evt.newValue;
                UpdateCommitButtonsEnabled();
            });
            commitBox.Add(commitMessageField);

            // 按钮父容器：与提交说明上下布局，高度固定，不被压缩；按钮自适应宽度
            var commitRow = new VisualElement();
            commitRow.style.flexDirection = FlexDirection.Row;
            commitRow.style.justifyContent = Justify.FlexStart;
            commitRow.style.marginTop = 4;
            commitRow.style.flexShrink = 0f;

            commitButton = new Button(() => { PerformCommit(false); }) { text = "提交到本地" };
            commitButton.style.flexGrow = 1f;
            commitButton.style.height = 22;
            commitButton.style.marginRight = 4;
            commitButton.style.backgroundColor = new Color(0.25f, 0.6f, 0.25f, 1f); // 略偏绿色

            commitAndPushButton = new Button(() => { PerformCommit(true); }) { text = "提交并推送" };
            commitAndPushButton.style.flexGrow = 1f;
            commitAndPushButton.style.height = 22;
            commitAndPushButton.style.backgroundColor = new Color(0.2f, 0.45f, 0.75f, 1f); // 略偏蓝色

            commitRow.Add(commitButton);
            commitRow.Add(commitAndPushButton);
            commitBox.Add(commitRow);

            root.Add(commitBox);

            UpdateHeaderLabels();
            UpdateCommitButtonsEnabled();
            RefreshListViews();
        }

        private void UpdateHeaderLabels()
        {
            if (pathLabel != null)
            {
                pathLabel.text = string.IsNullOrEmpty(targetAssetPath) ? string.Empty : $"路径: {targetAssetPath}";
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
                commitButton.SetEnabled(hasMessage);
            }

            if (commitAndPushButton != null)
            {
                commitAndPushButton.SetEnabled(hasMessage);
            }
        }

        private void RefreshListViews()
        {
            if (unstagedScrollView == null || stagedScrollView == null)
            {
                return;
            }

            var unstaged = EnumerateFilteredAssets(false).ToList();
            var staged = EnumerateFilteredAssets(true).ToList();

            if (unstagedHeaderLabel != null)
            {
                unstagedHeaderLabel.text = $"工作区变更（未暂存）: {unstaged.Count} 个";
            }

            if (stagedHeaderLabel != null)
            {
                stagedHeaderLabel.text = $"待提交（已暂存）: {staged.Count} 个";
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
                    ? "Git未检测到与该资源相关的变更"
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

            // 顶部：勾选框 + 时间（左）+ 资源名（右）
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;

            var selectToggle = new Toggle();
            selectToggle.style.width = 16;
            selectToggle.style.marginRight = 4;

            var isSelected = stagedView
                ? selectedStagedPaths.Contains(info.AssetPath)
                : selectedUnstagedPaths.Contains(info.AssetPath);
            selectToggle.value = isSelected;

            selectToggle.RegisterValueChangedCallback(evt =>
            {
                if (stagedView)
                {
                    if (evt.newValue)
                    {
                        selectedStagedPaths.Add(info.AssetPath);
                    }
                    else
                    {
                        selectedStagedPaths.Remove(info.AssetPath);
                    }
                }
                else
                {
                    if (evt.newValue)
                    {
                        selectedUnstagedPaths.Add(info.AssetPath);
                    }
                    else
                    {
                        selectedUnstagedPaths.Remove(info.AssetPath);
                    }
                }
            });

            headerRow.Add(selectToggle);

            // 资源名（左侧，根据类型着色）
            var nameLabel = new Label(info.FileName);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            Color nameColor;
            switch (info.ChangeType)
            {
                case GitChangeType.Added:
                    nameColor = new Color(0.5f, 0.85f, 0.5f); // 绿色，稍暗
                    break;
                case GitChangeType.Deleted:
                    nameColor = new Color(0.9f, 0.5f, 0.5f); // 红色，稍暗
                    break;
                case GitChangeType.Modified:
                    nameColor = new Color(0.95f, 0.75f, 0.4f); // 橙色，稍暗
                    break;
                case GitChangeType.Renamed:
                    nameColor = new Color(0.6f, 0.75f, 1.0f); // 蓝色，稍暗
                    break;
                default:
                    nameColor = Color.white;
                    break;
            }
            nameLabel.style.color = nameColor;
            headerRow.Add(nameLabel);

            // 单击名称：定位资源；双击名称：复制名称
            nameLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                {
                    CopyToClipboard(info.FileName);
                    ShowTempNotification($"已复制名称: {info.FileName}");
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

            // 占位弹性元素，让时间靠右
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            headerRow.Add(spacer);

            // 时间文本（最右侧）
            var displayTime = info.WorkingTreeTime;
            var timeText = displayTime.HasValue ? displayTime.Value.ToString("yyyy-MM-dd HH:mm") : "未提交";
            var timeLabel = new Label(timeText);
            timeLabel.style.marginLeft = 8;
            timeLabel.style.fontSize = 10;
            timeLabel.style.color = new Color(1f, 1f, 1f, 0.6f);
            headerRow.Add(timeLabel);

            container.Add(headerRow);

            // 第二行：完整路径（小号、半透明），未暂存时右侧带“移除”按钮
            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.alignItems = Align.Center;

            var displayPath = info.AssetPath;
            var lastSlash = string.IsNullOrEmpty(displayPath) ? -1 : displayPath.LastIndexOf('/') ;
            if (lastSlash > 0)
            {
                displayPath = displayPath.Substring(0, lastSlash);
            }

            var pathLabel = new Label(displayPath);
            pathLabel.style.fontSize = 10;
            pathLabel.style.color = new Color(1f, 1f, 1f, 0.6f);
            pathLabel.style.flexGrow = 1f;
            pathRow.Add(pathLabel);

            // 双击路径：复制路径
            pathLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                {
                    CopyToClipboard(info.AssetPath);
                    ShowTempNotification($"已复制路径: {info.AssetPath}");
                }
            });

            if (!stagedView)
            {
                var removeButton = new Button(() =>
                {
                    excludedPaths.Add(info.AssetPath);
                    RefreshListViews();
                })
                { text = "移除" };
                removeButton.style.marginLeft = 4;
                pathRow.Add(removeButton);
            }

            container.Add(pathRow);

            // 已暂存条目：在单独一行右侧放置“移出待提交”按钮
            if (stagedView)
            {
            }

            return container;
        }

        private Button CreateQuickRangeButton(string label, TimeSpan duration)
        {
            var button = new Button(() =>
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
            })
            { text = label };

            return button;
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

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var newTarget = EditorGUILayout.ObjectField("目标资源", targetAsset, typeof(UnityEngine.Object), false);
                if (EditorGUI.EndChangeCheck())
                {
                    targetAsset = newTarget;
                    excludedPaths.Clear();
                    GitUtility.SetContextAssetPath(targetAsset != null ? AssetDatabase.GetAssetPath(targetAsset) : null);
                    RefreshData();
                }

                if (GUILayout.Button("刷新", GUILayout.Width(60)))
                {
                    RefreshData();
                }

                if (GUILayout.Button("清除排除", GUILayout.Width(80)))
                {
                    excludedPaths.Clear();
                }

                if (GUILayout.Button("发送至待提交", GUILayout.Width(110)))
                {
                    SendVisibleChangesToStage();
                }
            }

            if (!string.IsNullOrEmpty(targetAssetPath))
            {
                EditorGUILayout.LabelField("路径", targetAssetPath);
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFilters()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("条件筛选", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                showAdded = GUILayout.Toggle(showAdded, "新增", EditorStyles.toolbarButton);
                showModified = GUILayout.Toggle(showModified, "修改", EditorStyles.toolbarButton);
                showDeleted = GUILayout.Toggle(showDeleted, "删除", EditorStyles.toolbarButton);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("时间范围 (格式: yyyy-MM-dd HH:mm)");

            EditorGUI.BeginChangeCheck();
            startTimeInput = EditorGUILayout.TextField("开始", startTimeInput);
            endTimeInput = EditorGUILayout.TextField("结束", endTimeInput);
            if (EditorGUI.EndChangeCheck())
            {
                startTimeFilter = TryParseDateTime(startTimeInput, out startTimeValid);
                endTimeFilter = TryParseDateTime(endTimeInput, out endTimeValid);
            }

            if (!startTimeValid)
            {
                EditorGUILayout.HelpBox("开始时间格式无效", MessageType.Warning);
            }

            if (!endTimeValid)
            {
                EditorGUILayout.HelpBox("结束时间格式无效", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawQuickRangeButton("1 小时内", TimeSpan.FromHours(1));
                DrawQuickRangeButton("5 小时内", TimeSpan.FromHours(5));
                DrawQuickRangeButton("1 天内", TimeSpan.FromDays(1));
                DrawQuickRangeButton("2 天内", TimeSpan.FromDays(2));
                DrawQuickRangeButton("5 天内", TimeSpan.FromDays(5));
            }

            if (GUILayout.Button("重置时间筛选"))
            {
                startTimeInput = string.Empty;
                endTimeInput = string.Empty;
                startTimeFilter = null;
                endTimeFilter = null;
                startTimeValid = true;
                endTimeValid = true;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.BeginVertical();
                DrawUnstagedList();
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(8);

                EditorGUILayout.BeginVertical();
                DrawStagedList();
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawUnstagedList()
        {
            var filtered = EnumerateFilteredAssets(false).ToList();
            EditorGUILayout.LabelField($"工作区变更（未暂存）: {filtered.Count} 个", EditorStyles.boldLabel);

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("没有符合条件的变更。", MessageType.Info);
                return;
            }

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            foreach (var info in filtered)
            {
                DrawAssetRow(info);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawStagedList()
        {
            var filtered = EnumerateFilteredAssets(true).ToList();
            EditorGUILayout.LabelField($"待提交（已暂存）: {filtered.Count} 个", EditorStyles.boldLabel);

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("没有已暂存的变更。", MessageType.Info);
                return;
            }

            stagedScrollPos = EditorGUILayout.BeginScrollView(stagedScrollPos);
            foreach (var info in filtered)
            {
                DrawStagedAssetRow(info);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetRow(GitAssetInfo info)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(info.AssetPath, EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("类型", info.ChangeType.ToDisplayName(), GUILayout.Width(120));
                var displayTime = info.WorkingTreeTime;
                var timeText = displayTime.HasValue ? displayTime.Value.ToString("yyyy-MM-dd HH:mm") : "未提交";
                EditorGUILayout.LabelField("本地修改时间", timeText);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制路径", GUILayout.Width(80)))
                {
                    CopyToClipboard(info.AssetPath);
                }

                if (GUILayout.Button("复制名称", GUILayout.Width(80)))
                {
                    CopyToClipboard(info.FileName);
                }

                if (GUILayout.Button("定位", GUILayout.Width(60)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.AssetPath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                        Selection.activeObject = asset;
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("移除", GUILayout.Width(60)))
                {
                    excludedPaths.Add(info.AssetPath);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStagedAssetRow(GitAssetInfo info)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(info.AssetPath, EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("类型", info.ChangeType.ToDisplayName(), GUILayout.Width(120));
                var displayTime = info.WorkingTreeTime;
                var timeText = displayTime.HasValue ? displayTime.Value.ToString("yyyy-MM-dd HH:mm") : "未提交";
                EditorGUILayout.LabelField("本地修改时间", timeText);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制路径", GUILayout.Width(80)))
                {
                    CopyToClipboard(info.AssetPath);
                }

                if (GUILayout.Button("复制名称", GUILayout.Width(80)))
                {
                    CopyToClipboard(info.FileName);
                }

                if (GUILayout.Button("定位", GUILayout.Width(60)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.AssetPath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                        Selection.activeObject = asset;
                    }
                }

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndVertical();
        }

        private void UnstageSingle(GitAssetInfo info)
        {
            var list = new List<GitAssetInfo> { info };
            var success = GitUtility.UnstageAssets(list, out var summary);
            EditorUtility.DisplayDialog("移出待提交", summary, "确定");
            if (success)
            {
                RefreshData();
            }
        }

        private void StageSelectedUnstaged()
        {
            var paths = selectedUnstagedPaths.ToList();
            if (paths.Count == 0)
            {
                ShowTempNotification("请先在左侧勾选要发送的变更。");
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

            var success = GitUtility.StageAssets(toStage, out var summary);
            if (success)
            {
                selectedUnstagedPaths.Clear();
                RefreshData();
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? $"已发送 {toStage.Count} 个变更至待提交。"
                    : summary);
            }
            else
            {
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? "发送至待提交失败，请检查 Git 输出。"
                    : summary);
            }
        }

        private void UnstageSelectedStaged()
        {
            var paths = selectedStagedPaths.ToList();
            if (paths.Count == 0)
            {
                ShowTempNotification("请先在右侧勾选要移出的变更。");
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

            var success = GitUtility.UnstageAssets(toUnstage, out var summary);
            if (success)
            {
                selectedStagedPaths.Clear();
                RefreshData();
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? $"已从待提交移出 {toUnstage.Count} 个变更。"
                    : summary);
            }
            else
            {
                ShowTempNotification(string.IsNullOrEmpty(summary)
                    ? "移出待提交失败，请检查 Git 输出。"
                    : summary);
            }
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
                    if (info.IsStaged && !info.IsUnstaged)
                    {
                        continue;
                    }
                }

                if (excludedPaths.Contains(info.AssetPath))
                {
                    continue;
                }

                if (!IsChangeTypeVisible(info.ChangeType))
                {
                    continue;
                }

                var filterTime = info.WorkingTreeTime;
                if (!IsWithinTimeRange(filterTime))
                {
                    continue;
                }

                yield return info;
            }
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
            assetInfos.Clear();
            statusMessage = string.Empty;
            selectedUnstagedPaths.Clear();
            selectedStagedPaths.Clear();

            if (targetAsset == null)
            {
                statusMessage = "请指定一个资源";
                Repaint();
                return;
            }

            targetAssetPath = GitUtility.NormalizeAssetPath(AssetDatabase.GetAssetPath(targetAsset));
            GitUtility.SetContextAssetPath(targetAssetPath);
            if (string.IsNullOrEmpty(targetAssetPath))
            {
                statusMessage = "无法解析资源路径";
                Repaint();
                return;
            }

            gitChanges = GitUtility.GetAssetRelatedChanges(targetAssetPath).ToList();
            if (gitChanges.Count == 0)
            {
                statusMessage = "Git未检测到与该资源相关的变更";
            }

            foreach (var change in gitChanges)
            {
                if (excludedPaths.Contains(change.Path))
                {
                    continue;
                }

                var lastTime = GitUtility.GetLastKnownChangeTime(change.Path);
                assetInfos.Add(new GitAssetInfo(change.Path, change.ChangeType, lastTime, change.WorkingTreeTime, change.IsStaged, change.IsUnstaged));
            }

            assetInfos.Sort((a, b) => string.Compare(a.AssetPath, b.AssetPath, StringComparison.OrdinalIgnoreCase));
            if (targetField != null)
            {
                targetField.value = targetAsset;
            }
            UpdateHeaderLabels();
            RefreshListViews();
            Repaint();
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

        private void DrawCommitArea()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("提交", EditorStyles.boldLabel);

            commitMessage = EditorGUILayout.TextField("提交说明", commitMessage);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                var hasMessage = !string.IsNullOrWhiteSpace(commitMessage);
                var prevEnabled = GUI.enabled;
                GUI.enabled = hasMessage;

                if (GUILayout.Button("提交到本地", GUILayout.Width(100)))
                {
                    PerformCommit(false);
                }

                if (GUILayout.Button("提交并推送", GUILayout.Width(110)))
                {
                    PerformCommit(true);
                }

                GUI.enabled = prevEnabled;
            }

            EditorGUILayout.EndVertical();
        }

        private void PerformCommit(bool pushAfter)
        {
            var message = commitMessage;
            if (string.IsNullOrWhiteSpace(message))
            {
                EditorUtility.DisplayDialog("提交", "请先填写提交说明。", "确定");
                return;
            }

            if (!GitUtility.Commit(message, out var commitSummary))
            {
                EditorUtility.DisplayDialog("提交", commitSummary, "确定");
                return;
            }

            var finalSummary = commitSummary;

            if (pushAfter)
            {
                if (GitUtility.Push(out var pushSummary))
                {
                    finalSummary = commitSummary + "\n" + pushSummary;
                }
                else
                {
                    // 此时提交已成功，仅推送失败，多数情况是远程有更新需要先拉取
                    finalSummary = commitSummary +
                                   "\n推送失败：远程可能已有新的提交，请在 UGit 中先拉取更新并解决冲突后再推送。" +
                                   (string.IsNullOrEmpty(pushSummary) ? string.Empty : "\n" + pushSummary);
                }
            }

            EditorUtility.DisplayDialog("提交", finalSummary, "确定");
            commitMessage = string.Empty;
            if (commitMessageField != null)
            {
                commitMessageField.value = string.Empty;
            }
            UpdateCommitButtonsEnabled();
            RefreshData();
        }

        private void DrawQuickRangeButton(string label, TimeSpan duration)
        {
            if (!GUILayout.Button(label, EditorStyles.miniButton))
            {
                return;
            }

            var now = DateTime.Now;
            var start = now - duration;

            startTimeFilter = start;
            endTimeFilter = now;
            startTimeInput = FormatDateTime(start);
            endTimeInput = FormatDateTime(now);
            startTimeValid = true;
            endTimeValid = true;
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
