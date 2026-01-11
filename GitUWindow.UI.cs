using System;
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

            input.style.backgroundColor = Rgba(0, 0, 0, 0.35f);
            input.style.borderTopLeftRadius = 4;
            input.style.borderTopRightRadius = 4;
            input.style.borderBottomRightRadius = 4;
            input.style.borderBottomLeftRadius = 4;
            input.style.borderTopWidth = 0;
            input.style.borderRightWidth = 0;
            input.style.borderBottomWidth = 0;
            input.style.borderLeftWidth = 0;
            input.style.borderTopColor = Color.clear;
            input.style.borderRightColor = Color.clear;
            input.style.borderBottomColor = Color.clear;
            input.style.borderLeftColor = Color.clear;
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

            void SetFocusedBorder(bool focused)
            {
                if (focused)
                {
                    input.style.borderTopWidth = 1;
                    input.style.borderRightWidth = 1;
                    input.style.borderBottomWidth = 1;
                    input.style.borderLeftWidth = 1;
                    var c = Rgb(139, 92, 246);
                    input.style.borderTopColor = c;
                    input.style.borderRightColor = c;
                    input.style.borderBottomColor = c;
                    input.style.borderLeftColor = c;
                }
                else
                {
                    input.style.borderTopWidth = 0;
                    input.style.borderRightWidth = 0;
                    input.style.borderBottomWidth = 0;
                    input.style.borderLeftWidth = 0;
                    input.style.borderTopColor = Color.clear;
                    input.style.borderRightColor = Color.clear;
                    input.style.borderBottomColor = Color.clear;
                    input.style.borderLeftColor = Color.clear;
                }
            }

            SetFocusedBorder(false);
            field.RegisterCallback<FocusInEvent>(_ => SetFocusedBorder(true));
            field.RegisterCallback<FocusOutEvent>(_ => SetFocusedBorder(false));
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

            input.style.backgroundColor = Rgba(0, 0, 0, 0.35f);
            input.style.borderTopLeftRadius = 4;
            input.style.borderTopRightRadius = 4;
            input.style.borderBottomRightRadius = 4;
            input.style.borderBottomLeftRadius = 4;
            input.style.borderTopWidth = 0;
            input.style.borderRightWidth = 0;
            input.style.borderBottomWidth = 0;
            input.style.borderLeftWidth = 0;
            input.style.borderTopColor = Color.clear;
            input.style.borderRightColor = Color.clear;
            input.style.borderBottomColor = Color.clear;
            input.style.borderLeftColor = Color.clear;
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

            void SetFocusedBorder(bool focused)
            {
                if (focused)
                {
                    input.style.borderTopWidth = 1;
                    input.style.borderRightWidth = 1;
                    input.style.borderBottomWidth = 1;
                    input.style.borderLeftWidth = 1;
                    var c = Rgb(139, 92, 246);
                    input.style.borderTopColor = c;
                    input.style.borderRightColor = c;
                    input.style.borderBottomColor = c;
                    input.style.borderLeftColor = c;
                }
                else
                {
                    input.style.borderTopWidth = 0;
                    input.style.borderRightWidth = 0;
                    input.style.borderBottomWidth = 0;
                    input.style.borderLeftWidth = 0;
                    input.style.borderTopColor = Color.clear;
                    input.style.borderRightColor = Color.clear;
                    input.style.borderBottomColor = Color.clear;
                    input.style.borderLeftColor = Color.clear;
                }
            }

            SetFocusedBorder(false);
            field.RegisterCallback<FocusInEvent>(_ => SetFocusedBorder(true));
            field.RegisterCallback<FocusOutEvent>(_ => SetFocusedBorder(false));
        }

        private bool BuildLayoutFromCode(VisualElement root)
        {
            if (root == null)
            {
                return false;
            }

            try
            {
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

                var targetFieldElement = new ObjectField { name = "targetField", label = "目标资源" };
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

                var refreshButtonElement = new Button { name = "refreshButton", text = "刷新" };
                refreshButtonElement.AddToClassList("primary-button");
                refreshButtonElement.AddToClassList("refresh-button");
                refreshButtonElement.style.backgroundColor = Rgba(255, 255, 255, 0.06f);
                refreshButtonElement.style.borderTopWidth = 1;
                refreshButtonElement.style.borderRightWidth = 1;
                refreshButtonElement.style.borderBottomWidth = 1;
                refreshButtonElement.style.borderLeftWidth = 1;
                var refreshBorder = Rgba(255, 255, 255, 0.12f);
                refreshButtonElement.style.borderTopColor = refreshBorder;
                refreshButtonElement.style.borderRightColor = refreshBorder;
                refreshButtonElement.style.borderBottomColor = refreshBorder;
                refreshButtonElement.style.borderLeftColor = refreshBorder;
                refreshButtonElement.style.height = 22;
                refreshButtonElement.style.width = 70;
                refreshButtonElement.style.borderTopLeftRadius = 4;
                refreshButtonElement.style.borderTopRightRadius = 4;
                refreshButtonElement.style.borderBottomRightRadius = 4;
                refreshButtonElement.style.borderBottomLeftRadius = 4;
                refreshButtonElement.style.marginRight = 2;
                refreshButtonElement.style.marginLeft = 2;
                refreshButtonElement.style.paddingRight = 2;
                refreshButtonElement.style.paddingLeft = 2;
                targetRow.Add(refreshButtonElement);

                var fengexian1 = new VisualElement { name = "fengexian1" };
                fengexian1.style.flexShrink = 0;
                fengexian1.style.height = 10;
                fengexian1.style.marginRight = 8;
                fengexian1.style.marginLeft = 8;
                fengexian1.style.width = 1;
                fengexian1.style.backgroundColor = Rgba(255, 255, 255, 0.12f);
                targetRow.Add(fengexian1);

                var statusBlock = new VisualElement();
                statusBlock.AddToClassList("status-block");
                statusBlock.style.flexDirection = FlexDirection.Row;
                statusBlock.style.alignItems = Align.Center;
                statusBlock.style.justifyContent = Justify.FlexEnd;
                statusBlock.style.flexShrink = 0;
                headerCard.Add(statusBlock);

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
                    button.style.width = 70;
                    return button;
                }

                statusBlock.Add(CreateTypeButton("modifiedButton", "修改 (M)", Rgb(242, 191, 102), marginLeft: 4, marginRight: 2));
                statusBlock.Add(CreateTypeButton("addedButton", "新增 (A)", Rgb(128, 217, 128), marginLeft: 2, marginRight: 2));
                statusBlock.Add(CreateTypeButton("deletedButton", "删除 (D)", Rgb(230, 128, 128), marginLeft: 2, marginRight: 2));

                var fengexian = new VisualElement { name = "fengexian" };
                fengexian.style.flexShrink = 0;
                fengexian.style.height = 10;
                fengexian.style.marginRight = 2;
                fengexian.style.marginLeft = 2;
                fengexian.style.width = 1;
                fengexian.style.backgroundColor = Rgba(255, 255, 255, 0.12f);
                statusBlock.Add(fengexian);

                var settingButton = new Button { name = "Setting", text = "S" };
                settingButton.style.height = 22;
                settingButton.style.width = 22;
                settingButton.style.marginLeft = 2;
                settingButton.style.marginRight = 10;
                settingButton.style.borderTopLeftRadius = 4;
                settingButton.style.borderTopRightRadius = 4;
                settingButton.style.borderBottomRightRadius = 4;
                settingButton.style.borderBottomLeftRadius = 4;
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
                filterBox.style.backgroundColor = Rgb(25, 25, 25);
                filterBox.style.justifyContent = Justify.SpaceBetween;
                filterBox.style.flexGrow = 0;
                rootContainer.Add(filterBox);

                var toolbarLeft = new VisualElement();
                toolbarLeft.AddToClassList("toolbar-left");
                toolbarLeft.style.flexDirection = FlexDirection.Row;
                toolbarLeft.style.alignItems = Align.Center;
                toolbarLeft.style.flexShrink = 1;
                toolbarLeft.style.width = 150;
                toolbarLeft.style.flexGrow = 0;
                toolbarLeft.style.marginLeft = 10;
                toolbarLeft.style.marginRight = 5;
                filterBox.Add(toolbarLeft);

                var assetTypeMenuElement = new ToolbarMenu { name = "assetTypeMenu" };
                assetTypeMenuElement.AddToClassList("toolbar-dropdown");
                assetTypeMenuElement.style.paddingTop = 0;
                assetTypeMenuElement.style.paddingRight = 5;
                assetTypeMenuElement.style.paddingBottom = 0;
                assetTypeMenuElement.style.paddingLeft = 5;
                assetTypeMenuElement.style.marginLeft = 0;
                assetTypeMenuElement.style.marginTop = 0;
                assetTypeMenuElement.style.marginRight = 0;
                assetTypeMenuElement.style.marginBottom = 0;
                assetTypeMenuElement.style.height = 22;
                assetTypeMenuElement.style.borderTopWidth = 0;
                assetTypeMenuElement.style.borderRightWidth = 0;
                assetTypeMenuElement.style.borderBottomWidth = 0;
                assetTypeMenuElement.style.borderLeftWidth = 0;
                assetTypeMenuElement.style.borderTopLeftRadius = 4;
                assetTypeMenuElement.style.borderTopRightRadius = 4;
                assetTypeMenuElement.style.borderBottomRightRadius = 4;
                assetTypeMenuElement.style.borderBottomLeftRadius = 4;
                assetTypeMenuElement.style.width = 140;
                toolbarLeft.Add(assetTypeMenuElement);

                var searchFieldElement = new TextField { name = "searchField", tooltip = "Search files, paths, or changes..." };
                searchFieldElement.AddToClassList("search-field");
                searchFieldElement.style.marginTop = 0;
                searchFieldElement.style.marginRight = 5;
                searchFieldElement.style.marginBottom = 0;
                searchFieldElement.style.marginLeft = 5;
                searchFieldElement.style.borderTopLeftRadius = 0;
                searchFieldElement.style.borderTopRightRadius = 0;
                searchFieldElement.style.borderBottomRightRadius = 0;
                searchFieldElement.style.borderBottomLeftRadius = 0;
                searchFieldElement.style.flexGrow = 1;
                searchFieldElement.style.flexShrink = 1;
                filterBox.Add(searchFieldElement);

                var assetTypeMenuElement2 = new ToolbarMenu { name = "assetTypeMenu" };
                assetTypeMenuElement2.AddToClassList("toolbar-dropdown");
                assetTypeMenuElement2.style.paddingTop = 0;
                assetTypeMenuElement2.style.paddingRight = 5;
                assetTypeMenuElement2.style.paddingBottom = 0;
                assetTypeMenuElement2.style.paddingLeft = 0;
                assetTypeMenuElement2.style.marginLeft = 0;
                assetTypeMenuElement2.style.marginTop = 0;
                assetTypeMenuElement2.style.marginRight = 0;
                assetTypeMenuElement2.style.marginBottom = 0;
                assetTypeMenuElement2.style.height = 22;
                assetTypeMenuElement2.style.borderTopWidth = 0;
                assetTypeMenuElement2.style.borderRightWidth = 0;
                assetTypeMenuElement2.style.borderBottomWidth = 0;
                assetTypeMenuElement2.style.borderLeftWidth = 0;
                assetTypeMenuElement2.style.borderTopLeftRadius = 4;
                assetTypeMenuElement2.style.borderTopRightRadius = 4;
                assetTypeMenuElement2.style.borderBottomRightRadius = 4;
                assetTypeMenuElement2.style.borderBottomLeftRadius = 4;
                assetTypeMenuElement2.style.width = 140;
                filterBox.Add(assetTypeMenuElement2);

                var upButton = new Button { text = "上" };
                upButton.style.height = 22;
                upButton.style.width = 22;
                upButton.style.marginRight = 4;
                upButton.style.marginLeft = 4;
                upButton.style.paddingRight = 2;
                upButton.style.paddingLeft = 2;
                filterBox.Add(upButton);

                var mainArea = new VisualElement { name = "mainArea" };
                mainArea.AddToClassList("main-area");
                mainArea.style.flexGrow = 1;
                mainArea.style.flexDirection = FlexDirection.Column;
                mainArea.style.height = 620;
                mainArea.style.flexShrink = 0;
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

                var unstagedTitle = new Label("UNSTAGED CHANGES");
                unstagedTitle.AddToClassList("panel-title");
                ApplyPanelTitleBaseStyle(unstagedTitle);
                unstagedTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
                unstagedHeaderRow.Add(unstagedTitle);

                var unstagedHeaderLabelElement = new Label { name = "unstagedHeaderLabel" };
                unstagedHeaderLabelElement.AddToClassList("panel-right");
                ApplyPanelRightBaseStyle(unstagedHeaderLabelElement);
                unstagedHeaderRow.Add(unstagedHeaderLabelElement);

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
                leftColumnElement.Add(unstagedListView);

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

                var stagedTitle = new Label("STAGED CHANGES");
                stagedTitle.AddToClassList("panel-title");
                ApplyPanelTitleBaseStyle(stagedTitle);
                stagedTitle.style.unityTextAlign = TextAnchor.MiddleLeft;
                stagedHeaderRow.Add(stagedTitle);

                var stagedHeaderLabelElement = new Label { name = "stagedHeaderLabel" };
                stagedHeaderLabelElement.AddToClassList("panel-right");
                ApplyPanelRightBaseStyle(stagedHeaderLabelElement);
                stagedHeaderRow.Add(stagedHeaderLabelElement);

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
                rightColumnElement.Add(stagedListView);

                var repositoryStatusLabelElement = new Label { name = "repositoryStatusLabel", enableRichText = true };
                repositoryStatusLabelElement.AddToClassList("repo-status");
                repositoryStatusLabelElement.style.fontSize = 11;
                repositoryStatusLabelElement.style.color = Rgba(229, 231, 235, 0.7f);
                repositoryStatusLabelElement.style.marginBottom = 10;
                repositoryStatusLabelElement.style.marginRight = 10;
                repositoryStatusLabelElement.style.marginLeft = 10;
                repositoryStatusLabelElement.style.marginTop = 10;
                rootContainer.Add(repositoryStatusLabelElement);

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

                var commitMessageTitleLabelElement = new Label("Commit Message");
                commitMessageTitleLabelElement.AddToClassList("commit-message-title");
                commitMessageTitleLabelElement.style.fontSize = 11;
                commitMessageTitleLabelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
                commitMessageTitleLabelElement.style.color = Color.white;
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

                var historyButtonElement = new Button { name = "historyButton", text = "记录" };
                historyButtonElement.AddToClassList("history-button");
                historyButtonElement.style.backgroundColor = Rgba(255, 255, 255, 0.06f);
                historyButtonElement.style.borderTopWidth = 1;
                historyButtonElement.style.borderRightWidth = 1;
                historyButtonElement.style.borderBottomWidth = 1;
                historyButtonElement.style.borderLeftWidth = 1;
                var historyBorder = Rgba(255, 255, 255, 0.12f);
                historyButtonElement.style.borderTopColor = historyBorder;
                historyButtonElement.style.borderRightColor = historyBorder;
                historyButtonElement.style.borderBottomColor = historyBorder;
                historyButtonElement.style.borderLeftColor = historyBorder;
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
                commitButtonElement.style.backgroundColor = Rgba(59, 130, 246, 0.9f);
                commitButtonElement.style.color = Color.white;
                commitButtonElement.style.unityFontStyleAndWeight = FontStyle.Bold;
                commitButtonElement.style.marginLeft = 4;
                commitButtonElement.style.marginRight = 4;
                commitButtonElement.style.borderTopLeftRadius = 6;
                commitButtonElement.style.borderTopRightRadius = 6;
                commitButtonElement.style.borderBottomRightRadius = 6;
                commitButtonElement.style.borderBottomLeftRadius = 6;
                commitSpacer.Add(commitButtonElement);

                var commitAndPushButtonElement = new Button { name = "commitAndPushButton", text = "提交并推送" };
                commitAndPushButtonElement.AddToClassList("commit-button-push");
                commitAndPushButtonElement.style.width = 140;
                commitAndPushButtonElement.style.height = 28;
                commitAndPushButtonElement.style.backgroundColor = Rgba(139, 92, 246, 0.9f);
                commitAndPushButtonElement.style.color = Color.white;
                commitAndPushButtonElement.style.unityFontStyleAndWeight = FontStyle.Bold;
                commitAndPushButtonElement.style.marginRight = 0;
                commitAndPushButtonElement.style.marginLeft = 4;
                commitAndPushButtonElement.style.borderTopLeftRadius = 6;
                commitAndPushButtonElement.style.borderTopRightRadius = 6;
                commitAndPushButtonElement.style.borderBottomRightRadius = 6;
                commitAndPushButtonElement.style.borderBottomLeftRadius = 6;
                commitSpacer.Add(commitAndPushButtonElement);

                var historyDropdownElement = new VisualElement { name = "historyDropdown" };
                historyDropdownElement.AddToClassList("history-modal");
                historyDropdownElement.style.display = DisplayStyle.None;
                historyDropdownElement.style.position = Position.Absolute;
                historyDropdownElement.style.left = Percent(15);
                historyDropdownElement.style.top = Percent(15);
                historyDropdownElement.style.right = Percent(15);
                historyDropdownElement.style.bottom = Percent(15);
                historyDropdownElement.style.flexGrow = 1;
                historyDropdownElement.style.paddingTop = 10;
                historyDropdownElement.style.paddingRight = 10;
                historyDropdownElement.style.paddingBottom = 10;
                historyDropdownElement.style.paddingLeft = 10;
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
                rootContainer.Add(historyDropdownElement);

                var historyListViewElement = new ListView { name = "historyListView" };
                historyListViewElement.style.flexGrow = 1;
                historyDropdownElement.Add(historyListViewElement);

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
                saveToDiskHint.style.height = 20;
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
                statusLabelElement.style.height = 20;
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

                var toastLabelElement = new Label("0") { name = "toastLabel" };
                toastLabelElement.AddToClassList("toast");
                toastLabelElement.style.display = DisplayStyle.None;
                toastLabelElement.style.position = Position.Absolute;
                toastLabelElement.style.left = Percent(20);
                toastLabelElement.style.right = Percent(20);
                toastLabelElement.style.bottom = 14;
                toastLabelElement.style.paddingTop = 10;
                toastLabelElement.style.paddingBottom = 10;
                toastLabelElement.style.paddingLeft = 12;
                toastLabelElement.style.paddingRight = 12;
                toastLabelElement.style.borderTopLeftRadius = 12;
                toastLabelElement.style.borderTopRightRadius = 12;
                toastLabelElement.style.borderBottomRightRadius = 12;
                toastLabelElement.style.borderBottomLeftRadius = 12;
                toastLabelElement.style.backgroundColor = Rgba(17, 17, 21, 0.92f);
                toastLabelElement.style.borderTopWidth = 1;
                toastLabelElement.style.borderRightWidth = 1;
                toastLabelElement.style.borderBottomWidth = 1;
                toastLabelElement.style.borderLeftWidth = 1;
                var toastBorder = Rgba(255, 255, 255, 0.12f);
                toastLabelElement.style.borderTopColor = toastBorder;
                toastLabelElement.style.borderRightColor = toastBorder;
                toastLabelElement.style.borderBottomColor = toastBorder;
                toastLabelElement.style.borderLeftColor = toastBorder;
                toastLabelElement.style.color = Rgba(229, 231, 235, 0.92f);
                toastLabelElement.style.fontSize = 11;
                toastLabelElement.style.whiteSpace = WhiteSpace.Normal;
                toastLabelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
                rootContainer.Add(toastLabelElement);

                ApplySearchFieldInternals(searchFieldElement);
                ApplyCommitFieldInternals(commitMessageFieldElement);
                CacheUIElements(root);
                return true;
            }
            catch (Exception ex)
            {
                ShowLayoutLoadError(root, $"构建 UI 失败: {ex.Message}");
                return false;
            }
        }
    }
}
