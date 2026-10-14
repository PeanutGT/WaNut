using System.Globalization;
using System.Windows.Data;
using WaNut.Core.Hosting;

namespace WaNut.App.Converters;

/// <summary>Turns a console line into its level, so warnings and errors can be colored.</summary>
public sealed class LogLineLevelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string line ? ServerLogParser.GetLevel(line) : LogLineLevel.Info;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
