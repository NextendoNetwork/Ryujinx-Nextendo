
using System;

namespace Ryujinx.Graphics.RenderDocApi
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class RenderDocApiVersionAttribute : Attribute
    {
        public Version MinVersion { get; }

        public RenderDocApiVersionAttribute(int major, int minor, int patch = 0)
        {
            MinVersion = new Version(major, minor, patch);
        }
    }
}
