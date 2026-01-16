// <copyright file="ComponentAssetDrawer.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Editor.Component
{
    using BovineLabs.Core;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    [CustomPropertyDrawer(typeof(ComponentAsset))]
    public class ComponentAssetDrawer : PropertyDrawer
    {
        /// <inheritdoc/>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                },
            };

            var field = new PropertyField(property)
            {
                style =
                {
                    flexGrow = 1f,
                },
            };

            var propertyCopy = property.Copy();
            var button = new Button(() => CreateAsset(propertyCopy)) { text = "+" };
            button.style.flexShrink = 0f;
            button.style.marginLeft = 2f;

            container.Add(field);
            container.Add(button);

            return container;
        }

        private static void CreateAsset(SerializedProperty property)
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Component Asset",
                "Component",
                "asset",
                "Choose a location for the new ComponentAsset.");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var asset = ScriptableObject.CreateInstance<ComponentAsset>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            property.objectReferenceValue = asset;
            property.serializedObject.ApplyModifiedProperties();
        }
    }
}
