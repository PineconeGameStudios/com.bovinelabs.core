// <copyright file="TypeAssetEditor.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Editor.Component
{
    using System.Collections.Generic;
    using BovineLabs.Core.Editor.Inspectors;
    using BovineLabs.Core.Editor.SearchWindow;
    using BovineLabs.Core.Editor.UI;
    using BovineLabs.Core.Utility;
    using UnityEditor;
    using UnityEditor.Search;
    using UnityEngine;
    using UnityEngine.Search;
    using UnityEngine.UIElements;

    [CustomEditor(typeof(TypeAsset))]
    public class TypeAssetEditor : ElementEditor
    {
        private static readonly List<SearchView.Item> TypeList = new();

        private Button? button;

        protected override VisualElement? CreateElement(SerializedProperty property)
        {
            return property.name switch
            {
                "typeName" => this.button = new Button(() => this.Search(property)) { text = property.stringValue },
                _ => base.CreateElement(property),
            };
        }

        private void Search(SerializedProperty property)
        {
            var context = SearchService.CreateContext(TypeAsset.SearchProviderType, "unmanaged=true");

            var viewState = new SearchViewState(context, SearchViewFlags.ListView | SearchViewFlags.OpenInBuilderMode | SearchViewFlags.DisableSavedSearchQuery | SearchViewFlags.CompactView)
            {
                windowTitle = new GUIContent("Type Selector"),
                title = "Select Type",
                position = SearchUtils.GetMainWindowCenteredPosition(new Vector2(600, 400)),
                selectHandler = (item, canceled) =>
                {
                    if (canceled || item == null)
                    {
                        return;
                    }

                    property.stringValue = item.data as string ?? string.Empty;
                    property.serializedObject.ApplyModifiedProperties();
                    this.button!.text = property.stringValue;
                },
            };
            SearchService.ShowPicker(viewState);
        }
    }
}
