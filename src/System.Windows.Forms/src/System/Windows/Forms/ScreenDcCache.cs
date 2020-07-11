// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Threading;
using static Interop;

namespace System.Windows.Forms
{
    /// <summary>
    ///  Cache of screen device contexts. This MUST be used only from one thread.
    /// </summary>
    internal sealed class ScreenDcCache : IDisposable
    {
        private readonly IntPtr[] _itemsCache;

        /// <summary>
        ///  Create a cache with space for the specified number of items.
        /// </summary>
        public ScreenDcCache(int cacheSpace = 5)
        {
            Debug.Assert(cacheSpace > 0);

            _itemsCache = new IntPtr[cacheSpace];
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

            return new ScreenDcScope(this, Gdi32.CreateCompatibleDC(default));
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

        ~ScreenDcCache()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
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
    }
}
