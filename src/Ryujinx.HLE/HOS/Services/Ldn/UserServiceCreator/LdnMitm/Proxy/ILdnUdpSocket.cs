using Ryujinx.HLE.HOS.Services.Ldn.Types;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnMitm.Proxy
{
    /// <summary>
    /// UDP socket used for LDN scan requests and responses.
    /// </summary>
    internal interface ILdnUdpSocket : ILdnSocket
    {
        void ClearScanResults();

        Dictionary<ulong, NetworkInfo> GetScanResults();
    }
}
