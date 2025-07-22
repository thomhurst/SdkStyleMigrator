using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SdkMigrator.Models;

namespace SdkMigrator.Converters;

public class RiskLevelToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MigrationRiskLevel riskLevel)
        {
            return riskLevel switch
            {
                MigrationRiskLevel.Low => new SolidColorBrush(Color.Parse("#10B981")),
                MigrationRiskLevel.Medium => new SolidColorBrush(Color.Parse("#F59E0B")),
                MigrationRiskLevel.High => new SolidColorBrush(Color.Parse("#EF4444")),
                _ => new SolidColorBrush(Color.Parse("#6B7280"))
            };
        }
        
        return new SolidColorBrush(Color.Parse("#6B7280"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}