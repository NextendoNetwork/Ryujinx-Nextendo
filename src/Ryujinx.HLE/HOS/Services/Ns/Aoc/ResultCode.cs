namespace Ryujinx.HLE.HOS.Services.Ns.Aoc
{
    enum ResultCode
    {
        EShopModuleId = 164,
        ModuleId = 166,
        ErrorCodeShift = 9,

        Success = 0,

        InvalidBufferSize = (200 << ErrorCodeShift) | ModuleId,
        InvalidPid = (300 << ErrorCodeShift) | ModuleId,
        NoPurchasedProductInfoAvailable = (400 << ErrorCodeShift) | EShopModuleId
    }
}
