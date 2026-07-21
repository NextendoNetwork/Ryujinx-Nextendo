using System;

namespace Ryujinx.Graphics.RenderDocApi
{
    public enum RenderDocVersion
    {
        Version_1_0_0 = 10000,
        Version_1_0_1 = 10001,
        Version_1_0_2 = 10002,
        Version_1_1_0 = 10100,
        Version_1_1_1 = 10101,
        Version_1_1_2 = 10102,
        Version_1_2_0 = 10200,
        Version_1_3_0 = 10300,
        Version_1_4_0 = 10400,
        Version_1_4_1 = 10401,
        Version_1_4_2 = 10402,
        Version_1_5_0 = 10500,
        Version_1_6_0 = 10600,
    }
    
    public static partial class Helpers
    {
        extension(RenderDocVersion rdv)
        {
            public Version SystemVersion
            {
                get
                {
                    int i = (int)rdv;
                    return new (i / 10000, (i % 10000) / 100, i % 100);
                }
            }
        }

        extension(Version sv)
        {
            public RenderDocVersion RenderDocVersion
            {
                get
                {
                    return (RenderDocVersion)(sv.Major * 10000 + sv.Minor * 100 + sv.Build);
                }
            }
        }
    }
}
