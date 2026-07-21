using Ryujinx.Horizon.Common;

namespace Ryujinx.Horizon
{
    public static class LibHacResultExtensions
    {
        extension(LibHac.Result libHacResult)
        {
            public Result Horizon => new((int)libHacResult.Module, (int)libHacResult.Description);
        }
    }
}
