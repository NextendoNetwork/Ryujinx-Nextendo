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
    partial class DeliveryCacheFileService : IDeliveryCacheFileService, IDisposable
    {
        private SharedRef<LibHac.Bcat.Impl.Ipc.IDeliveryCacheFileService> _libHacService;
        private int _disposalState;
        private string _seedPath;

        public DeliveryCacheFileService(ref SharedRef<LibHac.Bcat.Impl.Ipc.IDeliveryCacheFileService> libHacService)
        {
            _libHacService = SharedRef<LibHac.Bcat.Impl.Ipc.IDeliveryCacheFileService>.CreateMove(ref libHacService);
        }

        [CmifCommand(0)]
        public Result Open(DirectoryName directoryName, FileName fileName)
        {
            string dn = BcatSeed.ToName(ref directoryName);
            string fn = BcatSeed.ToName(ref fileName);
            Result res = _libHacService.Get.Open(ref directoryName, ref fileName).Horizon;
            if (res.IsFailure && System.IO.File.Exists(BcatSeed.FilePath(dn, fn)))
            {
                _seedPath = BcatSeed.FilePath(dn, fn);
                res = Result.Success;
            }
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat, $"[SEED] FileService.Open dir='{dn}' file='{fn}' -> {res} (seed={_seedPath != null})");
            return res;
        }

        [CmifCommand(1)]
        public Result Read(long offset, out long bytesRead, [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> data)
        {
            if (_seedPath != null)
            {
                using System.IO.FileStream fsr = System.IO.File.OpenRead(_seedPath);
                fsr.Seek(offset, System.IO.SeekOrigin.Begin);
                bytesRead = fsr.Read(data);
                Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat, $"[SEED] FileService.Read(seed) off={offset} read={bytesRead}");
                return Result.Success;
            }
            return _libHacService.Get.Read(out bytesRead, offset, data).Horizon;
        }

        [CmifCommand(2)]
        public Result GetSize(out long size)
        {
            if (_seedPath != null)
            {
                size = new System.IO.FileInfo(_seedPath).Length;
                return Result.Success;
            }
            return _libHacService.Get.GetSize(out size).Horizon;
        }

        [CmifCommand(3)]
        public Result GetDigest(out Digest digest)
        {
            if (_seedPath != null)
            {
                digest = default;
                byte[] hash = System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(_seedPath));
                BcatSeed.FillRaw(ref digest, hash);
                return Result.Success;
            }
            return _libHacService.Get.GetDigest(out digest).Horizon;
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
