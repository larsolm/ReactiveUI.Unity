using System.Runtime.InteropServices;

namespace ReactiveUI.Yoga
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void YogaDirtiedFunc(YogaNode node);
}
