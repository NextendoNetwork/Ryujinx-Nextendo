using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common;
using Ryujinx.Common.Helper;
using Ryujinx.Common.Logging;
using Ryujinx.Systems.Update.Client;
using Ryujinx.Systems.Update.Common;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Systems
{
    internal static partial class Updater
    {
        private static VersionResponse _versionResponse;
        private static UpdateClient _updateClient;

        private static async Task<Return<VersionResponse>> QueryLatestVersionAsync()
        {
            _updateClient ??= UpdateClient.Builder()
                .WithServerEndpoint("https://update.ryujinx.app") // This is the default, and doesn't need to be provided; it's here for transparency.
                .WithLogger((format, args, caller) => 
                    Logger.Info?.Print(
                        LogClass.Application, 
                        args.Length is 0 ? format : format.Format(args), 
                        caller: caller)
                );

            try
            {
                return await _updateClient.QueryLatestAsync(ReleaseInformation.IsCanaryBuild
                    ? ReleaseChannel.Canary
                    : ReleaseChannel.Stable);
            }
            catch (HttpRequestException hre)
                when (hre.HttpRequestError is HttpRequestError.ConnectionError)
            {
                return Return<VersionResponse>.Failure(
                    new MessageError("Connection error occurred. Is your internet down?"));
            }
            catch (HttpRequestException hre)
                when (hre.HttpRequestError is HttpRequestError.NameResolutionError)
            {
                return Return<VersionResponse>.Failure(
                    new MessageError("DNS resolution error occurred. Is your internet down?"));
            }
            catch (HttpRequestException hre)
                when (hre.StatusCode is HttpStatusCode.BadGateway)
            {
                return Return<VersionResponse>.Failure(
                    new MessageError("Could not connect to the update server, but it appears like you have internet. It seems like the update server is offline, try again later."));
            }
        }

        public static async Task<Optional<(Version Current, Version Incoming)>> CheckVersionAsync(bool showVersionUpToDate = false)
        {
            if (!Version.TryParse(Program.Version, out Version currentVersion))
            {
                Logger.Error?.Print(LogClass.Application,
                    $"Failed to convert the current {RyujinxApp.FullAppName} version!");

                await ContentDialogHelper.CreateWarningDialog(
                    LocaleManager.Instance[LocaleKeys.DialogUpdaterConvertFailedMessage],
                    LocaleManager.Instance[LocaleKeys.DialogUpdaterCancelUpdateMessage]);

                _running = false;

                return default;
            }

            try
            {
                _versionResponse = await QueryLatestVersionAsync().Then(x => x.Unwrap());
            }
            catch (Exception e)
            {
                Logger.Error?.Print(LogClass.Application, $"{e.GetType().AsPrettyString()} thrown when requesting updates: {e.Message}");

                _running = false;
                return default;
            }

            if (_versionResponse == null)
            {
                // logging is done via the UpdateClient library
                _running = false;
                return default;
            }

            // If build URL not found, assume no new update is available.
            if (string.IsNullOrEmpty(_versionResponse.ArtifactUrl))
            {
                if (showVersionUpToDate)
                {
                    await ContentDialogHelper.CreateUpdaterUpToDateInfoDialog(
                        LocaleManager.Instance[LocaleKeys.DialogUpdaterAlreadyOnLatestVersionMessage],
                        string.Empty, 
                        _versionResponse.ReleaseUrlFormat.Format(currentVersion));
                }

                Logger.Info?.Print(LogClass.Application, "Up to date.");

                _running = false;

                return default;
            }


            if (!Version.TryParse(_versionResponse.Version, out Version newVersion))
            {
                Logger.Error?.Print(LogClass.Application,
                    $"Failed to convert the received {RyujinxApp.FullAppName} version from the update server!");

                await ContentDialogHelper.CreateWarningDialog(
                    LocaleManager.Instance[LocaleKeys.DialogUpdaterConvertFailedServerMessage],
                    LocaleManager.Instance[LocaleKeys.DialogUpdaterCancelUpdateMessage]);

                _running = false;

                return default;
            }

            _connectionCount = (int)_versionResponse.MaxConcurrency;

            return (currentVersion, newVersion);
        }
    }
}
