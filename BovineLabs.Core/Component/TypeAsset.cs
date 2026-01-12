// <copyright file="TypeAsset.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core
{
    using System;
    using UnityEngine;

    [CreateAssetMenu(menuName = "BovineLabs/Components/Type", fileName = "Type")]
    public class TypeAsset : ScriptableObject
    {
        public const string SearchProviderType = "types";

        [SerializeField]
        private string typeName;

        public Type ResolveType()
        {

            return Type.GetType(this.typeName);
        }
    }
}
