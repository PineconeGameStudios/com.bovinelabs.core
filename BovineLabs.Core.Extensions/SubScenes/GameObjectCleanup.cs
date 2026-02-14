// <copyright file="GameObjectCleanup.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_SUBSCENE
namespace BovineLabs.Core.SubScenes
{
    using UnityEngine;

    [ExecuteAlways]
    [AddComponentMenu("")]
    public class GameObjectCleanup : MonoBehaviour
    {
        [HideInInspector]
        public bool IsActive;

        public void OnEnable()
        {
            if (!this.IsActive)
            {
                return;
            }

            this.IsActive = false;
            DestroyImmediate(this.gameObject);
        }
    }
}
#endif