// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static Interop;

namespace System.Windows.Forms
{
    /// <summary>
    ///  Thread safe collection of screen device contexts.
    /// </summary>
    internal sealed class ScreenDcCache : IDisposable
    {
        private readonly IntPtr[] _itemsCache;

        private static Thread s_thread;

        static ScreenDcCache()
        {
            // We need a thread that doesn't ever finish to create HDCs on so they'll stay valid for all threads
            // in the process for the life of the process.

            s_thread = new Thread(ThreadWorker.Start)
            {
                Name = "WinForms Background Worker"
            };
            s_thread.Start();
        }

        /// <summary>
        ///  Create a cache with space for the specified number of items.
        /// </summary>
        public ScreenDcCache(int cacheSpace = 5)
        {
            Debug.Assert(cacheSpace > 0);

            _itemsCache = new IntPtr[cacheSpace];

            // Create an initial stash of screen dc's
            ThreadWorker.QueueAndWaitForCompletion(() =>
            {
                int max = Math.Min(cacheSpace, 5);
                for (int i = 0; i < max; i++)
                {
                    _itemsCache[i] = (IntPtr)Gdi32.CreateCompatibleDC(default);
                }
            });
        }

        /// <summary>
        ///  Get a DC from the cache or create one if none are available.
        /// </summary>
        public ScreenDcScope Acquire()
        {
            IntPtr item;

            for (int i = 0; i < _itemsCache.Length; i++)
            {
                item = Interlocked.Exchange(ref _itemsCache[i], IntPtr.Zero);
                if (item != IntPtr.Zero)
                    return new ScreenDcScope(this, (Gdi32.HDC)item);
            }

            Gdi32.HDC newDc = default;
            ThreadWorker.QueueAndWaitForCompletion(() => newDc = Gdi32.CreateCompatibleDC(default));
            return new ScreenDcScope(this, newDc);
        }

        /// <summary>
        /// Release an item back to the cache, disposing if no room is available.
        /// </summary>
        private void Release(Gdi32.HDC item)
        {
            IntPtr temp = (IntPtr)item;

            for (int i = 0; i < _itemsCache.Length; i++)
            {
                // Flip with the array until we get back an empty slot
                temp = Interlocked.Exchange(ref _itemsCache[i], temp);
                if (temp == IntPtr.Zero)
                {
                    return;
                }
            }

            // Too many to store, delete the last item we swapped.
            Gdi32.DeleteDC((Gdi32.HDC)temp);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < _itemsCache.Length; i++)
                {
                    IntPtr hdc = _itemsCache[i];
                    if (hdc != IntPtr.Zero)
                    {
                        Gdi32.DeleteDC((Gdi32.HDC)hdc);
                    }
                }
            }
        }

        public readonly ref struct ScreenDcScope
        {
            public Gdi32.HDC HDC { get; }
            private readonly ScreenDcCache _cache;

            public ScreenDcScope(ScreenDcCache cache, Gdi32.HDC hdc)
            {
                _cache = cache;
                HDC = hdc;
            }

            public static implicit operator Gdi32.HDC(in ScreenDcScope scope) => scope.HDC;

            public void Dispose()
            {
                _cache.Release(HDC);
            }
        }

        private static class ThreadWorker
        {
            private static readonly object s_lock = new object();
            private static readonly ManualResetEventSlim s_pending = new ManualResetEventSlim(initialState: true);
            private static readonly Queue<Action> s_workQueue = new Queue<Action>();

            public static void Start()
            {
                while (true)
                {
                    // Sit idle until there is work to do.
                    s_pending.Wait();

                    lock (s_lock)
                    {
                        while (s_workQueue.TryDequeue(out Action? action))
                        {
                            action.Invoke();
                        }

                        // Keep Set() and Reset() in the lock to avoid resetting after setting without actually
                        // dequeueing the work item.
                        s_pending.Reset();
                    }
                }
            }

            public static void QueueAndWaitForCompletion(Action action)
            {
                Debug.Assert(s_thread.IsAlive);

                ManualResetEventSlim finished = new ManualResetEventSlim();

                Action trackAction = () =>
                {
                    action();
                    finished.Set();
                };

                lock (s_lock)
                {
                    s_workQueue.Enqueue(trackAction);
                    s_pending.Set();
                }

#if DEBUG
                if (!finished.Wait(50))
                {
                    throw new TimeoutException("Failed to get an HDC");
                }
#else
                finished.Wait();
#endif
            }
        }
    }
}
