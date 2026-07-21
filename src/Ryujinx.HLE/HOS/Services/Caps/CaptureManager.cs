using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.HLE.HOS.Services.Caps.Types;
using SkiaSharp;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Ryujinx.HLE.HOS.Services.Caps
{
    internal class CaptureManager
    {
        public CaptureManager(Switch device)
        {
            _ = device;
        }
        private readonly string _sdCardPath = FileSystem.VirtualFileSystem.GetSdCardPath();

        private uint _shimLibraryVersion;

        private const int ScreenshotWidth = 1280;
        private const int ScreenshotHeight = 720;
        private const int ScreenshotBytesPerPixel = 4;
        private const int ScreenshotDataSize = ScreenshotWidth * ScreenshotHeight * ScreenshotBytesPerPixel; // 0x384000

        public ResultCode SetShimLibraryVersion(ServiceCtx context)
        {
            ulong shimLibraryVersion = context.RequestData.ReadUInt64();
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ulong appletResourceUserId = context.RequestData.ReadUInt64();
#pragma warning restore IDE0059

            // TODO: Service checks if the pid is present in an internal list and returns ResultCode.BlacklistedPid if it is.
            //       The list contents needs to be determined.

            ResultCode resultCode = ResultCode.OutOfRange;

            if (shimLibraryVersion != 0)
            {
                if (_shimLibraryVersion == shimLibraryVersion)
                {
                    resultCode = ResultCode.Success;
                }
                else if (_shimLibraryVersion != 0)
                {
                    resultCode = ResultCode.ShimLibraryVersionAlreadySet;
                }
                else if (shimLibraryVersion == 1)
                {
                    resultCode = ResultCode.Success;

                    _shimLibraryVersion = 1;
                }
            }

            return resultCode;
        }

        public ResultCode SaveScreenShot(
            byte[] screenshotData,
            ulong appletResourceUserId,
            ulong titleId,
            out ApplicationAlbumEntry applicationAlbumEntry)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceCaps, new
            {
                appletResourceUserId,
                titleId,
                screenshotDataLength = screenshotData?.Length ?? 0,
            });

            applicationAlbumEntry = default;

            if (screenshotData == null || screenshotData.Length == 0)
            {
                return ResultCode.NullInputBuffer;
            }

            if (screenshotData.Length < ScreenshotDataSize)
            {
                Logger.Warning?.PrintMsg(
                    LogClass.ServiceCaps,
                    $"Invalid screenshot buffer size 0x{screenshotData.Length:X}; expected at least 0x{ScreenshotDataSize:X}.");

                return ResultCode.NullInputBuffer;
            }

            DateTime currentDateTime = DateTime.Now;

            applicationAlbumEntry = new ApplicationAlbumEntry()
            {
                Size = (ulong)Unsafe.SizeOf<ApplicationAlbumEntry>(),
                TitleId = titleId,
                AlbumFileDateTime = new AlbumFileDateTime()
                {
                    Year = (ushort)currentDateTime.Year,
                    Month = (byte)currentDateTime.Month,
                    Day = (byte)currentDateTime.Day,
                    Hour = (byte)currentDateTime.Hour,
                    Minute = (byte)currentDateTime.Minute,
                    Second = (byte)currentDateTime.Second,
                    UniqueId = 0,
                },
                AlbumStorage = AlbumStorage.Sd,
                ContentType = ContentType.Screenshot,
                Padding = new Array5<byte>(),
                Unknown0x1f = 1,
            };

            // NOTE: The hex hash is a HMAC-SHA256 (first 32 bytes) using a hardcoded secret key over the titleId, we can simulate it by hashing the titleId instead.
            string hash = Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(titleId)))[..0x20];

            string folderPath = Path.Combine(
                _sdCardPath,
                "Nintendo",
                "Album",
                currentDateTime.Year.ToString("0000", CultureInfo.InvariantCulture),
                currentDateTime.Month.ToString("00", CultureInfo.InvariantCulture),
                currentDateTime.Day.ToString("00", CultureInfo.InvariantCulture));

            string filePath = GenerateFilePath(folderPath, applicationAlbumEntry, currentDateTime, hash);

            _ = Directory.CreateDirectory(folderPath);

            while (File.Exists(filePath))
            {
                applicationAlbumEntry.AlbumFileDateTime.UniqueId++;
                filePath = GenerateFilePath(folderPath, applicationAlbumEntry, currentDateTime, hash);
            }

            using SKBitmap bitmap = new(new SKImageInfo(ScreenshotWidth, ScreenshotHeight, SKColorType.Rgba8888));

            nint pixels = bitmap.GetPixels();

            if (pixels == 0)
            {
                return ResultCode.InvalidArgument;
            }

            Marshal.Copy(screenshotData, 0, pixels, ScreenshotDataSize);

            using SKData data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 80);
            using FileStream file = File.OpenWrite(filePath);
            data.SaveTo(file);

            return ResultCode.Success;
        }

        private string GenerateFilePath(string folderPath, ApplicationAlbumEntry applicationAlbumEntry, DateTime currentDateTime, string hash)
        {
            string fileName = $"{currentDateTime:yyyyMMddHHmmss}{applicationAlbumEntry.AlbumFileDateTime.UniqueId:00}-{hash}.jpg";

            return Path.Combine(folderPath, fileName);
        }
    }
}
