using Ryujinx.Ava.UI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ryujinx.Ava.Systems.AppLibrary
{
    public class LdnGameDataReceivedEventArgs : EventArgs
    {
        public static new readonly LdnGameDataReceivedEventArgs Empty = new(null);

        public LdnGameDataReceivedEventArgs(LdnGameModel[] ldnData)
        {
            LdnData = ldnData ?? [];
        }
        
        public LdnGameDataReceivedEventArgs(IEnumerable<LdnGameModel> ldnData)
        {
            LdnData = ldnData?.ToArray() ?? [];
        }


        public LdnGameModel[] LdnData { get; }
    }
}
