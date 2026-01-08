// <copyright file="FacetGenerator.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.FacetGenerator
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;
    using CodeGenHelpers;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal enum FacetFieldKind
    {
        RefRW,
        RefRO,
        EnabledRefRW,
        EnabledRefRO,
        DynamicBuffer,
        Entity,
        EntityStorageInfo,
        EntityStorageInfoLookup,
        ComponentLookup,
        BufferLookup,
        Singleton,
        Facet,
    }

    [Generator]
    public class FacetGenerator : IIncrementalGenerator
    {
        internal static readonly SymbolDisplayFormat ShortTypeFormat = CreateShortTypeFormat();

        private static SymbolDisplayFormat CreateShortTypeFormat()
        {
            var format = SymbolDisplayFormat.MinimallyQualifiedFormat;

            return new SymbolDisplayFormat(
                format.GlobalNamespaceStyle,
                SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                format.GenericsOptions,
                format.MemberOptions,
                format.DelegateStyle,
                format.ExtensionMethodStyle,
                format.ParameterOptions,
                format.PropertyStyle,
                format.LocalOptions,
                format.KindOptions,
                format.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var facetSymbols = context.CompilationProvider.Select(static (compilation, _) => FacetSymbols.Create(compilation));

            var candidates = context.SyntaxProvider
                .CreateSyntaxProvider(predicate: IsSyntaxTargetForGeneration, transform: GetFacetCandidate)
                .Where(c => c != null);

            var inputs = candidates.Combine(facetSymbols)
                .Select((pair, cancellationToken) => GetSemanticTargetForGeneration(pair.Left, pair.Right, cancellationToken))
                .Where(r => r != null);

            context.RegisterSourceOutput(inputs, static (ctx, result) => Execute(ctx, result));
        }

        private static bool IsSyntaxTargetForGeneration(SyntaxNode syntaxNode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (syntaxNode is not TypeDeclarationSyntax typeDeclaration)
            {
                return false;
            }

            if (typeDeclaration.Kind() != SyntaxKind.StructDeclaration && typeDeclaration.Kind() != SyntaxKind.ClassDeclaration)
            {
                return false;
            }

            if (typeDeclaration.BaseList == null)
            {
                return false;
            }

            foreach (var baseType in typeDeclaration.BaseList.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (baseType.Type is IdentifierNameSyntax { Identifier: { ValueText: "IFacet" } })
                {
                    return true;
                }

                if (baseType.Type is QualifiedNameSyntax { Right: { Identifier: { ValueText: "IFacet" } } })
                {
                    return true;
                }
            }

            return false;
        }

        private static FacetCandidate GetFacetCandidate(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
        {
            var typeSyntax = (TypeDeclarationSyntax)ctx.Node;
            var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(typeSyntax, cancellationToken);
            if (typeSymbol == null)
            {
                return null;
            }

            return new FacetCandidate(typeSyntax, typeSymbol);
        }

        private static FacetResult GetSemanticTargetForGeneration(FacetCandidate candidate, FacetSymbols symbols, CancellationToken cancellationToken)
        {
            var typeSyntax = candidate.TypeSyntax;
            var typeSymbol = candidate.TypeSymbol;

            var diagnostics = new List<Diagnostic>();

            var facetInterface = symbols.FacetInterface;
            if (facetInterface == null || !typeSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, facetInterface)))
            {
                return null;
            }

            if (typeSymbol.TypeKind != TypeKind.Struct)
            {
                return new FacetResult(null, new[] { FacetDiagnostics.NonStructFacet(typeSymbol, typeSyntax.Identifier.GetLocation()) });
            }

            if (!typeSyntax.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                diagnostics.Add(FacetDiagnostics.MissingPartial(typeSymbol, typeSyntax.Identifier.GetLocation()));
            }

            INamedTypeSymbol optionalAttribute = symbols.OptionalAttribute;
            INamedTypeSymbol facetAttribute = symbols.FacetAttribute;
            INamedTypeSymbol readOnlyAttribute = symbols.ReadOnlyAttribute;
            INamedTypeSymbol singletonAttribute = symbols.SingletonAttribute;
            INamedTypeSymbol entityType = symbols.EntityType;
            INamedTypeSymbol entityStorageInfoType = symbols.EntityStorageInfoType;
            INamedTypeSymbol entityStorageInfoLookupType = symbols.EntityStorageInfoLookupType;
            INamedTypeSymbol componentLookupType = symbols.ComponentLookupType;
            INamedTypeSymbol bufferLookupType = symbols.BufferLookupType;

            var fields = new List<FacetField>();
            foreach (var fieldSymbol in typeSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (fieldSymbol.IsStatic)
                {
                    continue;
                }

                if (TryCreateFacetField(
                    fieldSymbol,
                    optionalAttribute,
                    facetAttribute,
                    readOnlyAttribute,
                    singletonAttribute,
                    entityType,
                    entityStorageInfoType,
                    entityStorageInfoLookupType,
                    componentLookupType,
                    bufferLookupType,
                    facetInterface,
                    diagnostics,
                    out var field))
                {
                    fields.Add(field);
                }
            }

            if (fields.Count == 0)
            {
                diagnostics.Add(FacetDiagnostics.NoFields(typeSymbol, typeSyntax.Identifier.GetLocation()));
            }

            ValidateFacetGraph(typeSymbol, facetAttribute, facetInterface, diagnostics);

            var hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            IReadOnlyList<FacetSingletonDependency> singletonDependencies = Array.Empty<FacetSingletonDependency>();
            IReadOnlyList<QueryBuilderInvocation> queryBuilderInvocations = Array.Empty<QueryBuilderInvocation>();

            if (!hasErrors && fields.Count > 0)
            {
                var singletonCache = new Dictionary<FacetTraversalKey, IReadOnlyList<FacetSingletonDependency>>();
                singletonDependencies = CollectSingletonDependencies(typeSymbol, fields, facetAttribute, singletonAttribute, readOnlyAttribute, facetInterface, singletonCache);
                queryBuilderInvocations = CollectQueryBuilderInvocations(
                    typeSymbol,
                    fields,
                    optionalAttribute,
                    facetAttribute,
                    readOnlyAttribute,
                    singletonAttribute,
                    entityType,
                    entityStorageInfoType,
                    entityStorageInfoLookupType,
                    componentLookupType,
                    bufferLookupType,
                    facetInterface);
            }

            var data = !hasErrors && fields.Count > 0
                ? new FacetData(typeSymbol, fields, singletonDependencies, queryBuilderInvocations)
                : null;

            return new FacetResult(data, diagnostics);
        }

        private static IReadOnlyList<FacetSingletonDependency> CollectSingletonDependencies(
            INamedTypeSymbol typeSymbol,
            IReadOnlyList<FacetField> fields,
            INamedTypeSymbol facetAttribute,
            INamedTypeSymbol singletonAttribute,
            INamedTypeSymbol readOnlyAttribute,
            INamedTypeSymbol facetInterface,
            IDictionary<FacetTraversalKey, IReadOnlyList<FacetSingletonDependency>> singletonCache)
        {
            var dependencies = new List<FacetSingletonDependency>();

            foreach (var field in fields)
            {
                if (field.IsSingleton)
                {
                    var parameterName = CreateSingletonParameterName(null, field.FieldName);
                    dependencies.Add(new FacetSingletonDependency(parameterName, field));
                    continue;
                }

                if (!field.IsFacet || field.ComponentTypeSymbol is not INamedTypeSymbol facetType)
                {
                    field.SetFacetSingletonDependencies(Array.Empty<FacetSingletonDependency>());
                    continue;
                }

                var nestedDependencies = CollectFacetSingletonDependencies(
                    facetType,
                    facetAttribute,
                    singletonAttribute,
                    readOnlyAttribute,
                    facetInterface,
                    new[] { field.FieldName },
                    singletonCache,
                    new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { typeSymbol });

                field.SetFacetSingletonDependencies(nestedDependencies);
                dependencies.AddRange(nestedDependencies);
            }

            return dependencies;
        }

        private static IReadOnlyList<FacetSingletonDependency> CollectFacetSingletonDependencies(
            INamedTypeSymbol facetType,
            INamedTypeSymbol facetAttribute,
            INamedTypeSymbol singletonAttribute,
            INamedTypeSymbol readOnlyAttribute,
            INamedTypeSymbol facetInterface,
            IReadOnlyList<string> path,
            IDictionary<FacetTraversalKey, IReadOnlyList<FacetSingletonDependency>> singletonCache,
            ISet<INamedTypeSymbol> recursionStack)
        {
            var cacheKey = new FacetTraversalKey(facetType, CreatePathKey(path));
            if (singletonCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var dependencies = new List<FacetSingletonDependency>();

            if (!recursionStack.Add(facetType))
            {
                singletonCache[cacheKey] = dependencies;
                return dependencies;
            }

            foreach (var fieldSymbol in facetType.GetMembers().OfType<IFieldSymbol>())
            {
                var attributes = fieldSymbol.GetAttributes();

                if (fieldSymbol.IsStatic)
                {
                    continue;
                }

                if (HasAttribute(attributes, singletonAttribute))
                {
                    var hasReadOnlyAttribute = HasAttribute(attributes, readOnlyAttribute);
                    var singletonField = new FacetField(fieldSymbol, fieldSymbol.Type, FacetFieldKind.Singleton, false, true, hasReadOnlyAttribute);
                    var parameterName = CreateSingletonParameterName(path, singletonField.FieldName);
                    dependencies.Add(new FacetSingletonDependency(parameterName, singletonField));
                    continue;
                }

                if (!HasAttribute(attributes, facetAttribute) ||
                    fieldSymbol.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } nestedFacetType ||
                    facetInterface == null ||
                    !nestedFacetType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, facetInterface)))
                {
                    continue;
                }

                var nestedPath = new List<string>(path) { fieldSymbol.Name };
                var nestedDependencies = CollectFacetSingletonDependencies(
                    nestedFacetType,
                    facetAttribute,
                    singletonAttribute,
                    readOnlyAttribute,
                    facetInterface,
                    nestedPath,
                    singletonCache,
                    recursionStack);

                dependencies.AddRange(nestedDependencies);
            }

            recursionStack.Remove(facetType);
            singletonCache[cacheKey] = dependencies;
            return dependencies;
        }

        private static void ValidateFacetGraph(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol facetAttribute,
            INamedTypeSymbol facetInterface,
            IList<Diagnostic> diagnostics)
        {
            if (facetAttribute == null || facetInterface == null)
            {
                return;
            }

            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var recursionStack = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            void Walk(INamedTypeSymbol current)
            {
                if (!recursionStack.Add(current))
                {
                    return;
                }

                foreach (var fieldSymbol in current.GetMembers().OfType<IFieldSymbol>())
                {
                    if (fieldSymbol.IsStatic)
                    {
                        continue;
                    }

                    var attributes = fieldSymbol.GetAttributes();
                    if (!HasAttribute(attributes, facetAttribute))
                    {
                        continue;
                    }

                    if (fieldSymbol.Type is not INamedTypeSymbol nestedFacetType || nestedFacetType.TypeKind != TypeKind.Struct)
                    {
                        continue;
                    }

                    if (!nestedFacetType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, facetInterface)))
                    {
                        continue;
                    }

                    if (recursionStack.Contains(nestedFacetType))
                    {
                        diagnostics.Add(FacetDiagnostics.FacetCycle(fieldSymbol, fieldSymbol.Locations.FirstOrDefault(), nestedFacetType));
                        continue;
                    }

                    if (!visited.Contains(nestedFacetType))
                    {
                        Walk(nestedFacetType);
                    }
                }

                recursionStack.Remove(current);
                visited.Add(current);
            }

            Walk(typeSymbol);
        }

        private static IReadOnlyList<QueryBuilderInvocation> CollectQueryBuilderInvocations(
            INamedTypeSymbol typeSymbol,
            IReadOnlyList<FacetField> fields,
            INamedTypeSymbol optionalAttribute,
            INamedTypeSymbol facetAttribute,
            INamedTypeSymbol readOnlyAttribute,
            INamedTypeSymbol singletonAttribute,
            INamedTypeSymbol entityType,
            INamedTypeSymbol entityStorageInfoType,
            INamedTypeSymbol entityStorageInfoLookupType,
            INamedTypeSymbol componentLookupType,
            INamedTypeSymbol bufferLookupType,
            INamedTypeSymbol facetInterface)
        {
            var invocations = new List<QueryBuilderInvocation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var visitedFacets = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { typeSymbol };

            foreach (var field in fields)
            {
                AddField(field);
            }

            return invocations;

            void AddInvocation(FacetField field)
            {
                var invocation = GetQueryBuilderInvocation(field);
                if (seen.Add(invocation))
                {
                    invocations.Add(new QueryBuilderInvocation(invocation, field.ComponentTypeSymbol));
                }
            }

            void AddField(FacetField field)
            {
                if (field.IsFacet)
                {
                    if (field.IsOptional)
                    {
                        return;
                    }

                    if (field.ComponentTypeSymbol is INamedTypeSymbol nestedFacetType)
                    {
                        AddFacetType(nestedFacetType);
                    }

                    return;
                }

                if (ShouldAddQueryBuilderInvocation(field))
                {
                    AddInvocation(field);
                }
            }

            void AddFacetType(INamedTypeSymbol facetType)
            {
                if (!visitedFacets.Add(facetType))
                {
                    return;
                }

                foreach (var fieldSymbol in facetType.GetMembers().OfType<IFieldSymbol>())
                {
                    if (fieldSymbol.IsStatic)
                    {
                        continue;
                    }

                    if (!TryCreateFacetField(
                        fieldSymbol,
                        optionalAttribute,
                        facetAttribute,
                        readOnlyAttribute,
                        singletonAttribute,
                        entityType,
                        entityStorageInfoType,
                        entityStorageInfoLookupType,
                        componentLookupType,
                        bufferLookupType,
                        facetInterface,
                        null,
                        out var nestedField))
                    {
                        continue;
                    }

                    AddField(nestedField);
                }
            }
        }

        private static string CreateSingletonParameterName(IReadOnlyList<string> path, string fieldName)
        {
            if (path == null || path.Count == 0)
            {
                return Camelize(fieldName);
            }

            var name = path[0];

            for (var i = 1; i < path.Count; i++)
            {
                name += Pascalize(path[i]);
            }

            return Camelize($"{name}{Pascalize(fieldName)}");
        }

        private static string CreatePathKey(IReadOnlyList<string> path)
        {
            if (path == null || path.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(".", path);
        }

        private static bool TryCreateFacetField(
            IFieldSymbol fieldSymbol,
            INamedTypeSymbol optionalAttribute,
            INamedTypeSymbol facetAttribute,
            INamedTypeSymbol readOnlyAttribute,
            INamedTypeSymbol singletonAttribute,
            INamedTypeSymbol entityType,
            INamedTypeSymbol entityStorageInfoType,
            INamedTypeSymbol entityStorageInfoLookupType,
            INamedTypeSymbol componentLookupType,
            INamedTypeSymbol bufferLookupType,
            INamedTypeSymbol facetInterface,
            IList<Diagnostic> diagnostics,
            out FacetField field)
        {
            field = null;

            var attributes = fieldSymbol.GetAttributes();

            var hasSingletonAttribute = HasAttribute(attributes, singletonAttribute);
            var hasFacetAttribute = HasAttribute(attributes, facetAttribute);
            var hasOptionalAttribute = HasAttribute(attributes, optionalAttribute);
            var hasReadOnlyAttribute = HasAttribute(attributes, readOnlyAttribute);

            if (hasSingletonAttribute && (hasFacetAttribute || hasOptionalAttribute))
            {
                diagnostics?.Add(FacetDiagnostics.SingletonAttributeConflict(fieldSymbol, fieldSymbol.Locations.FirstOrDefault()));
                return false;
            }

            var isOptional = !hasSingletonAttribute && hasOptionalAttribute;
            var isFacetField = !hasSingletonAttribute && hasFacetAttribute;

            if (hasSingletonAttribute)
            {
                if (fieldSymbol.Type is INamedTypeSymbol { Name: "DynamicBuffer", TypeArguments: { Length: 1 } } && !hasReadOnlyAttribute)
                {
                    diagnostics?.Add(FacetDiagnostics.ReadOnlySingletonBuffer(fieldSymbol, fieldSymbol.Locations.FirstOrDefault()));
                    return false;
                }

                field = new FacetField(fieldSymbol, fieldSymbol.Type, FacetFieldKind.Singleton, false, true, hasReadOnlyAttribute);
                return true;
            }

            if (isFacetField)
            {
                if (facetInterface != null &&
                    fieldSymbol.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } facetType &&
                    facetType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, facetInterface)))
                {
                    field = new FacetField(fieldSymbol, facetType, FacetFieldKind.Facet, isOptional, hasReadOnlyAttribute, hasReadOnlyAttribute);
                    return true;
                }

                diagnostics?.Add(FacetDiagnostics.InvalidFacetField(fieldSymbol, fieldSymbol.Locations.FirstOrDefault()));
                return false;
            }

            if (entityType != null && SymbolEqualityComparer.Default.Equals(fieldSymbol.Type, entityType))
            {
                field = new FacetField(fieldSymbol, fieldSymbol.Type, FacetFieldKind.Entity, isOptional, true, hasReadOnlyAttribute);
                return true;
            }

            if (entityStorageInfoType != null && SymbolEqualityComparer.Default.Equals(fieldSymbol.Type, entityStorageInfoType))
            {
                field = new FacetField(fieldSymbol, fieldSymbol.Type, FacetFieldKind.EntityStorageInfo, isOptional, true, hasReadOnlyAttribute);
                return true;
            }

            if (entityStorageInfoLookupType != null && SymbolEqualityComparer.Default.Equals(fieldSymbol.Type, entityStorageInfoLookupType))
            {
                field = new FacetField(fieldSymbol, fieldSymbol.Type, FacetFieldKind.EntityStorageInfoLookup, isOptional, true, hasReadOnlyAttribute);
                return true;
            }

            if (fieldSymbol.Type is not INamedTypeSymbol namedType || namedType.TypeArguments.Length != 1)
            {
                diagnostics?.Add(FacetDiagnostics.UnsupportedField(fieldSymbol, fieldSymbol.Locations.FirstOrDefault()));
                return false;
            }

            if (componentLookupType != null && SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, componentLookupType))
            {
                field = new FacetField(fieldSymbol, namedType.TypeArguments[0], FacetFieldKind.ComponentLookup, isOptional, hasReadOnlyAttribute, hasReadOnlyAttribute);
                return true;
            }

            if (bufferLookupType != null && SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, bufferLookupType))
            {
                field = new FacetField(fieldSymbol, namedType.TypeArguments[0], FacetFieldKind.BufferLookup, isOptional, hasReadOnlyAttribute, hasReadOnlyAttribute);
                return true;
            }

            FacetFieldKind kind;
            switch (namedType.Name)
            {
                case "RefRW" when hasReadOnlyAttribute:
                    diagnostics?.Add(FacetDiagnostics.ReadOnlyRefRW(fieldSymbol, fieldSymbol.Locations.FirstOrDefault()));
                    return false;
                case "RefRW":
                    kind = FacetFieldKind.RefRW;
                    break;
                case "RefRO":
                    kind = FacetFieldKind.RefRO;
                    break;
                case "EnabledRefRW" when hasReadOnlyAttribute:
                    diagnostics?.Add(FacetDiagnostics.ReadOnlyRefRW(fieldSymbol, fieldSymbol.Locations.FirstOrDefault()));
                    return false;
                case "EnabledRefRW":
                    kind = FacetFieldKind.EnabledRefRW;
                    break;
                case "EnabledRefRO":
                    kind = FacetFieldKind.EnabledRefRO;
                    break;
                case "DynamicBuffer":
                    kind = FacetFieldKind.DynamicBuffer;
                    break;
                default:
                    diagnostics?.Add(FacetDiagnostics.UnsupportedField(fieldSymbol, fieldSymbol.Locations.FirstOrDefault()));
                    return false;
            }

            var componentType = namedType.TypeArguments[0];
            var isReadOnly =
                kind == FacetFieldKind.RefRO ||
                kind == FacetFieldKind.EnabledRefRO ||
                hasReadOnlyAttribute && kind != FacetFieldKind.EnabledRefRW;

            field = new FacetField(fieldSymbol, componentType, kind, isOptional, isReadOnly, hasReadOnlyAttribute);
            return true;
        }

        private static void Execute(SourceProductionContext context, FacetResult result)
        {
            try
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                var data = result.Data;
                if (data == null)
                {
                    return;
                }

                var builder = Generate(data);
                if (builder == null)
                {
                    return;
                }

                var source = builder.Build();

                var hintName = $"{data.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)).Replace('<', '[').Replace('>', ']')}.IFacet.g.cs";
                context.AddSource(hintName, source);
            }
            catch (Exception ex)
            {
                SourceGenHelpers.Log(ex.ToString());
            }
        }

        private static CodeBuilder Generate(FacetData data)
        {
            var builder = CodeBuilder
                .Create(data.TypeSymbol.ContainingNamespace.ToDisplayString());

            var namespaces = new HashSet<string>(StringComparer.Ordinal)
            {
                "Unity.Collections",
                "Unity.Entities",
                "BovineLabs.Core.Extensions",
            };

            AddDeclaredUsingNamespaces(data.TypeSymbol, namespaces);
            AddRequiredTypeNamespaces(data, namespaces);

            foreach (var ns in namespaces)
            {
                builder.AddNamespaceImport(ns);
            }

            ResolveResolvedFieldNameConflicts(data.Fields);

            var typeBuilder = builder
                .AddClass(data.TypeSymbol.Name)
                .WithAccessModifier(data.TypeSymbol.DeclaredAccessibility)
                .OfType(TypeKind.Struct)
                .ReadOnly(data.TypeSymbol.IsReadOnly);

            typeBuilder.WithSummary($"Facet helpers generated for {data.TypeName}.");
            AddConstructor(typeBuilder, data);
            AddLookup(typeBuilder, data);
            AddResolvedChunk(typeBuilder, data);
            AddTypeHandle(typeBuilder, data);
            AddSingletonData(typeBuilder, data);
            AddCreateQueryBuilder(typeBuilder, data);

            return builder;
        }

        private static void AddDeclaredUsingNamespaces(INamedTypeSymbol typeSymbol, ISet<string> namespaces)
        {
            foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeSyntax)
                {
                    continue;
                }

                var compilationUnit = typeSyntax.SyntaxTree.GetCompilationUnitRoot();
                AddUsingDirectives(compilationUnit.Usings, namespaces);

                foreach (var namespaceSyntax in typeSyntax.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
                {
                    AddUsingDirectives(namespaceSyntax.Usings, namespaces);
                }
            }
        }

        private static void AddRequiredTypeNamespaces(FacetData data, ISet<string> namespaces)
        {
            foreach (var field in data.Fields)
            {
                AddTypeNamespaces(field.ComponentTypeSymbol, namespaces);
                AddTypeNamespaces(field.Symbol.Type, namespaces);
            }

            foreach (var invocation in data.QueryBuilderInvocations)
            {
                AddTypeNamespaces(invocation.ComponentTypeSymbol, namespaces);
            }
        }

        private static void AddTypeNamespaces(ITypeSymbol typeSymbol, ISet<string> namespaces)
        {
            if (typeSymbol == null)
            {
                return;
            }

            switch (typeSymbol)
            {
                case INamedTypeSymbol namedType:
                    if (!namedType.ContainingNamespace.IsGlobalNamespace)
                    {
                        namespaces.Add(namedType.ContainingNamespace.ToDisplayString());
                    }

                    foreach (var typeArgument in namedType.TypeArguments)
                    {
                        AddTypeNamespaces(typeArgument, namespaces);
                    }

                    break;
                case IArrayTypeSymbol arrayType:
                    AddTypeNamespaces(arrayType.ElementType, namespaces);
                    break;
                case IPointerTypeSymbol pointerType:
                    AddTypeNamespaces(pointerType.PointedAtType, namespaces);
                    break;
            }
        }

        private static void AddUsingDirectives(SyntaxList<UsingDirectiveSyntax> directives, ISet<string> namespaces)
        {
            foreach (var directive in directives)
            {
                var name = directive.Name.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (directive.Alias != null)
                {
                    var alias = directive.Alias.Name.ToString();
                    if (!string.IsNullOrWhiteSpace(alias))
                    {
                        namespaces.Add($"{alias} = {name}");
                    }

                    continue;
                }

                if (directive.StaticKeyword != default)
                {
                    namespaces.Add($"static {name}");
                    continue;
                }

                namespaces.Add(name);
            }
        }

        private static void AddConstructor(ClassBuilder typeBuilder, FacetData data)
        {
            var ctor = typeBuilder.AddConstructor(Accessibility.Public)
                .WithSummary($"Initializes a new instance of {data.TypeName}.");
            foreach (var field in data.Fields)
            {
                ctor.AddParameter(field.FieldTypeName, field.FieldName);
            }

            ctor.WithBody(body =>
            {
                foreach (var field in data.Fields)
                {
                    body.AppendLine($"this.{field.FieldName} = {field.FieldName};");
                }
            });
        }

        private static void AddCreateQueryBuilder(ClassBuilder typeBuilder, FacetData data)
        {
            var queries = data.QueryBuilderInvocations;

            var method = typeBuilder
                .AddMethod("CreateQueryBuilder", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("EntityQueryBuilder")
                .WithSummary($"Creates an EntityQueryBuilder requesting the required components for {data.TypeName}.")
                .WithParameterDoc("allocator", "Allocator used for the query builder.");

            method.AddParameterWithDefaultValue("Allocator", "allocator", "Allocator.Temp");

            method.WithBody(body =>
            {
                var chain = queries.Count == 0
                    ? string.Empty
                    : string.Concat(queries.Select(q => $".{q.Invocation}"));

                body.AppendLine($"return new EntityQueryBuilder(allocator){chain};");
            });
        }

        private static bool ShouldAddQueryBuilderInvocation(FacetField field)
        {
            return !field.IsOptional &&
                   !field.IsSingleton &&
                   !field.IsFacet &&
                   !field.IsEntity &&
                   !field.IsEntityStorageInfo &&
                   !field.IsEntityStorageInfoLookup &&
                   !field.IsComponentLookup &&
                   !field.IsBufferLookup;
        }

        private static void AddLookup(ClassBuilder typeBuilder, FacetData data)
        {
            var lookup = typeBuilder.AddNestedClass("Lookup", true, Accessibility.Public)
                .IsStruct()
                .WithSummary($"Provides entity-level access to {data.TypeName}.");

            var lookupFields = data.Fields.Where(f => !f.IsSingleton && !f.IsEntity).ToArray();
            var lookupSlots = GetLookupSlots(lookupFields);
            var singletonFields = data.Fields.Where(f => f.IsSingleton).ToArray();
            var singletonDependencies = data.SingletonDependencies;
            var singletonParameterNames = singletonDependencies
                .Where(dependency => dependency.Field.IsSingleton)
                .ToDictionary(dependency => dependency.Field, dependency => dependency.ParameterName);

            foreach (var slot in lookupSlots)
            {
                var field = slot.Field;
                var lookupField = lookup.AddProperty(field.LookupFieldName, Accessibility.Public).SetType(field.LookupTypeName);

                if (slot.IsReadOnly)
                {
                    lookupField.AddAttribute("ReadOnly");
                }
            }

            foreach (var field in singletonFields)
            {
                var singletonField = lookup
                    .AddProperty(field.LookupFieldName, Accessibility.Public)
                    .SetType(field.FieldTypeName);

                singletonField.AddAttribute("ReadOnly");
            }

            var indexer = lookup
                .AddProperty($"this[Entity entity]", Accessibility.Public)
                .SetType(data.TypeName)
                .WithSummary($"Gets the {data.TypeName} for the specified entity.");

            indexer.WithGetter(getter =>
            {
                foreach (var field in data.Fields)
                {
                    WriteLookupAcquisition(getter, field, false);
                }

                getter.AppendLine($"return new {data.TypeName}({string.Join(", ", data.Fields.Select(f => f.ArgumentName))});");
            });

            var tryGet = lookup.AddMethod("TryGet", Accessibility.Public).WithReturnType("bool");
            tryGet.AddParameter("Entity", "entity");
            tryGet.AddParameter($"out {data.TypeName}", "facet");
            tryGet.WithSummary($"Attempts to retrieve {data.TypeName} for an entity.")
                .WithParameterDoc("entity", "The entity to read.")
                .WithParameterDoc("facet", "The resolved facet when the entity has the required components.");

            tryGet.WithBody(body =>
            {
                body.AppendLine("facet = default;");
                body.NewLine();

                foreach (var field in data.Fields)
                {
                    WriteLookupAcquisition(body, field, true);
                }

                body.AppendLine($"facet = new {data.TypeName}({string.Join(", ", data.Fields.Select(f => f.ArgumentName))});");
                body.AppendLine("return true;");
            });

            var create = lookup.AddMethod("Create", Accessibility.Public).WithReturnType("void");
            create.AddParameter("ref SystemState", "state");
            create.WithSummary($"Initializes lookups used by {data.TypeName}.")
                .WithParameterDoc("state", "System state providing lookup handles.");
            create.WithBody(body =>
            {
                foreach (var slot in lookupSlots)
                {
                    var field = slot.Field;
                    var readOnlyArgument = slot.IsReadOnly ? "true" : string.Empty;

                    if (field.IsFacet)
                    {
                        body.AppendLine($"this.{field.LookupFieldName}.Create(ref state);");
                        continue;
                    }

                    if (field.IsEntityStorageInfo || field.IsEntityStorageInfoLookup)
                    {
                        body.AppendLine($"this.{field.LookupFieldName} = state.GetEntityStorageInfoLookup();");
                        continue;
                    }

                    if (field.IsComponentLookup)
                    {
                        body.AppendLine($"this.{field.LookupFieldName} = state.GetComponentLookup<{field.ComponentTypeName}>({readOnlyArgument});");
                        continue;
                    }

                    if (field.IsBufferLookup)
                    {
                        body.AppendLine($"this.{field.LookupFieldName} = state.GetBufferLookup<{field.ComponentTypeName}>({readOnlyArgument});");
                        continue;
                    }

                    var lookupExpression = field.IsBuffer
                        ? $"state.GetBufferLookup<{field.ComponentTypeName}>({readOnlyArgument})"
                        : $"state.GetComponentLookup<{field.ComponentTypeName}>({readOnlyArgument})";

                    body.AppendLine($"this.{field.LookupFieldName} = {lookupExpression};");
                }
            });

            var update = lookup.AddMethod("Update", Accessibility.Public).WithReturnType("void");
            update.AddParameter("ref SystemState", "state");
            foreach (var dependency in singletonDependencies)
            {
                update.AddParameter($"in {dependency.Field.ComponentTypeName}", dependency.ParameterName);
            }
            update.WithSummary($"Refreshes lookups for {data.TypeName} and updates singleton caches.")
                .WithParameterDoc("state", "System state used to update handles.");
            foreach (var dependency in singletonDependencies)
            {
                update.WithParameterDoc(dependency.ParameterName, GetSingletonRetrievalDoc(dependency.Field));
            }

            update.WithBody(body =>
            {
                foreach (var slot in lookupSlots)
                {
                    var field = slot.Field;

                    if (field.IsFacet)
                    {
                        body.AppendLine($"this.{field.LookupFieldName}.Update(ref state{GetFacetSingletonArguments(field)});");
                    }
                    else
                    {
                        body.AppendLine($"this.{field.LookupFieldName}.Update(ref state);");
                    }
                }

                foreach (var field in singletonFields)
                {
                    if (!singletonParameterNames.TryGetValue(field, out var parameterName))
                    {
                        parameterName = field.FieldName;
                    }

                    body.AppendLine($"this.{field.LookupFieldName} = {parameterName};");
                }
            });

            if (singletonDependencies.Count > 0)
            {
                var updateFromData = lookup.AddMethod("Update", Accessibility.Public).WithReturnType("void");
                updateFromData.AddParameter("ref SystemState", "state");
                updateFromData.AddParameter("SingletonData", "data");
                updateFromData.WithSummary($"Refreshes lookups for {data.TypeName} and updates singleton caches.")
                    .WithParameterDoc("state", "System state used to update handles.")
                    .WithParameterDoc("data", $"Singleton queries used to resolve {data.TypeName} singletons.");

                updateFromData.WithBody(body =>
                {
                    foreach (var dependency in singletonDependencies)
                    {
                        var expression = GetSingletonDataResolveExpression(dependency, "data");
                        body.AppendLine($"var {dependency.ParameterName} = {expression};");
                    }

                    body.NewLine();

                    var arguments = string.Join(", ", singletonDependencies.Select(dependency => $"in {dependency.ParameterName}"));
                    body.AppendLine($"this.Update(ref state, {arguments});");
                });
            }
        }

        private static IReadOnlyList<LookupSlot> GetLookupSlots(IEnumerable<FacetField> fields)
        {
            return fields
                .GroupBy(field => (field.LookupFieldName, field.LookupTypeName))
                .Select(group => new LookupSlot(group.First(), group.All(field => field.IsReadOnly)))
                .ToArray();
        }

        private sealed class LookupSlot
        {
            public LookupSlot(FacetField field, bool isReadOnly)
            {
                this.Field = field;
                this.IsReadOnly = isReadOnly;
            }

            public FacetField Field { get; }

            public bool IsReadOnly { get; }
        }

        private static void AddResolvedChunk(ClassBuilder typeBuilder, FacetData data)
        {
            var resolvedChunk = typeBuilder.AddNestedClass("ResolvedChunk", false, Accessibility.Public)
                .IsStruct()
                .WithSummary($"Chunk-level accessors for {data.TypeName}.");

            var resolvedFields = GetUniqueResolvedFields(data.Fields);

            foreach (var field in resolvedFields)
            {
                resolvedChunk.AddProperty(field.ResolvedFieldName, Accessibility.Public).SetType(field.ResolvedFieldTypeName);
            }

            var resolvedIndexer = resolvedChunk
                .AddProperty("this[int index]", Accessibility.Public)
                .SetType(data.TypeName)
                .WithSummary($"Gets the {data.TypeName} for an entity in the chunk by index.");

            var arguments = data.Fields.Select(GetResolvedArgument).ToArray();

            resolvedIndexer.WithGetterExpression($"new {data.TypeName}({string.Join(", ", arguments)})");
        }

        private static void ResolveResolvedFieldNameConflicts(IReadOnlyList<FacetField> fields)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var field in fields)
            {
                usedNames.Add(field.ResolvedFieldName);
            }

            foreach (var group in fields.GroupBy(field => field.ResolvedFieldName, StringComparer.Ordinal))
            {
                if (group.Select(field => field.ResolvedFieldTypeName).Distinct(StringComparer.Ordinal).Skip(1).Any())
                {
                    var keep = group.FirstOrDefault(field => !IsLookupField(field)) ?? group.First();

                    foreach (var field in group)
                    {
                        if (ReferenceEquals(field, keep))
                        {
                            continue;
                        }

                        var baseName = IsLookupField(field)
                            ? $"{field.ResolvedFieldName}Lookup"
                            : Pascalize(field.FieldName);

                        var uniqueName = GetUniqueName(baseName, usedNames);
                        field.SetResolvedFieldNameOverride(uniqueName);
                    }
                }
            }
        }

        private static IReadOnlyList<FacetField> GetUniqueResolvedFields(IEnumerable<FacetField> fields)
        {
            var result = new List<FacetField>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var field in fields)
            {
                if (seen.Add(field.ResolvedFieldName))
                {
                    result.Add(field);
                }
            }

            return result;
        }

        private static bool IsLookupField(FacetField field)
        {
            return field.IsComponentLookup || field.IsBufferLookup || field.IsEntityStorageInfoLookup;
        }

        private static string GetUniqueName(string baseName, ISet<string> usedNames)
        {
            if (usedNames.Add(baseName))
            {
                return baseName;
            }

            var index = 2;
            string candidate;

            do
            {
                candidate = $"{baseName}{index}";
                index++;
            }
            while (!usedNames.Add(candidate));

            return candidate;
        }

        private static void AddTypeHandle(ClassBuilder typeBuilder, FacetData data)
        {
            var typeHandle = typeBuilder.AddNestedClass("TypeHandle", true, Accessibility.Public)
                .IsStruct()
                .WithSummary($"Maintains type handles for chunk access to {data.TypeName}.");

            var typeHandleFields = data.Fields.Where(f => !f.IsSingleton).ToArray();
            var singletonFields = data.Fields.Where(f => f.IsSingleton).ToArray();
            var singletonDependencies = data.SingletonDependencies;
            var singletonParameterNames = singletonDependencies
                .Where(dependency => dependency.Field.IsSingleton)
                .ToDictionary(dependency => dependency.Field, dependency => dependency.ParameterName);

            foreach (var field in data.Fields)
            {
                var handleField = typeHandle.AddProperty(field.HandleName, Accessibility.Public).SetType(field.HandleTypeName);

                if (field.IsReadOnly)
                {
                    handleField.AddAttribute("ReadOnly");
                }
            }

            var create = typeHandle.AddMethod("Create", Accessibility.Public).WithReturnType("void");
            create.AddParameter("ref SystemState", "state");
            create.WithSummary($"Initializes type handles used by {data.TypeName}.")
                .WithParameterDoc("state", "System state used to create handles.");
            create.WithBody(body =>
            {
                foreach (var field in typeHandleFields)
                {
                    if (field.IsFacet)
                    {
                        body.AppendLine($"this.{field.HandleName}.Create(ref state);");
                        continue;
                    }

                    if (field.IsEntityStorageInfo || field.IsEntityStorageInfoLookup)
                    {
                        body.AppendLine($"this.{field.HandleName} = state.GetEntityStorageInfoLookup();");
                        continue;
                    }

                    if (field.IsComponentLookup)
                    {
                        body.AppendLine($"this.{field.HandleName} = state.GetComponentLookup<{field.ComponentTypeName}>({(field.IsReadOnly ? "true" : string.Empty)});");
                        continue;
                    }

                    if (field.IsBufferLookup)
                    {
                        body.AppendLine($"this.{field.HandleName} = state.GetBufferLookup<{field.ComponentTypeName}>({(field.IsReadOnly ? "true" : string.Empty)});");
                        continue;
                    }

                    if (field.IsEntity)
                    {
                        body.AppendLine($"this.{field.HandleName} = state.GetEntityTypeHandle();");
                        continue;
                    }

                    var method = field.IsBuffer ? "GetBufferTypeHandle" : "GetComponentTypeHandle";
                    var readOnly = field.IsReadOnly ? "true" : string.Empty;
                    body.AppendLine($"this.{field.HandleName} = state.{method}<{field.ComponentTypeName}>({readOnly});");
                }
            });

            var update = typeHandle.AddMethod("Update", Accessibility.Public).WithReturnType("void");
            update.AddParameter("ref SystemState", "state");
            foreach (var dependency in singletonDependencies)
            {
                update.AddParameter($"in {dependency.Field.ComponentTypeName}", dependency.ParameterName);
            }
            update.WithSummary($"Updates type handles for {data.TypeName} and refreshes singleton caches.")
                .WithParameterDoc("state", "System state used to update handles.");
            foreach (var dependency in singletonDependencies)
            {
                update.WithParameterDoc(dependency.ParameterName, GetSingletonRetrievalDoc(dependency.Field));
            }

            update.WithBody(body =>
            {
                foreach (var field in typeHandleFields)
                {
                    if (field.IsFacet)
                    {
                        body.AppendLine($"this.{field.HandleName}.Update(ref state{GetFacetSingletonArguments(field)});");
                    }
                    else
                    {
                        body.AppendLine($"this.{field.HandleName}.Update(ref state);");
                    }
                }

                foreach (var field in singletonFields)
                {
                    if (!singletonParameterNames.TryGetValue(field, out var parameterName))
                    {
                        parameterName = field.FieldName;
                    }

                    body.AppendLine($"this.{field.HandleName} = {parameterName};");
                }
            });

            if (singletonDependencies.Count > 0)
            {
                var updateFromData = typeHandle.AddMethod("Update", Accessibility.Public).WithReturnType("void");
                updateFromData.AddParameter("ref SystemState", "state");
                updateFromData.AddParameter("SingletonData", "data");
                updateFromData.WithSummary($"Updates type handles for {data.TypeName} and refreshes singleton caches.")
                    .WithParameterDoc("state", "System state used to update handles.")
                    .WithParameterDoc("data", $"Singleton queries used to resolve {data.TypeName} singletons.");

                updateFromData.WithBody(body =>
                {
                    foreach (var dependency in singletonDependencies)
                    {
                        var expression = GetSingletonDataResolveExpression(dependency, "data");
                        body.AppendLine($"var {dependency.ParameterName} = {expression};");
                    }

                    body.NewLine();

                    var arguments = string.Join(", ", singletonDependencies.Select(dependency => $"in {dependency.ParameterName}"));
                    body.AppendLine($"this.Update(ref state, {arguments});");
                });
            }

            var resolve = typeHandle.AddMethod("Resolve", Accessibility.Public).WithReturnType("ResolvedChunk");
            resolve.AddParameter("ArchetypeChunk", "chunk");
            resolve.WithSummary($"Resolves a chunk into {data.TypeName}.ResolvedChunk for job access.")
                .WithParameterDoc("chunk", "Chunk being processed.");

            var resolvedFields = GetUniqueResolvedFields(data.Fields);
            resolve.WithBody(body =>
            {
                // Unity chunk accessors return default handles/arrays when a component is absent,
                // so optional fields remain safe even if the query includes archetypes without them.
                var assignments = resolvedFields.Select(field =>
                {
                    var value = field.IsSingleton
                        ? $"this.{field.HandleName}"
                        : field.IsFacet
                            ? GetFacetResolveExpression(field)
                        : field.IsEntity
                            ? GetEntityResolveExpression(field)
                        : field.IsEntityStorageInfo
                            ? GetEntityStorageInfoResolveExpression()
                        : field.IsEntityStorageInfoLookup
                            ? $"this.{field.HandleName}"
                        : field.IsComponentLookup || field.IsBufferLookup
                            ? $"this.{field.HandleName}"
                            : field.IsBuffer
                                ? GetBufferResolveExpression(field)
                                : field.IsEnabled ? GetEnabledResolveExpression(field) : GetComponentResolveExpression(field);
                    return $"{field.ResolvedFieldName} = {value},";
                });

                using (body.BlockWithDelimiter("return new ResolvedChunk"))
                {
                    body.AppendLines(assignments, assignment => assignment);
                }
            });
        }

        private static void AddSingletonData(ClassBuilder typeBuilder, FacetData data)
        {
            var singletonDependencies = data.SingletonDependencies;
            if (singletonDependencies.Count == 0)
            {
                return;
            }

            var singletonData = typeBuilder.AddNestedClass("SingletonData", true, Accessibility.Public)
                .IsStruct()
                .WithSummary($"Provides singleton queries for {data.TypeName}.");

            foreach (var dependency in singletonDependencies)
            {
                var queryFieldName = GetSingletonDataQueryFieldName(dependency);
                singletonData.AddProperty(queryFieldName, Accessibility.Public)
                    .SetType("EntityQuery");
            }

            var create = singletonData.AddMethod("Create", Accessibility.Public).WithReturnType("void");
            create.AddParameter("ref SystemState", "state");
            create.WithSummary($"Initializes singleton queries for {data.TypeName}.")
                .WithParameterDoc("state", "System state used to build queries.");

            create.WithBody(body =>
            {
                foreach (var dependency in singletonDependencies)
                {
                    var queryFieldName = GetSingletonDataQueryFieldName(dependency);
                    var queryInvocation = GetSingletonQueryBuilderInvocation(dependency.Field);
                    body.AppendLine($"this.{queryFieldName} = new EntityQueryBuilder(Allocator.Temp).{queryInvocation}.Build(ref state);");
                }
            });
        }

        private static string GetComponentResolveExpression(FacetField field)
        {
            return $"chunk.GetNativeArray(ref this.{field.HandleName})";
        }

        private static string GetEntityResolveExpression(FacetField field)
        {
            return $"chunk.GetNativeArray(this.{field.HandleName})";
        }

        private static string GetEntityStorageInfoResolveExpression()
        {
            return "chunk";
        }

        private static string GetEnabledResolveExpression(FacetField field)
        {
            return $"chunk.GetEnabledMask(ref this.{field.HandleName})";
        }

        private static string GetBufferResolveExpression(FacetField field)
        {
            return $"chunk.GetBufferAccessor(ref this.{field.HandleName})";
        }

        private static string GetFacetResolveExpression(FacetField field)
        {
            return $"this.{field.HandleName}.Resolve(chunk)";
        }

        private static string GetFacetSingletonArguments(FacetField field)
        {
            if (!field.IsFacet || field.FacetSingletonDependencies.Count == 0)
            {
                return string.Empty;
            }

            var arguments = string.Join(", ", field.FacetSingletonDependencies.Select(d => d.ParameterName));
            return $", {arguments}";
        }

        private static void WriteLookupAcquisition(ICodeWriter writer, FacetField field, bool inTryGet)
        {
            var lookup = $"this.{field.LookupFieldName}";
            var name = field.ArgumentName;

            switch (field.Kind)
            {
                case FacetFieldKind.Entity:
                    writer.AppendLine($"var {name} = entity;");
                    break;

                case FacetFieldKind.EntityStorageInfo:
                    if (field.IsOptional)
                    {
                        writer.AppendLine($"var {name} = {lookup}.Exists(entity) ? {lookup}[entity] : default;");
                    }
                    else if (inTryGet)
                    {
                        using (writer.Block($"if (!{lookup}.Exists(entity))"))
                        {
                            writer.AppendLine("return false;");
                        }

                        writer.AppendLine($"var {name} = {lookup}[entity];");
                    }
                    else
                    {
                        writer.AppendLine($"var {name} = {lookup}[entity];");
                    }

                    break;

                case FacetFieldKind.EntityStorageInfoLookup:
                    writer.AppendLine($"var {name} = {lookup};");
                    break;

                case FacetFieldKind.ComponentLookup:
                case FacetFieldKind.BufferLookup:
                    writer.AppendLine($"var {name} = {lookup};");
                    break;

                case FacetFieldKind.Singleton:
                    writer.AppendLine($"var {name} = this.{field.LookupFieldName};");
                    break;

                case FacetFieldKind.Facet:
                    if (field.IsOptional)
                    {
                        writer.AppendLine($"{lookup}.TryGet(entity, out var {name});");
                    }
                    else if (inTryGet)
                    {
                        using (writer.Block($"if (!{lookup}.TryGet(entity, out var {name}))"))
                        {
                            writer.AppendLine("return false;");
                        }
                    }
                    else
                    {
                        writer.AppendLine($"var {name} = {lookup}[entity];");
                    }

                    break;

                case FacetFieldKind.DynamicBuffer:
                    if (field.IsOptional)
                    {
                        writer.AppendLine($"{lookup}.TryGetBuffer(entity, out var {name});");
                    }
                    else if (inTryGet)
                    {
                        using (writer.Block($"if (!{lookup}.TryGetBuffer(entity, out var {name}))"))
                        {
                            writer.AppendLine("return false;");
                        }
                    }
                    else
                    {
                        writer.AppendLine($"var {name} = {lookup}[entity];");
                    }

                    break;

                case FacetFieldKind.RefRW:
                    if (field.IsOptional)
                    {
                        writer.AppendLine($"{lookup}.TryGetRefRW(entity, out var {name});");
                    }
                    else if (inTryGet)
                    {
                        using (writer.Block($"if (!{lookup}.TryGetRefRW(entity, out var {name}))"))
                        {
                            writer.AppendLine("return false;");
                        }
                    }
                    else
                    {
                        writer.AppendLine($"var {name} = {lookup}.GetRefRW(entity);");
                    }

                    break;

                case FacetFieldKind.RefRO:
                    if (field.IsOptional)
                    {
                        writer.AppendLine($"{lookup}.TryGetRefRO(entity, out var {name});");
                    }
                    else if (inTryGet)
                    {
                        using (writer.Block($"if (!{lookup}.TryGetRefRO(entity, out var {name}))"))
                        {
                            writer.AppendLine("return false;");
                        }
                    }
                    else
                    {
                        writer.AppendLine($"var {name} = {lookup}.GetRefRO(entity);");
                    }

                    break;

                case FacetFieldKind.EnabledRefRW:
                    if (inTryGet || field.IsOptional)
                    {
                        writer.AppendLine($"var {name} = {lookup}.GetEnabledRefRWOptional<{field.ComponentTypeName}>(entity);");

                        if (!field.IsOptional)
                        {
                            using (writer.Block($"if (!{name}.IsValid)"))
                            {
                                writer.AppendLine("return false;");
                            }
                        }
                    }
                    else
                    {
                        writer.AppendLine($"var {name} = {lookup}.GetEnabledRefRW<{field.ComponentTypeName}>(entity);");
                    }

                    break;

                case FacetFieldKind.EnabledRefRO:
                    if (inTryGet || field.IsOptional)
                    {
                        writer.AppendLine($"var {name} = {lookup}.GetEnabledRefROOptional<{field.ComponentTypeName}>(entity);");

                        if (!field.IsOptional)
                        {
                            using (writer.Block($"if (!{name}.IsValid)"))
                            {
                                writer.AppendLine("return false;");
                            }
                        }
                    }
                    else
                    {
                        writer.AppendLine($"var {name} = {lookup}.GetEnabledRefRO<{field.ComponentTypeName}>(entity);");
                    }

                    break;
            }
        }

        private static string GetResolvedArgument(FacetField field)
        {
            if (field.IsSingleton)
            {
                return $"this.{field.ResolvedFieldName}";
            }

            if (field.IsFacet)
            {
                return $"{field.ResolvedFieldName}[index]";
            }

            if (field.IsEntity)
            {
                return $"{field.ResolvedFieldName}[index]";
            }

            if (field.IsEntityStorageInfo)
            {
                return $"new EntityStorageInfo {{ Chunk = this.{field.ResolvedFieldName}, IndexInChunk = index }}";
            }

            if (field.IsEntityStorageInfoLookup)
            {
                return $"this.{field.ResolvedFieldName}";
            }

            if (field.IsComponentLookup || field.IsBufferLookup)
            {
                return $"this.{field.ResolvedFieldName}";
            }

            if (field.IsBuffer)
            {
                return field.IsOptional
                    ? $"{field.ResolvedFieldName}.Length != 0 ? {field.ResolvedFieldName}[index] : default"
                    : $"{field.ResolvedFieldName}[index]";
            }

            if (field.IsEnabled)
            {
                var accessor = field.IsOptional ? "GetOptionalEnabledRef" : "GetEnabledRef";
                var rw = field.Kind == FacetFieldKind.EnabledRefRW ? "RW" : "RO";
                return $"{field.ResolvedFieldName}.{accessor}{rw}<{field.ComponentTypeName}>(index)";
            }

            var constructor = field.Kind == FacetFieldKind.RefRO
                ? $"new RefRO<{field.ComponentTypeName}>"
                : $"new RefRW<{field.ComponentTypeName}>";

            if (!field.IsOptional)
            {
                return $"{constructor}({field.ResolvedFieldName}, index)";
            }

            return $"{field.ResolvedFieldName}.IsCreated ? {constructor}({field.ResolvedFieldName}, index) : default";
        }

        private static string GetQueryBuilderInvocation(FacetField field)
        {
            return field.Kind switch
            {
                FacetFieldKind.RefRW => $"WithAllRW<{field.ComponentTypeName}>()",
                FacetFieldKind.RefRO => $"WithAll<{field.ComponentTypeName}>()",
                FacetFieldKind.EnabledRefRW => $"WithAllRW<{field.ComponentTypeName}>()",
                FacetFieldKind.EnabledRefRO => $"WithAll<{field.ComponentTypeName}>()",
                FacetFieldKind.DynamicBuffer when field.IsReadOnly => $"WithAll<{field.ComponentTypeName}>()",
                FacetFieldKind.DynamicBuffer => $"WithAllRW<{field.ComponentTypeName}>()",
                _ => throw new ArgumentOutOfRangeException(nameof(field.Kind), field.Kind, null),
            };
        }

        private static string GetXmlSafeTypeName(FacetField field)
        {
            return field.ComponentTypeName.Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static bool TryGetDynamicBufferElementType(ITypeSymbol typeSymbol, out ITypeSymbol elementType)
        {
            if (typeSymbol is INamedTypeSymbol { Name: "DynamicBuffer", TypeArguments: { Length: 1 } } namedType)
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }

            elementType = null;
            return false;
        }

        private static string GetSingletonRetrievalDoc(FacetField field)
        {
            var typeName = GetXmlSafeTypeName(field);

            if (TryGetDynamicBufferElementType(field.ComponentTypeSymbol, out var elementType))
            {
                var elementTypeName = elementType.ToDisplayString(ShortTypeFormat).Replace("<", "&lt;").Replace(">", "&gt;");
                return $"Singleton value for {typeName} which is typically retrieved via SystemAPI.GetSingletonBuffer&lt;{elementTypeName}&gt;(true).";
            }

            return $"Singleton value for {typeName} which is typically retrieved via SystemAPI.GetSingleton&lt;{typeName}&gt;().";
        }

        private static string GetSingletonDataQueryFieldName(FacetSingletonDependency dependency)
        {
            return $"{Pascalize(dependency.ParameterName)}Query";
        }

        private static string GetSingletonQueryComponentTypeName(FacetField field)
        {
            if (TryGetDynamicBufferElementType(field.ComponentTypeSymbol, out var elementType))
            {
                return elementType.ToDisplayString(ShortTypeFormat);
            }

            return field.ComponentTypeName;
        }

        private static string GetSingletonQueryBuilderInvocation(FacetField field)
        {
            var componentTypeName = GetSingletonQueryComponentTypeName(field);
            // RW dependency without RW access: allows safe writes to native containers stored on the singleton.
            var with = field.HasReadOnlyAttribute ? $"WithAll<{componentTypeName}>()" : $"WithAllRW<{componentTypeName}>()";
            return $"{with}.WithOptions(EntityQueryOptions.IncludeSystems)";
        }

        private static string GetSingletonDataResolveExpression(FacetSingletonDependency dependency, string dataParameterName)
        {
            var queryFieldName = GetSingletonDataQueryFieldName(dependency);

            if (TryGetDynamicBufferElementType(dependency.Field.ComponentTypeSymbol, out var elementType))
            {
                var elementTypeName = elementType.ToDisplayString(ShortTypeFormat);
                return $"{dataParameterName}.{queryFieldName}.GetSingletonBufferNoSync<{elementTypeName}>(true)";
            }

            return $"{dataParameterName}.{queryFieldName}.GetSingleton<{dependency.Field.ComponentTypeName}>()";
        }

        private static string Pascalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return $"{char.ToUpper(value[0], System.Globalization.CultureInfo.InvariantCulture)}{value.Substring(1)}";
        }

        private static string Camelize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return $"{char.ToLower(value[0], System.Globalization.CultureInfo.InvariantCulture)}{value.Substring(1)}";
        }

        private readonly struct FacetTraversalKey : IEquatable<FacetTraversalKey>
        {
            public FacetTraversalKey(INamedTypeSymbol facetType, string path)
            {
                this.FacetType = facetType;
                this.Path = path ?? string.Empty;
            }

            public INamedTypeSymbol FacetType { get; }

            public string Path { get; }

            public bool Equals(FacetTraversalKey other)
            {
                return SymbolEqualityComparer.Default.Equals(this.FacetType, other.FacetType) &&
                       string.Equals(this.Path, other.Path, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is FacetTraversalKey other && this.Equals(other);
            }

            public override int GetHashCode()
            {
                var hash = SymbolEqualityComparer.Default.GetHashCode(this.FacetType);

                unchecked
                {
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(this.Path);
                }

                return hash;
            }
        }

        private static bool HasAttribute(ImmutableArray<AttributeData> attributes, INamedTypeSymbol attribute)
        {
            if (attribute == null)
            {
                return false;
            }

            foreach (var attributeData in attributes)
            {
                if (SymbolEqualityComparer.Default.Equals(attributeData.AttributeClass, attribute))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class FacetResult
    {
        public FacetResult(FacetData data, IReadOnlyList<Diagnostic> diagnostics)
        {
            this.Data = data;
            this.Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
        }

        public FacetData Data { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }

    internal sealed class FacetCandidate
    {
        public FacetCandidate(TypeDeclarationSyntax typeSyntax, INamedTypeSymbol typeSymbol)
        {
            this.TypeSyntax = typeSyntax;
            this.TypeSymbol = typeSymbol;
        }

        public TypeDeclarationSyntax TypeSyntax { get; }

        public INamedTypeSymbol TypeSymbol { get; }
    }

    internal sealed class FacetSymbols
    {
        private FacetSymbols(
            INamedTypeSymbol facetInterface,
            INamedTypeSymbol optionalAttribute,
            INamedTypeSymbol facetAttribute,
            INamedTypeSymbol readOnlyAttribute,
            INamedTypeSymbol singletonAttribute,
            INamedTypeSymbol entityType,
            INamedTypeSymbol entityStorageInfoType,
            INamedTypeSymbol entityStorageInfoLookupType,
            INamedTypeSymbol componentLookupType,
            INamedTypeSymbol bufferLookupType)
        {
            this.FacetInterface = facetInterface;
            this.OptionalAttribute = optionalAttribute;
            this.FacetAttribute = facetAttribute;
            this.ReadOnlyAttribute = readOnlyAttribute;
            this.SingletonAttribute = singletonAttribute;
            this.EntityType = entityType;
            this.EntityStorageInfoType = entityStorageInfoType;
            this.EntityStorageInfoLookupType = entityStorageInfoLookupType;
            this.ComponentLookupType = componentLookupType;
            this.BufferLookupType = bufferLookupType;
        }

        public INamedTypeSymbol FacetInterface { get; }

        public INamedTypeSymbol OptionalAttribute { get; }

        public INamedTypeSymbol FacetAttribute { get; }

        public INamedTypeSymbol ReadOnlyAttribute { get; }

        public INamedTypeSymbol SingletonAttribute { get; }

        public INamedTypeSymbol EntityType { get; }

        public INamedTypeSymbol EntityStorageInfoType { get; }

        public INamedTypeSymbol EntityStorageInfoLookupType { get; }

        public INamedTypeSymbol ComponentLookupType { get; }

        public INamedTypeSymbol BufferLookupType { get; }

        public static FacetSymbols Create(Compilation compilation)
        {
            return new FacetSymbols(
                compilation.GetTypeByMetadataName("BovineLabs.Core.IFacet"),
                compilation.GetTypeByMetadataName("BovineLabs.Core.FacetOptionalAttribute"),
                compilation.GetTypeByMetadataName("BovineLabs.Core.FacetAttribute"),
                compilation.GetTypeByMetadataName("Unity.Collections.ReadOnlyAttribute"),
                compilation.GetTypeByMetadataName("BovineLabs.Core.SingletonAttribute"),
                compilation.GetTypeByMetadataName("Unity.Entities.Entity"),
                compilation.GetTypeByMetadataName("Unity.Entities.EntityStorageInfo"),
                compilation.GetTypeByMetadataName("Unity.Entities.EntityStorageInfoLookup"),
                compilation.GetTypeByMetadataName("Unity.Entities.ComponentLookup`1"),
                compilation.GetTypeByMetadataName("Unity.Entities.BufferLookup`1"));
        }
    }

    internal sealed class FacetData
    {
        public FacetData(
            INamedTypeSymbol typeSymbol,
            IReadOnlyList<FacetField> fields,
            IReadOnlyList<FacetSingletonDependency> singletonDependencies,
            IReadOnlyList<QueryBuilderInvocation> queryBuilderInvocations)
        {
            this.TypeSymbol = typeSymbol;
            this.Fields = fields;
            this.SingletonDependencies = singletonDependencies;
            this.QueryBuilderInvocations = queryBuilderInvocations;
            this.typeName = typeSymbol.ToDisplayString(FacetGenerator.ShortTypeFormat);
        }

        public INamedTypeSymbol TypeSymbol { get; }

        public IReadOnlyList<FacetField> Fields { get; }

        public IReadOnlyList<FacetSingletonDependency> SingletonDependencies { get; }

        public IReadOnlyList<QueryBuilderInvocation> QueryBuilderInvocations { get; }

        public string TypeName => this.typeName;

        private readonly string typeName;
    }

    internal sealed class QueryBuilderInvocation
    {
        public QueryBuilderInvocation(string invocation, ITypeSymbol componentTypeSymbol)
        {
            this.Invocation = invocation;
            this.ComponentTypeSymbol = componentTypeSymbol;
        }

        public string Invocation { get; }

        public ITypeSymbol ComponentTypeSymbol { get; }
    }

    internal sealed class FacetSingletonDependency
    {
        public FacetSingletonDependency(string parameterName, FacetField field)
        {
            this.ParameterName = parameterName;
            this.Field = field;
        }

        public string ParameterName { get; }

        public FacetField Field { get; }
    }

    internal sealed class FacetField
    {
        public FacetField(IFieldSymbol symbol, ITypeSymbol componentType, FacetFieldKind kind, bool isOptional, bool isReadOnly, bool hasReadOnlyAttribute)
        {
            this.Symbol = symbol;
            this.ComponentTypeSymbol = componentType;
            this.Kind = kind;
            this.IsOptional = isOptional;
            this.IsReadOnly = isReadOnly;
            this.HasReadOnlyAttribute = hasReadOnlyAttribute;
            this.FieldTypeName = symbol.Type.ToDisplayString(FacetGenerator.ShortTypeFormat);
            this.ComponentTypeName = componentType.ToDisplayString(FacetGenerator.ShortTypeFormat);
            this.ArgumentName = this.FieldName is "entity" or "facet" ? $"{this.FieldName}Value" : this.FieldName;
        }

        public IFieldSymbol Symbol { get; }

        public ITypeSymbol ComponentTypeSymbol { get; }

        public FacetFieldKind Kind { get; }

        public bool IsOptional { get; }

        public bool IsReadOnly { get; }

        public bool HasReadOnlyAttribute { get; }

        public bool IsEntity => this.Kind == FacetFieldKind.Entity;

        public bool IsEntityStorageInfo => this.Kind == FacetFieldKind.EntityStorageInfo;

        public bool IsEntityStorageInfoLookup => this.Kind == FacetFieldKind.EntityStorageInfoLookup;

        public bool IsComponentLookup => this.Kind == FacetFieldKind.ComponentLookup;

        public bool IsBufferLookup => this.Kind == FacetFieldKind.BufferLookup;

        public bool IsSingleton => this.Kind == FacetFieldKind.Singleton;

        public bool IsBuffer => this.Kind == FacetFieldKind.DynamicBuffer;

        public bool IsEnabled => this.Kind == FacetFieldKind.EnabledRefRW || this.Kind == FacetFieldKind.EnabledRefRO;

        public bool IsFacet => this.Kind == FacetFieldKind.Facet;

        public IReadOnlyList<FacetSingletonDependency> FacetSingletonDependencies { get; private set; } = Array.Empty<FacetSingletonDependency>();

        public string FieldName => this.Symbol.Name;

        public string ArgumentName { get; }

        public string FieldTypeName { get; }

        public string ComponentTypeName { get; }

        public string LookupFieldName
        {
            get
            {
                if (this.IsSingleton || this.IsFacet || this.IsEntityStorageInfo || this.IsEntityStorageInfoLookup || this.IsComponentLookup || this.IsBufferLookup)
                {
                    return this.PascalFieldName;
                }

                if (this.IsEntity)
                {
                    return "Entities";
                }

                return Pluralize(this.ComponentTypeSymbol.Name);
            }
        }

        public string ResolvedFieldName
        {
            get
            {
                if (!string.IsNullOrEmpty(this.resolvedFieldNameOverride))
                {
                    return this.resolvedFieldNameOverride;
                }

                if (this.IsSingleton || this.IsFacet || this.IsEntityStorageInfo || this.IsEntityStorageInfoLookup || this.IsComponentLookup || this.IsBufferLookup)
                {
                    return this.PascalFieldName;
                }

                if (this.IsEntity)
                {
                    return "Entities";
                }

                return Pluralize(this.ComponentTypeSymbol.Name);
            }
        }

        public string HandleName
        {
            get
            {
                if (this.IsSingleton)
                {
                    return this.PascalFieldName;
                }

                if (this.IsFacet || this.IsEntityStorageInfo || this.IsEntityStorageInfoLookup || this.IsComponentLookup || this.IsBufferLookup)
                {
                    return $"{this.PascalFieldName}Handle";
                }

                return $"{this.ComponentTypeSymbol.Name}Handle";
            }
        }

        public string LookupTypeName => this.IsSingleton
            ? this.FieldTypeName
            : this.IsFacet
                ? $"{this.ComponentTypeName}.Lookup"
                : this.IsEntityStorageInfo || this.IsEntityStorageInfoLookup
                    ? "EntityStorageInfoLookup"
                    : this.IsComponentLookup || this.IsBufferLookup
                        ? this.FieldTypeName
                    : this.IsEntity
                        ? this.ComponentTypeName
                        : this.IsBuffer
                            ? $"BufferLookup<{this.ComponentTypeName}>"
                            : $"ComponentLookup<{this.ComponentTypeName}>";

        public void SetFacetSingletonDependencies(IReadOnlyList<FacetSingletonDependency> dependencies)
        {
            this.FacetSingletonDependencies = dependencies ?? Array.Empty<FacetSingletonDependency>();
        }

        public void SetResolvedFieldNameOverride(string resolvedFieldName)
        {
            this.resolvedFieldNameOverride = resolvedFieldName;
        }

        public string ResolvedFieldTypeName
        {
            get
            {
                if (this.IsSingleton)
                {
                    return this.FieldTypeName;
                }

                if (this.IsFacet)
                {
                    return $"{this.ComponentTypeName}.ResolvedChunk";
                }

                if (this.IsBuffer)
                {
                    return $"BufferAccessor<{this.ComponentTypeName}>";
                }

                if (this.IsEntity)
                {
                    return "NativeArray<Entity>";
                }

                if (this.IsEntityStorageInfo)
                {
                    return "ArchetypeChunk";
                }

                if (this.IsEntityStorageInfoLookup)
                {
                    return "EntityStorageInfoLookup";
                }

                if (this.IsComponentLookup || this.IsBufferLookup)
                {
                    return this.FieldTypeName;
                }

                if (this.IsEnabled)
                {
                    return "EnabledMask";
                }

                return $"NativeArray<{this.ComponentTypeName}>";
            }
        }

        public string HandleTypeName => this.IsSingleton
            ? this.FieldTypeName
            : this.IsFacet
                ? $"{this.ComponentTypeName}.TypeHandle"
                : this.IsEntityStorageInfo || this.IsEntityStorageInfoLookup
                    ? "EntityStorageInfoLookup"
                    : this.IsComponentLookup || this.IsBufferLookup
                        ? this.FieldTypeName
                    : this.IsEntity
                        ? "EntityTypeHandle"
                        : this.IsBuffer
                            ? $"BufferTypeHandle<{this.ComponentTypeName}>"
                            : $"ComponentTypeHandle<{this.ComponentTypeName}>";

        private static string Pluralize(string name)
        {
            return name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name : $"{name}s";
        }

        private string resolvedFieldNameOverride;

        private string PascalFieldName => $"{char.ToUpper(this.FieldName[0], System.Globalization.CultureInfo.InvariantCulture)}{this.FieldName.Substring(1)}";
    }
}
