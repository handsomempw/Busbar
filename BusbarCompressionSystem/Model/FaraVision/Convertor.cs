using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace BusbarCompressionSystem.Model.FaraVision
{
    [ValueConversion(typeof(bool), typeof(string))]
    public class permission2lockorunlock_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((bool)value)
            {
                return Encoding.Unicode.GetString(BitConverter.GetBytes((UInt16)0xe968), 0, 2);
            }
            else
            {
                return Encoding.Unicode.GetString(BitConverter.GetBytes((UInt16)0xe966), 0, 2);
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }



    [ValueConversion(typeof(bool), typeof(string))]
    public class Bool2OKNG_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((bool)value)
            {
                return "OK";
            }
            else
            {
                return "NG";
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }


    [ValueConversion(typeof(bool), typeof(string))]
    public class Bool2OKNG_Converter_INV : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((bool)value)
            {
                return "NG";
            }
            else
            {
                return "OK";
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }


    [ValueConversion(typeof(bool), typeof(SolidColorBrush))]
    public class Bool2SolidColor_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((bool)value)
            {
                return Brushes.YellowGreen;
            }
            else
            {
                return Brushes.OrangeRed;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }


    [ValueConversion(typeof(bool), typeof(SolidColorBrush))]
    public class Bool2SolidColor_Converter_INV : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((bool)value)
            {
                return Brushes.OrangeRed;
            }
            else
            {
                return Brushes.YellowGreen;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }


    [ValueConversion(typeof(KStatus), typeof(string))]
    public class Status2ZH_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((KStatus)value == KStatus.Wait)
            {
                return "等待测试";
            }
            else if ((KStatus)value == KStatus.Processing)
            {
                return "测试中";
            }
            else if ((KStatus)value == KStatus.Processing_OK)
            {
                return "测试过程合格";
            }
            else if ((KStatus)value == KStatus.Processing_NG)
            {
                return "测试过程不合格";
            }
            else if ((KStatus)value == KStatus.OK)
            {
                return "合格";
            }
            else
            {
                return "不合格";
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    //[ValueConversion(typeof(ToolType), typeof(string))]
    //public class Tooltype2ZH_Converter : IValueConverter
    //{
    //    //源属性传给目标属性时，调用此方法ConvertBack
    //    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        if (value == null)
    //        { throw new ArgumentNullException("value can not be null"); }
    //        if ((ToolType)value == ToolType.Template)
    //        {
    //            return "模板";
    //        }
    //        else
    //        {
    //            return "面积";
    //        }

    //    }

    //    //目标属性传给源属性时，调用此方法ConvertBack
    //    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        return null;
    //    }
    //}





    [ValueConversion(typeof(KStatus), typeof(SolidColorBrush))]
    public class Status2BackGround_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((KStatus)value == KStatus.Wait)
            {
                return Brushes.Orange;
            }
            else if ((KStatus)value == KStatus.Processing)
            {
                return Brushes.BlueViolet;
            }
            else if ((KStatus)value == KStatus.OK)
            {
                return Brushes.YellowGreen;
            }
            else
            {
                return new SolidColorBrush(Color.FromArgb(0xff, 0xd7, 0x00, 0x0F));
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    [ValueConversion(typeof(KStatus), typeof(SolidColorBrush))]
    public class Status2ForeGround_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((KStatus)value == KStatus.Wait)
            {
                return Brushes.White;
            }
            else if ((KStatus)value == KStatus.Processing)
            {
                return Brushes.White;
            }
            else if ((KStatus)value == KStatus.OK)
            {
                return Brushes.White;
            }
            else
            {
                return Brushes.White;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    [ValueConversion(typeof(Visibility), typeof(string))]
    public class Bool2Visible_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((bool)value)
            {
                return Visibility.Visible;
            }
            else
            {
                return Visibility.Collapsed;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    [ValueConversion(typeof(Visibility), typeof(string))]
    public class Bool2Visible_Converter_INV : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((bool)value)
            {
                return Visibility.Collapsed;
            }
            else
            {
                return Visibility.Visible;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }


    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_Dimension : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((TestModes)value != TestModes.面积)
            {
                return Visibility.Collapsed;
            }
            else
            {
                return Visibility.Visible;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }


    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_Barcode : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((TestModes)value != TestModes.二维码)
            {
                return Visibility.Collapsed;
            }
            else
            {
                return Visibility.Visible;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }



    /// <summary>
    /// 模板匹配/模板定位共用面板的显隐控制。
    /// 工具模式为模板匹配或模板定位时 Visible，其余模式 Collapsed。
    /// 绑定对象：位置检测 GroupBox、画布上的位置检测 ROI 矩形框、模型设置按钮。
    /// </summary>
    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_ShapeMatch : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((TestModes)value != TestModes.模板匹配 && (TestModes)value != TestModes.模板定位)
            {
                return Visibility.Collapsed;
            }
            else
            {
                return Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// 模板匹配门卫参数区：仅模板匹配模式显示偏差范围等判定项，模板定位模式隐藏。
    /// </summary>
    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_TemplateMatchJudge : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value can not be null");
            }

            return (TestModes)value == TestModes.模板匹配 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// 模板匹配结果区：仅模板匹配模式显示 X/Y 偏差和判定字段，模板定位使用独立结果区。
    /// </summary>
    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_TemplateMatchOnly : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value can not be null");
            }

            return (TestModes)value == TestModes.模板匹配 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// 模板定位专属配置：仅模板定位模式显示 ROI 跟随矫正开关。
    /// </summary>
    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_TemplateLocatorOnly : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value can not be null");
            }

            return (TestModes)value == TestModes.模板定位 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// 产品结果输出区仅对参与 OK/NG/NG2 判定的工具开放。
    /// 模板定位只提供同指令 ROI 跟随矫正和追溯信息，机器人回包由面积、尺寸、模板匹配等判定工具承担。
    /// </summary>
    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_JudgingToolOnly : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value can not be null");
            }

            return (TestModes)value == TestModes.模板定位 ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// 直线检测模式的 Visibility 转换器。
    /// 仅在工具模式为直线检测时显示配置面板与画布叠加层。
    /// </summary>
    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_LineDetect : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((TestModes)value != TestModes.直线检测)
            {
                return Visibility.Collapsed;
            }
            else
            {
                return Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// 尺寸测量模式的Visibility转换器
    /// </summary>
    [ValueConversion(typeof(TestModes), typeof(Visibility))]
    public class TestMode2Visibility_DimensionMeasure : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            if ((TestModes)value != TestModes.尺寸测量)
            {
                return Visibility.Collapsed;
            }
            else
            {
                return Visibility.Visible;
            }
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }


    [ValueConversion(typeof(bool), typeof(bool))]
    public class BoolINV_Converter : IValueConverter
    {
        //源属性传给目标属性时，调用此方法ConvertBack
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            { throw new ArgumentNullException("value can not be null"); }
            return !((bool)value);
        }

        //目标属性传给源属性时，调用此方法ConvertBack
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
    public enum KStatus
    {
        Wait,
        Processing,
        Processing_OK,
        Processing_NG,
        OK,
        NG
    }

    public enum TestModes
    {
        面积,
        二维码,
        模板匹配,
        模板定位,
        尺寸测量,
        直线检测
    }

    /// <summary>
    /// 尺寸测量类型枚举
    /// </summary>
    public enum DimensionMeasureType
    {
        直线到直线,
        直线到圆心,
        圆心到圆心
    }
}
