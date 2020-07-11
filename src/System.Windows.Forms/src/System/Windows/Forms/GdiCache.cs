// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using static Interop;

namespace System.Windows.Forms
{
    internal static class GdiCache
    {
        [ThreadStatic]
        private static ScreenDcCache? s_dcCache;
        private static readonly FontCache s_fontCache = new FontCache();

        private static ScreenDcCache DcCache => s_dcCache ??= new ScreenDcCache();

        public static ScreenDcCache.ScreenDcScope GetScreenDC() => DcCache.Acquire();

        public static ScreenGraphicsScope GetScreenDCGraphics()
        {
            ScreenDcCache.ScreenDcScope scope = GetScreenDC();
            return new ScreenGraphicsScope(ref scope);
        }

        public static FontCache.FontScope GetHFONT(Font? font, Gdi32.QUALITY quality = Gdi32.QUALITY.DEFAULT)
            => font is null ? default : s_fontCache.GetHFONT(font, quality);

        public readonly ref struct ScreenGraphicsScope
        {
            private readonly ScreenDcCache.ScreenDcScope _dcScope;
            public Graphics Graphics { get; }

            public ScreenGraphicsScope(ref ScreenDcCache.ScreenDcScope scope)
            {
                _dcScope = scope;
                Graphics = scope.HDC.CreateGraphics();
            }

            public static implicit operator Graphics(in ScreenGraphicsScope scope) => scope.Graphics;

            public void Dispose()
            {
                Graphics?.Dispose();
                _dcScope.Dispose();
            }
        }
    }
}
