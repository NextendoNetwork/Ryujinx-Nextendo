using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;
using System;
using System.Buffers;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands
{
    struct SetRenderTargetsCommand : IGALCommand, IGALCommand<SetRenderTargetsCommand>
    {
        public static readonly ArrayPool<ITexture> ArrayPool = ArrayPool<ITexture>.Create(512, 50);
        public readonly CommandType CommandType => CommandType.SetRenderTargets;
        private int _colorsCount;
        private TableRef<ITexture[]> _colors;
        private TableRef<ITexture> _depthStencil;

        public void Set(int colorsCount, TableRef<ITexture[]> colors, TableRef<ITexture> depthStencil)
        {
            _colorsCount = colorsCount;
            _colors = colors;
            _depthStencil = depthStencil;
        }

        public static void Run(ref SetRenderTargetsCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            ITexture[] colors = command._colors.Get(threaded);
            Span<ITexture> colorsSpan = colors.AsSpan(0, command._colorsCount);

            for (int i = 0; i < colorsSpan.Length; i++)
            {
                colorsSpan[i] = ((ThreadedTexture)colorsSpan[i])?.Base;
            }
            
            renderer.Pipeline.SetRenderTargets(colorsSpan, command._depthStencil.GetAs<ThreadedTexture>(threaded)?.Base);
            
            ArrayPool.Return(colors);
        }
    }
}
