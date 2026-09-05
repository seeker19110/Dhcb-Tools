// Polyfill cho .NET Framework 4.8: các kiểu này chỉ có sẵn từ .NET 5+.
// Compiler C# 9+ cần chúng để emit `init` accessor và `required` keyword.
// File này bị bỏ qua hoàn toàn khi target là net5.0+ (conditional compilation không cần thiết
// vì SDK tự resolve đúng kiểu từ BCL).

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    [Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // chỉ tồn tại cho trình biên dịch, không bao giờ chạy
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { FeatureName = featureName; }
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

#endif
