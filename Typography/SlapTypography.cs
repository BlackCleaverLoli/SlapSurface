using System;

namespace SlapSurface;

internal enum SlapFontSize
{
    Small = 0,
    Regular = 1,
    Large = 2,
}

internal enum SlapFontWeight
{
    Regular = 0,
    Bold = 1,
}

internal readonly record struct SlapTypographySpec(
    Func<SlapFontSize, SlapFontWeight, IDisposable>? PushFont = null
);

internal static class TypographyScope
{
    private static int activeScopeCount;
    private static SlapTypographySpec currentSpec;

    public static IDisposable Push(
        SlapTypographySpec spec,
        SlapFontSize size = SlapFontSize.Regular,
        SlapFontWeight weight = SlapFontWeight.Regular)
    {
        if (spec.PushFont == null)
            return EmptyScope.Instance;

        var previousSpec = currentSpec;
        var fontScope = spec.PushFont(size, weight) ?? EmptyScope.Instance;
        currentSpec = spec;
        activeScopeCount++;
        return new ActiveTypographyScope(fontScope, previousSpec);
    }

    public static IDisposable PushFallback(
        SlapTypographySpec spec,
        SlapFontSize size = SlapFontSize.Regular,
        SlapFontWeight weight = SlapFontWeight.Regular) =>
        activeScopeCount > 0 ? EmptyScope.Instance : Push(spec, size, weight);

    public static IDisposable PushCurrent(
        SlapFontSize size = SlapFontSize.Regular,
        SlapFontWeight weight = SlapFontWeight.Regular) =>
        currentSpec.PushFont == null ? EmptyScope.Instance : Push(currentSpec, size, weight);

    private sealed class ActiveTypographyScope(IDisposable fontScope, SlapTypographySpec previousSpec) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            fontScope.Dispose();
            currentSpec = previousSpec;
            activeScopeCount--;
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        private EmptyScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
