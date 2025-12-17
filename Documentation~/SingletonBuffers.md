# Singleton Buffers (Settings)

## Overview

**Singleton Buffers** are a small runtime utility that lets many sources contribute to a `DynamicBuffer<T>`, while runtime code can safely treat that buffer as a *singleton* (exactly one entity holds it).

This is primarily useful for:
- Settings baked from multiple `SettingsAuthoring` instances (multiple SubScenes, world variants, etc.)
- Mod/plugin style additive contributions
- Any other "many-to-one" buffer configuration pattern

## Important: Two Different `[Singleton]` Attributes

There are two unrelated attributes named `SingletonAttribute`:

- `BovineLabs.Core.Settings.SingletonAttribute` (this feature)
  - Applied to **buffer element structs**: `public struct MyBufferElement : IBufferElementData`
  - Enables *runtime merge into one singleton buffer entity*

- `BovineLabs.Core.SingletonAttribute` (Facets feature)
  - Applied to **fields inside facets**
  - Enables *facet singleton injection*

If you want singleton-buffer merging, use `BovineLabs.Core.Settings.SingletonAttribute`.

## How It Works

### Runtime Merge System

`BovineLabs.Core.Settings.SingletonSystem`:
- Scans `TypeManager.AllTypes` for `IBufferElementData` types marked with `BovineLabs.Core.Settings.SingletonAttribute`
- For each type, creates an internal singleton entity containing:
  - the buffer component
  - `BovineLabs.Core.Settings.Singleton` tag
  - `BovineLabs.Core.Settings.SingletonInitialize` (disabled by default)
- Each update, for each marked type:
  - Finds any non-singleton entities with that buffer
  - Appends all their elements into the singleton buffer
  - Removes the buffer component from those source entities
  - Enables `SingletonInitialize` as a one-frame "changed" signal

### One-Frame Initialization Signal

`BovineLabs.Core.Settings.SingletonInitializeSystemGroup`:
- Updates only when there is at least one enabled `SingletonInitialize` component in the world

`BovineLabs.Core.Settings.SingletonInitializedSystem`:
- Runs at the end of that group and disables `SingletonInitialize` again

This allows you to put optional "rebuild caches" systems in the group and have them only update when singleton buffers have changed.

## Usage

### 1) Declare a buffer element as a singleton buffer

```csharp
using BovineLabs.Core.Settings;
using Unity.Entities;

[Singleton]
public struct MyConfigElement : IBufferElementData
{
    public int Value;
}
```

### 2) Add the buffer from any number of sources

For example, in settings baking you can add the buffer normally to the settings entity:

```csharp
public override void Bake(Baker<SettingsAuthoring> baker)
{
    var entity = baker.GetEntity(TransformUsageFlags.None);
    baker.AddBuffer<MyConfigElement>(entity);
}
```

Multiple settings entities can add the buffer; at runtime they will be merged into one singleton buffer.

### 3) Read it as a singleton buffer at runtime

Once merged, systems can read it using a singleton-buffer access pattern (for example via query `GetSingletonBufferNoSync<T>`).

## Notes / Limitations

- Merge behavior is **append-only**: elements are appended in an implementation-defined order (based on query/chunk order).
- If multiple sources provide duplicate or conflicting entries, you must resolve that at a higher level (e.g. by keying entries and picking a winner).
- This feature only applies to **buffers**. For singleton `IComponentData` configuration, use normal singleton components/settings baking patterns.

