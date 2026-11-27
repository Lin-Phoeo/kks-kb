using System;
using UnityEngine;

namespace CodexMEColorSpaceFix
{
    internal sealed class SelfTestRenderTargetScope : IDisposable
    {
        private readonly RenderTexture previous = RenderTexture.active;
        private readonly bool previousWrite = GL.sRGBWrite;

        public void Dispose()
        {
            RenderTexture.active = previous != null ? previous : null;
            GL.sRGBWrite = previousWrite;
        }
    }
}
