// <copyright file="UnsafeListLockFreePoolTests.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Tests.Collections
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using BovineLabs.Core.Collections;
    using NUnit.Framework;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Jobs;

    public class UnsafeListLockFreePoolTests
    {
        [Test]
        public void GetOrCreate_WhenPoolIsEmpty_TracksMissAndAllocation()
        {
            var pool = new UnsafeListLockFreePool<int>(4, Allocator.Persistent);

            try
            {
                var list = pool.GetOrCreate(8, Allocator.Persistent);

                Assert.IsTrue(list.IsCreated);
                Assert.AreEqual(0, list.Length);

#if BL_LOCKFREE_POOL_METRICS
                var metrics = pool.Metrics;
                Assert.AreEqual(0, metrics.Hits);
                Assert.AreEqual(1, metrics.Misses);
                Assert.AreEqual(1, metrics.Allocations);
#endif

                pool.ReturnOrDispose(list);

#if BL_LOCKFREE_POOL_METRICS
                metrics = pool.Metrics;
                Assert.AreEqual(1, metrics.Returned);
                Assert.AreEqual(0, metrics.Disposed);
#endif
            }
            finally
            {
                DisposePool(pool);
            }
        }

        [Test]
        public void ReturnOrDispose_WhenPoolIsFull_DisposesList()
        {
            var pool = new UnsafeListLockFreePool<int>(1, Allocator.Persistent);

            try
            {
                var first = CreateList(1);
                var second = CreateList(2);

                Assert.IsTrue(pool.TryAdd(first));

                pool.ReturnOrDispose(second);

#if BL_LOCKFREE_POOL_METRICS
                var metrics = pool.Metrics;
                Assert.AreEqual(1, metrics.Returned);
                Assert.AreEqual(1, metrics.Disposed);
#endif

                Assert.IsTrue(pool.TryGet(out var pooled));
                Assert.AreEqual(1, pooled[0]);
                pooled.Dispose();

                Assert.IsFalse(pool.TryGet(out _));
            }
            finally
            {
                DisposePool(pool);
            }
        }

        [Test]
        public void TryGet_ParallelConsumers_ReturnsEachListExactlyOnce()
        {
            const int count = 16 * 1024;
            var pool = new UnsafeListLockFreePool<int>(count, Allocator.Persistent);

            try
            {
                for (var i = 0; i < count; i++)
                {
                    Assert.IsTrue(pool.TryAdd(CreateList(i)));
                }

                var seen = new int[count];
                var nextTicket = 0;
                var workerCount = Math.Max(2, Environment.ProcessorCount);

                Parallel.For(
                    0,
                    workerCount,
                    _ =>
                    {
                        while (true)
                        {
                            var ticket = Interlocked.Increment(ref nextTicket) - 1;
                            if (ticket >= count)
                            {
                                return;
                            }

                            UnsafeList<int> list;
                            while (!pool.TryGet(out list))
                            {
                                Thread.SpinWait(1);
                            }

                            var value = list[0];
                            Interlocked.Increment(ref seen[value]);
                            list.Dispose();
                        }
                    });

                Assert.IsFalse(pool.TryGet(out _));

                for (var i = 0; i < count; i++)
                {
                    Assert.AreEqual(1, seen[i], $"Unexpected pop count for value {i}");
                }

#if BL_LOCKFREE_POOL_METRICS
                var metrics = pool.Metrics;
                Assert.AreEqual(count, metrics.Hits);
                Assert.AreEqual(1, metrics.Misses);
#endif
            }
            finally
            {
                DisposePool(pool);
            }
        }

        [Test]
        public unsafe void TryGet_BurstIJobFor_ParallelConsumers_ReturnEachListExactlyOnce()
        {
            const int count = 16 * 1024;
            var pool = new UnsafeListLockFreePool<int>(count, Allocator.Persistent);
            var seen = new NativeArray<int>(count, Allocator.TempJob);
            var failures = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                for (var i = 0; i < count; i++)
                {
                    Assert.IsTrue(pool.TryAdd(CreateList(i)));
                }

                var job = new BurstPopJob
                {
                    Pool = pool,
                    Seen = seen,
                    Failures = failures,
                };

                job.ScheduleParallel(count, 64, default).Complete();

                Assert.AreEqual(0, failures[0], "Some job workers failed to pop from the pool.");
                for (var i = 0; i < count; i++)
                {
                    Assert.AreEqual(1, seen[i], $"Unexpected pop count for value {i}");
                }

#if BL_LOCKFREE_POOL_METRICS
                var metrics = pool.Metrics;
                Assert.AreEqual(count, metrics.Hits);
#endif
            }
            finally
            {
                seen.Dispose();
                failures.Dispose();
                DisposePool(pool);
            }
        }

        private static UnsafeList<int> CreateList(int value)
        {
            var list = new UnsafeList<int>(1, Allocator.Persistent);
            list.Add(value);
            return list;
        }

        private static void DisposePool(UnsafeListLockFreePool<int> pool)
        {
            while (pool.TryGet(out var list))
            {
                if (list.IsCreated)
                {
                    list.Dispose();
                }
            }

            pool.Dispose();
        }

        [BurstCompile]
        private unsafe struct BurstPopJob : IJobFor
        {
            public UnsafeListLockFreePool<int> Pool;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> Seen;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> Failures;

            public void Execute(int index)
            {
                if (!this.Pool.TryGet(out var list))
                {
                    var failurePtr = (int*)this.Failures.GetUnsafePtr();
                    Interlocked.Increment(ref failurePtr[0]);
                    return;
                }

                var value = list[0];
                var seenPtr = (int*)this.Seen.GetUnsafePtr();
                Interlocked.Increment(ref seenPtr[value]);
                list.Dispose();
            }
        }
    }
}
