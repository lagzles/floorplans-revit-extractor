// .NET Framework 4.8 doesn't ship the compiler-required type behind
// init-only setters and records; this is the standard minimal polyfill so
// the add-in can use records like the Core does.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
