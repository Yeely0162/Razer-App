using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RazerChromaWLEDConnect.Core;

namespace RazerChromaWLEDConnect.Converters
{
    /// <summary>Turns "#RRGGBB" into a frozen brush. Used by swatches and previews.</summary>
    public sealed class HexToBrushConverter : IValueConverter
    {
        private static readonly Brush Fallback = CreateBrush("#0E1013");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string hex = value as string;
            if (string.IsNullOrEmpty(hex)) return Fallback;
            return CreateBrush(hex);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        public static Brush CreateBrush(string hex)
        {
            try
            {
                Color colour = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(colour);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return Fallback;
            }
        }
    }

    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            if (parameter is string && ((string)parameter).Equals("invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }

    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }
    }

    /// <summary>Maps "ok" / "warn" / "err" / "off" to the status colours.</summary>
    public sealed class StatusKeyToBrushConverter : IValueConverter
    {
        private static readonly Brush Ok = Frozen("#44D62C");
        private static readonly Brush Warn = Frozen("#E3B341");
        private static readonly Brush Err = Frozen("#F0616B");
        private static readonly Brush Off = Frozen("#5A6067");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key = value as string;
            if (key == "ok") return Ok;
            if (key == "warn") return Warn;
            if (key == "err") return Err;
            return Off;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
