// <copyright file="SubSceneLoadFlags.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_SUBSCENE
namespace BovineLabs.Core.SubScenes
{
    using System;
    using Unity.Entities;

    [Flags]
    public enum SubSceneLoadFlags : byte
    {
        Game = 1 << 0,
        Service = 1 << 1,
#if UNITY_NETCODE
        Client = 1 << 2,
        Server = 1 << 3,
        ThinClient = 1 << 4,
#endif
        Menu = 1 << 5,
    }

    public static class SubSceneLoadFlagsUtil
    {
        public static string FormatString(SubSceneLoadFlags subSceneLoadFlags)
        {
            var s = SubSceneLoadFlags.Game | SubSceneLoadFlags.Service | SubSceneLoadFlags.Menu;
#if UNITY_NETCODE
            s |= SubSceneLoadFlags.Client | SubSceneLoadFlags.Server | SubSceneLoadFlags.ThinClient;
#endif

            return (subSceneLoadFlags & s) == s ? "All" : subSceneLoadFlags.ToString();
        }

        public static string FormatString(WorldFlags worldFlags)
        {
            return FormatString(ConvertFlags(worldFlags));
        }

        private static SubSceneLoadFlags ConvertFlags(WorldFlags targetWorld)
        {
            var flags = (SubSceneLoadFlags)0;

            if ((targetWorld & WorldFlags.Game) != 0)
            {
                flags |= SubSceneLoadFlags.Game;
            }

            if ((targetWorld & Worlds.ServiceWorld) != 0)
            {
                flags |= SubSceneLoadFlags.Service;
            }

#if UNITY_NETCODE
            if ((targetWorld & WorldFlags.GameClient) != 0)
            {
                flags |= SubSceneLoadFlags.Client;
            }

            if ((targetWorld & WorldFlags.GameServer) != 0)
            {
                flags |= SubSceneLoadFlags.Server;
            }

            if ((targetWorld & WorldFlags.GameThinClient) != 0)
            {
                flags |= SubSceneLoadFlags.ThinClient;
            }
#endif
            if ((targetWorld & Worlds.MenuWorld) != 0)
            {
                flags |= SubSceneLoadFlags.Menu;
            }

            return flags;
        }
    }
}
#endif
