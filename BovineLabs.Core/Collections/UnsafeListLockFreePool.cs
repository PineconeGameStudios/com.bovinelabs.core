// <copyright file="UnsafeListLockFreePool.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Collections
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Mathematics;

    /// <summary>
    /// Lock-free pool for <see cref="UnsafeList{T}" /> with multi-consumer pop semantics and single-producer push semantics.
    /// </summary>
    /// <typeparam name="T">The element type in the pooled list.</typeparam>
    public readonly unsafe struct UnsafeListLockFreePool<T> : IDisposable
        where T : unmanaged
    {
        private readonly int capacity;
        private readonly Allocator allocator;

        [NativeDisableUnsafePtrRestriction]
        private readonly UnsafeList<T>* buffer;

        [NativeDisableUnsafePtrRestriction]
        private readonly int* length;

#if BL_LOCKFREE_POOL_METRICS
        [NativeDisableUnsafePtrRestriction]
        private readonly Counters* counters;
#endif

        public UnsafeListLockFreePool(int capacity, Allocator allocator = Allocator.Persistent)
        {
            capacity = GetCapacity(capacity);
            this.capacity = capacity;
            this.allocator = allocator;

            this.buffer = (UnsafeList<T>*)UnsafeUtility.MallocTracked(
                sizeof(UnsafeList<T>) * capacity,
                UnsafeUtility.AlignOf<UnsafeList<T>>(),
                allocator,
                0);

            this.length = (int*)UnsafeUtility.MallocTracked(
                sizeof(int),
                UnsafeUtility.AlignOf<int>(),
                allocator,
                0);

            *this.length = 0;
#if BL_LOCKFREE_POOL_METRICS
            this.counters = (Counters*)UnsafeUtility.MallocTracked(
                sizeof(Counters),
                UnsafeUtility.AlignOf<Counters>(),
                allocator,
                0);

            UnsafeUtility.MemClear(this.counters, sizeof(Counters));
#endif
        }

        public bool IsCreated => this.buffer != null;

#if BL_LOCKFREE_POOL_METRICS
        public UnsafeListLockFreePoolMetrics Metrics =>
            new(
                Volatile.Read(ref this.counters->Hits),
                Volatile.Read(ref this.counters->Misses),
                Volatile.Read(ref this.counters->Allocations),
                Volatile.Read(ref this.counters->Returned),
                Volatile.Read(ref this.counters->Disposed));
#endif

        public void Dispose()
        {
            var poolLength = Volatile.Read(ref *this.length);
            for (var i = 0; i < poolLength; i++)
            {
                ref var list = ref this.buffer[i];
                if (list.IsCreated)
                {
                    list.Dispose();
#if BL_LOCKFREE_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Disposed);
#endif
                }
            }

            UnsafeUtility.FreeTracked(this.buffer, this.allocator);
            UnsafeUtility.FreeTracked(this.length, this.allocator);
#if BL_LOCKFREE_POOL_METRICS
            UnsafeUtility.FreeTracked(this.counters, this.allocator);
#endif
        }

        /// <summary>
        /// Returns an item from the pool if one is available.
        /// </summary>
        /// <param name="element">The pooled list.</param>
        /// <returns>True when a pooled list was returned; otherwise false.</returns>
        public bool TryGet(out UnsafeList<T> element)
        {
            while (true)
            {
                var currentLength = Volatile.Read(ref *this.length);
                if (currentLength <= 0)
                {
#if BL_LOCKFREE_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Misses);
#endif
                    element = default;
                    return false;
                }

                var nextLength = currentLength - 1;
                var nextElement = this.buffer[nextLength];

                if (Interlocked.CompareExchange(ref *this.length, nextLength, currentLength) == currentLength)
                {
#if BL_LOCKFREE_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Hits);
#endif
                    element = nextElement;
                    return true;
                }
            }
        }

        /// <summary>
        /// Adds an item back to the pool.
        /// </summary>
        /// <remarks>
        /// This method is intended for a single producer.
        /// </remarks>
        /// <param name="element">The list to return to the pool.</param>
        /// <returns>True when the item was returned to the pool; otherwise false.</returns>
        public bool TryAdd(UnsafeList<T> element)
        {
            while (true)
            {
                var currentLength = Volatile.Read(ref *this.length);
                if (currentLength >= this.capacity)
                {
                    return false;
                }

                this.buffer[currentLength] = element;

                if (Interlocked.CompareExchange(ref *this.length, currentLength + 1, currentLength) == currentLength)
                {
#if BL_LOCKFREE_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Returned);
#endif
                    return true;
                }
            }
        }

        public UnsafeList<T> GetOrCreate(int minimumCapacity, AllocatorManager.AllocatorHandle listAllocator)
        {
            if (this.TryGet(out var list))
            {
                return list;
            }

#if BL_LOCKFREE_POOL_METRICS
            Interlocked.Increment(ref this.counters->Allocations);
#endif
            return new UnsafeList<T>(minimumCapacity, listAllocator);
        }

        public void ReturnOrDispose(UnsafeList<T> list)
        {
            if (this.TryAdd(list))
            {
                return;
            }

            if (list.IsCreated)
            {
                list.Dispose();
#if BL_LOCKFREE_POOL_METRICS
                Interlocked.Increment(ref this.counters->Disposed);
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetCapacity(int requestedCapacity)
        {
            requestedCapacity = math.max(1, requestedCapacity);
            return math.ceilpow2(requestedCapacity);
        }

#if BL_LOCKFREE_POOL_METRICS
        private struct Counters
        {
            public int Hits;
            public int Misses;
            public int Allocations;
            public int Returned;
            public int Disposed;
        }
#endif
    }

#if BL_LOCKFREE_POOL_METRICS
    public readonly struct UnsafeListLockFreePoolMetrics
    {
        public UnsafeListLockFreePoolMetrics(int hits, int misses, int allocations, int returned, int disposed)
        {
            this.Hits = hits;
            this.Misses = misses;
            this.Allocations = allocations;
            this.Returned = returned;
            this.Disposed = disposed;
        }

        public int Hits { get; }

        public int Misses { get; }

        public int Allocations { get; }

        public int Returned { get; }

        public int Disposed { get; }
    }
#endif
}
