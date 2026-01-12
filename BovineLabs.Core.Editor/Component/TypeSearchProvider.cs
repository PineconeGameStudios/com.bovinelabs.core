// <copyright file="TypeSearchProvider.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Editor.Component
{
    using System;
    using System.Collections.Generic;
    using BovineLabs.Core.Utility;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Editor.Bridge;
    using UnityEditor;
    using UnityEditor.Search;
    using UnityEditor.ShortcutManagement;

    public static class TypeSearchProvider
    {
        private static QueryEngine<TypeDescriptor>? queryEngine;

        private static QueryEngine<TypeDescriptor> QueryEngine => queryEngine ??= SetupQueryEngine();

        [SearchItemProvider]
        private static SearchProvider CreateProvider()
        {
            return new SearchProvider(TypeAsset.SearchProviderType, "Types")
            {
                filterId = "at:",
                isExplicitProvider = true,
                active = true,
                showDetails = true,
                fetchItems = FetchItems,
                fetchPropositions = FetchPropositions,
            };
        }

        [MenuItem("Window/Search/Types", priority = 1391)]
        private static void OpenProviderMenu()
        {
            OpenProvider();
        }

        [Shortcut("Help/Quick Search/Types")]
        private static void PopQuickSearch()
        {
            OpenProvider();
        }

        private static void OpenProvider()
        {
            SearchService.ShowContextual(TypeAsset.SearchProviderType);
        }

        private static IEnumerable<SearchItem> FetchItems(SearchContext context, List<SearchItem> items, SearchProvider provider)
        {
            var searchQuery = context.searchQuery;

            ParsedQuery<TypeDescriptor>? query = null;

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = QueryEngine.ParseQuery(context.searchQuery);
                if (!query.valid)
                {
                    query = null;
                }
            }

            var toFilter = new TypeDescriptor[1];

            var score = 0;
            foreach (var type in ReflectionUtility.AllTypes)
            {
                toFilter[0] = new TypeDescriptor(type);

                foreach (var data in query?.Apply(toFilter) ?? toFilter)
                {
                    yield return provider.CreateItem(context, data.FullName, score++, data.Name, data.SimplifiedQualifiedName, null, data.FullName);
                }
            }
        }

        private static IEnumerable<SearchProposition> FetchPropositions(SearchContext context, SearchPropositionOptions options)
        {
            foreach (var p in SearchBridge.GetPropositions(QueryEngine))
            {
                yield return p;
            }

            // foreach (var l in SearchBridge.GetPropositionsFromListBlockType(typeof(InheritTypeBlock)))
            // {
            //     yield return l;
            // }
        }

        private static QueryEngine<TypeDescriptor> SetupQueryEngine()
        {
            var query = new QueryEngine<TypeDescriptor>();
            query.SetSearchDataCallback(GetWords);

            SearchBridge.SetFilter(query, "unmanaged", data => data.IsUnmanaged)
                .AddOrUpdateProposition(category: null, label: "Is Unmanaged", replacement: "unmanaged=true", help: "Limit search to unmanaged types");

            SearchBridge.SetFilter(query, "unityobject", data => data.IsUnityObject)
                .AddOrUpdateProposition(category: null, label: "Is Unity Object", replacement: "unityobject=true", help: "Limit search to Unity Objects");

            query.AddFilter<string>("inherit", OnInheritFilter, /*Transformer,*/ new[] { "=", ":" });
            // query.TryGetFilter("inherit", out var inherit);
            // inherit.AddOrUpdateProposition(category: null, label: "Inherits", replacement: "inherit:", help: "Search Entry by Inheritance");

            return query;
        }

        private static bool OnInheritFilter(TypeDescriptor descriptor, string operatorToken, string filterValue)
        {
            var type = Type.GetType(filterValue); // this is awful but i can't seem to figure it out
            return type != null && type.IsAssignableFrom(descriptor.Type);
        }

        private static IEnumerable<string> GetWords(TypeDescriptor desc)
        {
            yield return desc.Name;
        }

        private readonly struct TypeDescriptor
        {
            public readonly Type Type;

            public TypeDescriptor(Type type)
            {
                this.Type = type;
            }

            public string Name => this.Type.Name;

            public string SimplifiedQualifiedName => $"{this.Type.FullName}, {this.Type.Assembly.GetName().Name}";

            public string FullName => this.Type.AssemblyQualifiedName;

            public bool IsUnmanaged => UnsafeUtility.IsUnmanaged(this.Type);

            public bool IsUnityObject => typeof(UnityEngine.Object).IsAssignableFrom(this.Type);
        }
        //
        // [QueryListBlock("Inherit", "inherit", "inherit")]
        // private class InheritTypeBlock : QueryListBlock
        // {
        //     public InheritTypeBlock(IQuerySource source, string id, string value, QueryListBlockAttribute attr)
        //         : base(source, id, value, attr)
        //     {
        //     }
        //
        //     public override IEnumerable<SearchProposition> GetPropositions(SearchPropositionFlags flags = SearchPropositionFlags.None)
        //     {
        //         var c = flags.HasFlag(SearchPropositionFlags.NoCategory) ? null : this.category;
        //
        //         foreach (var type in ReflectionUtility.GetAllImplementations<UnityEngine.Object>())
        //         {
        //             var simplifiedName = $"{type.FullName}, {type.Assembly.GetName().Name}";
        //
        //             yield return new SearchProposition(c, simplifiedName, simplifiedName, type: this.GetType(), data: simplifiedName);
        //         }
        //     }
        // }
    }
}