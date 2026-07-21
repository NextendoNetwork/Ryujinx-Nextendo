using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Applets.Browser;
using Ryujinx.HLE.HOS.Applets.Cabinet;
using Ryujinx.HLE.HOS.Applets.Dummy;
using Ryujinx.HLE.HOS.Applets.Error;
using Ryujinx.HLE.HOS.Services.Am.AppletAE;

namespace Ryujinx.HLE.HOS.Applets
{
    static class AppletManager
    {
        public static IApplet Create(AppletId applet, Horizon system)
        {
            switch (applet)
            {
                case AppletId.Controller:
                    return new ControllerApplet(system);
                case AppletId.Error:
                    return new ErrorApplet(system);
                case AppletId.PlayerSelect:
                    return new PlayerSelectApplet(system);
                case AppletId.SoftwareKeyboard:
                    return new SoftwareKeyboardApplet(system);
                case AppletId.LibAppletWeb:
                case AppletId.LibAppletShop:
                case AppletId.LibAppletOff:
                // LibAppletWhitelisted (0x18) is the "whitelisted" offline web applet. Splatoon 2
                // launches it via nn::web::ShowLobbyPage when you open a PRIVATE BATTLE lobby.
                // Without a handler it fell through to DummyApplet (pushes a bare ulong(0) and
                // finishes instantly), so S2's nn::web::CommonApi::StartLibraryApplet read a bad
                // applet state and hit NN_ABORT_UNLESS -> guest Break -> 2162-0001 crash.
                // BrowserApplet returns a proper web result (ExitReason=ExitButton), the graceful
                // "user closed the page" path the SDK expects.
                case AppletId.LibAppletWhitelisted:
                    return new BrowserApplet(system);
                case AppletId.MiiEdit:
                    Logger.Warning?.Print(LogClass.Application, $"Please use the Mii Editor inside Actions/Tools");
                    return new DummyApplet(system);
                case AppletId.Cabinet:
                    return new CabinetApplet(system);
            }

            Logger.Warning?.Print(LogClass.Application, $"Applet {applet} not implemented!");
            return new DummyApplet(system);
        }
    }
}
