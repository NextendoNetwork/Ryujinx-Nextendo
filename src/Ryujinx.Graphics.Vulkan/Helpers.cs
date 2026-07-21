using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;

namespace Ryujinx.Graphics.Vulkan
{
    public static class Helpers
    {
        extension(Vk api)
        {
            /// <summary>
            ///     C# implementation of the RENDERDOC_DEVICEPOINTER_FROM_VKINSTANCE macro from the RenderDoc API header, since we cannot use macros from C#.
            /// </summary>
            /// <returns>The dispatch table pointer, which sits as the first pointer-sized object in the memory pointed to by the <see cref="Vk"/>'s <see cref="Instance"/> pointer.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void* GetRenderDocDevicePointer() =>
                api.CurrentInstance is not null
                    ? api.CurrentInstance.Value.GetRenderDocDevicePointer()
                    : null;
        }

        extension(Instance instance)
        {
            /// <summary>
            ///     C# implementation of the RENDERDOC_DEVICEPOINTER_FROM_VKINSTANCE macro from the RenderDoc API header, since we cannot use macros from C#.
            /// </summary>
            /// <returns>The dispatch table pointer, which sits as the first pointer-sized object in the memory pointed to by the <see cref="Instance"/>'s pointer.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void* GetRenderDocDevicePointer()
                => (*((void**)(instance.Handle)));
        }
    }
}
