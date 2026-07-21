using Microsoft.IO;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.HLE.HOS.Services.Am.AppletAE;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Ryujinx.HLE.HOS.Applets.Browser
{
    internal class BrowserApplet : IApplet
    {
        public event EventHandler AppletStateChanged;

        private AppletSession _normalSession;

        private CommonArguments _commonArguments;
        private List<BrowserArgument> _arguments;
        private ShimKind _shimKind;

        private readonly Horizon _system;

        public BrowserApplet(Horizon system)
        {
            _system = system;
        }

        public ResultCode Start(AppletSession normalSession, AppletSession interactiveSession)
        {
            _normalSession = normalSession;

            _commonArguments = IApplet.ReadStruct<CommonArguments>(_normalSession.Pop());

            Logger.Stub?.PrintStub(LogClass.ServiceAm, $"WebApplet version: 0x{_commonArguments.AppletVersion:x8}");

            ReadOnlySpan<byte> webArguments = _normalSession.Pop();

            (_shimKind, _arguments) = BrowserArgument.ParseArguments(webArguments);

            Logger.Stub?.PrintStub(LogClass.ServiceAm, $"Web Arguments: {_arguments.Count}");

            foreach (BrowserArgument argument in _arguments)
            {
                Logger.Stub?.PrintStub(LogClass.ServiceAm, $"{argument.Type}: {argument.GetValue()}");
            }

            // Lobby web page (ShimKind.Lobby) — Splatoon 2 opens this from a PRIVATE BATTLE lobby
            // via nn::web::ShowLobbyPage. That SDK path does StartLibraryApplet -> JoinLibraryApplet
            // -> GetLibraryAppletExitReason -> PopFromOutChannel, and ASSERTS the applet output is
            // exactly 0x1010 bytes (sizeof nn::web::LobbyPageReturnValue). The generic web responses
            // below are the wrong size, so the SDK's NN_ABORT_UNLESS fired -> guest Break ->
            // 2162-0001 crash. Push a correctly-sized, zeroed LobbyPageReturnValue (exit reason stays
            // Normal because GetResult()==Success): a clean "lobby closed, no selection" so S2
            // resumes the private lobby instead of crashing.
            if (_shimKind == ShimKind.Lobby)
            {
                // NSO Online Lounge web page (ShimKind.Lobby). Splatoon 2 opens this when VOICE CHAT
                // is turned ON (the "the smartphone app is required for voice chat" page) — it is NOT
                // part of the normal private-battle flow (with voice chat OFF, S2 never launches it
                // and the in-game salon holds fine). We can't render Nintendo's lounge web UI, so just
                // return a valid, correctly-sized (0x1010) LobbyPageReturnValue and complete normally,
                // so S2 cleanly backs out of the voice-chat page instead of crashing (2162-0001) on the
                // SDK's PopFromOutChannel size assert. (LobbyExitReason at offset 0 is informational
                // here; S2 tears the lounge session down regardless once the page closes.)
                Logger.Info?.Print(LogClass.ServiceAm, "[Lobby applet] NSO voice-chat lounge page (not needed for private battle) — showing notice + backing out");

                // Tell the user WHY this happens instead of silently dropping them: the NSO voice-chat
                // lounge needs the Nintendo Switch Online phone app, which isn't available here.
                _system?.Device?.UIHandler?.DisplayMessageDialog(
                    "Nextendo — Chat vocal indisponible / Voice chat unavailable",
                    "Le chat vocal de Splatoon 2 passe par l'application Nintendo Switch Online (téléphone), "
                    + "qui n'est pas disponible ici.\n\n"
                    + "Reviens en arrière et DÉSACTIVE le chat vocal dans Splatoon 2, puis relance ta partie "
                    + "privée — elle fonctionnera normalement.\n\n"
                    + "------------------------------------------------------------\n\n"
                    + "Splatoon 2 voice chat requires the Nintendo Switch Online phone app, which isn't "
                    + "available here.\n\n"
                    + "Go back and TURN OFF voice chat in Splatoon 2, then start your private battle again.");

                _normalSession.Push(new byte[0x1010]);
                AppletStateChanged?.Invoke(this, null);

                return ResultCode.Success;
            }

            // eShop button redirect: games open the "Shop" shim when the user taps the
            // eShop icon. Ryujinx has no real shop, so instead of the no-op stub we open a
            // configurable URL in the host's default browser. Edit "eshop_url.txt" next to
            // Ryujinx.exe to point it anywhere (Nextendo, your own store, or any URL).
            if (_shimKind == ShimKind.Shop)
            {
                string url = "https://nextendo.network";
                try
                {
                    string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
                    string cfg = Path.Combine(exeDir, "eshop_url.txt");
                    if (File.Exists(cfg))
                    {
                        string content = File.ReadAllText(cfg).Trim();
                        if (content.Length > 0)
                        {
                            url = content;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ServiceAm, $"[eShop redirect] config read failed: {ex.Message}");
                }

                try
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    Logger.Info?.Print(LogClass.ServiceAm, $"[eShop redirect] opened {url} in host browser");
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ServiceAm, $"[eShop redirect] failed to open {url}: {ex.Message}");
                }
            }

            if ((_commonArguments.AppletVersion >= 0x80000 && _shimKind == ShimKind.Web) || (_commonArguments.AppletVersion >= 0x30000 && _shimKind == ShimKind.Share))
            {
                List<BrowserOutput> result =
                [
                    new(BrowserOutputType.ExitReason, (uint)WebExitReason.ExitButton)
                ];

                _normalSession.Push(BuildResponseNew(result));
            }
            else
            {
                WebCommonReturnValue result = new()
                {
                    ExitReason = WebExitReason.ExitButton,
                };

                _normalSession.Push(BuildResponseOld(result));
            }

            AppletStateChanged?.Invoke(this, null);

            return ResultCode.Success;
        }

        private static byte[] BuildResponseOld(WebCommonReturnValue result)
        {
            using RecyclableMemoryStream stream = MemoryStreamManager.Shared.GetStream();
            using BinaryWriter writer = new(stream);
            writer.WriteStruct(result);

            return stream.ToArray();
        }
        private byte[] BuildResponseNew(List<BrowserOutput> outputArguments)
        {
            using RecyclableMemoryStream stream = MemoryStreamManager.Shared.GetStream();
            using BinaryWriter writer = new(stream);
            writer.WriteStruct(new WebArgHeader
            {
                Count = (ushort)outputArguments.Count,
                ShimKind = _shimKind,
            });

            foreach (BrowserOutput output in outputArguments)
            {
                output.Write(writer);
            }

            writer.Write(new byte[0x2000 - writer.BaseStream.Position]);

            return stream.ToArray();
        }
    }
}
