using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Xml.Serialization;
using BusbarCompressionSystem.FaraVision;
using BusbarCompressionSystem.Model.FaraVision;

namespace BusbarCompressionSystem.Model.FaraVision.Tool
{
    public class ToolModel : ObservableObject
    {
        [XmlElement("工具模式")]
        public TestModes TestMode { set; get; } = TestModes.二维码;

        [XmlElement("相机曝光时间")]

        public int ExposureTime { set; get; } = 3000;

        [XmlElement("产品位置序号")]

        public byte ProductPositionNO { set; get; } = 0;


        //[XmlElement("相机ID")]
        //public string CameraID { set; get; } = string.Empty;

        [XmlElement("相机序号")]
        public int CameraIndex { set; get; } = 0;



        [XmlIgnore]
        public QRCode.Reg Reg = new QRCode.Reg();

        [XmlIgnore]
        public PositionDetect.ShapeMatch ShapeMatch = new PositionDetect.ShapeMatch();


        [XmlElement("工具序号")]
        public int Index { set; get; } = 0;

        [XmlElement("触发指令")]
        public string Command { set; get; } = "T1-1";

        [XmlElement("工具名称")]
        public string Name { set; get; } = string.Empty;

        #region 二维码识别

        //[XmlElement("是否识别二维码")]
        //public bool DecodeBarcode { set; get; } = false;

        [XmlElement("二维码内容")]
        public string BarcodeStr { set; get; } = string.Empty;
        [XmlElement("二维码ROI")]
        public ROI BarCodeROI { set; get; } = new ROI();

        [XmlElement("延时")]
        public int Delaytimes { set; get; } = 100;

        [XmlElement("结束码")]

        public string FinishedCode { set; get; } = "REPORTCODE";
        [XmlElement("母排扫码模式")]

        public bool MPMode { set; get; } = true;

        [XmlElement("发送扫码内容")]
        public bool SendBarcode { set; get; } = true;



        #endregion

        #region 位置检测

        //[XmlElement("位置检测")]
        //public bool PositionDetect { set; get; } = true;


        [XmlElement("位置检测模型文件名称")]
        public string ModelFileName { set; get; } = string.Empty;

        [XmlElement("模板匹配分值下限")]

        public double MinScore { set; get; } = 0.8;

        public double ActualScore { set; get; } = 0;


        [XmlElement("比例um/pixel")]
        public double K { set; get; } = 1000;

        [XmlElement("初始X轴坐标")]
        public double InitX { set; get; } = 0;
        [XmlElement("初始Y轴坐标")]
        public double InitY { set; get; } = 0;


        [XmlElement("检测X轴坐标")]
        public double ActualX { set; get; } = 0;
        [XmlElement("检测Y轴坐标")]
        public double ActualY { set; get; } = 0;
        [XmlElement("检测角度")]
        public double ActualAngle { set; get; } = 0;
        [XmlElement("X轴偏差")]
        public double DeltaX { set; get; } = 0;
        [XmlElement("Y轴偏差")]
        public double DeltaY { set; get; } = 0;

        [XmlElement("允许角度偏差")]
        public double AllowAngleDelta { set; get; } = 20;




        //[XmlElement("X轴取反")]
        //public bool DirectX { set; get; } = false;
        //[XmlElement("Y轴取反")]
        //public bool DirectY { set; get; } = false;
        //[XmlElement("XY轴对换")]
        //public bool InvertXY { set; get; } = false;

        [XmlElement("发送结果状态")]
        public bool SendStatus { set; get; } = true;

        //[XmlElement("发送定位数据")]
        //public bool SendPositionData { set; get; } = true;


        //[XmlElement("发送二维码数据")]
        //public bool SendBarcodeData { set; get; } = true;



        //[XmlElement("结果取反")]
        //public bool InvertResult { set; get; } = false;


        [XmlElement("X位置偏差范围")]
        ///单位mm
        public double Allow_X_Delta { set; get; } = 10;
        [XmlElement("Y位置偏差范围")]
        ///单位mm
        public double Allow_Y_Delta { set; get; } = 10;
        //public double Allow_Delta { set; get; } = 10;

        [XmlElement("状态颜色")]
        ///透明,等待
        ///黄色，运行中
        ///绿色，OK
        ///红色, NG
        [XmlIgnore]
        public SolidColorBrush StatusColor
        {

            get
            {
                switch (_ToolStatus)
                {
                    case ToolStatus.识别中:
                        {
                            return Brushes.Orange;
                        }
                    case ToolStatus.OK:
                        {
                            return Brushes.GreenYellow;
                        }
                    case ToolStatus.NG:
                    case ToolStatus.NG2:
                        {
                            return Brushes.OrangeRed;
                        }
                    default:
                        {
                            return new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
                        }
                }
            }

        }


        private ToolStatus _ToolStatus = ToolStatus.等待中;

        public ToolStatus ToolStatus
        {
            set
            {
                _ToolStatus = value;
                RaisePropertyChanged(() => ToolStatus);
                RaisePropertyChanged(() => StatusColor);

            }
            get { return _ToolStatus; }
        }



        [XmlElement("位置检测ROI")]
        public ROI PositionROI { set; get; } = new ROI();

        #endregion

        #region 面积检测
        [XmlElement("面积ROI")]
        public ROI DimensionROI { set; get; } = new ROI();
        //[XmlElement("面积检测")]
        //public bool DimensionDetect { set; get; } = true;
        [XmlElement("实际面积")]
        public double ActualDimension { set; get; } = 0;
        [XmlElement("最小面积")]
        public double MinDimension { set; get; } = 0;
        [XmlElement("最大面积")]
        public double MaxDimension { set; get; } = 1000000;
        [XmlElement("灰色模式")]
        public bool GrayMode { set; get; } = false;
        [XmlElement("启用红色通道")]
        public bool RedChannelEnabled { set; get; } = true;
        [XmlElement("启用绿色通道")]
        public bool GreenChannelEnabled { set; get; } = true;
        [XmlElement("启用蓝色通道")]
        public bool BlueChannelEnabled { set; get; } = true;
        [XmlElement("最小灰度")]
        public byte MinGray { set; get; } = 128;
        [XmlElement("最大灰度")]
        public byte MaxGray { set; get; } = 255;
        [XmlElement("最小红色通道")]
        public byte MinRed { set; get; } = 128;
        [XmlElement("最大红色通道")]
        public byte MaxRed { set; get; } = 255;
        [XmlElement("最小绿色通道")]
        public byte MinGreen { set; get; } = 128;
        [XmlElement("最大绿色通道")]
        public byte MaxGreen { set; get; } = 255;
        [XmlElement("最小蓝色通道")]
        public byte MinBlue { set; get; } = 128;
        [XmlElement("最大蓝色通道")]
        public byte MaxBlue { set; get; } = 255;
        [XmlElement("过滤最小面积")]
        public int MinAreaFilter { set; get; } = 0;
        [XmlElement("过滤最大面积")]
        public int MaxAreaFilter { set; get; } = int.MaxValue;
        #endregion

        #region 尺寸测量
        /// <summary>
        /// 尺寸测量类型
        /// </summary>
        [XmlElement("尺寸测量类型")]
        public DimensionMeasureType MeasureType { set; get; } = DimensionMeasureType.直线到直线;

        /// <summary>
        /// 测量对象1 ROI（直线或圆）
        /// </summary>
        [XmlElement("测量对象1ROI")]
        public ROI MeasureObject1ROI { set; get; } = new ROI();

        /// <summary>
        /// 测量对象2 ROI（直线或圆）
        /// </summary>
        [XmlElement("测量对象2ROI")]
        public ROI MeasureObject2ROI { set; get; } = new ROI();

        /// <summary>
        /// 实际测量值（单位：mm，通过DimensionK参数转换）
        /// </summary>
        [XmlElement("实际测量值mm")]
        public double ActualMeasureValue { set; get; } = 0;

        /// <summary>
        /// 最小允许值（单位：mm）
        /// </summary>
        [XmlElement("最小测量值mm")]
        public double MinMeasureValue { set; get; } = 0;

        /// <summary>
        /// 最大允许值（单位：mm）
        /// </summary>
        [XmlElement("最大测量值mm")]
        public double MaxMeasureValue { set; get; } = 1000;

        /// <summary>
        /// 世界坐标校准：尺寸测量专用比例参数（单位：um/pixel），独立于位置检测的K参数
        /// </summary>
        [XmlElement("尺寸测量比例um/pixel")]
        public double DimensionK { set; get; } = 1000;

        /// <summary>
        /// 校准真实尺寸（单位：mm）
        /// </summary>
        [XmlElement("校准真实尺寸mm")]
        public double CalibrationRealSize { set; get; } = 0;

        /// <summary>
        /// 校准像素尺寸（单位：pixel，测量得到）
        /// </summary>
        [XmlElement("校准像素尺寸")]
        public double CalibrationPixelSize { set; get; } = 0;

        // ========== 卡尺工具参数 ==========

        /// <summary>
        /// 卡尺长度（沿测量方向的长度，单位：pixel）
        /// </summary>
        [XmlElement("卡尺长度")]
        public int CaliperLength { set; get; } = 100;

        /// <summary>
        /// 卡尺宽度（垂直于测量方向的宽度，单位：pixel）
        /// </summary>
        [XmlElement("卡尺宽度")]
        public int CaliperWidth { set; get; } = 5;

        /// <summary>
        /// 卡尺角度（测量方向的角度，单位：度）
        /// </summary>
        [XmlElement("卡尺角度")]
        public double CaliperAngle { set; get; } = 0;

        /// <summary>
        /// 边缘检测阈值（灰度值差异）
        /// </summary>
        [XmlElement("边缘检测阈值")]
        public int EdgeThreshold { set; get; } = 30;

        /// <summary>
        /// 边缘极性（"positive"从暗到亮, "negative"从亮到暗, "all"全部边缘）
        /// </summary>
        [XmlElement("边缘极性")]
        public string EdgePolarity { set; get; } = "all";

        /// <summary>
        /// 亚像素精度
        /// </summary>
        [XmlElement("亚像素精度")]
        public bool SubPixelAccuracy { set; get; } = true;

        /// <summary>
        /// 边缘选择策略（"first"第一个, "last"最后一个, "all"全部, "strongest"最强）
        /// </summary>
        [XmlElement("边缘选择策略")]
        public string EdgeSelection { set; get; } = "first";

        // ========== HALCON Metrology 模型参数 ==========

        /// <summary>
        /// Metrology搜索范围/容差（Tolerance参数，单位：pixel）
        /// 定义在线段两侧搜索边缘的范围
        /// </summary>
        [XmlElement("Metrology搜索范围")]
        public int MetrologyTolerance { set; get; } = 200;

        /// <summary>
        /// 卡尺数量（num_measures参数）
        /// 沿线段分布的测量点数量，越多精度越高但速度越慢
        /// </summary>
        [XmlElement("Metrology卡尺数量")]
        public int MetrologyNumMeasures { set; get; } = 20;

        /// <summary>
        /// 高斯平滑系数（measure_sigma参数）
        /// 用于边缘检测前的图像平滑，减少噪声影响
        /// </summary>
        [XmlElement("Metrology高斯平滑")]
        public double MetrologyMeasureSigma { set; get; } = 2.0;

        /// <summary>
        /// 边缘检测阈值（measure_threshold参数）
        /// 灰度梯度阈值，低于此值的边缘将被忽略
        /// </summary>
        [XmlElement("Metrology边缘阈值")]
        public int MetrologyMeasureThreshold { set; get; } = 20;

        /// <summary>
        /// 边缘过渡类型（measure_transition参数）
        /// 'positive': 暗到亮, 'negative': 亮到暗, 'uniform': 双向, 'all': 全部
        /// </summary>
        [XmlElement("Metrology边缘过渡类型")]
        public string MetrologyMeasureTransition { set; get; } = "uniform";

        /// <summary>
        /// 边缘选择模式（measure_select参数）
        /// 'first': 第一个, 'last': 最后一个, 'all': 全部
        /// </summary>
        [XmlElement("Metrology边缘选择")]
        public string MetrologyMeasureSelect { set; get; } = "all";

        /// <summary>
        /// 最小拟合分数（min_score参数）
        /// 拟合质量阈值，低于此值的结果将被拒绝（0.0-1.0）
        /// </summary>
        [XmlElement("Metrology最小分数")]
        public double MetrologyMinScore { set; get; } = 0.4;

        /// <summary>
        /// 卡尺长度1（measure_length1参数，单位：pixel）
        /// 卡尺矩形在测量方向上的半长度
        /// </summary>
        [XmlElement("Metrology卡尺长度1")]
        public int MetrologyMeasureLength1 { set; get; } = 10;

        /// <summary>
        /// 卡尺长度2（measure_length2参数，单位：pixel）
        /// 卡尺矩形垂直于测量方向的半宽度
        /// </summary>
        [XmlElement("Metrology卡尺长度2")]
        public int MetrologyMeasureLength2 { set; get; } = 5;

        /// <summary>
        /// 是否显示调试信息（显示卡尺位置、边缘点等）
        /// </summary>
        [XmlElement("显示Metrology调试信息")]
        public bool ShowMetrologyDebugInfo { set; get; } = true;

        #endregion

        [XmlIgnore]
        public BitmapSource BitmapSource { get; set; } = null;
        [XmlIgnore]
        [XmlElement("实时照片")]
        public BitmapSource CurrentBitmapSource { set; get; } = null;
        [XmlElement("实时照片路径")]
        public string CurrentBitmapFileName { set; get; } = string.Empty;

        public HObject Image = null;


        /// <summary>
        /// 光标位置
        /// </summary>
        [XmlIgnore]
        public Kposition Tposition { set; get; } = new Kposition() { X = 0, Y = 0 };
        [XmlIgnore]
        public KColor TColor { set; get; } = new KColor();

        #region 发送数据
        public string OKCMD { set; get; } = "OK";
        public string NG1CMD { set; get; } = "NG";
        public string NG2CMD { set; get; } = "NG";

        #endregion

    }

    public enum ToolStatus
    {
        等待中,
        识别中,
        OK,
        NG,
        NG2
    }




    /// <summary>
    /// ROI类型枚举
    /// </summary>
    public enum ROIType
    {
        Rectangle,  // 矩形ROI
        Line,       // 线段ROI（用于Metrology测量）
        Circle      // 圆形ROI（用于圆心测量）
    }

    public class ROI : ObservableObject
    {
        /// <summary>
        /// ROI类型（矩形、线段或圆形）
        /// </summary>
        [XmlElement("ROI类型")]
        public ROIType Type { set; get; } = ROIType.Rectangle;

        // 矩形/线段参数
        public int Row1 { set; get; } = 0;
        public int Row2 { set; get; } = 0;
        public int Col1 { set; get; } = 0;
        public int Col2 { set; get; } = 0;
        
        // 圆形参数
        /// <summary>
        /// 圆心Row坐标（用于Circle类型）
        /// </summary>
        [XmlElement("圆心行坐标")]
        public double CircleCenterRow { set; get; } = 0;
        
        /// <summary>
        /// 圆心Column坐标（用于Circle类型）
        /// </summary>
        [XmlElement("圆心列坐标")]
        public double CircleCenterCol { set; get; } = 0;
        
        /// <summary>
        /// 圆半径（用于Circle类型）
        /// </summary>
        [XmlElement("圆半径")]
        public double CircleRadius { set; get; } = 0;
    }
}
