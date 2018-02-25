﻿using System;
using System.Globalization;
using System.Windows.Data;
using Filtration.ObjectModel.Enums;
using Filtration.ObjectModel.Extensions;

namespace Filtration.ThemeEditor.Converters
{
    public class ThemeComponentTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return string.Empty;
            
            }
            var type = (ThemeComponentType) value;

            switch (type.GetAttributeDescription())
            {
                case "Text":
                {
                    return "文字样式";
                }
                case "Border":
                {
                    return "边框样式";
                }
                case "Background":
                {
                    return "背景样式";
                }
            }

            return type.GetAttributeDescription();

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
