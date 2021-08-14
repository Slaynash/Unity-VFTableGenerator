using System.Runtime.InteropServices;

namespace vftablechecker
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct ImageNtHeaders
    {
        [FieldOffset(0)]
        public uint signature;
        [FieldOffset(4)]
        public ImageFileHeader fileHeader;
    }
}
