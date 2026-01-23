using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TLNexus.GitU
{
    internal partial class GitUWindow
    {
        private static Color Rgb(byte r, byte g, byte b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
        private static Color Rgba(byte r, byte g, byte b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);
        private static Color Html(string html) => ColorUtility.TryParseHtmlString(html, out var c) ? c : Color.magenta;
        private static Length Percent(float value) => new Length(value, LengthUnit.Percent);
        private static readonly Color AccentColor = new Color(57f / 255f, 209f / 255f, 157f / 255f, 1f);

        private static void ApplyCardBaseStyle(VisualElement element)
        {
            element.style.backgroundColor = Rgba(255, 255, 255, 0.035f);
            element.style.borderTopWidth = 1;
            element.style.borderRightWidth = 1;
            element.style.borderBottomWidth = 1;
            element.style.borderLeftWidth = 1;
            var borderColor = Rgba(255, 255, 255, 0.08f);
            element.style.borderTopColor = borderColor;
            element.style.borderRightColor = borderColor;
            element.style.borderBottomColor = borderColor;
            element.style.borderLeftColor = borderColor;
            element.style.borderTopLeftRadius = 12;
            element.style.borderTopRightRadius = 12;
            element.style.borderBottomRightRadius = 12;
            element.style.borderBottomLeftRadius = 12;
            element.style.paddingTop = 10;
            element.style.paddingRight = 10;
            element.style.paddingBottom = 10;
            element.style.paddingLeft = 10;
            element.style.marginBottom = 10;
        }

        private static void ApplyToolbarCardBaseStyle(VisualElement element)
        {
            element.style.paddingTop = 10;
            element.style.paddingRight = 10;
            element.style.paddingBottom = 10;
            element.style.paddingLeft = 10;
        }

        private static void ApplyPanelBaseStyle(VisualElement element)
        {
            element.style.flexGrow = 1;
            element.style.flexDirection = FlexDirection.Column;
            element.style.backgroundColor = Rgba(0, 0, 0, 0.16f);
            element.style.borderTopWidth = 1;
            element.style.borderRightWidth = 1;
            element.style.borderBottomWidth = 1;
            element.style.borderLeftWidth = 1;
            var borderColor = Rgba(255, 255, 255, 0.08f);
            element.style.borderTopColor = borderColor;
            element.style.borderRightColor = borderColor;
            element.style.borderBottomColor = borderColor;
            element.style.borderLeftColor = borderColor;
            element.style.borderTopLeftRadius = 12;
            element.style.borderTopRightRadius = 12;
            element.style.borderBottomRightRadius = 12;
            element.style.borderBottomLeftRadius = 12;
            element.style.paddingTop = 8;
            element.style.paddingRight = 8;
            element.style.paddingBottom = 8;
            element.style.paddingLeft = 8;
        }

        private static void ApplyPanelHeaderBaseStyle(VisualElement element)
        {
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.justifyContent = Justify.SpaceBetween;
            element.style.height = 24;
            element.style.paddingLeft = 2;
            element.style.paddingRight = 2;
            element.style.marginBottom = 8;
            element.style.borderBottomWidth = 1;
            element.style.borderBottomColor = Rgba(255, 255, 255, 0.06f);
        }

        private static void ApplyPanelTitleBaseStyle(Label label)
        {
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = Rgba(229, 231, 235, 0.85f);
        }

        private static void ApplyPanelRightBaseStyle(Label label)
        {
            label.style.fontSize = 11;
            label.style.color = Rgba(229, 231, 235, 0.6f);
            label.style.unityTextAlign = TextAnchor.MiddleRight;
        }

        private static void ApplySearchFieldInternals(TextField field)
        {
            if (field == null)
            {
                return;
            }

            var label = field.Q<Label>(className: "unity-base-field__label");
            if (label != null)
            {
                label.style.display = DisplayStyle.None;
            }

            var input = field.Q<VisualElement>(className: "unity-text-field__input");
            if (input == null)
            {
                return;
            }

            var defaultBorder = Rgba(255, 255, 255, 0.04f);
            input.style.backgroundColor = Rgba(0, 0, 0, 0.35f);
            input.style.borderTopLeftRadius = 4;
            input.style.borderTopRightRadius = 4;
            input.style.borderBottomRightRadius = 4;
            input.style.borderBottomLeftRadius = 4;
            input.style.borderTopWidth = 1;
            input.style.borderRightWidth = 1;
            input.style.borderBottomWidth = 1;
            input.style.borderLeftWidth = 1;
            input.style.borderTopColor = defaultBorder;
            input.style.borderRightColor = defaultBorder;
            input.style.borderBottomColor = defaultBorder;
            input.style.borderLeftColor = defaultBorder;
            input.style.paddingLeft = 8;
            input.style.paddingRight = 8;
            input.style.height = 22;

            var textElement = input.Q<TextElement>(className: "unity-text-element");
            if (textElement != null)
            {
                textElement.style.fontSize = 10;
            }
            input.Query<TextElement>().ForEach(e => e.style.fontSize = 10);
            input.style.fontSize = 10;

            var hoverBorder = Rgba(255, 255, 255, 0.18f);
            bool isFocused = false;
            bool isHovered = false;

            void UpdateBorder()
            {
                var borderColor = isFocused
                    ? AccentColor
                    : (isHovered ? hoverBorder : defaultBorder);
                input.style.borderTopColor = borderColor;
                input.style.borderRightColor = borderColor;
                input.style.borderBottomColor = borderColor;
                input.style.borderLeftColor = borderColor;
            }

            field.RegisterCallback<FocusInEvent>(_ => { isFocused = true; UpdateBorder(); });
            field.RegisterCallback<FocusOutEvent>(_ => { isFocused = false; UpdateBorder(); });
            field.RegisterCallback<MouseEnterEvent>(_ => { isHovered = true; UpdateBorder(); });
            field.RegisterCallback<MouseLeaveEvent>(_ => { isHovered = false; UpdateBorder(); });
        }

        private static void ApplyCommitFieldInternals(TextField field)
        {
            if (field == null)
            {
                return;
            }

            var label = field.Q<Label>(className: "unity-base-field__label");
            if (label != null)
            {
                label.style.display = DisplayStyle.None;
            }

            var input = field.Q<VisualElement>(className: "unity-text-field__input");
            if (input == null)
            {
                return;
            }

            var defaultBorder = Rgba(255, 255, 255, 0.04f);
            input.style.backgroundColor = Rgba(0, 0, 0, 0.35f);
            input.style.borderTopLeftRadius = 4;
            input.style.borderTopRightRadius = 4;
            input.style.borderBottomRightRadius = 4;
            input.style.borderBottomLeftRadius = 4;
            input.style.borderTopWidth = 1;
            input.style.borderRightWidth = 1;
            input.style.borderBottomWidth = 1;
            input.style.borderLeftWidth = 1;
            input.style.borderTopColor = defaultBorder;
            input.style.borderRightColor = defaultBorder;
            input.style.borderBottomColor = defaultBorder;
            input.style.borderLeftColor = defaultBorder;
            input.style.paddingLeft = 8;
            input.style.paddingRight = 8;
            input.style.paddingTop = 6;
            input.style.paddingBottom = 6;
            input.style.alignItems = Align.FlexStart;
            input.style.justifyContent = Justify.FlexStart;
            input.style.height = 72;

            var textElement = input.Q<TextElement>(className: "unity-text-element");
            if (textElement != null)
            {
                textElement.style.unityTextAlign = TextAnchor.UpperLeft;
                textElement.style.whiteSpace = WhiteSpace.Normal;
                textElement.style.fontSize = 12;
            }
            input.Query<TextElement>().ForEach(e => e.style.fontSize = 12);
            input.style.fontSize = 12;

            var hoverBorder = Rgba(255, 255, 255, 0.18f);
            bool isFocused = false;
            bool isHovered = false;

            void UpdateBorder()
            {
                var borderColor = isFocused
                    ? AccentColor
                    : (isHovered ? hoverBorder : defaultBorder);
                input.style.borderTopColor = borderColor;
                input.style.borderRightColor = borderColor;
                input.style.borderBottomColor = borderColor;
                input.style.borderLeftColor = borderColor;
            }

            field.RegisterCallback<FocusInEvent>(_ => { isFocused = true; UpdateBorder(); });
            field.RegisterCallback<FocusOutEvent>(_ => { isFocused = false; UpdateBorder(); });
            field.RegisterCallback<MouseEnterEvent>(_ => { isHovered = true; UpdateBorder(); });
            field.RegisterCallback<MouseLeaveEvent>(_ => { isHovered = false; UpdateBorder(); });
        }

        private static void ApplyTargetFieldInternals(ObjectField field)
        {
            if (field == null)
            {
                return;
            }

            void ApplyInternalStyles()
            {
                var label = field.Q<Label>(className: "unity-base-field__label");
                if (label != null)
                {
                    label.style.display = DisplayStyle.None;
                }
            }

            field.RegisterCallback<AttachToPanelEvent>(_ => ApplyInternalStyles());
            field.RegisterCallback<GeometryChangedEvent>(_ => ApplyInternalStyles());
        }

        private bool BuildLayoutFromCode(VisualElement root)
        {
            if (root == null)
            {
                return false;
            }

            try
            {
                Texture2D FindEditorIcon(params string[] names)
                {
                    foreach (string name in names)
                    {
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        var texture = EditorGUIUtility.FindTexture(name);
                        if (texture != null)
                        {
                            return texture;
                        }
                    }

                    return EditorGUIUtility.FindTexture("TextAsset Icon");
                }

                var rootContainer = new VisualElement { name = "rootContainer" };
                rootContainer.AddToClassList("gitU-root");
                rootContainer.style.flexGrow = 1;
                rootContainer.style.flexShrink = 0;
                rootContainer.style.flexDirection = FlexDirection.Column;
                rootContainer.style.justifyContent = Justify.FlexStart;
                rootContainer.style.height = StyleKeyword.Auto;
                rootContainer.style.width = StyleKeyword.Auto;
                rootContainer.style.paddingTop = 0;
                rootContainer.style.paddingRight = 0;
                rootContainer.style.paddingBottom = 0;
                rootContainer.style.paddingLeft = 0;
                rootContainer.style.backgroundColor = Rgb(15, 15, 15);
                rootContainer.style.color = Html("#e5e7eb");
                root.Add(rootContainer);

                var headerCard = new VisualElement { name = "headerCard" };
                headerCard.AddToClassList("card");
                headerCard.AddToClassList("top-card");
                ApplyCardBaseStyle(headerCard);
                headerCard.style.flexDirection = FlexDirection.Row;
                headerCard.style.flexGrow = 0;
                headerCard.style.flexShrink = 1;
                headerCard.style.justifyContent = Justify.SpaceBetween;
                headerCard.style.alignItems = Align.Center;
                headerCard.style.marginTop = 0;
                headerCard.style.marginRight = 0;
                headerCard.style.marginBottom = 0;
                headerCard.style.marginLeft = 0;
                headerCard.style.paddingTop = 0;
                headerCard.style.paddingRight = 0;
                headerCard.style.paddingBottom = 0;
                headerCard.style.paddingLeft = 0;
                headerCard.style.height = 50;
                headerCard.style.borderTopLeftRadius = 0;
                headerCard.style.borderTopRightRadius = 0;
                headerCard.style.borderBottomRightRadius = 0;
                headerCard.style.borderBottomLeftRadius = 0;
                headerCard.style.borderTopWidth = 0;
                headerCard.style.borderRightWidth = 0;
                headerCard.style.borderLeftWidth = 0;
                var headerBorderColor = Rgba(255, 255, 255, 0.04f);
                headerCard.style.borderBottomColor = headerBorderColor;
                headerCard.style.backgroundColor = Rgb(20, 20, 20);
                rootContainer.Add(headerCard);

                var targetRow = new VisualElement();
                targetRow.AddToClassList("target-row");
                targetRow.style.flexDirection = FlexDirection.Row;
                targetRow.style.alignItems = Align.Center;
                targetRow.style.flexShrink = 1;
                targetRow.style.marginTop = 0;
                targetRow.style.flexGrow = 0;
                targetRow.style.marginLeft = 10;
                headerCard.Add(targetRow);

                var targetFieldElement = new ObjectField { name = "targetField", label = string.Empty };
                targetFieldElement.AddToClassList("target-field");
                targetFieldElement.style.flexGrow = 0;
                targetFieldElement.style.flexShrink = 1;
                targetFieldElement.style.marginRight = 2;
                targetFieldElement.style.marginLeft = 2;
                targetFieldElement.style.height = StyleKeyword.Auto;
                targetFieldElement.style.borderTopLeftRadius = 0;
                targetFieldElement.style.borderTopRightRadius = 0;
                targetFieldElement.style.borderBottomRightRadius = 0;
                targetFieldElement.style.borderBottomLeftRadius = 0;
                targetRow.Add(targetFieldElement);

                // 多选时显示的文本标签，默认隐藏
                var multiSelectLabel = new Label { name = "multiSelectLabel", text = "" };
                multiSelectLabel.style.display = DisplayStyle.None;
                multiSelectLabel.style.marginLeft = 4;
                multiSelectLabel.style.marginRight = 4;
                multiSelectLabel.style.color = new Color(1f, 1f, 1f, 0.7f);
                multiSelectLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                targetRow.Add(multiSelectLabel);

                var refreshButtonElement = new Button { name = "refreshButton", text = string.Empty };
                refreshButtonElement.AddToClassList("primary-button");
                refreshButtonElement.AddToClassList("refresh-button");
                refreshButtonElement.style.backgroundColor = Color.clear;
                refreshButtonElement.style.borderTopWidth = 0;
                refreshButtonElement.style.borderRightWidth = 0;
                refreshButtonElement.style.borderBottomWidth = 0;
                refreshButtonElement.style.borderLeftWidth = 0;
                refreshButtonElement.style.height = 22;
                refreshButtonElement.style.width = 22;
                refreshButtonElement.style.borderTopLeftRadius = 4;
                refreshButtonElement.style.borderTopRightRadius = 4;
                refreshButtonElement.style.borderBottomRightRadius = 4;
                refreshButtonElement.style.borderBottomLeftRadius = 4;
                refreshButtonElement.style.marginRight = 2;
                refreshButtonElement.style.marginLeft = 2;
                refreshButtonElement.style.paddingRight = 0;
                refreshButtonElement.style.paddingLeft = 0;
                refreshButtonElement.style.flexDirection = FlexDirection.Row;
                refreshButtonElement.style.alignItems = Align.Center;
                refreshButtonElement.style.justifyContent = Justify.Center;
                var refreshIconColor = new Color(1f, 1f, 1f, 0.7f);
                var refreshIcon = new Image { name = "refreshButtonIcon" };
                refreshIcon.style.width = 16;
                refreshIcon.style.height = 16;
                refreshIcon.scaleMode = ScaleMode.ScaleToFit;
                refreshIcon.tintColor = refreshIconColor;
                var refreshIconContent = EditorGUIUtility.IconContent("d_Refresh");
                if (refreshIconContent == null || refreshIconContent.image == null)
                {
                    refreshIconContent = EditorGUIUtility.IconContent("Refresh");
                }
                if (refreshIconContent == null || refreshIconContent.image == null)
                {
                    refreshIconContent = EditorGUIUtility.IconContent("d_RotateTool");
                }
                refreshIcon.image = refreshIconContent?.image;
                if (refreshIcon.image == null)
                {
                    refreshIcon.style.display = DisplayStyle.None;
                }
                refreshButtonElement.Add(refreshIcon);
                var refreshHoverBg = Rgba(255, 255, 255, 0.10f);
                refreshButtonElement.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    refreshButtonElement.style.backgroundColor = refreshHoverBg;
                    refreshIcon.tintColor = Color.white;
                });
                refreshButtonElement.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    refreshButtonElement.style.backgroundColor = Color.clear;
                    refreshIcon.tintColor = refreshIconColor;
                });

                var fengexian1 = new VisualElement { name = "fengexian1" };
                fengexian1.style.flexShrink = 0;
                fengexian1.style.height = 10;
                fengexian1.style.marginRight = 2;
                fengexian1.style.marginLeft = 2;
                fengexian1.style.width = 1;
                fengexian1.style.backgroundColor = Rgba(255, 255, 255, 0.12f);

                var statusBlock = new VisualElement();
                statusBlock.AddToClassList("status-block");
                statusBlock.style.flexDirection = FlexDirection.Row;
                statusBlock.style.alignItems = Align.Center;
                statusBlock.style.justifyContent = Justify.FlexEnd;
                statusBlock.style.flexShrink = 0;
                headerCard.Add(statusBlock);

                statusBlock.Add(refreshButtonElement);
                statusBlock.Add(fengexian1);

                Button CreateTypeButton(string name, string text, Color color, int marginLeft, int marginRight)
                {
                    var button = new Button { name = name, text = text };
                    button.AddToClassList("type-button");
                    button.style.backgroundColor = Rgba(255, 255, 255, 0.06f);
                    button.style.borderTopWidth = 1;
                    button.style.borderRightWidth = 1;
                    button.style.borderBottomWidth = 1;
                    button.style.borderLeftWidth = 1;
                    var border = Rgba(255, 255, 255, 0.12f);
                    button.style.borderTopColor = border;
                    button.style.borderRightColor = border;
                    button.style.borderBottomColor = border;
                    button.style.borderLeftColor = border;
                    button.style.paddingLeft = 10;
                    button.style.paddingRight = 10;
                    button.style.color = color;
                    button.style.marginLeft = marginLeft;
                    button.style.marginRight = marginRight;
                    button.style.borderTopLeftRadius = 4;
                    button.style.borderTopRightRadius = 4;
                    button.style.borderBottomRightRadius = 4;
                    button.style.borderBottomLeftRadius = 4;
                    button.style.height = 22;
                    button.style.width = 96;

                    // 存储按钮的原始颜色，用于悬停后恢复
                    button.userData = color;

                    // 悬停效果：在当前状态基础上“稍微提亮”边框与背景（不使用强调色）。
                    var disabledBg = Rgba(255, 255, 255, 0.06f);
                    var disabledHoverBg = Rgba(255, 255, 255, 0.10f);
                    var disabledBorder = Rgba(255, 255, 255, 0.12f);
                    var disabledHoverBorder = Rgba(255, 255, 255, 0.22f);
                    const float enabledBgAlpha = 0.10f;
                    const float enabledHoverBgAlpha = 0.16f;
                    bool isHovered = false;

                    void ApplyHoverVisual()
                    {
                        // enabled 时文本颜色为各自的 color（alpha=1）；disabled 时为 alpha≈0.6
                        var enabled = button.style.color.value.a >= 0.95f;

                        if (enabled && button.userData is Color accent)
                        {
                            var bgAlpha = isHovered ? enabledHoverBgAlpha : enabledBgAlpha;
                            button.style.backgroundColor = new Color(accent.r, accent.g, accent.b, bgAlpha);

                            var borderColor = isHovered
                                ? new Color(
                                    Mathf.Clamp01(accent.r + (1f - accent.r) * 0.12f),
                                    Mathf.Clamp01(accent.g + (1f - accent.g) * 0.12f),
                                    Mathf.Clamp01(accent.b + (1f - accent.b) * 0.12f),
                                    1f)
                                : new Color(accent.r, accent.g, accent.b, 1f);
                            button.style.borderTopColor = borderColor;
                            button.style.borderRightColor = borderColor;
                            button.style.borderBottomColor = borderColor;
                            button.style.borderLeftColor = borderColor;
                            return;
                        }

                        button.style.backgroundColor = isHovered ? disabledHoverBg : disabledBg;
                        var border = isHovered ? disabledHoverBorder : disabledBorder;
                        button.style.borderTopColor = border;
                        button.style.borderRightColor = border;
                        button.style.borderBottomColor = border;
                        button.style.borderLeftColor = border;
                    }

                    button.RegisterCallback<MouseEnterEvent>(_ => { isHovered = true; ApplyHoverVisual(); });
                    button.RegisterCallback<MouseLeaveEvent>(_ => { isHovered = false; ApplyHoverVisual(); });

                    // 点击会改变 enabled/disabled 外观；用 schedule 让悬停态在视觉更新后继续生效。
                    button.RegisterCallback<ClickEvent>(_ =>
                    {
                        button.schedule.Execute(ApplyHoverVisual).ExecuteLater(0);
                    });

                    return button;
                }

                var settingButton = new Button { name = "Setting", text = string.Empty };
                var settingHoverBg = Rgba(255, 255, 255, 0.10f);
                settingButton.style.backgroundColor = Color.clear;
                settingButton.style.height = 22;
                settingButton.style.width = 22;
                settingButton.style.marginLeft = 2;
                settingButton.style.marginRight = 10;
                settingButton.style.borderTopLeftRadius = 4;
                settingButton.style.borderTopRightRadius = 4;
                settingButton.style.borderBottomRightRadius = 4;
                settingButton.style.borderBottomLeftRadius = 4;
                settingButton.style.borderTopWidth = 0;
                settingButton.style.borderRightWidth = 0;
                settingButton.style.borderBottomWidth = 0;
                settingButton.style.borderLeftWidth = 0;
                settingButton.style.paddingLeft = 0;
                settingButton.style.paddingRight = 0;
                settingButton.style.flexDirection = FlexDirection.Row;
                settingButton.style.alignItems = Align.Center;
                settingButton.style.justifyContent = Justify.Center;
                var settingIconColor = new Color(1f, 1f, 1f, 0.7f);
                var settingIcon = new Image { name = "settingButtonIcon" };
                settingIcon.style.width = 16;
                settingIcon.style.height = 16;
                settingIcon.scaleMode = ScaleMode.ScaleToFit;
                settingIcon.tintColor = settingIconColor;
                var settingIconContent = EditorGUIUtility.IconContent("Settings");
                if (settingIconContent == null || settingIconContent.image == null)
                {
                    settingIconContent = EditorGUIUtility.IconContent("d_Settings");
                }
                if (settingIconContent == null || settingIconContent.image == null)
                {
                    settingIconContent = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow");
                }
                settingIcon.image = settingIconContent?.image;
                if (settingIcon.image == null)
                {
                    settingIcon.style.display = DisplayStyle.None;
                }
                settingButton.Add(settingIcon);
                settingButton.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    settingButton.style.backgroundColor = settingHoverBg;
                    settingIcon.tintColor = Color.white;
                });
                settingButton.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    settingButton.style.backgroundColor = Color.clear;
                    settingIcon.tintColor = settingIconColor;
                });
                statusBlock.Add(settingButton);

                var filterBox = new VisualElement { name = "filterBox" };
                filterBox.AddToClassList("card");
                filterBox.AddToClassList("toolbar-card");
                ApplyCardBaseStyle(filterBox);
                ApplyToolbarCardBaseStyle(filterBox);
                filterBox.style.marginTop = 10;
                filterBox.style.marginRight = 10;
                filterBox.style.marginBottom = 10;
                filterBox.style.marginLeft = 10;
                filterBox.style.paddingTop = 0;
                filterBox.style.paddingRight = 0;
                filterBox.style.paddingBottom = 0;
                filterBox.style.paddingLeft = 0;
                filterBox.style.flexShrink = 1;
                filterBox.style.flexDirection = FlexDirection.Row;
                filterBox.style.height = 44;
                filterBox.style.alignItems = Align.Center;
                filterBox.style.borderTopLeftRadius = 8;
                filterBox.style.borderTopRightRadius = 8;
                filterBox.style.borderBottomRightRadius = 8;
                filterBox.style.borderBottomLeftRadius = 8;
                var filterBorderColor = Rgba(255, 255, 255, 0.04f);
                filterBox.style.borderTopColor = filterBorderColor;
                filterBox.style.borderRightColor = filterBorderColor;
                filterBox.style.borderBottomColor = filterBorderColor;
                filterBox.style.borderLeftColor = filterBorderColor;
                filterBox.style.backgroundColor = Rgb(25, 25, 25);
                filterBox.style.justifyContent = Justify.SpaceBetween;
                filterBox.style.flexGrow = 0;
                rootContainer.Add(filterBox);

                var toolbarLeft = new VisualElement();
                toolbarLeft.AddToClassList("toolbar-left");
                toolbarLeft.style.flexDirection = FlexDirection.Row;
                toolbarLeft.style.alignItems = Align.Center;
                toolbarLeft.style.flexShrink = 1;
                toolbarLeft.style.width = 100;
                toolbarLeft.style.flexGrow = 0;
                toolbarLeft.style.marginLeft = 10;
                toolbarLeft.style.marginRight = 0;
                filterBox.Add(toolbarLeft);

                var assetTypeMenuElement = new ToolbarMenu { name = "assetTypeMenu" };
                assetTypeMenuElement.AddToClassList("toolbar-dropdown");
                assetTypeMenuElement.focusable = true;
                assetTypeMenuElement.style.paddingTop = 0;
                assetTypeMenuElement.style.paddingRight = 8;
                assetTypeMenuElement.style.paddingBottom = 0;
                assetTypeMenuElement.style.paddingLeft = 8;
                assetTypeMenuElement.style.marginLeft = 0;
                assetTypeMenuElement.style.marginTop = 0;
                assetTypeMenuElement.style.marginRight = 0;
                assetTypeMenuElement.style.marginBottom = 0;
                assetTypeMenuElement.style.height = 22;
                assetTypeMenuElement.style.borderTopWidth = 1;
                assetTypeMenuElement.style.borderRightWidth = 1;
                assetTypeMenuElement.style.borderBottomWidth = 1;
                assetTypeMenuElement.style.borderLeftWidth = 1;
                assetTypeMenuElement.style.borderTopLeftRadius = 4;
                assetTypeMenuElement.style.borderTopRightRadius = 4;
                assetTypeMenuElement.style.borderBottomRightRadius = 4;
                assetTypeMenuElement.style.borderBottomLeftRadius = 4;
                assetTypeMenuElement.style.width = 100;
                var menuDefaultBorder = Rgba(255, 255, 255, 0.04f);
                var menuHoverBorder = Rgba(255, 255, 255, 0.18f);
                var menuBackground = Rgba(0, 0, 0, 0.35f);
                bool menuHovered = false;
                bool menuFocused = false;

                void ApplyMenuState()
                {
                    var borderColor = (menuHovered || menuFocused) ? menuHoverBorder : menuDefaultBorder;
                    assetTypeMenuElement.style.borderTopColor = borderColor;
                    assetTypeMenuElement.style.borderRightColor = borderColor;
                    assetTypeMenuElement.style.borderBottomColor = borderColor;
                    assetTypeMenuElement.style.borderLeftColor = borderColor;
                    assetTypeMenuElement.style.backgroundColor = menuBackground;
                }

                assetTypeMenuElement.RegisterCallback<MouseEnterEvent>(_ => { menuHovered = true; ApplyMenuState(); });
                assetTypeMenuElement.RegisterCallback<MouseLeaveEvent>(_ => { menuHovered = false; ApplyMenuState(); });
                assetTypeMenuElement.RegisterCallback<FocusInEvent>(_ => { menuFocused = true; ApplyMenuState(); });
                assetTypeMenuElement.RegisterCallback<FocusOutEvent>(_ => { menuFocused = false; ApplyMenuState(); });
                ApplyMenuState();
                toolbarLeft.Add(assetTypeMenuElement);

                var searchFieldElement = new TextField { name = "searchField", tooltip = "Search files, paths, or changes..." };
                searchFieldElement.AddToClassList("search-field");
                searchFieldElement.style.marginTop = 0;
                searchFieldElement.style.marginRight = 10;
                searchFieldElement.style.marginBottom = 0;
                searchFieldElement.style.marginLeft = 10;
                searchFieldElement.style.borderTopLeftRadius = 0;
                searchFieldElement.style.borderTopRightRadius = 0;
                searchFieldElement.style.borderBottomRightRadius = 0;
                searchFieldElement.style.borderBottomLeftRadius = 0;
                searchFieldElement.style.flexGrow = 1;
                searchFieldElement.style.flexShrink = 1;
                filterBox.Add(searchFieldElement);

                var changeTypeButtons = new VisualElement { name = "changeTypeButtons" };
                changeTypeButtons.style.flexDirection = FlexDirection.Row;
                changeTypeButtons.style.alignItems = Align.Center;
                changeTypeButtons.style.justifyContent = Justify.FlexEnd;
                changeTypeButtons.style.flexGrow = 0;
                changeTypeButtons.style.flexShrink = 0;
                changeTypeButtons.style.marginLeft = 0;
                changeTypeButtons.style.marginRight = 10;
                changeTypeButtons.style.marginTop = 0;
                changeTypeButtons.style.marginBottom = 0;
                filterBox.Add(changeTypeButtons);

                changeTypeButtons.Add(CreateTypeButton("modifiedButton", "修改 (M)", Rgb(242, 191, 102), marginLeft: 0, marginRight: 2));
                changeTypeButtons.Add(CreateTypeButton("addedButton", "新增 (A)", Rgb(128, 217, 128), marginLeft: 2, marginRight: 2));
                changeTypeButtons.Add(CreateTypeButton("deletedButton", "删除 (D)", Rgb(230, 128, 128), marginLeft: 2, marginRight: 0));

                var mainArea = new VisualElement { name = "mainArea" };
                mainArea.AddToClassList("main-area");
                mainArea.style.flexGrow = 1;
                mainArea.style.flexDirection = FlexDirection.Column;
                mainArea.style.height = StyleKeyword.Auto;
                mainArea.style.flexShrink = 1;
                rootContainer.Add(mainArea);

                var middleRow = new VisualElement { name = "middleRow" };
                middleRow.AddToClassList("split-view");
                middleRow.style.flexGrow = 1;
                middleRow.style.flexDirection = FlexDirection.Row;
                middleRow.style.justifyContent = Justify.SpaceBetween;
                middleRow.style.minHeight = 240;
                middleRow.style.marginBottom = 0;
                middleRow.style.marginLeft = 10;
                middleRow.style.marginRight = 10;
                middleRow.style.width = StyleKeyword.Auto;
                mainArea.Add(middleRow);

                var leftColumnElement = new VisualElement { name = "leftColumn" };
                leftColumnElement.AddToClassList("panel");
                leftColumnElement.AddToClassList("left-panel");
                ApplyPanelBaseStyle(leftColumnElement);
                leftColumnElement.style.marginRight = 5;
                leftColumnElement.style.paddingTop = 0;
                leftColumnElement.style.paddingRight = 0;
                leftColumnElement.style.paddingBottom = 0;
                leftColumnElement.style.paddingLeft = 0;
                leftColumnElement.style.borderLeftColor = Rgb(50, 50, 50);
                leftColumnElement.style.borderRightColor = Rgb(50, 50, 50);
                leftColumnElement.style.borderTopColor = Rgb(50, 50, 50);
                leftColumnElement.style.borderBottomColor = Rgb(50, 50, 50);
                leftColumnElement.style.borderTopLeftRadius = 8;
                leftColumnElement.style.borderTopRightRadius = 8;
                leftColumnElement.style.borderBottomRightRadius = 8;
                leftColumnElement.style.borderBottomLeftRadius = 8;
                leftColumnElement.style.width = Percent(50);
                middleRow.Add(leftColumnElement);

                var unstagedHeaderRow = new VisualElement { name = "unstagedHeaderRow" };
                unstagedHeaderRow.AddToClassList("panel-header");
                ApplyPanelHeaderBaseStyle(unstagedHeaderRow);
                unstagedHeaderRow.style.borderBottomWidth = 0;
                unstagedHeaderRow.style.borderLeftColor = Rgb(50, 50, 50);
                unstagedHeaderRow.style.borderRightColor = Rgb(50, 50, 50);
                unstagedHeaderRow.style.borderTopColor = Rgb(50, 50, 50);
                unstagedHeaderRow.style.borderBottomColor = Rgb(50, 50, 50);
                unstagedHeaderRow.style.paddingRight = 4;
                unstagedHeaderRow.style.paddingLeft = 10;
                unstagedHeaderRow.style.backgroundColor = Color.clear;
                unstagedHeaderRow.style.height = 24;
                unstagedHeaderRow.style.borderTopWidth = 0;
                unstagedHeaderRow.style.borderRightWidth = 0;
                unstagedHeaderRow.style.borderLeftWidth = 0;
                unstagedHeaderRow.style.paddingTop = 8;
                leftColumnElement.Add(unstagedHeaderRow);

                var unstagedTitleRow = new VisualElement { name = "unstagedTitleRow" };
                unstagedTitleRow.style.flexDirection = FlexDirection.Row;
                unstagedTitleRow.style.alignItems = Align.Center;
                unstagedTitleRow.style.flexGrow = 1;
                unstagedTitleRow.style.flexShrink = 1;
                unstagedHeaderRow.Add(unstagedTitleRow);

                var titleIconColor = Rgba(229, 231, 235, 0.75f);
                var unstagedIcon = new Image { name = "unstagedTitleIcon" };
                unstagedIcon.style.width = 12;
                unstagedIcon.style.height = 12;
                unstagedIcon.style.marginRight = 6;
                unstagedIcon.scaleMode = ScaleMode.ScaleToFit;
                unstagedIcon.tintColor = titleIconColor;
                unstagedIcon.image = FindEditorIcon(
                    "d_P4_AddedLocal",
                    "P4_AddedLocal",
                    "d_P4_CheckedOutLocal",
                    "P4_CheckedOutLocal",
                    "d_P4_CheckOut",
                    "P4_CheckOut",
                    "d_console.warnicon.sml",
                    "console.warnicon.sml",
                    "d_editicon.sml",
                    "editicon.sml",
                    "d_Animation.Record",
                    "Animation.Record",
                    "d_CollabChanges",
                    "CollabChanges",
                    "d_P4_Conflicted",
                    "P4_Conflicted"
                );
                if (unstagedIcon.image == null)
                {
                    unstagedIcon.style.display = DisplayStyle.None;
                    unstagedIcon.style.marginRight = 0;
                }
                unstagedTitleRow.Add(unstagedIcon);

                var unstagedTitle = new Label("UNSTAGED CHANGES") { name = "unstagedTitleLabel" };
                unstagedTitle.AddToClassList("panel-title");
                ApplyPanelTitleBaseStyle(unstagedTitle);
                unstagedTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
                unstagedTitleRow.Add(unstagedTitle);

                var unstagedHeaderLabelElement = new Label { name = "unstagedHeaderLabel" };
                unstagedHeaderLabelElement.AddToClassList("panel-right");
                ApplyPanelRightBaseStyle(unstagedHeaderLabelElement);
                unstagedHeaderRow.Add(unstagedHeaderLabelElement);

                var unstagedListContainer = new VisualElement { name = "unstagedListContainer" };
                unstagedListContainer.style.flexGrow = 1;
                unstagedListContainer.style.flexShrink = 1;
                unstagedListContainer.style.position = Position.Relative;
                unstagedListContainer.style.marginTop = 0;
                unstagedListContainer.style.marginRight = 0;
                unstagedListContainer.style.marginBottom = 0;
                unstagedListContainer.style.marginLeft = 0;
                unstagedListContainer.style.paddingTop = 0;
                unstagedListContainer.style.paddingRight = 0;
                unstagedListContainer.style.paddingBottom = 0;
                unstagedListContainer.style.paddingLeft = 0;
                leftColumnElement.Add(unstagedListContainer);

                var unstagedListView = new ListView { name = "unstagedScrollView" };
                unstagedListView.AddToClassList("panel-list");
                unstagedListView.style.flexGrow = 1;
                unstagedListView.style.height = Percent(100);
                unstagedListView.style.backgroundColor = Color.clear;
                unstagedListView.style.marginTop = 0;
                unstagedListView.style.marginRight = 0;
                unstagedListView.style.marginBottom = 0;
                unstagedListView.style.marginLeft = 0;
                unstagedListView.style.borderTopWidth = 0;
                unstagedListView.style.borderRightWidth = 0;
                unstagedListView.style.borderBottomWidth = 0;
                unstagedListView.style.borderLeftWidth = 0;
                unstagedListView.style.borderTopColor = Color.clear;
                unstagedListView.style.borderRightColor = Color.clear;
                unstagedListView.style.borderBottomColor = Color.clear;
                unstagedListView.style.borderLeftColor = Color.clear;
                unstagedListView.style.borderTopLeftRadius = 0;
                unstagedListView.style.borderTopRightRadius = 0;
                unstagedListView.style.borderBottomRightRadius = 8;
                unstagedListView.style.borderBottomLeftRadius = 8;
                unstagedListView.style.paddingTop = 0;
                unstagedListView.style.paddingRight = 0;
                unstagedListView.style.paddingBottom = 0;
                unstagedListView.style.paddingLeft = 0;
                var unstagedScrollView = unstagedListView.Q<ScrollView>();
                if (unstagedScrollView != null)
                {
                    unstagedScrollView.style.marginTop = 0;
                    unstagedScrollView.style.marginRight = 0;
                    unstagedScrollView.style.marginBottom = 0;
                    unstagedScrollView.style.marginLeft = 0;
                    unstagedScrollView.style.paddingTop = 0;
                    unstagedScrollView.style.paddingRight = 0;
                    unstagedScrollView.style.paddingBottom = 0;
                    unstagedScrollView.style.paddingLeft = 0;
                    unstagedScrollView.style.borderTopWidth = 0;
                    unstagedScrollView.style.borderRightWidth = 0;
                    unstagedScrollView.style.borderBottomWidth = 0;
                    unstagedScrollView.style.borderLeftWidth = 0;
                    unstagedScrollView.style.borderTopColor = Color.clear;
                    unstagedScrollView.style.borderRightColor = Color.clear;
                    unstagedScrollView.style.borderBottomColor = Color.clear;
                    unstagedScrollView.style.borderLeftColor = Color.clear;
                    unstagedScrollView.style.backgroundColor = Color.clear;
                    unstagedScrollView.style.borderTopLeftRadius = 0;
                    unstagedScrollView.style.borderTopRightRadius = 0;
                }
                unstagedListContainer.Add(unstagedListView);

                var unstagedEmptyHintOverlay = new VisualElement { name = "unstagedEmptyHintOverlay" };
                unstagedEmptyHintOverlay.style.position = Position.Absolute;
                unstagedEmptyHintOverlay.style.left = 0;
                unstagedEmptyHintOverlay.style.right = 0;
                unstagedEmptyHintOverlay.style.top = 0;
                unstagedEmptyHintOverlay.style.bottom = 0;
                unstagedEmptyHintOverlay.style.backgroundColor = Color.clear;
                unstagedEmptyHintOverlay.style.display = DisplayStyle.None;
                unstagedEmptyHintOverlay.pickingMode = PickingMode.Position;
                unstagedListContainer.Add(unstagedEmptyHintOverlay);

                var unstagedEmptyHintLabel = new Label
                {
                    name = "unstagedEmptyHintLabel",
                    text = "暂无变更\n\n提示：\n拖拽条目到“待提交”可加入待提交\nCtrl：多选\nShift：连续选择\nCtrl+A：全选"
                };
                unstagedEmptyHintLabel.style.flexGrow = 1;
                unstagedEmptyHintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                unstagedEmptyHintLabel.style.whiteSpace = WhiteSpace.Normal;
                unstagedEmptyHintLabel.style.fontSize = 11;
                unstagedEmptyHintLabel.style.color = Rgba(255, 255, 255, 0.45f);
                unstagedEmptyHintLabel.style.paddingLeft = 14;
                unstagedEmptyHintLabel.style.paddingRight = 14;
                unstagedEmptyHintLabel.style.paddingTop = 10;
                unstagedEmptyHintLabel.style.paddingBottom = 10;
                unstagedEmptyHintOverlay.Add(unstagedEmptyHintLabel);

                var rightColumnElement = new VisualElement { name = "rightColumn" };
                rightColumnElement.AddToClassList("panel");
                rightColumnElement.AddToClassList("right-panel");
                ApplyPanelBaseStyle(rightColumnElement);
                rightColumnElement.style.marginLeft = 5;
                rightColumnElement.style.paddingTop = 0;
                rightColumnElement.style.paddingRight = 0;
                rightColumnElement.style.paddingBottom = 0;
                rightColumnElement.style.paddingLeft = 0;
                rightColumnElement.style.borderLeftColor = Rgb(50, 50, 50);
                rightColumnElement.style.borderRightColor = Rgb(50, 50, 50);
                rightColumnElement.style.borderTopColor = Rgb(50, 50, 50);
                rightColumnElement.style.borderBottomColor = Rgb(50, 50, 50);
                rightColumnElement.style.borderTopLeftRadius = 8;
                rightColumnElement.style.borderTopRightRadius = 8;
                rightColumnElement.style.borderBottomRightRadius = 8;
                rightColumnElement.style.borderBottomLeftRadius = 8;
                rightColumnElement.style.width = Percent(50);
                middleRow.Add(rightColumnElement);

                var stagedHeaderRow = new VisualElement { name = "stagedHeaderRow" };
                stagedHeaderRow.AddToClassList("panel-header");
                ApplyPanelHeaderBaseStyle(stagedHeaderRow);
                stagedHeaderRow.style.borderLeftColor = Rgb(50, 50, 50);
                stagedHeaderRow.style.borderRightColor = Rgb(50, 50, 50);
                stagedHeaderRow.style.borderTopColor = Rgb(50, 50, 50);
                stagedHeaderRow.style.borderBottomColor = Rgb(50, 50, 50);
                stagedHeaderRow.style.backgroundColor = Color.clear;
                stagedHeaderRow.style.height = 24;
                stagedHeaderRow.style.paddingLeft = 10;
                stagedHeaderRow.style.borderTopWidth = 0;
                stagedHeaderRow.style.borderRightWidth = 0;
                stagedHeaderRow.style.borderBottomWidth = 0;
                stagedHeaderRow.style.borderLeftWidth = 0;
                stagedHeaderRow.style.unityTextAlign = TextAnchor.MiddleLeft;
                stagedHeaderRow.style.paddingRight = 4;
                stagedHeaderRow.style.paddingTop = 8;
                rightColumnElement.Add(stagedHeaderRow);

                var stagedTitleRow = new VisualElement { name = "stagedTitleRow" };
                stagedTitleRow.style.flexDirection = FlexDirection.Row;
                stagedTitleRow.style.alignItems = Align.Center;
                stagedTitleRow.style.flexGrow = 1;
                stagedTitleRow.style.flexShrink = 1;
                stagedHeaderRow.Add(stagedTitleRow);

                var stagedIcon = new Image { name = "stagedTitleIcon" };
                stagedIcon.style.width = 12;
                stagedIcon.style.height = 12;
                stagedIcon.style.marginRight = 6;
                stagedIcon.scaleMode = ScaleMode.ScaleToFit;
                stagedIcon.tintColor = titleIconColor;
                stagedIcon.image = FindEditorIcon(
                    "d_TestPassed",
                    "TestPassed",
                    "d_Valid",
                    "Valid",
                    "d_P4_CheckOut",
                    "P4_CheckOut",
                    "d_SaveAs",
                    "SaveAs"
                );
                if (stagedIcon.image == null)
                {
                    stagedIcon.style.display = DisplayStyle.None;
                    stagedIcon.style.marginRight = 0;
                }
                stagedTitleRow.Add(stagedIcon);

                var stagedTitle = new Label("STAGED CHANGES") { name = "stagedTitleLabel" };
                stagedTitle.AddToClassList("panel-title");
                ApplyPanelTitleBaseStyle(stagedTitle);
                stagedTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
                stagedTitleRow.Add(stagedTitle);

                var stagedHeaderLabelElement = new Label { name = "stagedHeaderLabel" };
                stagedHeaderLabelElement.AddToClassList("panel-right");
                ApplyPanelRightBaseStyle(stagedHeaderLabelElement);
                stagedHeaderRow.Add(stagedHeaderLabelElement);

                var stagedListContainer = new VisualElement { name = "stagedListContainer" };
                stagedListContainer.style.flexGrow = 1;
                stagedListContainer.style.flexShrink = 1;
                stagedListContainer.style.position = Position.Relative;
                stagedListContainer.style.marginTop = 0;
                stagedListContainer.style.marginRight = 0;
                stagedListContainer.style.marginBottom = 0;
                stagedListContainer.style.marginLeft = 0;
                stagedListContainer.style.paddingTop = 0;
                stagedListContainer.style.paddingRight = 0;
                stagedListContainer.style.paddingBottom = 0;
                stagedListContainer.style.paddingLeft = 0;
                rightColumnElement.Add(stagedListContainer);

                var stagedListView = new ListView { name = "stagedScrollView" };
                stagedListView.AddToClassList("panel-list");
                stagedListView.style.flexGrow = 1;
                stagedListView.style.height = Percent(100);
                stagedListView.style.backgroundColor = Color.clear;
                stagedListView.style.marginTop = 0;
                stagedListView.style.marginRight = 0;
                stagedListView.style.marginBottom = 0;
                stagedListView.style.marginLeft = 0;
                stagedListView.style.borderTopWidth = 0;
                stagedListView.style.borderRightWidth = 0;
                stagedListView.style.borderBottomWidth = 0;
                stagedListView.style.borderLeftWidth = 0;
                stagedListView.style.borderTopColor = Color.clear;
                stagedListView.style.borderRightColor = Color.clear;
                stagedListView.style.borderBottomColor = Color.clear;
                stagedListView.style.borderLeftColor = Color.clear;
                stagedListView.style.borderTopLeftRadius = 0;
                stagedListView.style.borderTopRightRadius = 0;
                stagedListView.style.borderBottomRightRadius = 8;
                stagedListView.style.borderBottomLeftRadius = 8;
                stagedListView.style.paddingTop = 0;
                stagedListView.style.paddingRight = 0;
                stagedListView.style.paddingBottom = 0;
                stagedListView.style.paddingLeft = 0;
                var stagedScrollView = stagedListView.Q<ScrollView>();
                if (stagedScrollView != null)
                {
                    stagedScrollView.style.marginTop = 0;
                    stagedScrollView.style.marginRight = 0;
                    stagedScrollView.style.marginBottom = 0;
                    stagedScrollView.style.marginLeft = 0;
                    stagedScrollView.style.paddingTop = 0;
                    stagedScrollView.style.paddingRight = 0;
                    stagedScrollView.style.paddingBottom = 0;
                    stagedScrollView.style.paddingLeft = 0;
                    stagedScrollView.style.borderTopWidth = 0;
                    stagedScrollView.style.borderRightWidth = 0;
                    stagedScrollView.style.borderBottomWidth = 0;
                    stagedScrollView.style.borderLeftWidth = 0;
                    stagedScrollView.style.borderTopColor = Color.clear;
                    stagedScrollView.style.borderRightColor = Color.clear;
                    stagedScrollView.style.borderBottomColor = Color.clear;
                    stagedScrollView.style.borderLeftColor = Color.clear;
                    stagedScrollView.style.backgroundColor = Color.clear;
                    stagedScrollView.style.borderTopLeftRadius = 0;
                    stagedScrollView.style.borderTopRightRadius = 0;
                }
                stagedListContainer.Add(stagedListView);

                var stagedEmptyHintOverlay = new VisualElement { name = "stagedEmptyHintOverlay" };
                stagedEmptyHintOverlay.style.position = Position.Absolute;
                stagedEmptyHintOverlay.style.left = 0;
                stagedEmptyHintOverlay.style.right = 0;
                stagedEmptyHintOverlay.style.top = 0;
                stagedEmptyHintOverlay.style.bottom = 0;
                stagedEmptyHintOverlay.style.backgroundColor = Color.clear;
                stagedEmptyHintOverlay.style.display = DisplayStyle.None;
                stagedEmptyHintOverlay.pickingMode = PickingMode.Position;
                stagedListContainer.Add(stagedEmptyHintOverlay);

                var stagedEmptyHintLabel = new Label
                {
                    name = "stagedEmptyHintLabel",
                    text = "暂无待提交条目\n\n提示：\n拖拽条目到“变更区”可取消待提交\nCtrl：多选\nShift：连续选择\nCtrl+A：全选"
                };
                stagedEmptyHintLabel.style.flexGrow = 1;
                stagedEmptyHintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                stagedEmptyHintLabel.style.whiteSpace = WhiteSpace.Normal;
                stagedEmptyHintLabel.style.fontSize = 11;
                stagedEmptyHintLabel.style.color = Rgba(255, 255, 255, 0.45f);
                stagedEmptyHintLabel.style.paddingLeft = 14;
                stagedEmptyHintLabel.style.paddingRight = 14;
                stagedEmptyHintLabel.style.paddingTop = 10;
                stagedEmptyHintLabel.style.paddingBottom = 10;
                stagedEmptyHintOverlay.Add(stagedEmptyHintLabel);

                var repositoryStatusRowElement = new VisualElement { name = "repositoryStatusRow" };
                repositoryStatusRowElement.AddToClassList("repo-status-row");
                repositoryStatusRowElement.style.flexDirection = FlexDirection.Row;
                repositoryStatusRowElement.style.alignItems = Align.Center;
                repositoryStatusRowElement.style.justifyContent = Justify.SpaceBetween;
                repositoryStatusRowElement.style.marginBottom = 10;
                repositoryStatusRowElement.style.marginRight = 10;
                repositoryStatusRowElement.style.marginLeft = 10;
                repositoryStatusRowElement.style.marginTop = 10;
                rootContainer.Add(repositoryStatusRowElement);

                var repositoryStatusLabelElement = new Label { name = "repositoryStatusLabel", enableRichText = true };
                repositoryStatusLabelElement.AddToClassList("repo-status");
                repositoryStatusLabelElement.style.fontSize = 11;
                repositoryStatusLabelElement.style.color = Rgba(229, 231, 235, 0.7f);
                repositoryStatusLabelElement.style.flexGrow = 1;
                repositoryStatusLabelElement.style.flexShrink = 1;
                repositoryStatusLabelElement.style.marginTop = 0;
                repositoryStatusLabelElement.style.marginRight = 0;
                repositoryStatusLabelElement.style.marginBottom = 0;
                repositoryStatusLabelElement.style.marginLeft = 0;
                repositoryStatusRowElement.Add(repositoryStatusLabelElement);

                var repositoryStatusRightElement = new VisualElement { name = "repositoryStatusRight" };
                repositoryStatusRightElement.AddToClassList("repo-status-right");
                repositoryStatusRightElement.style.flexDirection = FlexDirection.Row;
                repositoryStatusRightElement.style.alignItems = Align.Center;
                repositoryStatusRightElement.style.justifyContent = Justify.FlexEnd;
                repositoryStatusRightElement.style.flexGrow = 0;
                repositoryStatusRightElement.style.flexShrink = 0;
                repositoryStatusRowElement.Add(repositoryStatusRightElement);

                var sortInfoLabelElement = new Label { name = "sortInfoLabel" };
                sortInfoLabelElement.style.fontSize = 10;
                sortInfoLabelElement.style.color = Rgba(255, 255, 255, 0.5f);
                sortInfoLabelElement.style.unityTextAlign = TextAnchor.MiddleRight;
                sortInfoLabelElement.style.whiteSpace = WhiteSpace.NoWrap;
                sortInfoLabelElement.style.flexGrow = 0;
                sortInfoLabelElement.style.flexShrink = 0;
                sortInfoLabelElement.style.marginLeft = 0;
                sortInfoLabelElement.style.marginRight = 3;
                sortInfoLabelElement.style.marginTop = 0;
                sortInfoLabelElement.style.marginBottom = 0;
                repositoryStatusRightElement.Add(sortInfoLabelElement);

                var repositoryStatusUpButtonElement = new Button { name = "repositoryStatusUpButton", text = string.Empty };
                repositoryStatusUpButtonElement.style.width = 20;
                repositoryStatusUpButtonElement.style.height = 20;
                repositoryStatusUpButtonElement.style.minWidth = 20;
                repositoryStatusUpButtonElement.style.minHeight = 20;
                repositoryStatusUpButtonElement.style.flexShrink = 0;
                repositoryStatusUpButtonElement.style.marginLeft = 4;
                repositoryStatusUpButtonElement.style.marginRight = 0;
                repositoryStatusUpButtonElement.style.marginTop = 0;
                repositoryStatusUpButtonElement.style.marginBottom = 0;
                repositoryStatusUpButtonElement.style.paddingTop = 0;
                repositoryStatusUpButtonElement.style.paddingRight = 0;
                repositoryStatusUpButtonElement.style.paddingBottom = 0;
                repositoryStatusUpButtonElement.style.paddingLeft = 0;
                repositoryStatusUpButtonElement.style.flexDirection = FlexDirection.Row;
                repositoryStatusUpButtonElement.style.alignItems = Align.Center;
                repositoryStatusUpButtonElement.style.justifyContent = Justify.Center;
                repositoryStatusUpButtonElement.style.unityBackgroundImageTintColor = Color.clear;

                var sortButtonHoverBg = Rgba(255, 255, 255, 0.10f);
                var sortIconColor = new Color(1f, 1f, 1f, 0.7f);
                repositoryStatusUpButtonElement.style.backgroundColor = Color.clear;
                repositoryStatusUpButtonElement.style.borderTopLeftRadius = 4;
                repositoryStatusUpButtonElement.style.borderTopRightRadius = 4;
                repositoryStatusUpButtonElement.style.borderBottomRightRadius = 4;
                repositoryStatusUpButtonElement.style.borderBottomLeftRadius = 4;
                repositoryStatusUpButtonElement.style.borderTopWidth = 0;
                repositoryStatusUpButtonElement.style.borderRightWidth = 0;
                repositoryStatusUpButtonElement.style.borderBottomWidth = 0;
                repositoryStatusUpButtonElement.style.borderLeftWidth = 0;
                var sortIcon = new Image { name = "repositoryStatusUpButtonIcon" };
                sortIcon.style.width = 16;
                sortIcon.style.height = 16;
                sortIcon.scaleMode = ScaleMode.ScaleToFit;
                sortIcon.tintColor = sortIconColor;
                Texture2D sortTexture = null;
                string[] sortIconCandidates =
                {
                    "d_TreeEditor.SortAscending",
                    "TreeEditor.SortAscending",
                    "d_TreeEditor.SortDescending",
                    "TreeEditor.SortDescending",
                    "d_AlphabeticalSorting",
                    "AlphabeticalSorting",
                    "d_SortAscending",
                    "SortAscending",
                    "d_Sort",
                    "Sort",
                    "d_FilterByType",
                    "FilterByType"
                };
                foreach (string candidate in sortIconCandidates)
                {
                    sortTexture = EditorGUIUtility.FindTexture(candidate);
                    if (sortTexture != null)
                    {
                        break;
                    }
                }
                if (sortTexture == null)
                {
                    sortTexture = EditorGUIUtility.IconContent("TextAsset Icon")?.image as Texture2D;
                }
                sortIcon.image = sortTexture;
                var hasSortIcon = sortTexture != null && sortTexture.width > 0 && sortTexture.height > 0;
                sortIcon.style.display = hasSortIcon ? DisplayStyle.Flex : DisplayStyle.None;
                repositoryStatusUpButtonElement.Add(sortIcon);

                var sortTextFallback = new Label { name = "repositoryStatusUpButtonLabel", text = "⇅" };
                sortTextFallback.style.width = 14;
                sortTextFallback.style.height = 14;
                sortTextFallback.style.unityTextAlign = TextAnchor.MiddleCenter;
                sortTextFallback.style.fontSize = 12;
                sortTextFallback.style.color = sortIconColor;
                sortTextFallback.style.display = hasSortIcon ? DisplayStyle.None : DisplayStyle.Flex;
                repositoryStatusUpButtonElement.Add(sortTextFallback);

                repositoryStatusUpButtonElement.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    repositoryStatusUpButtonElement.style.backgroundColor = sortButtonHoverBg;
                    sortIcon.tintColor = Color.white;
                    sortTextFallback.style.color = Color.white;
                });
                repositoryStatusUpButtonElement.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    repositoryStatusUpButtonElement.style.backgroundColor = Color.clear;
                    sortIcon.tintColor = sortIconColor;
                    sortTextFallback.style.color = sortIconColor;
                });
                repositoryStatusRightElement.Add(repositoryStatusUpButtonElement);

                var commitBox = new VisualElement { name = "commitBox" };
                commitBox.AddToClassList("card");
                commitBox.AddToClassList("commit-card");
                ApplyCardBaseStyle(commitBox);
                commitBox.style.paddingTop = 0;
                commitBox.style.paddingRight = 0;
                commitBox.style.paddingBottom = 0;
                commitBox.style.paddingLeft = 0;
                commitBox.style.flexGrow = 0;
                commitBox.style.height = 160;
                commitBox.style.marginTop = 10;
                commitBox.style.marginRight = 10;
                commitBox.style.marginBottom = 10;
                commitBox.style.marginLeft = 10;
                commitBox.style.backgroundColor = Rgb(25, 25, 25);
                var commitBorderColor = Rgba(255, 255, 255, 0.04f);
                commitBox.style.borderTopColor = commitBorderColor;
                commitBox.style.borderRightColor = commitBorderColor;
                commitBox.style.borderBottomColor = commitBorderColor;
                commitBox.style.borderLeftColor = commitBorderColor;
                commitBox.style.justifyContent = Justify.SpaceBetween;
                commitBox.style.borderTopLeftRadius = 8;
                commitBox.style.borderTopRightRadius = 8;
                commitBox.style.borderBottomRightRadius = 8;
                commitBox.style.borderBottomLeftRadius = 8;
                rootContainer.Add(commitBox);

                var commitHeaderRow = new VisualElement { name = "commitHeaderRow" };
                commitHeaderRow.AddToClassList("row");
                commitHeaderRow.AddToClassList("commit-header-row");
                commitHeaderRow.style.flexDirection = FlexDirection.Row;
                commitHeaderRow.style.alignItems = Align.Center;
                commitHeaderRow.style.justifyContent = Justify.SpaceBetween;
                commitHeaderRow.style.flexShrink = 0;
                commitHeaderRow.style.marginTop = 10;
                commitHeaderRow.style.marginRight = 10;
                commitHeaderRow.style.marginLeft = 10;
                commitHeaderRow.style.marginBottom = 6;
                commitBox.Add(commitHeaderRow);

                var commitMessageTitleLabelElement = new Label("Commit Message") { name = "commitMessageTitleLabel" };
                commitMessageTitleLabelElement.AddToClassList("commit-message-title");
                ApplyPanelTitleBaseStyle(commitMessageTitleLabelElement);
                commitMessageTitleLabelElement.style.flexGrow = 1;
                commitMessageTitleLabelElement.style.flexShrink = 1;
                commitMessageTitleLabelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
                commitHeaderRow.Add(commitMessageTitleLabelElement);

                var commitMessageHintLabelElement = new Label("按Enter可换行") { name = "commitMessageHintLabel" };
                commitMessageHintLabelElement.AddToClassList("commit-message-hint");
                commitMessageHintLabelElement.style.fontSize = 10;
                commitMessageHintLabelElement.style.color = Rgba(255, 255, 255, 0.5f);
                commitMessageHintLabelElement.style.flexGrow = 0;
                commitMessageHintLabelElement.style.flexShrink = 0;
                commitMessageHintLabelElement.style.unityTextAlign = TextAnchor.MiddleRight;
                commitHeaderRow.Add(commitMessageHintLabelElement);

                var commitMessageFieldElement = new TextField { name = "commitMessageField" };
                commitMessageFieldElement.multiline = true;
                commitMessageFieldElement.AddToClassList("text-field-grow");
                commitMessageFieldElement.AddToClassList("commit-field");
                commitMessageFieldElement.style.flexGrow = 1;
                commitMessageFieldElement.style.flexShrink = 0;
                commitMessageFieldElement.style.height = StyleKeyword.Auto;
                commitMessageFieldElement.style.flexDirection = FlexDirection.Column;
                commitMessageFieldElement.style.marginTop = 0;
                commitMessageFieldElement.style.marginRight = 10;
                commitMessageFieldElement.style.marginLeft = 10;
                commitMessageFieldElement.style.marginBottom = 0;
                commitMessageFieldElement.style.fontSize = 14;
                commitBox.Add(commitMessageFieldElement);

                var commitRow = new VisualElement { name = "commitRow" };
                commitRow.AddToClassList("row");
                commitRow.AddToClassList("commit-row");
                commitRow.style.marginTop = 10;
                commitRow.style.flexDirection = FlexDirection.Row;
                commitRow.style.alignItems = Align.Center;
                commitRow.style.justifyContent = Justify.SpaceBetween;
                commitRow.style.marginBottom = 10;
                commitRow.style.marginRight = 10;
                commitRow.style.marginLeft = 10;
                commitBox.Add(commitRow);

                var historyButtonElement = new VisualElement { name = "historyButton" };
                historyButtonElement.AddToClassList("history-button");
                historyButtonElement.style.flexDirection = FlexDirection.Row;
                historyButtonElement.style.alignItems = Align.Center;
                historyButtonElement.style.justifyContent = Justify.FlexStart;
                var historyTextColor = Rgba(229, 231, 235, 0.7f);
                var historyHoverTextColor = Color.white;
                var historyIconColor = new Color(1f, 1f, 1f, 0.7f);
                historyButtonElement.style.backgroundColor = Color.clear;
                historyButtonElement.style.borderTopWidth = 0;
                historyButtonElement.style.borderRightWidth = 0;
                historyButtonElement.style.borderBottomWidth = 0;
                historyButtonElement.style.borderLeftWidth = 0;
                historyButtonElement.style.borderTopLeftRadius = 4;
                historyButtonElement.style.borderTopRightRadius = 4;
                historyButtonElement.style.borderBottomRightRadius = 4;
                historyButtonElement.style.borderBottomLeftRadius = 4;
                historyButtonElement.style.marginRight = 4;
                historyButtonElement.style.height = 28;
                historyButtonElement.style.width = 80;
                historyButtonElement.style.paddingTop = 0;
                historyButtonElement.style.paddingRight = 0;
                historyButtonElement.style.paddingBottom = 0;
                historyButtonElement.style.paddingLeft = 0;
                historyButtonElement.style.marginLeft = 0;
                var historyIcon = new Image { name = "historyButtonIcon" };
                historyIcon.style.width = 16;
                historyIcon.style.height = 16;
                historyIcon.style.marginRight = 4;
                historyIcon.scaleMode = ScaleMode.ScaleToFit;
                historyIcon.tintColor = historyIconColor;
                var historyIconContent = EditorGUIUtility.IconContent("d_UnityEditor.HistoryWindow");
                if (historyIconContent == null || historyIconContent.image == null)
                {
                    historyIconContent = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow");
                }
                if (historyIconContent == null || historyIconContent.image == null)
                {
                    historyIconContent = EditorGUIUtility.IconContent("console.infoicon.sml");
                }
                historyIcon.image = historyIconContent?.image;
                if (historyIcon.image == null)
                {
                    historyIcon.style.display = DisplayStyle.None;
                    historyIcon.style.marginRight = 0;
                }
                historyButtonElement.Add(historyIcon);

                var historyLabelElement = new Label { name = "historyButtonLabel", text = "记录" };
                historyLabelElement.style.whiteSpace = WhiteSpace.NoWrap;
                historyLabelElement.style.color = historyTextColor;
                historyButtonElement.Add(historyLabelElement);
                historyButtonElement.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    if (!historyButtonElement.enabledSelf)
                    {
                        return;
                    }

                    historyLabelElement.style.color = historyHoverTextColor;
                    historyIcon.tintColor = historyHoverTextColor;
                });
                historyButtonElement.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    historyLabelElement.style.color = historyTextColor;
                    historyIcon.tintColor = historyIconColor;
                });
                commitRow.Add(historyButtonElement);

                var commitSpacer = new VisualElement();
                commitSpacer.AddToClassList("commit-spacer");
                commitSpacer.style.flexDirection = FlexDirection.Row;
                commitSpacer.style.flexGrow = 0;
                commitSpacer.style.flexShrink = 0;
                commitSpacer.style.justifyContent = Justify.SpaceBetween;
                commitRow.Add(commitSpacer);

                var commitButtonElement = new Button { name = "commitButton", text = "提交到本地" };
                commitButtonElement.AddToClassList("commit-button-local");
                commitButtonElement.style.width = 140;
                commitButtonElement.style.height = 28;
                var accentColor = AccentColor;
                commitButtonElement.style.backgroundColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0f);
                commitButtonElement.style.color = accentColor;
                commitButtonElement.style.unityFontStyleAndWeight = FontStyle.Bold;
                commitButtonElement.style.marginLeft = 4;
                commitButtonElement.style.marginRight = 4;
                commitButtonElement.style.borderTopLeftRadius = 6;
                commitButtonElement.style.borderTopRightRadius = 6;
                commitButtonElement.style.borderBottomRightRadius = 6;
                commitButtonElement.style.borderBottomLeftRadius = 6;
                commitButtonElement.style.borderTopWidth = 1;
                commitButtonElement.style.borderRightWidth = 1;
                commitButtonElement.style.borderBottomWidth = 1;
                commitButtonElement.style.borderLeftWidth = 1;
                commitButtonElement.style.borderTopColor = accentColor;
                commitButtonElement.style.borderRightColor = accentColor;
                commitButtonElement.style.borderBottomColor = accentColor;
                commitButtonElement.style.borderLeftColor = accentColor;
                commitButtonElement.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    if (!commitButtonElement.enabledSelf)
                    {
                        return;
                    }

                    commitButtonElement.style.backgroundColor = Color.white;
                    commitButtonElement.style.borderTopColor = Color.white;
                    commitButtonElement.style.borderRightColor = Color.white;
                    commitButtonElement.style.borderBottomColor = Color.white;
                    commitButtonElement.style.borderLeftColor = Color.white;
                    commitButtonElement.style.color = Rgba(0, 0, 0, 0.95f);
                });
                commitButtonElement.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    commitButtonElement.style.backgroundColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0f);
                    commitButtonElement.style.borderTopColor = accentColor;
                    commitButtonElement.style.borderRightColor = accentColor;
                    commitButtonElement.style.borderBottomColor = accentColor;
                    commitButtonElement.style.borderLeftColor = accentColor;
                    commitButtonElement.style.color = accentColor;
                });
                commitSpacer.Add(commitButtonElement);

                var commitAndPushButtonElement = new Button { name = "commitAndPushButton", text = "提交并推送" };
                commitAndPushButtonElement.AddToClassList("commit-button-push");
                commitAndPushButtonElement.style.width = 140;
                commitAndPushButtonElement.style.height = 28;
                commitAndPushButtonElement.style.backgroundColor = accentColor;
                commitAndPushButtonElement.style.color = Rgba(0, 0, 0, 0.95f);
                commitAndPushButtonElement.style.unityFontStyleAndWeight = FontStyle.Bold;
                commitAndPushButtonElement.style.marginRight = 0;
                commitAndPushButtonElement.style.marginLeft = 4;
                commitAndPushButtonElement.style.borderTopLeftRadius = 6;
                commitAndPushButtonElement.style.borderTopRightRadius = 6;
                commitAndPushButtonElement.style.borderBottomRightRadius = 6;
                commitAndPushButtonElement.style.borderBottomLeftRadius = 6;
                commitAndPushButtonElement.style.borderTopWidth = 1;
                commitAndPushButtonElement.style.borderRightWidth = 1;
                commitAndPushButtonElement.style.borderBottomWidth = 1;
                commitAndPushButtonElement.style.borderLeftWidth = 1;
                commitAndPushButtonElement.style.borderTopColor = accentColor;
                commitAndPushButtonElement.style.borderRightColor = accentColor;
                commitAndPushButtonElement.style.borderBottomColor = accentColor;
                commitAndPushButtonElement.style.borderLeftColor = accentColor;
                commitAndPushButtonElement.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    if (!commitAndPushButtonElement.enabledSelf)
                    {
                        return;
                    }

                    commitAndPushButtonElement.style.backgroundColor = Color.white;
                    commitAndPushButtonElement.style.borderTopColor = Color.white;
                    commitAndPushButtonElement.style.borderRightColor = Color.white;
                    commitAndPushButtonElement.style.borderBottomColor = Color.white;
                    commitAndPushButtonElement.style.borderLeftColor = Color.white;
                    commitAndPushButtonElement.style.color = Rgba(0, 0, 0, 0.95f);
                });
                commitAndPushButtonElement.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    commitAndPushButtonElement.style.backgroundColor = accentColor;
                    commitAndPushButtonElement.style.borderTopColor = accentColor;
                    commitAndPushButtonElement.style.borderRightColor = accentColor;
                    commitAndPushButtonElement.style.borderBottomColor = accentColor;
                    commitAndPushButtonElement.style.borderLeftColor = accentColor;
                    commitAndPushButtonElement.style.color = Rgba(0, 0, 0, 0.95f);
                });
                commitSpacer.Add(commitAndPushButtonElement);

                var historyOverlayElement = new VisualElement { name = "historyOverlay" };
                historyOverlayElement.style.display = DisplayStyle.None;
                historyOverlayElement.style.position = Position.Absolute;
                historyOverlayElement.style.left = 0;
                historyOverlayElement.style.right = 0;
                historyOverlayElement.style.top = 0;
                historyOverlayElement.style.bottom = 0;
                historyOverlayElement.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
                historyOverlayElement.style.justifyContent = Justify.Center;
                historyOverlayElement.style.alignItems = Align.Center;
                rootContainer.Add(historyOverlayElement);

                var historyDropdownElement = new VisualElement { name = "historyDropdown" };
                historyDropdownElement.AddToClassList("history-modal");
                historyDropdownElement.style.display = DisplayStyle.None;
                historyDropdownElement.style.position = Position.Relative;
                historyDropdownElement.style.width = 480;
                historyDropdownElement.style.height = 640;
                historyDropdownElement.style.flexGrow = 0;
                historyDropdownElement.style.flexShrink = 0;
                historyDropdownElement.style.flexDirection = FlexDirection.Column;
                historyDropdownElement.style.paddingTop = 0;
                historyDropdownElement.style.paddingRight = 0;
                historyDropdownElement.style.paddingBottom = 0;
                historyDropdownElement.style.paddingLeft = 0;
                historyDropdownElement.style.backgroundColor = Html("#111115");
                historyDropdownElement.style.borderTopWidth = 1;
                historyDropdownElement.style.borderRightWidth = 1;
                historyDropdownElement.style.borderBottomWidth = 1;
                historyDropdownElement.style.borderLeftWidth = 1;
                var dropdownBorder = Rgba(255, 255, 255, 0.12f);
                historyDropdownElement.style.borderTopColor = dropdownBorder;
                historyDropdownElement.style.borderRightColor = dropdownBorder;
                historyDropdownElement.style.borderBottomColor = dropdownBorder;
                historyDropdownElement.style.borderLeftColor = dropdownBorder;
                historyDropdownElement.style.borderTopLeftRadius = 12;
                historyDropdownElement.style.borderTopRightRadius = 12;
                historyDropdownElement.style.borderBottomRightRadius = 12;
                historyDropdownElement.style.borderBottomLeftRadius = 12;
                historyOverlayElement.Add(historyDropdownElement);

                var historyHeaderBarElement = new VisualElement { name = "historyHeaderBar" };
                historyHeaderBarElement.AddToClassList("history-header");
                historyHeaderBarElement.style.flexDirection = FlexDirection.Row;
                historyHeaderBarElement.style.alignItems = Align.Center;
                historyHeaderBarElement.style.justifyContent = Justify.SpaceBetween;
                historyHeaderBarElement.style.flexGrow = 0;
                historyHeaderBarElement.style.flexShrink = 0;
                historyHeaderBarElement.style.height = 32;
                historyHeaderBarElement.style.backgroundColor = Rgba(255, 255, 255, 0.03f);
                historyHeaderBarElement.style.paddingLeft = 0;
                historyHeaderBarElement.style.paddingRight = 0;
                historyHeaderBarElement.style.paddingTop = 0;
                historyHeaderBarElement.style.paddingBottom = 0;
                historyHeaderBarElement.style.borderBottomWidth = 1;
                historyHeaderBarElement.style.borderBottomColor = dropdownBorder;
                historyDropdownElement.Add(historyHeaderBarElement);

                var historyTitleLabelElement = new Label("提交记录") { name = "historyTitleLabel" };
                historyTitleLabelElement.AddToClassList("history-title");
                historyTitleLabelElement.style.fontSize = 11;
                historyTitleLabelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
                historyTitleLabelElement.style.color = Rgba(229, 231, 235, 0.85f);
                historyTitleLabelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
                historyTitleLabelElement.style.flexGrow = 1;
                historyTitleLabelElement.style.flexShrink = 1;
                historyTitleLabelElement.style.marginLeft = 10;
                historyHeaderBarElement.Add(historyTitleLabelElement);

                var historyContentElement = new VisualElement { name = "historyContent" };
                historyContentElement.AddToClassList("history-content");
                historyContentElement.style.flexGrow = 1;
                historyContentElement.style.flexShrink = 1;
                historyContentElement.style.paddingLeft = 0;
                historyContentElement.style.paddingRight = 0;
                historyContentElement.style.paddingTop = 0;
                historyContentElement.style.paddingBottom = 0;
                historyDropdownElement.Add(historyContentElement);

                var historyListViewElement = new ListView { name = "historyListView" };
                historyListViewElement.style.flexGrow = 1;
                historyListViewElement.style.flexShrink = 1;
                historyContentElement.Add(historyListViewElement);

                var historyStatusBarElement = new VisualElement { name = "historyStatusBar" };
                historyStatusBarElement.AddToClassList("history-status");
                historyStatusBarElement.style.flexDirection = FlexDirection.Row;
                historyStatusBarElement.style.alignItems = Align.Center;
                historyStatusBarElement.style.justifyContent = Justify.SpaceBetween;
                historyStatusBarElement.style.flexGrow = 0;
                historyStatusBarElement.style.flexShrink = 0;
                historyStatusBarElement.style.height = 28;
                historyStatusBarElement.style.backgroundColor = Rgba(255, 255, 255, 0.03f);
                historyStatusBarElement.style.paddingLeft = 0;
                historyStatusBarElement.style.paddingRight = 0;
                historyStatusBarElement.style.borderTopWidth = 1;
                historyStatusBarElement.style.borderTopColor = dropdownBorder;
                historyDropdownElement.Add(historyStatusBarElement);

                var historyHintLabelElement = new Label("提示：最多显示前100条记录") { name = "historyHintLabel" };
                historyHintLabelElement.AddToClassList("history-hint");
                historyHintLabelElement.style.fontSize = 10;
                historyHintLabelElement.style.color = Rgba(255, 255, 255, 0.5f);
                historyHintLabelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
                historyHintLabelElement.style.flexGrow = 1;
                historyHintLabelElement.style.flexShrink = 1;
                historyHintLabelElement.style.marginLeft = 10;
                historyStatusBarElement.Add(historyHintLabelElement);

                var saveToDiskHint = new Label { name = "saveToDiskHintLabel" };
                saveToDiskHint.AddToClassList("hint-text");
                saveToDiskHint.AddToClassList("save-hint");
                saveToDiskHint.style.color = Rgba(255, 90, 90, 0.95f);
                saveToDiskHint.style.whiteSpace = WhiteSpace.Normal;
                saveToDiskHint.style.unityFontStyleAndWeight = FontStyle.Bold;
                saveToDiskHint.style.fontSize = 11;
                saveToDiskHint.style.paddingTop = 0;
                saveToDiskHint.style.paddingRight = 0;
                saveToDiskHint.style.paddingBottom = 0;
                saveToDiskHint.style.paddingLeft = 0;
                saveToDiskHint.style.marginTop = 0;
                saveToDiskHint.style.marginRight = 10;
                saveToDiskHint.style.marginBottom = 0;
                saveToDiskHint.style.marginLeft = 10;
                saveToDiskHint.style.flexShrink = 1;
                saveToDiskHint.style.flexGrow = 0;
                saveToDiskHint.style.minHeight = 20;
                saveToDiskHint.style.height = StyleKeyword.Auto;
                saveToDiskHint.style.justifyContent = Justify.FlexStart;
                saveToDiskHint.style.unityTextAlign = TextAnchor.MiddleLeft;
                rootContainer.Add(saveToDiskHint);

                var statusLabelElement = new Label { name = "statusLabel" };
                statusLabelElement.AddToClassList("status-text");
                statusLabelElement.style.whiteSpace = WhiteSpace.Normal;
                statusLabelElement.style.fontSize = 11;
                statusLabelElement.style.color = Rgba(229, 231, 235, 0.85f);
                statusLabelElement.style.flexShrink = 1;
                statusLabelElement.style.flexGrow = 0;
                statusLabelElement.style.minHeight = 20;
                statusLabelElement.style.height = StyleKeyword.Auto;
                statusLabelElement.style.marginTop = 0;
                statusLabelElement.style.marginRight = 10;
                statusLabelElement.style.marginBottom = 0;
                statusLabelElement.style.marginLeft = 10;
                statusLabelElement.style.paddingTop = 0;
                statusLabelElement.style.paddingRight = 0;
                statusLabelElement.style.paddingBottom = 0;
                statusLabelElement.style.paddingLeft = 0;
                statusLabelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
                rootContainer.Add(statusLabelElement);

                var toastContainerElement = new VisualElement { name = "toastContainer" };
                toastContainerElement.style.position = Position.Absolute;
                toastContainerElement.style.left = 0;
                toastContainerElement.style.right = 0;
                toastContainerElement.style.top = 0;
                toastContainerElement.style.bottom = 0;
                toastContainerElement.style.alignItems = Align.Center;
                toastContainerElement.style.justifyContent = Justify.Center;
                toastContainerElement.pickingMode = PickingMode.Ignore;
                rootContainer.Add(toastContainerElement);

                var toastLabelElement = new Label("0") { name = "toastLabel" };
                toastLabelElement.AddToClassList("toast");
                toastLabelElement.style.display = DisplayStyle.None;
                toastLabelElement.style.width = StyleKeyword.Auto;
                toastLabelElement.style.maxWidth = 520;
                toastLabelElement.style.paddingTop = 10;
                toastLabelElement.style.paddingBottom = 10;
                toastLabelElement.style.paddingLeft = 12;
                toastLabelElement.style.paddingRight = 12;
                toastLabelElement.style.borderTopLeftRadius = 8;
                toastLabelElement.style.borderTopRightRadius = 8;
                toastLabelElement.style.borderBottomRightRadius = 8;
                toastLabelElement.style.borderBottomLeftRadius = 8;
                toastLabelElement.style.backgroundColor = AccentColor;
                toastLabelElement.style.borderTopWidth = 0;
                toastLabelElement.style.borderRightWidth = 0;
                toastLabelElement.style.borderBottomWidth = 0;
                toastLabelElement.style.borderLeftWidth = 0;
                toastLabelElement.style.color = Rgba(0, 0, 0, 0.95f);
                toastLabelElement.style.fontSize = 11;
                toastLabelElement.style.whiteSpace = WhiteSpace.Normal;
                toastLabelElement.style.unityTextAlign = TextAnchor.MiddleCenter;
                toastLabelElement.pickingMode = PickingMode.Ignore;
                toastContainerElement.Add(toastLabelElement);

                var dragBadgeElement = new VisualElement { name = "dragBadge" };
                dragBadgeElement.style.display = DisplayStyle.None;
                dragBadgeElement.style.position = Position.Absolute;
                dragBadgeElement.style.left = 0;
                dragBadgeElement.style.top = 0;
                dragBadgeElement.style.width = 22;
                dragBadgeElement.style.height = 22;
                dragBadgeElement.style.backgroundColor = AccentColor;
                dragBadgeElement.style.borderTopWidth = 1;
                dragBadgeElement.style.borderRightWidth = 1;
                dragBadgeElement.style.borderBottomWidth = 1;
                dragBadgeElement.style.borderLeftWidth = 1;
                var dragBadgeBorder = Rgba(255, 255, 255, 0.28f);
                dragBadgeElement.style.borderTopColor = dragBadgeBorder;
                dragBadgeElement.style.borderRightColor = dragBadgeBorder;
                dragBadgeElement.style.borderBottomColor = dragBadgeBorder;
                dragBadgeElement.style.borderLeftColor = dragBadgeBorder;
                dragBadgeElement.style.borderTopLeftRadius = 4;
                dragBadgeElement.style.borderTopRightRadius = 4;
                dragBadgeElement.style.borderBottomRightRadius = 4;
                dragBadgeElement.style.borderBottomLeftRadius = 4;
                dragBadgeElement.style.alignItems = Align.Center;
                dragBadgeElement.style.justifyContent = Justify.Center;
                dragBadgeElement.pickingMode = PickingMode.Ignore;
                var dragBadgeLabelElement = new Label { name = "dragBadgeLabel" };
                dragBadgeLabelElement.style.color = Rgba(0, 0, 0, 0.95f);
                dragBadgeLabelElement.style.fontSize = 10;
                dragBadgeLabelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
                dragBadgeLabelElement.style.unityTextAlign = TextAnchor.MiddleCenter;
                dragBadgeElement.Add(dragBadgeLabelElement);
                rootContainer.Add(dragBadgeElement);

                ApplySearchFieldInternals(searchFieldElement);
                ApplyCommitFieldInternals(commitMessageFieldElement);
                ApplyTargetFieldInternals(targetFieldElement);
                CacheUIElements(root);
                return true;
            }
            catch (Exception ex)
            {
                ShowLayoutLoadError(root, isChineseUi
                    ? $"构建 UI 失败: {ex.Message}"
                    : $"Failed to build UI: {ex.Message}");
                return false;
            }
        }
    }
}
