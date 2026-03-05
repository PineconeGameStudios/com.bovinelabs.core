// <copyright file="UnmanagedPool.cs" company="BovineLabs">
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

    public readonly unsafe struct UnmanagedPool<T> : IDisposable
        where T : unmanaged
    {
        private readonly int capacity;
        private readonly Allocator allocator;

        [NativeDisableUnsafePtrRestriction]
        private readonly T* buffer;

        [NativeDisableUnsafePtrRestriction]
        private readonly int* length;

#if BL_UNMANAGED_POOL_METRICS
        [NativeDisableUnsafePtrRestriction]
        private readonly Counters* counters;
#endif

        public UnmanagedPool(int capacity, Allocator allocator = Allocator.Persistent)
        {
            capacity = GetCapacity(capacity);
            this.capacity = capacity;
            this.allocator = allocator;

            this.buffer = (T*)UnsafeUtility.MallocTracked(sizeof(T) * capacity, UnsafeUtility.AlignOf<T>(), allocator, 0);
            this.length = (int*)UnsafeUtility.MallocTracked(sizeof(int), UnsafeUtility.AlignOf<int>(), allocator, 0);
            *this.length = 0;
#if BL_UNMANAGED_POOL_METRICS
            this.counters = (Counters*)UnsafeUtility.MallocTracked(sizeof(Counters), UnsafeUtility.AlignOf<Counters>(), allocator, 0);
            UnsafeUtility.MemClear(this.counters, sizeof(Counters));
#endif
        }

        public bool IsCreated => this.buffer != null;

#if BL_UNMANAGED_POOL_METRICS
        public UnmanagedPoolMetrics Metrics =>
            new(
                Volatile.Read(ref this.counters->Hits),
                Volatile.Read(ref this.counters->Misses),
                Volatile.Read(ref this.counters->Returned),
                Volatile.Read(ref this.counters->Rejected));
#endif

        public void Dispose()
        {
            UnsafeUtility.FreeTracked(this.buffer, this.allocator);
            UnsafeUtility.FreeTracked(this.length, this.allocator);
#if BL_UNMANAGED_POOL_METRICS
            UnsafeUtility.FreeTracked(this.counters, this.allocator);
#endif
        }

        /// <summary>
        /// Attempts to return an element to the pool.
        /// </summary>
        /// <remarks>
        /// This path is intended for a single producer.
        /// </remarks>
        public bool TryAdd(T element)
        {
            while (true)
            {
                var currentLength = Volatile.Read(ref *this.length);
                if (currentLength >= this.capacity)
                {
#if BL_UNMANAGED_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Rejected);
#endif
                    return false;
                }

                this.buffer[currentLength] = element;
                if (Interlocked.CompareExchange(ref *this.length, currentLength + 1, currentLength) == currentLength)
                {
#if BL_UNMANAGED_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Returned);
#endif
                    return true;
                }
            }
        }

        public bool TryGet(out T element)
        {
            while (true)
            {
                var currentLength = Volatile.Read(ref *this.length);
                if (currentLength <= 0)
                {
#if BL_UNMANAGED_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Misses);
#endif
                    element = default;
                    return false;
                }

                var nextLength = currentLength - 1;
                var nextElement = this.buffer[nextLength];
                if (Interlocked.CompareExchange(ref *this.length, nextLength, currentLength) == currentLength)
                {
#if BL_UNMANAGED_POOL_METRICS
                    Interlocked.Increment(ref this.counters->Hits);
#endif
                    element = nextElement;
                    return true;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetCapacity(int newCapacity)
        {
            newCapacity = math.max(newCapacity, CollectionHelper.CacheLineSize / sizeof(T));
            newCapacity = math.ceilpow2(newCapacity);
            return newCapacity;
        }

#if BL_UNMANAGED_POOL_METRICS
        private struct Counters
        {
            public int Hits;
            public int Misses;
            public int Returned;
            public int Rejected;
        }
#endif
    }

#if BL_UNMANAGED_POOL_METRICS
    public readonly struct UnmanagedPoolMetrics
    {
        public UnmanagedPoolMetrics(int hits, int misses, int returned, int rejected)
        {
            this.Hits = hits;
            this.Misses = misses;
            this.Returned = returned;
            this.Rejected = rejected;
        }

        public int Hits { get; }

        public int Misses { get; }

        public int Returned { get; }

        public int Rejected { get; }
    }
#endif
}
