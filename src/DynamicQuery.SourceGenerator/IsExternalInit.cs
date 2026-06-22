// Polyfill. The analyzer TFM is netstandard2.0, which predates
// System.Runtime.CompilerServices.IsExternalInit — the type the C# compiler requires to emit
// record types and `init` accessors. Providing it as an internal type lets this generator use
// records (value-equality models, which the incremental generator pipeline caches on) while still
// targeting netstandard2.0 as Roslyn analyzers must. net5.0+ defines this in the BCL; netstandard2.0
// does not, so we supply our own internal copy. (Internal => no conflict with consumers' BCL copy.)

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
