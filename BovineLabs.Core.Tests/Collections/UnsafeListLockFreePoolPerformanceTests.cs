// <copyright file="UnsafeListLockFreePoolPerformanceTests.cs" company="BovineLabs">
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
    using Unity.PerformanceTesting;

    public class UnsafeListLockFreePoolPerformanceTests
    {
        private const int PoolSize = 16 * 1024;
        private const int WarmupCount = 3;
        private const int MeasurementCount = 12;

        [Test]
        [Performance]
        public void LegacyUnmanagedPool_ParallelPopSerialPush()
        {
            var pool = new UnmanagedPool<UnsafeList<int>>(PoolSize, Allocator.Persistent);
            var staging = new UnsafeList<int>[PoolSize];

            try
            {
                FillPool(pool);

                Measure
                    .Method(() => RunRound(pool, staging))
                    .SampleGroup("Legacy UnmanagedPool")
                    .WarmupCount(WarmupCount)
                    .MeasurementCount(MeasurementCount)
                    .Run();
            }
            finally
            {
                DisposePool(pool);
            }
        }

        [Test]
        [Performance]
        public void LockFreePool_ParallelPopSerialPush()
        {
            var pool = new UnsafeListLockFreePool<int>(PoolSize, Allocator.Persistent);
            var staging = new UnsafeList<int>[PoolSize];

            try
            {
                FillPool(pool);

                Measure
                    .Method(() => RunRound(pool, staging))
                    .SampleGroup("LockFree UnsafeListPool")
                    .WarmupCount(WarmupCount)
                    .MeasurementCount(MeasurementCount)
                    .Run();
            }
            finally
            {
                DisposePool(pool);
            }
        }

        [Test]
        [Performance]
        public void LegacyUnmanagedPool_BurstIJobFor_ParallelPopDispose()
        {
            UnmanagedPool<UnsafeList<int>> pool = default;
            NativeArray<int> failures = default;

            Measure
                .Method(() =>
                {
                    failures[0] = 0;

                    new LegacyBurstPopJob
                    {
                        Pool = pool,
                        Failures = failures,
                    }.ScheduleParallel(PoolSize, 64, default).Complete();

                    if (failures[0] != 0)
                    {
                        throw new InvalidOperationException($"Legacy burst pop had {failures[0]} failures.");
                    }
                })
                .SampleGroup("Legacy UnmanagedPool Burst IJobFor")
                .SetUp(() =>
                {
                    pool = new UnmanagedPool<UnsafeList<int>>(PoolSize, Allocator.Persistent);
                    FillPool(pool);
                    failures = new NativeArray<int>(1, Allocator.Persistent);
                })
                .CleanUp(() =>
                {
                    failures.Dispose();
                    DisposePool(pool);
                })
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        [Test]
        [Performance]
        public void LockFreePool_BurstIJobFor_ParallelPopDispose()
        {
            UnsafeListLockFreePool<int> pool = default;
            NativeArray<int> failures = default;

            Measure
                .Method(() =>
                {
                    failures[0] = 0;

                    new LockFreeBurstPopJob
                    {
                        Pool = pool,
                        Failures = failures,
                    }.ScheduleParallel(PoolSize, 64, default).Complete();

                    if (failures[0] != 0)
                    {
                        throw new InvalidOperationException($"Lock-free burst pop had {failures[0]} failures.");
                    }
                })
                .SampleGroup("LockFree UnsafeListPool Burst IJobFor")
                .SetUp(() =>
                {
                    pool = new UnsafeListLockFreePool<int>(PoolSize, Allocator.Persistent);
                    FillPool(pool);
                    failures = new NativeArray<int>(1, Allocator.Persistent);
                })
                .CleanUp(() =>
                {
                    failures.Dispose();
                    DisposePool(pool);
                })
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        private static void FillPool(UnmanagedPool<UnsafeList<int>> pool)
        {
            for (var i = 0; i < PoolSize; i++)
            {
                Assert.IsTrue(pool.TryAdd(CreateList(i)));
            }
        }

        private static void FillPool(UnsafeListLockFreePool<int> pool)
        {
            for (var i = 0; i < PoolSize; i++)
            {
                Assert.IsTrue(pool.TryAdd(CreateList(i)));
            }
        }

        private static void RunRound(UnmanagedPool<UnsafeList<int>> pool, UnsafeList<int>[] staging)
        {
            var ticket = 0;
            Parallel.For(
                0,
                Math.Max(2, Environment.ProcessorCount),
                _ =>
                {
                    while (true)
                    {
                        var index = Interlocked.Increment(ref ticket) - 1;
                        if (index >= staging.Length)
                        {
                            return;
                        }

                        UnsafeList<int> list;
                        while (!pool.TryGet(out list))
                        {
                            Thread.SpinWait(1);
                        }

                        staging[index] = list;
                    }
                });

            for (var i = 0; i < staging.Length; i++)
            {
                if (!pool.TryAdd(staging[i]))
                {
                    throw new InvalidOperationException("Failed to return a list to the legacy pool.");
                }
            }
        }

        private static void RunRound(UnsafeListLockFreePool<int> pool, UnsafeList<int>[] staging)
        {
            var ticket = 0;
            Parallel.For(
                0,
                Math.Max(2, Environment.ProcessorCount),
                _ =>
                {
                    while (true)
                    {
                        var index = Interlocked.Increment(ref ticket) - 1;
                        if (index >= staging.Length)
                        {
                            return;
                        }

                        UnsafeList<int> list;
                        while (!pool.TryGet(out list))
                        {
                            Thread.SpinWait(1);
                        }

                        staging[index] = list;
                    }
                });

            for (var i = 0; i < staging.Length; i++)
            {
                if (!pool.TryAdd(staging[i]))
                {
                    throw new InvalidOperationException("Failed to return a list to the lock-free pool.");
                }
            }
        }

        private static UnsafeList<int> CreateList(int value)
        {
            var list = new UnsafeList<int>(1, Allocator.Persistent);
            list.Add(value);
            return list;
        }

        private static void DisposePool(UnmanagedPool<UnsafeList<int>> pool)
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
        private unsafe struct LegacyBurstPopJob : IJobFor
        {
            public UnmanagedPool<UnsafeList<int>> Pool;

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

                list.Dispose();
            }
        }

        [BurstCompile]
        private unsafe struct LockFreeBurstPopJob : IJobFor
        {
            public UnsafeListLockFreePool<int> Pool;

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

                list.Dispose();
            }
        }
    }
}
