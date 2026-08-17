using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm
{
    /// <summary>
    /// The network client owning a <see cref="LanDiscovery"/> instance, notified when the LDN network changes.
    /// </summary>
    internal interface ILdnDiscoveryClient
    {
        void InvokeNetworkChange(NetworkInfo info, bool connected, DisconnectReason reason = DisconnectReason.None);
    }
}
