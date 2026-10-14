using System.Windows.Markup;

namespace WaNut.App.Resources;

/// <summary>
/// XAML access to Strings.resx: <c>Text="{res:Text Main_CreateServer}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Text.Get(Key);
    }
}
