using Ryujinx.Audio.Renderer.Server;
using Ryujinx.Audio.Renderer.Server.MemoryPool;
using System.Runtime.InteropServices;

namespace Ryujinx.Audio.Renderer.Common
{
    /// <summary>
    /// Represents the input parameter for <see cref="BehaviourInfo"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BehaviourParameter
    {
        /// <summary>
        /// The current audio renderer revision in use.
        /// </summary>
        public int UserRevision;

        /// <summary>
        /// Reserved/padding.
        /// </summary>
        private readonly uint _padding;

        /// <summary>
        /// The flags given controlling behaviour of the audio renderer
        /// </summary>
        /// <remarks>See <see cref="BehaviourInfo.UpdateFlags(ulong)"/> and <see cref="BehaviourInfo.IsMemoryPoolForceMappingEnabled"/>.</remarks>
        public ulong Flags;

        /// <summary>
        /// Represents an error during <see cref="Server.AudioRenderSystem.Update(System.Memory{byte}, System.Memory{byte}, System.Buffers.ReadOnlySequence{byte})"/>.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ErrorInfo
        {
            /// <summary>
            /// The error code to report.
            /// </summary>
            public ResultCode ErrorCode;

            /// <summary>
            /// Reserved/padding.
            /// </summary>
            private readonly uint _padding;

            /// <summary>
            /// Extra information given with the <see cref="ResultCode"/>
            /// </summary>
            /// <remarks>This is usually used to report a faulting cpu address when a <see cref="MemoryPoolInfo"/> mapping fail.</remarks>
            public ulong ExtraErrorInfo;
        }
    }
}
