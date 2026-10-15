namespace WaNut.Core.Tests.TestSupport;

/// <summary>Records every report as it happens, unlike <see cref="Progress{T}"/>, which posts them later.</summary>
internal sealed class ListProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value) => Reports.Add(value);
}
