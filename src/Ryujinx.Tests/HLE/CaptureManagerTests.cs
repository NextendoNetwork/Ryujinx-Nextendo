using NUnit.Framework;
using Ryujinx.HLE.HOS.Services.Caps;
using Ryujinx.HLE.HOS.Services.Caps.Types;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ryujinx.Tests.HLE
{
    public class CaptureManagerTests
    {
        private const int ScreenshotWidth = 1280;
        private const int ScreenshotHeight = 720;
        private const int BytesPerPixel = 4;

        private const int ScreenshotDataSize = ScreenshotWidth * ScreenshotHeight * BytesPerPixel; // 0x384000
        private const int PaddedScreenshotDataSize = ScreenshotWidth * 768 * BytesPerPixel;        // 0x3C0000

        [Test]
        public void SaveScreenShotRejectsBufferSmallerThan720p()
        {
            using TempSdCard tempSdCard = new();

            CaptureManager captureManager = CreateCaptureManager(tempSdCard.Path);
            byte[] screenshotData = new byte[ScreenshotDataSize - 1];

            ResultCode result = captureManager.SaveScreenShot(
                screenshotData,
                appletResourceUserId: 0,
                titleId: 0x0100000000001000,
                out _);

            Assert.That(result, Is.EqualTo(ResultCode.NullInputBuffer));
            Assert.That(Directory.Exists(Path.Combine(tempSdCard.Path, "Nintendo", "Album")), Is.False);
        }

        [Test]
        public void SaveScreenShotAcceptsExact720pBuffer()
        {
            using TempSdCard tempSdCard = new();

            CaptureManager captureManager = CreateCaptureManager(tempSdCard.Path);
            byte[] screenshotData = CreateTestPattern(ScreenshotDataSize);

            ResultCode result = captureManager.SaveScreenShot(
                screenshotData,
                appletResourceUserId: 0,
                titleId: 0x0100000000001000,
                out ApplicationAlbumEntry applicationAlbumEntry);

            string filePath = GetSingleAlbumFile(tempSdCard.Path);

            using SKBitmap bitmap = SKBitmap.Decode(filePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ResultCode.Success));
                Assert.That(bitmap.Width, Is.EqualTo(ScreenshotWidth));
                Assert.That(bitmap.Height, Is.EqualTo(ScreenshotHeight));
                Assert.That(applicationAlbumEntry.TitleId, Is.EqualTo(0x0100000000001000));
                Assert.That(applicationAlbumEntry.AlbumStorage, Is.EqualTo(AlbumStorage.Sd));
                Assert.That(applicationAlbumEntry.ContentType, Is.EqualTo(ContentType.Screenshot));
                Assert.That(applicationAlbumEntry.Unknown0x1f, Is.EqualTo(1));
            });
        }

        [Test]
        public void SaveScreenShotAcceptsBufferLargerThan720p()
        {
            using TempSdCard tempSdCard = new();

            CaptureManager captureManager = CreateCaptureManager(tempSdCard.Path);
            byte[] screenshotData = CreateTestPattern(PaddedScreenshotDataSize);

            ResultCode result = captureManager.SaveScreenShot(
                screenshotData,
                appletResourceUserId: 0,
                titleId: 0x0100000000001000,
                out ApplicationAlbumEntry applicationAlbumEntry);

            string filePath = GetSingleAlbumFile(tempSdCard.Path);

            using SKBitmap bitmap = SKBitmap.Decode(filePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ResultCode.Success));
                Assert.That(bitmap.Width, Is.EqualTo(ScreenshotWidth));
                Assert.That(bitmap.Height, Is.EqualTo(ScreenshotHeight));
                Assert.That(applicationAlbumEntry.TitleId, Is.EqualTo(0x0100000000001000));
            });
        }

        [Test]
        public void SaveScreenShotCreatesUniqueFileNamesForRepeatedSaves()
        {
            using TempSdCard tempSdCard = new();

            CaptureManager captureManager = CreateCaptureManager(tempSdCard.Path);
            byte[] screenshotData = CreateTestPattern(ScreenshotDataSize);

            ResultCode firstResult = captureManager.SaveScreenShot(
                screenshotData,
                appletResourceUserId: 0,
                titleId: 0x0100000000001000,
                out _);

            ResultCode secondResult = captureManager.SaveScreenShot(
                screenshotData,
                appletResourceUserId: 0,
                titleId: 0x0100000000001000,
                out _);

            string[] files = Directory.GetFiles(
                Path.Combine(tempSdCard.Path, "Nintendo", "Album"),
                "*.jpg",
                SearchOption.AllDirectories);

            Assert.Multiple(() =>
            {
                Assert.That(firstResult, Is.EqualTo(ResultCode.Success));
                Assert.That(secondResult, Is.EqualTo(ResultCode.Success));
                Assert.That(files, Has.Length.EqualTo(2));
            });
        }

        private static CaptureManager CreateCaptureManager(string sdCardPath)
        {
            CaptureManager captureManager = (CaptureManager)RuntimeHelpers.GetUninitializedObject(typeof(CaptureManager));

            typeof(CaptureManager)
                .GetField("_sdCardPath", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(captureManager, sdCardPath);

            return captureManager;
        }

        private static string GetSingleAlbumFile(string sdCardPath)
        {
            string albumPath = Path.Combine(sdCardPath, "Nintendo", "Album");

            string[] files = Directory.GetFiles(albumPath, "*.jpg", SearchOption.AllDirectories);

            Assert.That(files, Has.Length.EqualTo(1));

            return files.Single();
        }

        private static byte[] CreateTestPattern(int size)
        {
            byte[] data = new byte[size];

            int pixelCount = size / BytesPerPixel;

            for (int i = 0; i < pixelCount; i++)
            {
                int x = i % ScreenshotWidth;
                int y = i / ScreenshotWidth;

                data[(i * 4) + 0] = (byte)(x & 0xff);
                data[(i * 4) + 1] = (byte)(y & 0xff);
                data[(i * 4) + 2] = 0x80;
                data[(i * 4) + 3] = 0xff;
            }

            return data;
        }

        private sealed class TempSdCard : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "sdcard-" + Guid.NewGuid());

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
        }
    }
}
