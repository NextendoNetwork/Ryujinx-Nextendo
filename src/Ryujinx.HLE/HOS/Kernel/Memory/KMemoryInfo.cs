using Ryujinx.Common;

namespace Ryujinx.HLE.HOS.Kernel.Memory
{
    class KMemoryInfo
    {
        public static readonly ObjectPool<KMemoryInfo> Pool = new(() => new KMemoryInfo());
        
        public ulong Address { get; private set; }
        public ulong Size { get; private set; }

        public MemoryState State { get; private set; }
        public KMemoryPermission Permission { get; private set; }
        public MemoryAttribute Attribute { get;private set;  }
        public KMemoryPermission SourcePermission { get; private set; }

        public int IpcRefCount { get; private set; }
        public int DeviceRefCount { get; private set; }

        public KMemoryInfo Set(
            ulong address,
            ulong size,
            MemoryState state,
            KMemoryPermission permission,
            MemoryAttribute attribute,
            KMemoryPermission sourcePermission,
            int ipcRefCount,
            int deviceRefCount)
        {
            Address = address;
            Size = size;
            State = state;
            Permission = permission;
            Attribute = attribute;
            SourcePermission = sourcePermission;
            IpcRefCount = ipcRefCount;
            DeviceRefCount = deviceRefCount;

            return this;
        }
    }
}
