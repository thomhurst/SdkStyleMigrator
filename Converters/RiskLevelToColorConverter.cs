using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SdkMigrator.Models;

namespace SdkMigrator.Converters;

public class RiskLevelToColorConverter : IValueConverter
{
    // Cache static brush instances for better performance
    private static readonly SolidColorBrush LowRiskBrush = new(Color.Parse("#10B981"));
    private static readonly SolidColorBrush MediumRiskBrush = new(Color.Parse("#F59E0B"));
    private static readonly SolidColorBrush HighRiskBrush = new(Color.Parse("#EF4444"));
    private static readonly SolidColorBrush DefaultBrush = new(Color.Parse("#6B7280"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MigrationRiskLevel riskLevel)
        {
            return riskLevel switch
            {
                MigrationRiskLevel.Low => LowRiskBrush,
                MigrationRiskLevel.Medium => MediumRiskBrush,
                MigrationRiskLevel.High => HighRiskBrush,
                _ => DefaultBrush
            };
        }
        
        return DefaultBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}