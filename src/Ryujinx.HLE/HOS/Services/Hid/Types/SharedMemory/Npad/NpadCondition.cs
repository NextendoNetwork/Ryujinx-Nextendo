namespace Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Npad
{
    struct NpadCondition
    {
#pragma warning disable CS0414 // Field is assigned but its value is never used
        private uint _00;
        private uint _04;
        private NpadJoyHoldType _holdType;
        private uint _0C;
#pragma warning restore CS0414 // Field is assigned but its value is never used
        
        public static NpadCondition Create()
        {
            return new NpadCondition()
            {
                _00 = 0,
                _04 = 1,
                _holdType = NpadJoyHoldType.Horizontal,
                _0C = 1,
            };
        }
    }
}
