// <copyright file="AppAPI.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if UNITY_EDITOR || BL_DEBUG
#define DEBUG_LOG
#endif

namespace BovineLabs.Core.States
{
    using System.Runtime.CompilerServices;
    using BovineLabs.Core.Collections;
    using BovineLabs.Core.Extensions;
    using BovineLabs.Core.Keys;
    using BovineLabs.Core.Utility;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;

    /// <summary> Main thread state API for managing game state, UI and input. </summary>
    public static class AppAPI
    {
        /// <summary> Gets the current state. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <returns> The current state. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TA StateCurrent<T, TA>(in EntityManager entityManager)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
            return entityManager.GetSingleton<T>().Value;
        }

        /// <summary> Gets the current state. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <returns> The current state. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TA StateCurrent<T, TA>(ref SystemState systemState)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
            return StateCurrent<T, TA>(systemState.EntityManager);
        }

        /// <summary> Checks if a state is currently enabled. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <param name="name"> The client state to check. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        /// <returns> True if the state is enabled. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool StateIsEnabled<T, TA, TS>(in EntityManager entityManager, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
            var state = KSettingsBase<TS, byte>.NameToKey(name);
            if (!entityManager.TryGetSingleton<T>(out var v))
            {
                return false;
            }

            return v.Value[state];
        }

        /// <summary> Checks if a state is currently enabled. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <param name="name"> The client state to check. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        /// <returns> True if the state is enabled. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool StateIsEnabled<T, TA, TS>(ref SystemState systemState, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
            return StateIsEnabled<T, TA, TS>(systemState.EntityManager, name);
        }

        /// <summary> Disables a specific client state. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <param name="name"> The client state to set. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateSet<T, TA, TS>(in EntityManager entityManager, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
#if DEBUG_LOG
            entityManager.GetSingleton<BLLogger>(false).LogDebug($"{GetName<T, TA>()} set to {name}");
#endif

            var state = KSettingsBase<TS, byte>.NameToKey(name);
            entityManager.SetSingleton(new T { Value = new TA { [state] = true } });
        }

        /// <summary> Disables a specific client state. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <param name="name"> The client state to set. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateSet<T, TA, TS>(ref SystemState systemState, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
            StateSet<T, TA, TS>(systemState.EntityManager, name);
        }

        /// <summary> Enables a specific client state. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <param name="state"> The client state to set. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateSet<T, TA>(in EntityManager entityManager, byte state)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
#if DEBUG_LOG
            entityManager.GetSingleton<BLLogger>(false).LogDebug($"{GetName<T, TA>()} set to {state}");
#endif

            entityManager.SetSingleton(new T { Value = new TA { [state] = true } });
        }

        /// <summary> Enables a specific client state. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <param name="state"> The client state to set. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateSet<T, TA>(ref SystemState systemState, byte state)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
            StateSet<T, TA>(systemState.EntityManager, state);
        }

        /// <summary> Enables a specific client state. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <param name="name"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateEnable<T, TA, TS>(in EntityManager entityManager, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
#if DEBUG_LOG
            entityManager.GetSingleton<BLLogger>(false).LogDebug($"{GetName<T, TA>()} enabled {name}");
#endif

            var state = KSettingsBase<TS, byte>.NameToKey(name);
            StateEnable<T, TA>(entityManager, state, true);
        }

        /// <summary> Enables a specific client state. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <param name="name"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateEnable<T, TA, TS>(ref SystemState systemState, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
            StateEnable<T, TA, TS>(systemState.EntityManager, name);
        }

        /// <summary> Enables a specific client state. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <param name="state"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateEnable<T, TA>(in EntityManager entityManager, byte state)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
#if DEBUG_LOG
            entityManager.GetSingleton<BLLogger>(false).LogDebug($"{GetName<T, TA>()} enable {state}");
#endif

            StateEnable<T, TA>(entityManager, state, true);
        }

        /// <summary> Enables a specific client state. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <param name="state"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateEnable<T, TA>(ref SystemState systemState, byte state)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
            StateEnable<T, TA>(systemState.EntityManager, state);
        }

        /// <summary> Disables a specific client state. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <param name="name"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateDisable<T, TA, TS>(in EntityManager entityManager, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
#if DEBUG_LOG
            entityManager.GetSingleton<BLLogger>(false).LogDebug($"{GetName<T, TA>()} disabled {name}");
#endif
            var state = KSettingsBase<TS, byte>.NameToKey(name);
            StateEnable<T, TA>(entityManager, state, false);
        }

        /// <summary> Disables a specific client state. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <param name="name"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        /// <typeparam name="TS"> The state type. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateDisable<T, TA, TS>(ref SystemState systemState, FixedString32Bytes name)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
            where TS : KSettingsBase<TS, byte>
        {
            StateDisable<T, TA, TS>(systemState.EntityManager, name);
        }

        /// <summary> Disables a specific client state. </summary>
        /// <param name="entityManager"> The entity manager. </param>
        /// <param name="state"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateDisable<T, TA>(in EntityManager entityManager, byte state)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
#if DEBUG_LOG
            entityManager.GetSingleton<BLLogger>(false).LogDebug($"{GetName<T, TA>()} disable {state}");
#endif
            StateEnable<T, TA>(entityManager, state, false);
        }

        /// <summary> Disables a specific client state. </summary>
        /// <param name="systemState"> The owning system state. </param>
        /// <param name="state"> The client state to disable. </param>
        /// <typeparam name="T"> The state. </typeparam>
        /// <typeparam name="TA"> The bit array size. </typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StateDisable<T, TA>(ref SystemState systemState, byte state)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
            StateDisable<T, TA>(systemState.EntityManager, state);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StateEnable<T, TA>(in EntityManager entityManager, byte state, bool enabled)
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
            var gameState = entityManager.GetSingletonRW<T>();

            ref var r = ref UnsafeUtility.As<T, BitArray256>(ref gameState.ValueRW);
            r[state] = enabled;
        }

        private static FixedString128Bytes GetName<T, TA>()
            where T : unmanaged, IState<TA>
            where TA : unmanaged, IBitArray<TA>
        {
            return TypeManagerEx.GetTypeName(TypeManager.GetTypeIndex<T>());
        }
    }
}
