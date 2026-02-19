// <copyright file="RelevancySettings.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_RELEVANCY && UNITY_NETCODE
namespace BovineLabs.Core.Authoring.Relevancy
{
    using BovineLabs.Core.Authoring.Settings;
    using BovineLabs.Core.Relevancy;
    using BovineLabs.Core.Settings;
    using Unity.Entities;
    using UnityEngine;

    [SettingsGroup("Core")]
    [SettingsWorld("Server")]
    public class RelevancySettings : SettingsBase
    {
        [SerializeField]
        private float clampExtents = 150 / 2f; // Half size

        [SerializeField]
        private float expandExtents = 8;

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);

            baker.AddComponent(entity, new RelevanceConfig
            {
                ClampExtents = this.clampExtents,
                ExpandExtents = this.expandExtents,
            });
        }
    }
}
#endif