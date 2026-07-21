using LibHac.Bcat;
using LibHac.Common;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Bcat;
using Ryujinx.Horizon.Sdk.Sf;
using Ryujinx.Horizon.Sdk.Sf.Hipc;
using System;
using System.Threading;

namespace Ryujinx.Horizon.Bcat.Ipc
{
    partial class DeliveryCacheDirectoryService : IDeliveryCacheDirectoryService, IDisposable
    {
        private SharedRef<LibHac.Bcat.Impl.Ipc.IDeliveryCacheDirectoryService> _libHacService;
        private int _disposalState;
        private string _seedDir;

        public DeliveryCacheDirectoryService(ref SharedRef<LibHac.Bcat.Impl.Ipc.IDeliveryCacheDirectoryService> libHacService)
        {
            _libHacService = SharedRef<LibHac.Bcat.Impl.Ipc.IDeliveryCacheDirectoryService>.CreateMove(ref libHacService);
        }

        [CmifCommand(0)]
        public Result Open(DirectoryName directoryName)
        {
            string name = BcatSeed.ToName(ref directoryName);
            Result res = _libHacService.Get.Open(ref directoryName).Horizon;
            if (res.IsFailure && System.IO.Directory.Exists(BcatSeed.DirPath(name)))
            {
                _seedDir = name;
                res = Result.Success;
            }
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat, $"[SEED] DirectoryService.Open dir='{name}' -> {res} (seed={_seedDir != null})");
            return res;
        }

        [CmifCommand(1)]
        public Result Read(out int entriesRead, [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<DeliveryCacheDirectoryEntry> entriesBuffer)
        {
            if (_seedDir != null)
            {
                string[] files = System.IO.Directory.GetFiles(BcatSeed.DirPath(_seedDir));
                int n = Math.Min(files.Length, entriesBuffer.Length);
                for (int i = 0; i < n; i++)
                {
                    System.IO.FileInfo fi = new(files[i]);
                    FileName fn = default;
                    BcatSeed.FillName(ref fn, fi.Name);
                    Digest dg = default;
                    entriesBuffer[i] = new DeliveryCacheDirectoryEntry(in fn, fi.Length, in dg);
                }
                entriesRead = n;
                Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat, $"[SEED] DirectoryService.Read(seed) entriesRead={n}");
                return Result.Success;
            }
            return _libHacService.Get.Read(out entriesRead, entriesBuffer).Horizon;
        }

        [CmifCommand(2)]
        public Result GetCount(out int count)
        {
            if (_seedDir != null)
            {
                count = System.IO.Directory.GetFiles(BcatSeed.DirPath(_seedDir)).Length;
                Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat, $"[SEED] DirectoryService.GetCount(seed) count={count}");
                return Result.Success;
            }
            Result res = _libHacService.Get.GetCount(out count).Horizon;
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat, $"[SEED] DirectoryService.GetCount count={count} -> {res}");
            return res;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposalState, 1) == 0)
            {
                _libHacService.Destroy();
            }
        }
    }
}
