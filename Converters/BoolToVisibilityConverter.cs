using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace CPRTouchVision.Converters
{
    public class BoolToVisibilityConverter : IValueConverter 
    { 
        public object Convert(object value, Type targetType, object parameter, string language) 
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed; 
        public object ConvertBack(object value, Type targetType, object parameter, string language) 
            => throw new NotImplementedException(); 
    }

    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b)
                ? new SolidColorBrush(Colors.Red)
                : new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
