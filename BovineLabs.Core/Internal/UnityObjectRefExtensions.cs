// <copyright file="UnityObjectRefExtensions.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Internal
{
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;
    using UnityEngine;

    public static class UnityObjectRefExtensions
    {
        public static int GetInstanceId<T>(this UnityObjectRef<T> unityObjectRef)
            where T : Object
        {
            return unityObjectRef.Id.instanceId;
        }

        public static void SetInstanceId<T>(this ref UnityObjectRef<T> unityObjectRef, int instanceId)
            where T : Object
        {
            unityObjectRef.Id.instanceId = instanceId;
        }

        public static void SetInstanceId<T>(this ref UnityObjectRef<T> unityObjectRef, EntityId entityId)
            where T : Object
        {
            unityObjectRef.Id.instanceId = UnsafeUtility.As<EntityId, int>(ref entityId);
        }
    }
}
