// <copyright file="EditorWorldSafeShutdown.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Editor
{
    using BovineLabs.Core.Editor.Internal;
    using UnityEditor;

    /// <summary> Workaround to fix entities 1.X errors when changing play mode states and have an entity selected and it errors. </summary>
    internal static class EditorWorldSafeShutdown
    {
        internal static void Initialize()
        {
            EditorApplication.playModeStateChanged += _ => EntitySelection.UnSelect();
        }
    }
}
