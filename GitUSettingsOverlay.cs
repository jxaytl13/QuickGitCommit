#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TLNexus.GitU
{
    /// <summary>
    /// GitU 设置弹窗（Overlay）
    /// 
    /// 设计目标：
    /// 1) 不依赖 UXML / USS，全部用 C# 硬编码创建 UI 和样式；
    /// 2) 提供 Show/Hide 接口，作为一个可复用的“通用设置弹窗”模块；
    /// 3) 文案支持中英文切换（由外部决定当前语言并负责持久化）。
    /// 
    /// 用法（在任意 EditorWindow 的 CreateGUI 中）：
    /// var overlay = new GitUSettingsOverlay(...);
    /// rootVisualElement.Add(overlay.Root);
    /// overlay.Show(); / overlay.Hide();
    /// </summary>
    internal sealed class GitUSettingsOverlay
    {
        // Overlay 根节点：铺满窗口，半透明背景，点击空白区域关闭。
        private readonly VisualElement _overlayRoot;

        // 弹窗主体节点：放置具体内容。
        private readonly VisualElement _dialogRoot;

        // 下面这些 UI 控件需要在语言切换时更新文案。
        private readonly Label _versionLabel;
        private readonly Label _authorLabel;
        private readonly Button _authorLinkButton;
        private readonly Button _documentLinkButton;
        private readonly Label _languageLabel;
        private readonly DropdownField _languageDropdown;
        private readonly Label _featuresTitleLabel;
        private readonly VisualElement _autoSaveBeforeOpenRow;
        private readonly Label _autoSaveBeforeOpenTitleLabel;
        private readonly Label _autoSaveBeforeOpenDescLabel;
        private readonly Toggle _autoSaveBeforeOpenToggle;
        private readonly VisualElement _autoOpenGitClientAfterCommitRow;
        private readonly Label _autoOpenGitClientAfterCommitTitleLabel;
        private readonly Label _autoOpenGitClientAfterCommitDescLabel;
        private readonly Toggle _autoOpenGitClientAfterCommitToggle;
        private readonly VisualElement _autoOpenGitClientAfterCommitCard;
        private readonly VisualElement _gitClientPathRow;
        private readonly Label _gitClientPathTitleLabel;
        private readonly Label _gitClientPathDescLabel;
        private readonly TextField _gitClientPathField;
        private readonly Button _gitClientBrowseButton;
        private readonly Label _configTitleLabel;
        private readonly Label _configPathLabel;
        private readonly Label _toolsTitleLabel;
        private readonly Label _toolIntroductionLabel;

        private string _languageOptionEnglish;
        private string _languageOptionChinese;

        private readonly Func<string> _getConfigPath;
        private readonly Action<bool> _onLanguageChanged;
        private readonly Action<bool> _onAutoSaveBeforeOpenChanged;
        private readonly Action<bool> _onAutoOpenGitClientAfterCommitChanged;
        private readonly Action<string> _onGitClientPathChanged;

        public enum TextKey
        {
            SettingsVersion,
            SettingsAuthor,
            SettingsVisitAuthor,
            SettingsDocumentation,
            SettingsLanguage,
            SettingsFeatures,
            SettingsAutoSaveBeforeOpenTitle,
            SettingsAutoSaveBeforeOpenDesc,
            SettingsAutoOpenGitClientAfterCommitTitle,
            SettingsAutoOpenGitClientAfterCommitDesc,
            SettingsGitClientPathTitle,
            SettingsGitClientPathDesc,
            SettingsBrowse,
            SettingsConfig,
            SettingsStoragePath,
            SettingsTools,
            LanguageOptionEnglish,
            LanguageOptionChinese
        }

        private readonly Func<TextKey, bool, string> _getText;

        private readonly string _aboutVersion;
        private readonly string _aboutAuthor;
        private readonly string _aboutAuthorLink;
        private readonly string _aboutDocumentLinkZh;
        private readonly string _aboutDocumentLinkEn;

        private readonly string _toolIntroductionZh;
        private readonly string _toolIntroductionEn;

        private bool _isChinese;
        private bool _autoSaveBeforeOpen;
        private bool _autoOpenGitClientAfterCommit;
        private string _gitClientPath;

        private readonly List<VisualElement> _contentEnterTargets = new List<VisualElement>();
        private readonly List<IVisualElementScheduledItem> _contentEnterAnimItems = new List<IVisualElementScheduledItem>();

        private const float ContentEnterOffsetY = 6f;
        private const float ContentEnterDurationSeconds = 0.2f;
        private const int ContentEnterStaggerMs = 10;
        private const int MaxContentEnterAnimatedItems = 30;

        private const float LanguageDropdownWidth = 120f;

        private static readonly Color Accent = new Color(57f / 255f, 209f / 255f, 157f / 255f, 1f);

        private const string AboutAuthorLink = "https://jxaytl13.github.io";
        private const string AboutDocumentLinkZh = "https://my.feishu.cn/wiki/IngGwL2hviirYgkDkTzcpLR1n8e?from=from_copylink";
        private const string AboutDocumentLinkEn = "https://my.feishu.cn/wiki/UNSVw7dAZiHSaBkM3encs7ybnne?from=from_copylink";

        /// <summary>
        /// 供外部挂到 EditorWindow.rootVisualElement 上。
        /// </summary>
        public VisualElement Root => _overlayRoot;

        public GitUSettingsOverlay(
            string aboutVersion,
            string aboutAuthor,
            string aboutAuthorLink,
            string aboutDocumentLink,
            string aboutDocumentLinkChinese,
            string aboutDocumentLinkEnglish,
            string toolIntroductionZh,
            string toolIntroductionEn,
            Func<string> getConfigPath,
            bool initialChinese,
            bool initialAutoSaveBeforeOpen,
            bool initialAutoOpenGitClientAfterCommit,
            string initialGitClientPath,
            Action<bool> onLanguageChanged,
            Action<bool> onAutoSaveBeforeOpenChanged,
            Action<bool> onAutoOpenGitClientAfterCommitChanged,
            Action<string> onGitClientPathChanged,
            Func<TextKey, bool, string> getText = null)
        {
            _aboutVersion = aboutVersion;
            _aboutAuthor = aboutAuthor;
            _aboutAuthorLink = string.IsNullOrEmpty(aboutAuthorLink) ? AboutAuthorLink : aboutAuthorLink;
            _aboutDocumentLinkZh = string.IsNullOrEmpty(aboutDocumentLinkChinese)
                ? (string.IsNullOrEmpty(aboutDocumentLink) ? AboutDocumentLinkZh : aboutDocumentLink)
                : aboutDocumentLinkChinese;
            _aboutDocumentLinkEn = string.IsNullOrEmpty(aboutDocumentLinkEnglish)
                ? (string.IsNullOrEmpty(aboutDocumentLink) ? AboutDocumentLinkEn : aboutDocumentLink)
                : aboutDocumentLinkEnglish;
            _toolIntroductionZh = toolIntroductionZh;
            _toolIntroductionEn = toolIntroductionEn;
            _getConfigPath = getConfigPath;
            _isChinese = initialChinese;
            _autoSaveBeforeOpen = initialAutoSaveBeforeOpen;
            _autoOpenGitClientAfterCommit = initialAutoOpenGitClientAfterCommit;
            _gitClientPath = initialGitClientPath ?? string.Empty;
            _onLanguageChanged = onLanguageChanged;
            _onAutoSaveBeforeOpenChanged = onAutoSaveBeforeOpenChanged;
            _onAutoOpenGitClientAfterCommitChanged = onAutoOpenGitClientAfterCommitChanged;
            _onGitClientPathChanged = onGitClientPathChanged;
            _getText = getText ?? GetDefaultText;

            _overlayRoot = new VisualElement();
            _dialogRoot = new VisualElement();

            // -------- Overlay 样式（铺满窗口 + 半透明黑底）--------
            _overlayRoot.style.display = DisplayStyle.None;
            _overlayRoot.style.position = Position.Absolute;
            _overlayRoot.style.left = 0;
            _overlayRoot.style.right = 0;
            _overlayRoot.style.top = 0;
            _overlayRoot.style.bottom = 0;
            _overlayRoot.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            _overlayRoot.style.justifyContent = Justify.Center;
            _overlayRoot.style.alignItems = Align.Center;

            // 点击遮罩层空白区域关闭（点击弹窗内部不关闭）。
            _overlayRoot.RegisterCallback<MouseDownEvent>(OnOverlayMouseDown);

            // -------- Dialog 样式（固定宽高 + 深色面板）--------
            _dialogRoot.style.flexDirection = FlexDirection.Column;
            _dialogRoot.style.width = 520;
            _dialogRoot.style.height = 620;
            _dialogRoot.style.paddingLeft = 0;
            _dialogRoot.style.paddingRight = 0;
            _dialogRoot.style.paddingTop = 0;
            _dialogRoot.style.paddingBottom = 0;
            _dialogRoot.style.backgroundColor = new Color(20f / 255f, 20f / 255f, 20f / 255f, 1f);
            _dialogRoot.style.borderTopWidth = 1;
            _dialogRoot.style.borderRightWidth = 1;
            _dialogRoot.style.borderBottomWidth = 1;
            _dialogRoot.style.borderLeftWidth = 1;
            var dialogBorder = new Color(40f / 255f, 40f / 255f, 40f / 255f, 1f);
            _dialogRoot.style.borderTopColor = dialogBorder;
            _dialogRoot.style.borderRightColor = dialogBorder;
            _dialogRoot.style.borderBottomColor = dialogBorder;
            _dialogRoot.style.borderLeftColor = dialogBorder;
            _dialogRoot.style.borderTopLeftRadius = 4;
            _dialogRoot.style.borderTopRightRadius = 4;
            _dialogRoot.style.borderBottomLeftRadius = 4;
            _dialogRoot.style.borderBottomRightRadius = 4;

            // 版本/作者
            _versionLabel = CreateInfoLabel();
            _authorLabel = CreateInfoLabel();

            // 链接按钮
            _authorLinkButton = CreateLinkButton();
            if (string.IsNullOrEmpty(_aboutAuthorLink))
            {
                _authorLinkButton.SetEnabled(false);
                _authorLinkButton.tooltip = string.Empty;
            }
            else
            {
                _authorLinkButton.SetEnabled(true);
                _authorLinkButton.tooltip = string.Empty;
                _authorLinkButton.clicked += () => Application.OpenURL(_aboutAuthorLink);
            }

            _documentLinkButton = CreateLinkButton();
            if (string.IsNullOrEmpty(_aboutDocumentLinkZh) && string.IsNullOrEmpty(_aboutDocumentLinkEn))
            {
                _documentLinkButton.SetEnabled(false);
                _documentLinkButton.tooltip = string.Empty;
            }
            else
            {
                _documentLinkButton.SetEnabled(true);
                _documentLinkButton.tooltip = string.Empty;
                _documentLinkButton.clicked += () =>
                {
                    var link = _isChinese ? _aboutDocumentLinkZh : _aboutDocumentLinkEn;
                    if (string.IsNullOrEmpty(link))
                    {
                        link = _isChinese ? _aboutDocumentLinkEn : _aboutDocumentLinkZh;
                    }

                    if (!string.IsNullOrEmpty(link))
                    {
                        Application.OpenURL(link);
                    }
                };
            }

            // 语言行
            var languageRow = new VisualElement();
            languageRow.style.flexDirection = FlexDirection.Row;
            languageRow.style.justifyContent = Justify.FlexStart;
            languageRow.style.alignItems = Align.Center;
            languageRow.style.height = 30;
            languageRow.style.marginTop = 20;
            languageRow.style.marginBottom = 10;
            languageRow.style.width = Length.Percent(100);

            _languageLabel = new Label();
            _languageLabel.style.flexGrow = 1;
            _languageLabel.style.flexShrink = 1;
            _languageLabel.style.minWidth = 0;
            _languageLabel.style.marginRight = 10;
            _languageLabel.style.height = 30;
            _languageLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            // 这个 spacer 用来占据中间剩余空间，把 Dropdown 顶到最右边。
            var languageSpacer = new VisualElement();
            languageSpacer.style.flexGrow = 1;
            languageSpacer.style.flexShrink = 1;
            languageSpacer.style.minWidth = 0;

            _languageDropdown = new DropdownField();
            _languageDropdown.style.flexGrow = 0;
            _languageDropdown.style.flexShrink = 0;
            _languageDropdown.style.width = LanguageDropdownWidth;
            _languageDropdown.style.minWidth = LanguageDropdownWidth;
            _languageDropdown.style.maxWidth = LanguageDropdownWidth;
            ConfigureDropdownField(_languageDropdown);
            _languageDropdown.RegisterValueChangedCallback(evt =>
            {
                // DropdownField 的值本身就是字符串，这里用“是否中文”做统一出口。
                var chinese = evt.newValue == _languageOptionChinese;
                if (_isChinese == chinese)
                {
                    return;
                }

                _isChinese = chinese;
                _onLanguageChanged?.Invoke(_isChinese);
                ApplyLanguage(_isChinese);
            });

            languageRow.Add(_languageLabel);
            languageRow.Add(languageSpacer);
            languageRow.Add(_languageDropdown);

            // ScrollView：整个设置窗口内容都可滚动（取消“仅功能部分滚动”的设计）。
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;

            var scrollContent = new VisualElement();
            scrollContent.style.flexDirection = FlexDirection.Column;
            scrollContent.style.alignItems = Align.Stretch;
            scrollContent.style.justifyContent = Justify.FlexStart;
            scrollContent.style.width = Length.Percent(100);
            scrollContent.style.minWidth = 0;
            scrollContent.style.paddingLeft = 20;
            scrollContent.style.paddingRight = 20;
            scrollContent.style.paddingTop = 20;
            scrollContent.style.paddingBottom = 20;
            scroll.Add(scrollContent);

            _featuresTitleLabel = CreateSectionTitleLabel();
            _featuresTitleLabel.style.marginTop = 14;
            _featuresTitleLabel.style.marginBottom = 6;

            _autoSaveBeforeOpenDescLabel = new Label();
            StyleFeatureDescriptionLabel(_autoSaveBeforeOpenDescLabel);

            _autoSaveBeforeOpenRow = CreateToggleRow(out _autoSaveBeforeOpenTitleLabel, out _autoSaveBeforeOpenToggle);
            ApplyToggleVisualStyle(_autoSaveBeforeOpenToggle);
            ConfigureFeatureToggleRow(_autoSaveBeforeOpenRow, _autoSaveBeforeOpenTitleLabel, _autoSaveBeforeOpenDescLabel, _autoSaveBeforeOpenToggle);
            _autoSaveBeforeOpenRow.style.marginTop = 6;
            _autoSaveBeforeOpenRow.style.marginBottom = 8;
            _autoSaveBeforeOpenRow.style.backgroundColor = new Color(28f / 255f, 28f / 255f, 28f / 255f, 1f);
            _autoSaveBeforeOpenRow.style.borderTopWidth = 1;
            _autoSaveBeforeOpenRow.style.borderRightWidth = 1;
            _autoSaveBeforeOpenRow.style.borderBottomWidth = 1;
            _autoSaveBeforeOpenRow.style.borderLeftWidth = 1;
            var cardBorder = new Color(30f / 255f, 30f / 255f, 30f / 255f, 1f);
            _autoSaveBeforeOpenRow.style.borderTopColor = cardBorder;
            _autoSaveBeforeOpenRow.style.borderRightColor = cardBorder;
            _autoSaveBeforeOpenRow.style.borderBottomColor = cardBorder;
            _autoSaveBeforeOpenRow.style.borderLeftColor = cardBorder;
            _autoSaveBeforeOpenRow.style.borderTopLeftRadius = 8;
            _autoSaveBeforeOpenRow.style.borderTopRightRadius = 8;
            _autoSaveBeforeOpenRow.style.borderBottomLeftRadius = 8;
            _autoSaveBeforeOpenRow.style.borderBottomRightRadius = 8;
            _autoSaveBeforeOpenToggle.SetValueWithoutNotify(_autoSaveBeforeOpen);
            _autoSaveBeforeOpenToggle.RegisterValueChangedCallback(evt =>
            {
                if (_autoSaveBeforeOpen == evt.newValue)
                {
                    return;
                }

                _autoSaveBeforeOpen = evt.newValue;
                _onAutoSaveBeforeOpenChanged?.Invoke(_autoSaveBeforeOpen);
            });

            _autoOpenGitClientAfterCommitDescLabel = new Label();
            StyleFeatureDescriptionLabel(_autoOpenGitClientAfterCommitDescLabel);

            _autoOpenGitClientAfterCommitRow = CreateToggleRow(out _autoOpenGitClientAfterCommitTitleLabel, out _autoOpenGitClientAfterCommitToggle);
            ApplyToggleVisualStyle(_autoOpenGitClientAfterCommitToggle);
            ConfigureFeatureToggleRow(_autoOpenGitClientAfterCommitRow, _autoOpenGitClientAfterCommitTitleLabel, _autoOpenGitClientAfterCommitDescLabel, _autoOpenGitClientAfterCommitToggle);
            _autoOpenGitClientAfterCommitToggle.SetValueWithoutNotify(_autoOpenGitClientAfterCommit);
            _autoOpenGitClientAfterCommitToggle.RegisterValueChangedCallback(evt =>
            {
                if (_autoOpenGitClientAfterCommit == evt.newValue)
                {
                    return;
                }

                _autoOpenGitClientAfterCommit = evt.newValue;
                _onAutoOpenGitClientAfterCommitChanged?.Invoke(_autoOpenGitClientAfterCommit);
                UpdateGitClientPathControlsEnabled();
            });

            _gitClientPathTitleLabel = new Label();
            _gitClientPathDescLabel = new Label();
            StyleFeatureTitleLabel(_gitClientPathTitleLabel);
            StyleFeatureDescriptionLabel(_gitClientPathDescLabel);

            _gitClientPathRow = CreateFeatureControlRowStacked(
                _gitClientPathTitleLabel,
                _gitClientPathDescLabel,
                CreateGitClientPathControl(out _gitClientPathField, out _gitClientBrowseButton));
            _gitClientPathField.SetValueWithoutNotify(_gitClientPath);
            _gitClientPathField.RegisterValueChangedCallback(evt =>
            {
                if (_gitClientPath == (evt.newValue ?? string.Empty))
                {
                    return;
                }

                _gitClientPath = evt.newValue ?? string.Empty;
                _onGitClientPathChanged?.Invoke(_gitClientPath);
            });
            _gitClientBrowseButton.clicked += () =>
            {
                var title = _isChinese ? "选择 Git 客户端程序" : "Select Git Client Executable";
                var current = _gitClientPath ?? string.Empty;
                var startDir = string.Empty;
                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        startDir = Directory.Exists(current) ? current : Path.GetDirectoryName(current);
                    }
                    catch
                    {
                        startDir = string.Empty;
                    }
                }

                var chosen = EditorUtility.OpenFilePanel(title, startDir ?? string.Empty, "exe");
                if (string.IsNullOrEmpty(chosen))
                {
                    return;
                }

                _gitClientPath = chosen;
                _gitClientPathField.SetValueWithoutNotify(_gitClientPath);
                _onGitClientPathChanged?.Invoke(_gitClientPath);
            };

            _autoOpenGitClientAfterCommitCard = new VisualElement();
            _autoOpenGitClientAfterCommitCard.style.flexDirection = FlexDirection.Column;
            _autoOpenGitClientAfterCommitCard.style.alignItems = Align.Stretch;
            _autoOpenGitClientAfterCommitCard.style.justifyContent = Justify.FlexStart;
            ApplyCardStyle(_autoOpenGitClientAfterCommitCard);
            _autoOpenGitClientAfterCommitCard.Add(_autoOpenGitClientAfterCommitRow);
            _autoOpenGitClientAfterCommitCard.Add(_gitClientPathRow);

            _configTitleLabel = CreateSectionTitleLabel();
            _configPathLabel = CreateToolTextLabel();
            _configPathLabel.style.whiteSpace = WhiteSpace.Normal;
            _configTitleLabel.style.display = DisplayStyle.None;
            _configPathLabel.style.display = DisplayStyle.None;

            _toolsTitleLabel = CreateSectionTitleLabel();
            _toolIntroductionLabel = CreateToolTextLabel();
            _toolIntroductionLabel.style.whiteSpace = WhiteSpace.Normal;
            _toolIntroductionLabel.style.unityTextAlign = TextAnchor.UpperLeft;

            scrollContent.Add(_versionLabel);
            _contentEnterTargets.Add(_versionLabel);
            scrollContent.Add(_authorLabel);
            _contentEnterTargets.Add(_authorLabel);
            scrollContent.Add(_authorLinkButton);
            _contentEnterTargets.Add(_authorLinkButton);
            scrollContent.Add(_documentLinkButton);
            _contentEnterTargets.Add(_documentLinkButton);
            scrollContent.Add(languageRow);
            _contentEnterTargets.Add(languageRow);

            scrollContent.Add(_featuresTitleLabel);
            _contentEnterTargets.Add(_featuresTitleLabel);
            scrollContent.Add(_autoSaveBeforeOpenRow);
            _contentEnterTargets.Add(_autoSaveBeforeOpenRow);
            scrollContent.Add(_autoOpenGitClientAfterCommitCard);
            _contentEnterTargets.Add(_autoOpenGitClientAfterCommitCard);

            scrollContent.Add(_configTitleLabel);
            _contentEnterTargets.Add(_configTitleLabel);
            scrollContent.Add(_configPathLabel);
            _contentEnterTargets.Add(_configPathLabel);
            scrollContent.Add(_toolsTitleLabel);
            _contentEnterTargets.Add(_toolsTitleLabel);
            scrollContent.Add(_toolIntroductionLabel);
            _contentEnterTargets.Add(_toolIntroductionLabel);

            // 组装
            _dialogRoot.Add(scroll);

            _overlayRoot.Add(_dialogRoot);

            // 初始化文案
            ApplyLanguage(_isChinese);
            UpdateGitClientPathControlsEnabled();
        }

        /// <summary>
        /// 显示弹窗。
        /// </summary>
        public void Show()
        {
            _overlayRoot.style.display = DisplayStyle.Flex;
            _overlayRoot.Focus();
            StartContentEnterAnimation();
        }

        /// <summary>
        /// 隐藏弹窗。
        /// </summary>
        public void Hide()
        {
            StopContentEnterAnimation();
            _overlayRoot.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 外部可在加载配置后调用，以更新弹窗语言（不会触发 LanguageChanged 回调）。
        /// </summary>
        public void SetLanguageWithoutNotify(bool chinese)
        {
            _isChinese = chinese;
            ApplyLanguage(_isChinese);
        }

        private void OnOverlayMouseDown(MouseDownEvent evt)
        {
            // 如果点击到了弹窗外部，则关闭。
            var localPos = _dialogRoot.WorldToLocal(evt.mousePosition);
            if (!_dialogRoot.contentRect.Contains(localPos))
            {
                Hide();
                evt.StopPropagation();
            }
        }

        private void StartContentEnterAnimation()
        {
            StopContentEnterAnimation(resetStyles: false);

            var count = Mathf.Min(_contentEnterTargets.Count, MaxContentEnterAnimatedItems);
            for (var i = 0; i < count; i++)
            {
                var element = _contentEnterTargets[i];
                if (element == null)
                {
                    continue;
                }

                element.style.opacity = 0f;
                element.style.top = ContentEnterOffsetY;

                var delayMs = i * ContentEnterStaggerMs;
                var startTime = EditorApplication.timeSinceStartup + delayMs / 1000.0;

                IVisualElementScheduledItem scheduled = null;
                scheduled = element.schedule.Execute(() =>
                {
                    if (element.panel == null || _overlayRoot.resolvedStyle.display == DisplayStyle.None)
                    {
                        scheduled?.Pause();
                        return;
                    }

                    var t = (float)((EditorApplication.timeSinceStartup - startTime) / ContentEnterDurationSeconds);
                    if (t <= 0f)
                    {
                        return;
                    }

                    t = Mathf.Clamp01(t);
                    var eased = 1f - Mathf.Pow(1f - t, 3f);

                    element.style.opacity = eased;
                    element.style.top = Mathf.Lerp(ContentEnterOffsetY, 0f, eased);

                    if (t >= 1f)
                    {
                        element.style.opacity = 1f;
                        element.style.top = 0f;
                        scheduled?.Pause();
                    }
                }).StartingIn(delayMs).Every(16);

                _contentEnterAnimItems.Add(scheduled);
            }
        }

        private void StopContentEnterAnimation(bool resetStyles = true)
        {
            for (var i = 0; i < _contentEnterAnimItems.Count; i++)
            {
                _contentEnterAnimItems[i]?.Pause();
            }
            _contentEnterAnimItems.Clear();

            if (!resetStyles)
            {
                return;
            }

            for (var i = 0; i < _contentEnterTargets.Count; i++)
            {
                var element = _contentEnterTargets[i];
                if (element == null)
                {
                    continue;
                }

                element.style.opacity = 1f;
                element.style.top = 0f;
            }
        }

        private void ApplyLanguage(bool chinese)
        {
            var configPath = _getConfigPath != null ? _getConfigPath() : string.Empty;

            if (chinese)
            {
                _versionLabel.text = string.Format(_getText(TextKey.SettingsVersion, true), _aboutVersion);
                _authorLabel.text = string.Format(_getText(TextKey.SettingsAuthor, true), _aboutAuthor);
                _authorLinkButton.text = _getText(TextKey.SettingsVisitAuthor, true);
                _documentLinkButton.text = _getText(TextKey.SettingsDocumentation, true);
                _languageLabel.text = _getText(TextKey.SettingsLanguage, true);
                _featuresTitleLabel.text = _getText(TextKey.SettingsFeatures, true);
                _autoSaveBeforeOpenTitleLabel.text = _getText(TextKey.SettingsAutoSaveBeforeOpenTitle, true);
                _autoSaveBeforeOpenDescLabel.text = _getText(TextKey.SettingsAutoSaveBeforeOpenDesc, true);
                _autoOpenGitClientAfterCommitTitleLabel.text = _getText(TextKey.SettingsAutoOpenGitClientAfterCommitTitle, true);
                _autoOpenGitClientAfterCommitDescLabel.text = _getText(TextKey.SettingsAutoOpenGitClientAfterCommitDesc, true);
                _gitClientPathTitleLabel.text = _getText(TextKey.SettingsGitClientPathTitle, true);
                _gitClientPathDescLabel.text = _getText(TextKey.SettingsGitClientPathDesc, true);
                _gitClientBrowseButton.text = _getText(TextKey.SettingsBrowse, true);
                _configTitleLabel.text = _getText(TextKey.SettingsConfig, true);
                _configPathLabel.text = string.Format(_getText(TextKey.SettingsStoragePath, true), configPath);
                _toolsTitleLabel.text = _getText(TextKey.SettingsTools, true);
                _toolIntroductionLabel.text = _toolIntroductionZh;
            }
            else
            {
                _versionLabel.text = string.Format(_getText(TextKey.SettingsVersion, false), _aboutVersion);
                _authorLabel.text = string.Format(_getText(TextKey.SettingsAuthor, false), _aboutAuthor);
                _authorLinkButton.text = _getText(TextKey.SettingsVisitAuthor, false);
                _documentLinkButton.text = _getText(TextKey.SettingsDocumentation, false);
                _languageLabel.text = _getText(TextKey.SettingsLanguage, false);
                _featuresTitleLabel.text = _getText(TextKey.SettingsFeatures, false);
                _autoSaveBeforeOpenTitleLabel.text = _getText(TextKey.SettingsAutoSaveBeforeOpenTitle, false);
                _autoSaveBeforeOpenDescLabel.text = _getText(TextKey.SettingsAutoSaveBeforeOpenDesc, false);
                _autoOpenGitClientAfterCommitTitleLabel.text = _getText(TextKey.SettingsAutoOpenGitClientAfterCommitTitle, false);
                _autoOpenGitClientAfterCommitDescLabel.text = _getText(TextKey.SettingsAutoOpenGitClientAfterCommitDesc, false);
                _gitClientPathTitleLabel.text = _getText(TextKey.SettingsGitClientPathTitle, false);
                _gitClientPathDescLabel.text = _getText(TextKey.SettingsGitClientPathDesc, false);
                _gitClientBrowseButton.text = _getText(TextKey.SettingsBrowse, false);
                _configTitleLabel.text = _getText(TextKey.SettingsConfig, false);
                _configPathLabel.text = string.Format(_getText(TextKey.SettingsStoragePath, false), configPath);
                _toolsTitleLabel.text = _getText(TextKey.SettingsTools, false);
                _toolIntroductionLabel.text = _toolIntroductionEn;
            }

            _languageOptionEnglish = _getText(TextKey.LanguageOptionEnglish, chinese);
            _languageOptionChinese = _getText(TextKey.LanguageOptionChinese, chinese);
            _languageDropdown.choices = new System.Collections.Generic.List<string> { _languageOptionEnglish, _languageOptionChinese };
            _languageDropdown.SetValueWithoutNotify(chinese ? _languageOptionChinese : _languageOptionEnglish);
        }

        private static string GetDefaultText(TextKey key, bool chinese)
        {
            if (chinese)
            {
                return key switch
                {
                    TextKey.SettingsVersion => "版本: {0}",
                    TextKey.SettingsAuthor => "作者: {0}",
                    TextKey.SettingsVisitAuthor => "访问作者主页",
                    TextKey.SettingsDocumentation => "文档",
                    TextKey.SettingsLanguage => "语言:",
                    TextKey.SettingsFeatures => "功能",
                    TextKey.SettingsAutoSaveBeforeOpenTitle => "打开 GitU 前自动保存（Ctrl+S）",
                    TextKey.SettingsAutoSaveBeforeOpenDesc => "开启后：打开 GitU 时会自动执行一次保存（Save Assets + Save Open Scenes），避免未写盘导致“变更识别不到”。",
                    TextKey.SettingsAutoOpenGitClientAfterCommitTitle => "提交后打开 Git 客户端",
                    TextKey.SettingsAutoOpenGitClientAfterCommitDesc => "开启后：提交到本地 / 提交并推送完成时，提示框点击“确定”会自动打开外部 Git 软件。",
                    TextKey.SettingsGitClientPathTitle => "Git 客户端路径",
                    TextKey.SettingsGitClientPathDesc => "请填写外部 Git 软件的可执行文件路径（例如 UGit / GitHub Desktop / Sourcetree / TortoiseGit 等）。",
                    TextKey.SettingsBrowse => "浏览",
                    TextKey.SettingsConfig => "配置",
                    TextKey.SettingsStoragePath => "存储路径: {0}",
                    TextKey.SettingsTools => "工具说明",
                    TextKey.LanguageOptionEnglish => "English",
                    TextKey.LanguageOptionChinese => "中文",
                    _ => string.Empty
                };
            }

            return key switch
            {
                TextKey.SettingsVersion => "Version: {0}",
                TextKey.SettingsAuthor => "Author: {0}",
                TextKey.SettingsVisitAuthor => "Visit Author's Homepage",
                TextKey.SettingsDocumentation => "Documentation",
                TextKey.SettingsLanguage => "Language:",
                TextKey.SettingsFeatures => "Features",
                TextKey.SettingsAutoSaveBeforeOpenTitle => "Auto save before opening GitU (Ctrl+S)",
                TextKey.SettingsAutoSaveBeforeOpenDesc => "When enabled: GitU will auto-save once on open (Save Assets + Save Open Scenes) to avoid missing changes not written to disk.",
                TextKey.SettingsAutoOpenGitClientAfterCommitTitle => "Open Git client after commit",
                TextKey.SettingsAutoOpenGitClientAfterCommitDesc => "When enabled: after Commit / Commit & Push, clicking OK in the result dialog will open your external Git client.",
                TextKey.SettingsGitClientPathTitle => "Git client path",
                TextKey.SettingsGitClientPathDesc => "Set the executable path of your external Git client (e.g. UGit / GitHub Desktop / Sourcetree / TortoiseGit).",
                TextKey.SettingsBrowse => "Browse",
                TextKey.SettingsConfig => "Configuration",
                TextKey.SettingsStoragePath => "Storage: {0}",
                TextKey.SettingsTools => "About",
                TextKey.LanguageOptionEnglish => "English",
                TextKey.LanguageOptionChinese => "Chinese",
                _ => string.Empty
            };
        }

        private static Label CreateInfoLabel()
        {
            var label = new Label();
            label.style.height = 20;
            label.style.flexShrink = 0;
            label.style.flexGrow = 0;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginBottom = 10;
            label.style.color = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 12;
            return label;
        }

        private static Label CreateSectionTitleLabel()
        {
            var label = new Label();
            label.style.marginTop = 20;
            label.style.marginBottom = 10;
            label.style.color = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label CreateToolTextLabel()
        {
            var label = new Label();
            label.style.color = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
            label.style.fontSize = 12;
            label.style.marginTop = 10;
            return label;
        }

        private static void StyleFeatureTitleLabel(Label label)
        {
            if (label == null)
            {
                return;
            }

            label.style.height = StyleKeyword.Auto;
            label.style.minWidth = 0;
            label.style.flexShrink = 1;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.marginLeft = 0;
            label.style.paddingLeft = 0;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new Color(1f, 1f, 1f, 0.92f);
            label.style.fontSize = 13;
        }

        private static void StyleFeatureDescriptionLabel(Label label)
        {
            if (label == null)
            {
                return;
            }

            label.style.marginTop = 2;
            label.style.marginBottom = 0;
            label.style.color = new Color(1f, 1f, 1f, 0.45f);
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
        }

        private static void ApplyCardStyle(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            element.style.marginBottom = 8;
            element.style.backgroundColor = new Color(28f / 255f, 28f / 255f, 28f / 255f, 1f);
            element.style.borderTopWidth = 1;
            element.style.borderRightWidth = 1;
            element.style.borderBottomWidth = 1;
            element.style.borderLeftWidth = 1;
            var border = new Color(30f / 255f, 30f / 255f, 30f / 255f, 1f);
            element.style.borderTopColor = border;
            element.style.borderRightColor = border;
            element.style.borderBottomColor = border;
            element.style.borderLeftColor = border;
            element.style.borderTopLeftRadius = 8;
            element.style.borderTopRightRadius = 8;
            element.style.borderBottomLeftRadius = 8;
            element.style.borderBottomRightRadius = 8;
        }

        private static VisualElement CreateFeatureControlRow(Label titleLabel, Label descLabel, VisualElement rightControl)
        {
            var row = new VisualElement();

            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.height = StyleKeyword.Auto;
            row.style.minHeight = 46;
            row.style.marginTop = 0;
            row.style.paddingLeft = 12;
            row.style.paddingRight = 12;
            row.style.paddingTop = 8;
            row.style.paddingBottom = 8;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Column;
            left.style.alignItems = Align.FlexStart;
            left.style.justifyContent = Justify.FlexStart;
            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.style.minWidth = 0;
            left.style.marginRight = 12;

            titleLabel.style.alignSelf = Align.FlexStart;
            descLabel.style.alignSelf = Align.FlexStart;

            left.Add(titleLabel);
            left.Add(descLabel);

            if (rightControl != null)
            {
                rightControl.style.alignSelf = Align.Center;
            }

            row.Add(left);
            if (rightControl != null)
            {
                row.Add(rightControl);
            }

            return row;
        }

        private static VisualElement CreateFeatureControlRowStacked(Label titleLabel, Label descLabel, VisualElement control)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.alignItems = Align.Stretch;
            container.style.justifyContent = Justify.FlexStart;
            container.style.height = StyleKeyword.Auto;
            container.style.minHeight = 46;
            container.style.marginTop = 0;
            container.style.paddingLeft = 12;
            container.style.paddingRight = 12;
            container.style.paddingTop = 8;
            container.style.paddingBottom = 10;

            titleLabel.style.alignSelf = Align.FlexStart;
            descLabel.style.alignSelf = Align.FlexStart;

            container.Add(titleLabel);
            container.Add(descLabel);

            if (control != null)
            {
                control.style.marginTop = 8;
                control.style.alignSelf = Align.Stretch;
                container.Add(control);
            }

            return container;
        }

        private static VisualElement CreateGitClientPathControl(out TextField pathField, out Button browseButton)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.justifyContent = Justify.FlexEnd;
            container.style.flexGrow = 1;
            container.style.flexShrink = 1;
            container.style.minWidth = 0;

            pathField = new TextField();
            pathField.style.flexGrow = 1;
            pathField.style.flexShrink = 1;
            pathField.style.minWidth = 0;
            pathField.style.height = 24;
            pathField.style.marginRight = 6;
            pathField.style.paddingLeft = 6;
            pathField.style.paddingRight = 6;
            pathField.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            var border = new Color(1f, 1f, 1f, 0.12f);
            pathField.style.borderTopWidth = 1;
            pathField.style.borderRightWidth = 1;
            pathField.style.borderBottomWidth = 1;
            pathField.style.borderLeftWidth = 1;
            pathField.style.borderTopColor = border;
            pathField.style.borderRightColor = border;
            pathField.style.borderBottomColor = border;
            pathField.style.borderLeftColor = border;
            pathField.style.borderTopLeftRadius = 4;
            pathField.style.borderTopRightRadius = 4;
            pathField.style.borderBottomLeftRadius = 4;
            pathField.style.borderBottomRightRadius = 4;
            pathField.style.unityTextAlign = TextAnchor.MiddleLeft;

            browseButton = new Button();
            browseButton.style.height = 24;
            browseButton.style.width = 64;
            browseButton.style.paddingLeft = 6;
            browseButton.style.paddingRight = 6;
            browseButton.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            browseButton.style.borderTopWidth = 1;
            browseButton.style.borderRightWidth = 1;
            browseButton.style.borderBottomWidth = 1;
            browseButton.style.borderLeftWidth = 1;
            browseButton.style.borderTopColor = border;
            browseButton.style.borderRightColor = border;
            browseButton.style.borderBottomColor = border;
            browseButton.style.borderLeftColor = border;
            browseButton.style.borderTopLeftRadius = 4;
            browseButton.style.borderTopRightRadius = 4;
            browseButton.style.borderBottomLeftRadius = 4;
            browseButton.style.borderBottomRightRadius = 4;

            container.Add(pathField);
            container.Add(browseButton);

            return container;
        }

        private void UpdateGitClientPathControlsEnabled()
        {
            var enabled = _autoOpenGitClientAfterCommit;
            _gitClientPathField?.SetEnabled(enabled);
            _gitClientBrowseButton?.SetEnabled(enabled);
            if (_gitClientPathRow != null)
            {
                _gitClientPathRow.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static void ConfigureFeatureToggleRow(VisualElement row, Label titleLabel, Label descLabel, Toggle toggle)
        {
            if (row == null || titleLabel == null || descLabel == null || toggle == null)
            {
                return;
            }

            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.height = StyleKeyword.Auto;
            row.style.minHeight = 46;
            row.style.marginTop = 0;
            row.style.paddingLeft = 12;
            row.style.paddingRight = 12;
            row.style.paddingTop = 8;
            row.style.paddingBottom = 8;

            titleLabel.style.height = StyleKeyword.Auto;
            titleLabel.style.minWidth = 0;
            titleLabel.style.flexShrink = 1;
            titleLabel.style.marginTop = 0;
            titleLabel.style.marginBottom = 0;
            titleLabel.style.marginLeft = 0;
            titleLabel.style.paddingLeft = 0;
            titleLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.color = new Color(1f, 1f, 1f, 0.92f);
            titleLabel.style.fontSize = 13;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Column;
            left.style.alignItems = Align.FlexStart;
            left.style.justifyContent = Justify.FlexStart;
            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.style.minWidth = 0;
            left.style.marginRight = 12;

            descLabel.style.marginLeft = 0;
            descLabel.style.paddingLeft = 0;
            descLabel.style.alignSelf = Align.FlexStart;
            titleLabel.style.alignSelf = Align.FlexStart;

            left.Add(titleLabel);
            left.Add(descLabel);

            toggle.style.marginLeft = 0;
            toggle.style.marginRight = 0;
            toggle.style.alignSelf = Align.Center;

            row.Clear();
            row.Add(left);
            row.Add(toggle);
        }

        private static VisualElement CreateToggleRow(out Label label, out Toggle toggle)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.FlexStart;
            row.style.height = 26;
            row.style.marginTop = 6;
            row.style.overflow = Overflow.Visible; // 防止裁剪 toggle

            label = new Label();
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.minWidth = 0;
            label.style.height = 26;
            label.style.alignSelf = Align.Center;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.color = new Color(180f / 255f, 180f / 255f, 180f / 255f, 1f);
            label.style.fontSize = 12;

            var toggleLocal = new Toggle();
            toggleLocal.text = string.Empty;
            toggleLocal.style.flexGrow = 0;
            toggleLocal.style.flexShrink = 0;
            toggleLocal.style.marginLeft = 8;
            toggleLocal.style.marginRight = 0; // 右边距防止裁剪
            toggleLocal.style.alignSelf = Align.Center;
            toggleLocal.style.marginTop = 0;
            toggleLocal.style.marginBottom = 0;
            toggleLocal.style.overflow = Overflow.Visible;
            toggle = toggleLocal;

            row.Add(label);
            row.Add(toggleLocal);
            return row;
        }

        private sealed class ToggleStyleState
        {
            public Action apply;
        }

        private static void ApplyToggleVisualStyle(Toggle toggle)
        {
            if (toggle == null)
            {
                return;
            }

            var input = toggle.Q<VisualElement>(className: "unity-toggle__input") ?? toggle.Q<VisualElement>("unity-toggle__input");
            var checkmark = toggle.Q<VisualElement>(className: "unity-toggle__checkmark") ?? toggle.Q<VisualElement>("unity-toggle__checkmark");
            if (input == null || checkmark == null)
            {
                return;
            }

            // Material Design (Android) switch style (copied from HierarchyCube settings window).
            const float trackWidth = 28f;
            const float trackHeight = 14f;
            const float thumbSize = 18f;
            const float thumbOverflow = (thumbSize - trackHeight) * 0.5f;

            toggle.style.width = trackWidth + thumbOverflow * 2;
            toggle.style.height = thumbSize;
            toggle.style.overflow = Overflow.Visible;

            input.style.marginLeft = 0;
            input.style.marginRight = 0;
            input.style.alignSelf = Align.Center;
            input.style.position = Position.Relative;
            input.style.overflow = Overflow.Visible;

            input.style.width = trackWidth;
            input.style.height = trackHeight;
            input.style.minWidth = trackWidth;

            input.style.borderTopWidth = 0;
            input.style.borderRightWidth = 0;
            input.style.borderBottomWidth = 0;
            input.style.borderLeftWidth = 0;
            input.style.borderTopLeftRadius = trackHeight * 0.5f;
            input.style.borderTopRightRadius = trackHeight * 0.5f;
            input.style.borderBottomLeftRadius = trackHeight * 0.5f;
            input.style.borderBottomRightRadius = trackHeight * 0.5f;

            checkmark.style.alignSelf = Align.Center;
            checkmark.style.position = Position.Absolute;
            checkmark.style.top = -thumbOverflow;
            checkmark.style.width = thumbSize;
            checkmark.style.height = thumbSize;
            checkmark.style.borderTopLeftRadius = thumbSize * 0.5f;
            checkmark.style.borderTopRightRadius = thumbSize * 0.5f;
            checkmark.style.borderBottomLeftRadius = thumbSize * 0.5f;
            checkmark.style.borderBottomRightRadius = thumbSize * 0.5f;
            checkmark.style.unityBackgroundImageTintColor = new Color(0f, 0f, 0f, 0f);
            checkmark.style.backgroundImage = new StyleBackground((Texture2D)null);

            checkmark.style.borderTopWidth = 1;
            checkmark.style.borderRightWidth = 1;
            checkmark.style.borderBottomWidth = 1;
            checkmark.style.borderLeftWidth = 1;

            var trackOff = new Color(1f, 1f, 1f, 0.16f);
            var trackOn = new Color(Accent.r, Accent.g, Accent.b, 0.45f);
            var trackOffDisabled = new Color(1f, 1f, 1f, 0.08f);
            var trackOnDisabled = new Color(Accent.r, Accent.g, Accent.b, 0.20f);

            var thumbOff = new Color(0.95f, 0.95f, 0.95f, 0.92f);
            var thumbOn = Accent;
            var thumbDisabled = new Color(0.8f, 0.8f, 0.8f, 0.55f);

            var thumbBorder = new Color(0f, 0f, 0f, 0.25f);
            var thumbBorderOn = new Color(0f, 0f, 0f, 0f);

            bool hovered = false;

            void Apply()
            {
                bool enabled = toggle.enabledInHierarchy;
                bool checkedOn = toggle.value;

                float hoverBoost = hovered && enabled ? 0.06f : 0f;

                if (!enabled)
                {
                    input.style.backgroundColor = checkedOn ? trackOnDisabled : trackOffDisabled;
                    checkmark.style.backgroundColor = thumbDisabled;
                    checkmark.style.borderTopColor = thumbBorder;
                    checkmark.style.borderRightColor = thumbBorder;
                    checkmark.style.borderBottomColor = thumbBorder;
                    checkmark.style.borderLeftColor = thumbBorder;
                }
                else
                {
                    input.style.backgroundColor = checkedOn
                        ? new Color(trackOn.r, trackOn.g, trackOn.b, Mathf.Clamp01(trackOn.a + hoverBoost))
                        : new Color(trackOff.r, trackOff.g, trackOff.b, Mathf.Clamp01(trackOff.a + hoverBoost));

                    checkmark.style.backgroundColor = checkedOn ? thumbOn : thumbOff;
                    var border = checkedOn ? thumbBorderOn : thumbBorder;
                    checkmark.style.borderTopColor = border;
                    checkmark.style.borderRightColor = border;
                    checkmark.style.borderBottomColor = border;
                    checkmark.style.borderLeftColor = border;
                }

                checkmark.style.left = checkedOn ? (trackWidth - thumbSize + thumbOverflow + 2) : 0;
                checkmark.style.opacity = 1f;
            }

            if (toggle.userData is ToggleStyleState state)
            {
                state.apply = Apply;
            }
            else
            {
                toggle.userData = new ToggleStyleState { apply = Apply };
            }

            Apply();

            toggle.RegisterValueChangedCallback(_ => Apply());
            toggle.RegisterCallback<PointerEnterEvent>(_ =>
            {
                hovered = true;
                Apply();
            });
            toggle.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hovered = false;
                Apply();
            });
            toggle.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                toggle.schedule.Execute(Apply);
            });
        }

        private static void ConfigureDropdownField(DropdownField dropdown)
        {
            var accentBorder = new Color(57f / 255f, 209f / 255f, 157f / 255f, 1f);
            var hoverCallbacksRegistered = false;

            var borderTargets = new List<VisualElement>();
            var defaultBorderColors = new Dictionary<VisualElement, Color[]>();

            void AddBorderTarget(VisualElement element)
            {
                if (element == null)
                {
                    return;
                }

                if (!borderTargets.Contains(element))
                {
                    borderTargets.Add(element);
                }

                if (!defaultBorderColors.ContainsKey(element))
                {
                    defaultBorderColors[element] = new[]
                    {
                        element.resolvedStyle.borderTopColor,
                        element.resolvedStyle.borderRightColor,
                        element.resolvedStyle.borderBottomColor,
                        element.resolvedStyle.borderLeftColor
                    };
                }
            }

            void RefreshBorderTargets()
            {
                borderTargets.Clear();

                void CollectBorderTargets(VisualElement element)
                {
                    if (element == null)
                    {
                        return;
                    }

                    if (element.resolvedStyle.borderTopWidth > 0
                        || element.resolvedStyle.borderRightWidth > 0
                        || element.resolvedStyle.borderBottomWidth > 0
                        || element.resolvedStyle.borderLeftWidth > 0)
                    {
                        AddBorderTarget(element);
                    }

                    foreach (var child in element.hierarchy.Children())
                    {
                        CollectBorderTargets(child);
                    }
                }

                CollectBorderTargets(dropdown);
            }

            void ApplyHoverBorder(bool hover)
            {
                RefreshBorderTargets();

                foreach (var target in borderTargets)
                {
                    if (target == null)
                    {
                        continue;
                    }

                    if (hover)
                    {
                        target.style.borderTopColor = accentBorder;
                        target.style.borderRightColor = accentBorder;
                        target.style.borderBottomColor = accentBorder;
                        target.style.borderLeftColor = accentBorder;
                        continue;
                    }

                    if (defaultBorderColors.TryGetValue(target, out var colors) && colors != null && colors.Length == 4)
                    {
                        target.style.borderTopColor = colors[0];
                        target.style.borderRightColor = colors[1];
                        target.style.borderBottomColor = colors[2];
                        target.style.borderLeftColor = colors[3];
                    }
                }
            }

            void ApplyInternalStyles()
            {
                var inputElement = dropdown.Q<VisualElement>(className: "unity-base-field__input")
                                   ?? dropdown.Q<VisualElement>(className: "unity-base-popup-field__input")
                                   ?? dropdown.Q<VisualElement>(className: "unity-popup-field__input");

                if (inputElement != null)
                {
                    inputElement.style.minWidth = 0;
                    inputElement.style.flexGrow = 1;
                    inputElement.style.flexShrink = 1;
                    inputElement.style.flexBasis = 0;
                    inputElement.style.borderTopWidth = 1;
                    inputElement.style.borderRightWidth = 1;
                    inputElement.style.borderBottomWidth = 1;
                    inputElement.style.borderLeftWidth = 1;
                    inputElement.style.borderTopLeftRadius = 4;
                    inputElement.style.borderTopRightRadius = 4;
                    inputElement.style.borderBottomLeftRadius = 4;
                    inputElement.style.borderBottomRightRadius = 4;
                    inputElement.style.paddingLeft = 3;
                    inputElement.style.paddingRight = 3;
                    inputElement.style.paddingTop = 3;
                    inputElement.style.paddingBottom = 3;
                    inputElement.style.marginLeft = 0;
                    inputElement.style.marginRight = 0;
                    inputElement.style.marginTop = 0;
                    inputElement.style.marginBottom = 0;
                    inputElement.style.backgroundColor = new Color(1f, 1f, 1f, 0.2f);
                }

                RefreshBorderTargets();

                var text = dropdown.Q<VisualElement>(className: "unity-popup-field__text");
                if (text != null)
                {
                    text.style.minWidth = 0;
                    text.style.flexGrow = 1;
                    text.style.flexShrink = 1;
                    text.style.overflow = Overflow.Hidden;
                    text.style.textOverflow = TextOverflow.Ellipsis;
                }

                if (!hoverCallbacksRegistered)
                {
                    hoverCallbacksRegistered = true;
                    dropdown.RegisterCallback<PointerEnterEvent>(_ => ApplyHoverBorder(true), TrickleDown.TrickleDown);
                    dropdown.RegisterCallback<PointerLeaveEvent>(_ => ApplyHoverBorder(false), TrickleDown.TrickleDown);
                    dropdown.RegisterCallback<MouseEnterEvent>(_ => ApplyHoverBorder(true), TrickleDown.TrickleDown);
                    dropdown.RegisterCallback<MouseLeaveEvent>(_ => ApplyHoverBorder(false), TrickleDown.TrickleDown);
                }
            }

            dropdown.RegisterCallback<AttachToPanelEvent>(_ => ApplyInternalStyles());
            dropdown.RegisterCallback<GeometryChangedEvent>(_ => ApplyInternalStyles());
        }

        private static Button CreateLinkButton()
        {
            var button = new Button();

            // 对齐“提交并推送”按钮风格：默认强调色填充 + 强调色描边 + 黑字；悬停时白底白描边。
            var accent = new Color(57f / 255f, 209f / 255f, 157f / 255f, 1f);
            var normalBg = accent;
            var hoverBg = Color.white;
            var textColor = new Color(0f, 0f, 0f, 0.95f);

            button.style.height = 28;
            button.style.flexShrink = 0;
            button.style.marginTop = 5;
            button.style.marginBottom = 5;
            button.style.paddingLeft = 4;
            button.style.paddingRight = 4;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
            button.style.borderTopColor = accent;
            button.style.borderRightColor = accent;
            button.style.borderBottomColor = accent;
            button.style.borderLeftColor = accent;
            button.style.backgroundColor = normalBg;
            button.style.color = textColor;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;

            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!button.enabledSelf)
                {
                    return;
                }

                button.style.backgroundColor = hoverBg;
                button.style.borderTopColor = Color.white;
                button.style.borderRightColor = Color.white;
                button.style.borderBottomColor = Color.white;
                button.style.borderLeftColor = Color.white;
            });

            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.backgroundColor = normalBg;
                button.style.borderTopColor = accent;
                button.style.borderRightColor = accent;
                button.style.borderBottomColor = accent;
                button.style.borderLeftColor = accent;
            });

            return button;
        }
    }
}
#endif
