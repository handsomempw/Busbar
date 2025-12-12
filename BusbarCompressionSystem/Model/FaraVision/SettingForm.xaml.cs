using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.ViewModel;
using HalconDotNet;
using Microsoft.Win32;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Runtime.InteropServices; // 用于GDI句柄管理

namespace BusbarCompressionSystem.Model.FaraVision
{


    /// <summary>
    /// SettingForm.xaml 的交互逻辑
    /// </summary>
    public partial class SettingForm : WindowX
    {
        /// <summary>
        /// 释放GDI句柄，防止句柄泄漏
        /// </summary>
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        private static extern bool DeleteObject(IntPtr hObject);

        ViewModelLocator vml;
        ToolModel t = null;


        bool selectbroi = false;
        bool selectproi = false;
        bool selectdroi = false;
        bool selectmeasureobject1roi = false;
        bool selectmeasureobject2roi = false;

        // 线段ROI绘制状态（两次点击模式）
        private bool _lineDrawingInProgress = false;  // 是否正在绘制线段（已点击第一个点）
        private System.Windows.Point _lineStartPoint;  // 线段起点
        private System.Windows.Threading.DispatcherTimer _metrologyPreviewTimer;  // Metrology参数变更防抖定时器
        
        // 圆形ROI绘制状态（点击圆心 + 拖动半径模式）
        private bool _circleDrawingInProgress = false;  // 是否正在绘制圆形（已点击圆心）
        private System.Windows.Point _circleCenter;  // 圆心位置
        private System.Windows.Shapes.Ellipse _previewCircle;  // 预览圆形控件
        private bool _isFirstCircleClick = true;  // 是否是第一次点击（记录圆心），用于区分MouseUp事件

        public SettingForm()
        {
            InitializeComponent();
            vml = this.FindResource("Locator") as ViewModelLocator;
            t = (ToolModel)DataContext;
        }

        /// <summary>
        /// 选择图片按钮点击事件（参数配置界面）
        /// 
        /// 业务场景：
        /// - 用户在配置工具参数时，需要选择标准样本图片用于ROI绘制、参数调试、校准等操作
        /// - 选中的图片会保存到项目目录，后续生产流程会加载该图片进行模板匹配
        /// 
        /// 核心逻辑：
        /// 1. 弹出文件选择对话框，限定.jpg格式
        /// 2. 调用LoadAndConvertImage加载图片并转换为HALCON和WPF格式
        /// 3. 保存图片副本到项目目录（{Prjdir}\{Name}\Tool{Index}.jpg）
        /// 4. 缩放图片适应窗口大小（zoom_all）
        /// 
        /// 与其他模块关联：
        /// - LoadAndConvertImage：统一图片加载逻辑，避免重复读取
        /// - refreshrectangle：刷新矩形/线段/圆形ROI的WPF显示控件
        /// - ResetDrawingStates：重置所有选择标志和绘制状态，确保交互流程正确
        /// </summary>
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "*.jpg|*.jpg";
                if (ofd.ShowDialog() == true)
                {
                    string filename = ofd.FileName;

                    // 1. 加载并转换图片
                    if (!LoadAndConvertImage(filename))
                    {
                        return; // 加载失败，LoadAndConvertImage已显示错误提示
                    }

                    // 2. 保存图片到项目目录
                    string newfilename = $"{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{t.Index}.jpg";
                    string dir = System.IO.Path.GetDirectoryName(newfilename);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.Copy(filename, newfilename, true);

                    // 3. 缩放图片以适应窗口
                    zoom_all();
                    
                    // 4. 刷新已保存的ROI显示（修复切换图片后ROI不显示的问题）
                    refreshrectangle();
                    
                    // 5. 重置绘制状态（修复无法重新绘制ROI的问题）
                    ResetDrawingStates();
                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show(ex.Message, "错误", MessageBoxIcon.Error, true, 10000);
            }
            GC.Collect();
        }

        #region 画矩形

        private System.Windows.Point _downPoint;
        private bool _started = false;
        private void rectange_MouseMove(object sender, MouseEventArgs e)
        {
            // 处理圆形ROI绘制时的实时预览（拖动半径）
            if (_circleDrawingInProgress)
            {
                var currentPoint = e.GetPosition(show_image_canvas);
                // 计算当前半径
                double radius = Math.Sqrt(
                    Math.Pow(currentPoint.X - _circleCenter.X, 2) +
                    Math.Pow(currentPoint.Y - _circleCenter.Y, 2)
                );
                
                // 更新圆形预览
                UpdateCirclePreview(_circleCenter, radius);
                return;
            }
            
            // 处理线段ROI绘制时的实时预览
            if (_lineDrawingInProgress && (selectmeasureobject1roi || selectmeasureobject2roi))
            {
                var currentPoint = e.GetPosition(show_image_canvas);

                if (selectmeasureobject1roi)
                {
                    LineMeasureObject1.X2 = currentPoint.X;
                    LineMeasureObject1.Y2 = currentPoint.Y;
                }
                else if (selectmeasureobject2roi)
                {
                    LineMeasureObject2.X2 = currentPoint.X;
                    LineMeasureObject2.Y2 = currentPoint.Y;
                }
                return;
            }

            // 处理矩形ROI的拖动绘制
            if (_started)
            {
                var point = e.GetPosition(show_image_canvas);
                var rect = new Rect(_downPoint, point);
                if (selectbroi)
                {
                    RectangleBarcode.Margin = new Thickness(rect.Left, rect.Top, 0, 0);
                    RectangleBarcode.Width = rect.Width;
                    RectangleBarcode.Height = rect.Height;
                }
                else if (selectproi)
                {
                    RectanglePosition.Margin = new Thickness(rect.Left, rect.Top, 0, 0);
                    RectanglePosition.Width = rect.Width;
                    RectanglePosition.Height = rect.Height;
                }
                else if (selectdroi)
                {
                    RectangleDimension.Margin = new Thickness(rect.Left, rect.Top, 0, 0);
                    RectangleDimension.Width = rect.Width;
                    RectangleDimension.Height = rect.Height;
                }
            }
        }

        private void rectange_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 处理圆形ROI的两次点击模式（点击圆心）
            if ((selectmeasureobject1roi && t.MeasureObject1ROI.Type == ROIType.Circle) ||
                (selectmeasureobject2roi && t.MeasureObject2ROI.Type == ROIType.Circle))
            {
                if (!_circleDrawingInProgress)
                {
                    // 第一次点击：记录圆心
                    var clickPoint = e.GetPosition(show_image_canvas);
                    _circleCenter = clickPoint;
                    _circleDrawingInProgress = true;
                    _isFirstCircleClick = true;  // 标记这是第一次点击
                    
                    // 初始化预览圆形控件
                    InitializeCirclePreview();
                }
                // 第二次点击在MouseUp中处理
                return;
            }
            
            // 处理线段ROI的两次点击模式
            if ((selectmeasureobject1roi && t.MeasureObject1ROI.Type == ROIType.Line) ||
                (selectmeasureobject2roi && t.MeasureObject2ROI.Type == ROIType.Line))
            {
                var clickPoint = e.GetPosition(show_image_canvas);

                if (!_lineDrawingInProgress)
                {
                    // 第一次点击：记录起点
                    _lineStartPoint = clickPoint;
                    _lineDrawingInProgress = true;

                    // 更新Line控件起点
                    if (selectmeasureobject1roi)
                    {
                        LineMeasureObject1.X1 = clickPoint.X;
                        LineMeasureObject1.Y1 = clickPoint.Y;
                        LineMeasureObject1.X2 = clickPoint.X;  // 终点暂时与起点相同
                        LineMeasureObject1.Y2 = clickPoint.Y;
                    }
                    else if (selectmeasureobject2roi)
                    {
                        LineMeasureObject2.X1 = clickPoint.X;
                        LineMeasureObject2.Y1 = clickPoint.Y;
                        LineMeasureObject2.X2 = clickPoint.X;
                        LineMeasureObject2.Y2 = clickPoint.Y;
                    }
                }
                // 第二次点击在MouseUp中处理
                return;
            }

            // 处理矩形ROI的拖动模式
            if (selectproi || selectbroi || selectdroi)
            {
                _downPoint = e.GetPosition(show_image_canvas);
                _started = true;
            }
        }

        private void rectange_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 处理圆形ROI的点击（第一次点击的MouseUp忽略，第二次点击的MouseUp确定半径）
            if (_circleDrawingInProgress && (selectmeasureobject1roi || selectmeasureobject2roi))
            {
                // 如果是第一次点击的MouseUp，忽略它，等待用户移动鼠标后的第二次点击
                if (_isFirstCircleClick)
                {
                    _isFirstCircleClick = false;  // 标记下次点击为第二次
                    return;  // 忽略第一次点击的MouseUp
                }
                
                // 第二次点击：确定半径
                var endPoint = e.GetPosition(show_image_canvas);
                
                // 计算半径
                double radius = Math.Sqrt(
                    Math.Pow(endPoint.X - _circleCenter.X, 2) +
                    Math.Pow(endPoint.Y - _circleCenter.Y, 2)
                );
                
                // 检查半径是否有效（至少5像素）
                if (radius < 5)
                {
                    // 半径太小，忽略
                    RemoveCirclePreview();
                    _circleDrawingInProgress = false;
                    _isFirstCircleClick = true;  // 重置状态
                    return;
                }
                
                // 保存圆形ROI数据（Row=Y, Col=X）
                if (selectmeasureobject1roi)
                {
                    t.MeasureObject1ROI.CircleCenterRow = _circleCenter.Y;
                    t.MeasureObject1ROI.CircleCenterCol = _circleCenter.X;
                    t.MeasureObject1ROI.CircleRadius = radius;
                    
                    selectmeasureobject1roi = false;
                    SelectMeasureObject1ROI.Background = System.Windows.Media.Brushes.Gray;
                }
                else if (selectmeasureobject2roi)
                {
                    t.MeasureObject2ROI.CircleCenterRow = _circleCenter.Y;
                    t.MeasureObject2ROI.CircleCenterCol = _circleCenter.X;
                    t.MeasureObject2ROI.CircleRadius = radius;
                    
                    selectmeasureobject2roi = false;
                    SelectMeasureObject2ROI.Background = System.Windows.Media.Brushes.Gray;
                }
                
                // 清理WPF预览圆形
                RemoveCirclePreview();
                _circleDrawingInProgress = false;
                _isFirstCircleClick = true;  // 重置状态，准备下次绘制

                // 刷新UI上保存的ROI显示（包含新增的圆形ROI持久化显示）
                refreshrectangle();
                
                // 更新UI参数显示
                UpdateROIParamsUIVisibility();
                
                // 调用HALCON预览方法显示圆形ROI和边缘检测结果
                try
                {
                    if (t.Image != null && t.TestMode == TestModes.尺寸测量)
                    {
                        vml.Main.PreviewDimensionMeasurement(t.Image, t, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.HWindow);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"圆形ROI预览失败: {ex.Message}");
                    NoticeBox.Show($"预览失败: {ex.Message}\n请检查Metrology参数设置", "提示", MessageBoxIcon.Warning, true, 5000);
                }
                
                return;
            }
            
            // 处理线段ROI的第二次点击（终点）
            if (_lineDrawingInProgress && (selectmeasureobject1roi || selectmeasureobject2roi))
            {
                var endPoint = e.GetPosition(show_image_canvas);

                // 检查线段长度是否有效（至少5像素）
                double lineLength = Math.Sqrt(
                    Math.Pow(endPoint.X - _lineStartPoint.X, 2) +
                    Math.Pow(endPoint.Y - _lineStartPoint.Y, 2)
                );

                if (lineLength < 5)
                {
                    // 线段太短，忽略
                    _lineDrawingInProgress = false;
                    return;
                }

                // 保存线段ROI数据（Row=Y, Col=X）
                if (selectmeasureobject1roi)
                {
                    t.MeasureObject1ROI.Row1 = (int)_lineStartPoint.Y;
                    t.MeasureObject1ROI.Col1 = (int)_lineStartPoint.X;
                    t.MeasureObject1ROI.Row2 = (int)endPoint.Y;
                    t.MeasureObject1ROI.Col2 = (int)endPoint.X;

                    selectmeasureobject1roi = false;
                    SelectMeasureObject1ROI.Background = System.Windows.Media.Brushes.Gray;
                }
                else if (selectmeasureobject2roi)
                {
                    t.MeasureObject2ROI.Row1 = (int)_lineStartPoint.Y;
                    t.MeasureObject2ROI.Col1 = (int)_lineStartPoint.X;
                    t.MeasureObject2ROI.Row2 = (int)endPoint.Y;
                    t.MeasureObject2ROI.Col2 = (int)endPoint.X;

                    selectmeasureobject2roi = false;
                    SelectMeasureObject2ROI.Background = System.Windows.Media.Brushes.Gray;
                }

                _lineDrawingInProgress = false;
                return;
            }

            // 处理矩形ROI的拖动完成
            if (selectbroi)
            {

                if (RectangleBarcode.Width == 0 || RectangleBarcode.Height == 0) { return; }
                if (double.IsNaN(RectangleBarcode.Width) || double.IsNaN(RectangleBarcode.Height)) { return; }
                _started = false;
                t.BarCodeROI.Row1 = (int)RectangleBarcode.Margin.Top;
                t.BarCodeROI.Col1 = (int)RectangleBarcode.Margin.Left;
                t.BarCodeROI.Row2 = (int)(RectangleBarcode.Margin.Top + RectangleBarcode.Height);
                t.BarCodeROI.Col2 = (int)(RectangleBarcode.Margin.Left + RectangleBarcode.Width);
                selectbroi = false;
                SelectBarcodeROI.Background = selectproi ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.Gray;
            }
            else if (selectproi)
            {
                if (RectanglePosition.Width == 0 || RectanglePosition.Height == 0) { return; }
                if (double.IsNaN(RectanglePosition.Width) || double.IsNaN(RectanglePosition.Height)) { return; }
                _started = false;
                t.PositionROI.Row1 = (int)RectanglePosition.Margin.Top;
                t.PositionROI.Col1 = (int)RectanglePosition.Margin.Left;
                t.PositionROI.Row2 = (int)(RectanglePosition.Margin.Top + RectanglePosition.Height);
                t.PositionROI.Col2 = (int)(RectanglePosition.Margin.Left + RectanglePosition.Width);
                selectproi = false;
                SelectPositionROI.Background = selectproi ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.Gray;
            }
            else if (selectdroi)
            {

                if (RectangleDimension.Width == 0 || RectangleDimension.Height == 0) { return; }
                if (double.IsNaN(RectangleDimension.Width) || double.IsNaN(RectangleDimension.Height)) { return; }
                _started = false;
                t.DimensionROI.Row1 = (int)RectangleDimension.Margin.Top;
                t.DimensionROI.Col1 = (int)RectangleDimension.Margin.Left;
                t.DimensionROI.Row2 = (int)(RectangleDimension.Margin.Top + RectangleDimension.Height);
                t.DimensionROI.Col2 = (int)(RectangleDimension.Margin.Left + RectangleDimension.Width);
                selectdroi = false;
                SelectDimensionROI.Background = selectdroi ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.Gray;

            }

        }



        private void refreshrectangle()
        {
            try
            {

                var t = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool;
                RectangleBarcode.Margin = new Thickness(t.BarCodeROI.Col1, t.BarCodeROI.Row1, 0, 0);
                RectangleBarcode.Width = t.BarCodeROI.Col2 - t.BarCodeROI.Col1;
                RectangleBarcode.Height = t.BarCodeROI.Row2 - t.BarCodeROI.Row1;

                RectanglePosition.Margin = new Thickness(t.PositionROI.Col1, t.PositionROI.Row1, 0, 0);
                RectanglePosition.Width = t.PositionROI.Col2 - t.PositionROI.Col1;
                RectanglePosition.Height = t.PositionROI.Row2 - t.PositionROI.Row1;

                RectangleDimension.Margin = new Thickness(t.DimensionROI.Col1, t.DimensionROI.Row1, 0, 0);
                RectangleDimension.Width = t.DimensionROI.Col2 - t.DimensionROI.Col1;
                RectangleDimension.Height = t.DimensionROI.Row2 - t.DimensionROI.Row1;

                // 刷新测量对象1 ROI
                if (t.MeasureObject1ROI.Type == ROIType.Circle && t.MeasureObject1ROI.CircleRadius > 0)
                {
                    LineMeasureObject1.Visibility = Visibility.Collapsed;
                    CircleMeasureObject1.Visibility = Visibility.Visible;
                    double r = t.MeasureObject1ROI.CircleRadius;
                    CircleMeasureObject1.Width = r * 2;
                    CircleMeasureObject1.Height = r * 2;
                    CircleMeasureObject1.Margin = new Thickness(
                        t.MeasureObject1ROI.CircleCenterCol - r,
                        t.MeasureObject1ROI.CircleCenterRow - r,
                        0, 0);
                }
                else
                {
                    // 线段/矩形模式：显示线段
                    LineMeasureObject1.Visibility = Visibility.Visible;
                    CircleMeasureObject1.Visibility = Visibility.Collapsed;
                    LineMeasureObject1.X1 = t.MeasureObject1ROI.Col1;
                    LineMeasureObject1.Y1 = t.MeasureObject1ROI.Row1;
                    LineMeasureObject1.X2 = t.MeasureObject1ROI.Col2;
                    LineMeasureObject1.Y2 = t.MeasureObject1ROI.Row2;
                }

                // 刷新测量对象2 ROI
                if (t.MeasureObject2ROI.Type == ROIType.Circle && t.MeasureObject2ROI.CircleRadius > 0)
                {
                    LineMeasureObject2.Visibility = Visibility.Collapsed;
                    CircleMeasureObject2.Visibility = Visibility.Visible;
                    double r = t.MeasureObject2ROI.CircleRadius;
                    CircleMeasureObject2.Width = r * 2;
                    CircleMeasureObject2.Height = r * 2;
                    CircleMeasureObject2.Margin = new Thickness(
                        t.MeasureObject2ROI.CircleCenterCol - r,
                        t.MeasureObject2ROI.CircleCenterRow - r,
                        0, 0);
                }
                else
                {
                    LineMeasureObject2.Visibility = Visibility.Visible;
                    CircleMeasureObject2.Visibility = Visibility.Collapsed;
                    LineMeasureObject2.X1 = t.MeasureObject2ROI.Col1;
                    LineMeasureObject2.Y1 = t.MeasureObject2ROI.Row1;
                    LineMeasureObject2.X2 = t.MeasureObject2ROI.Col2;
                    LineMeasureObject2.Y2 = t.MeasureObject2ROI.Row2;
                }
            }
            catch (Exception ex) { }

        }
        #endregion

        //private bool mouseDown = false;
        private System.Windows.Point mouseXY;
        int X, Y = 0;
        private void Show_image_ctr_MouseMove(object sender, MouseEventArgs e)
        {
            if (_started)
            {
                var group = show_image_ctr.FindResource("Imageview") as TransformGroup;
                var transform = group.Children[0] as ScaleTransform;
                System.Windows.Controls.Image img = sender as System.Windows.Controls.Image;
                X = Convert.ToInt32(e.GetPosition(show_image_ctr).X);
                Y = Convert.ToInt32(e.GetPosition(img).Y);

            }


            try
            {
                System.Windows.Controls.Image img = sender as System.Windows.Controls.Image;

                X = Convert.ToInt32(e.GetPosition(show_image_ctr).X);
                Y = Convert.ToInt32(e.GetPosition(img).Y);
                var t = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool;
                t.Tposition.X = X;
                t.Tposition.Y = Y;

                var c = vml.Main.GetPixelData(X, Y);
                t.TColor = c;
            }
            catch (Exception ex)
            {

            }


        }




        private void ContentControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("Left");
            rectange_MouseDown(sender, e);

        }
        private void ContentControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("mouseup");
            rectange_MouseUp(sender, e);

        }

        private void ContentControl_MouseMove(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("mousemove");
            rectange_MouseMove(sender, e);

        }
        private void Domousemove(ContentControl img, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            var group = show_image_ctr.FindResource("Imageview") as TransformGroup;
            var transform = group.Children[1] as TranslateTransform;
            var position = e.GetPosition(img);
            transform.X -= mouseXY.X - position.X;
            transform.Y -= mouseXY.Y - position.Y;
            mouseXY = position;
        }
        private void ContentControl_MouseWheel(object sender, MouseWheelEventArgs e)
        {

        }
        private void DowheelZoom(TransformGroup group, System.Windows.Point point, double delta)
        {
            var pointToContent = group.Inverse.Transform(point);
            var transform = group.Children[0] as ScaleTransform;
            if (transform.ScaleX + delta < 0.1) return;
            transform.ScaleX += delta;
            transform.ScaleY += delta;
            var transform1 = group.Children[1] as TranslateTransform;
            transform1.X = -1 * ((pointToContent.X * transform.ScaleX) - point.X);
            transform1.Y = -1 * ((pointToContent.Y * transform.ScaleY) - point.Y);

        }

        public void zoom_all()
        {
            try
            {
                zoom_all(vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.Width, vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.Height);
                //zoom_all(vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.BitmapSource.Width, vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.BitmapSource.Height);
            }
            catch (Exception ex) {; }
        }
        public void zoom_all(double w2, double h2)
        {
            try
            {
                TransformGroup group = show_image_ctr.FindResource("Imageview") as TransformGroup;
                var transform = group.Children[0] as ScaleTransform;
                double w1 = BackFrame.ActualWidth, h1 = BackFrame.ActualHeight;
                //double w2 = show_image_ctr.ActualWidth, h2 = show_image_ctr.ActualHeight;
                double scaleX = w1 / w2, scaleY = h1 / h2;
                double scale = Math.Min(scaleX, scaleY);
                transform.CenterX = 0;
                transform.CenterY = 0;
                transform.ScaleX = scale;
                transform.ScaleY = scale;


                move_to_center(group, scale, w1, h1);
            }
            catch
            {
                ;
            }
        }

        private void WindowX_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            zoom_all();
            refreshrectangle();
        }

        private void SelectPositionROI_Click(object sender, RoutedEventArgs e)
        {
            selectproi = !selectproi;

            SelectPositionROI.Background = selectproi ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.Gray;
        }

        private void SelectBarcodeROI_Click(object sender, RoutedEventArgs e)
        {
            selectbroi = !selectbroi;
            SelectBarcodeROI.Background = selectbroi ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.Gray;

        }

        private void modelsetting_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string shmfilename = $"{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.Index - 1].Index}.shm";
                ModelWindow modelWindow = new ModelWindow(t.Image, shmfilename);
                modelWindow.ShowDialog();
            }
            catch { }

        }

        private void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否确定应用并更新所有配置?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == MessageBoxResult.Yes)
            {
                for (int i = 0; i < vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
                {
                    try
                    {
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[i].MinScore = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.MinScore;
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[i].K = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.K;
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[i].Allow_X_Delta = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.Allow_X_Delta;
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[i].Allow_Y_Delta = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.Allow_Y_Delta;
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[i].AllowAngleDelta = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.AllowAngleDelta;
                    }
                    catch { }
                }
            }
        }

        private void SelectDimensionROI_Click(object sender, RoutedEventArgs e)
        {
            selectdroi = !selectdroi;
            SelectDimensionROI.Background = selectdroi ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.Gray;
        }

        private void DimensionApply_Click(object sender, RoutedEventArgs e)
        {
            int area = vml.Main.CoculateDimension(vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.Image, vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.HWindow, true);
            NoticeBox.Show($"识别面积大小:{area}", "提示", MessageBoxIcon.Info, true, 10000);
        }

        /// <summary>
        /// 面积测试按钮点击事件（从文件选择图片进行测试）
        /// 
        /// 业务场景：
        /// - 用户在调试面积测量参数时，使用测试图片快速验证阈值、面积过滤等参数是否合理
        /// - 与DimensionApply_Click的区别：该方法临时加载图片测试，不影响工具配置的图片（t.Image）
        /// 
        /// 核心逻辑：
        /// 1. 弹出文件选择对话框，限定.jpg格式
        /// 2. 临时加载图片到HObject（不更新t.Image，避免覆盖配置图片）
        /// 3. 调用CoculateDimension计算面积，并在HALCON窗口绘制结果
        /// 4. 弹框显示面积数值，供用户判断参数是否合理
        /// 5. 释放临时图像资源，避免内存泄漏
        /// 
        /// - 旧版本：连续调用两次GenEmptyObj，造成资源浪费
        /// - 新版本：只调用一次GenEmptyObj，并在完成后显式释放资源
        /// 
        /// 与其他模块关联：
        /// - CoculateDimension：面积计算核心方法，使用阈值分割、连通域筛选等算法
        /// </summary>
        private void DimensionApply_Pic_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "*.jpg|*.jpg";
                if (ofd.ShowDialog() == true)
                {
                    string filename = ofd.FileName;

                    // 加载测试图片
                    HObject image;
                    HOperatorSet.GenEmptyObj(out image);
                    HOperatorSet.ReadImage(out image, filename);
                    
                    // 计算面积
                    int area = vml.Main.CoculateDimension(image, vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.HWindow, true);
                    NoticeBox.Show($"识别面积大小:{area}", "提示", MessageBoxIcon.Info, true, 10000);
                    
                    // 释放图像资源
                    image?.Dispose();
                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"面积测试失败: {ex.Message}", "错误", MessageBoxIcon.Error, true, 5000);
            }
        }

        #region 尺寸测量相关事件处理

        private void SelectMeasureObject1ROI_Click(object sender, RoutedEventArgs e)
        {
            selectmeasureobject1roi = !selectmeasureobject1roi;
            SelectMeasureObject1ROI.Background = selectmeasureobject1roi ? System.Windows.Media.Brushes.Lime : System.Windows.Media.Brushes.Gray;

            // 根据当前测量类型确定ROI类型（由MeasureType_SelectionChanged统一管理）
            if (selectmeasureobject1roi)
            {
                selectmeasureobject2roi = false;
                SelectMeasureObject2ROI.Background = System.Windows.Media.Brushes.Gray;
                _lineDrawingInProgress = false;  // 重置线段绘制状态
                _circleDrawingInProgress = false;  // 重置圆形绘制状态
                _isFirstCircleClick = true;  // 重置圆形点击状态
            }
        }

        private void SelectMeasureObject2ROI_Click(object sender, RoutedEventArgs e)
        {
            selectmeasureobject2roi = !selectmeasureobject2roi;
            SelectMeasureObject2ROI.Background = selectmeasureobject2roi ? System.Windows.Media.Brushes.Cyan : System.Windows.Media.Brushes.Gray;

            // 根据当前测量类型确定ROI类型（由MeasureType_SelectionChanged统一管理）
            if (selectmeasureobject2roi)
            {
                selectmeasureobject1roi = false;
                SelectMeasureObject1ROI.Background = System.Windows.Media.Brushes.Gray;
                _lineDrawingInProgress = false;  // 重置线段绘制状态
                _circleDrawingInProgress = false;  // 重置圆形绘制状态
                _isFirstCircleClick = true;  // 重置圆形点击状态
            }
        }

        /// <summary>
        /// 测量类型切换事件：根据测量类型自动设置ROI类型
        /// </summary>
        private void MeasureType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (t == null) return;
            
            // 根据测量类型自动设置ROI类型
            switch (t.MeasureType)
            {
                case DimensionMeasureType.直线到直线:
                    // 两个对象都是Line
                    t.MeasureObject1ROI.Type = ROIType.Line;
                    t.MeasureObject2ROI.Type = ROIType.Line;
                    break;
                    
                case DimensionMeasureType.直线到圆心:
                    // 对象1是Line，对象2是Circle
                    t.MeasureObject1ROI.Type = ROIType.Line;
                    t.MeasureObject2ROI.Type = ROIType.Circle;
                    break;
                    
                case DimensionMeasureType.圆心到圆心:
                    // 两个对象都是Circle
                    t.MeasureObject1ROI.Type = ROIType.Circle;
                    t.MeasureObject2ROI.Type = ROIType.Circle;
                    break;
            }
            
            // 更新UI显示：根据ROI类型切换参数显示
            UpdateROIParamsUIVisibility();
            
            // 清理现有ROI选择状态
            selectmeasureobject1roi = false;
            selectmeasureobject2roi = false;
            SelectMeasureObject1ROI.Background = System.Windows.Media.Brushes.Gray;
            SelectMeasureObject2ROI.Background = System.Windows.Media.Brushes.Gray;
            
            // 重置绘制状态
            _lineDrawingInProgress = false;
            _circleDrawingInProgress = false;
            _isFirstCircleClick = true;  // 重置圆形点击状态
            RemoveCirclePreview();
            
            // 清空线段预览（将线段设置为0长度，实际上隐藏它们）
            LineMeasureObject1.X1 = 0;
            LineMeasureObject1.Y1 = 0;
            LineMeasureObject1.X2 = 0;
            LineMeasureObject1.Y2 = 0;
            LineMeasureObject2.X1 = 0;
            LineMeasureObject2.Y1 = 0;
            LineMeasureObject2.X2 = 0;
            LineMeasureObject2.Y2 = 0;
        }

        /// <summary>
        /// 根据ROI类型更新参数UI的显示/隐藏
        /// </summary>
        private void UpdateROIParamsUIVisibility()
        {
            if (t == null) return;
            
            // 测量对象1的UI切换
            if (t.MeasureObject1ROI.Type == ROIType.Circle)
            {
                MeasureObject1LineParams.Visibility = Visibility.Collapsed;
                MeasureObject1CircleParams.Visibility = Visibility.Visible;
            }
            else
            {
                MeasureObject1LineParams.Visibility = Visibility.Visible;
                MeasureObject1CircleParams.Visibility = Visibility.Collapsed;
            }
            
            // 测量对象2的UI切换
            if (t.MeasureObject2ROI.Type == ROIType.Circle)
            {
                MeasureObject2LineParams.Visibility = Visibility.Collapsed;
                MeasureObject2CircleParams.Visibility = Visibility.Visible;
            }
            else
            {
                MeasureObject2LineParams.Visibility = Visibility.Visible;
                MeasureObject2CircleParams.Visibility = Visibility.Collapsed;
            }
        }

        private void CalibrateDimensionK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证图像
                if (t.Image == null)
                {
                    NoticeBox.Show("请先选择图片", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                // 验证ROI设置（根据ROI类型检查）
                if (!IsROIValid(t.MeasureObject1ROI))
                {
                    NoticeBox.Show("请先选择测量对象1区域", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                // 对于直线到直线测量，需要验证测量对象2的ROI
                if (t.MeasureType == DimensionMeasureType.直线到直线)
                {
                    if (!IsROIValid(t.MeasureObject2ROI))
                    {
                        NoticeBox.Show("直线到直线测量需要选择测量对象2区域", "提示", MessageBoxIcon.Warning, true, 5000);
                        return;
                    }
                }

                // 验证真实尺寸输入
                if (t.CalibrationRealSize <= 0)
                {
                    NoticeBox.Show("请输入有效的真实尺寸（大于0）", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                // 执行完整的边缘检测+测量算法（校准模式）
                // Source: 方案A完整测量校准 - 调用实际测量算法获取像素值
                double pixelSize = vml.Main.MeasureDimension(t.Image, t, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.HWindow, true, calibrationMode: true);

                if (pixelSize <= 0)
                {
                    NoticeBox.Show("测量失败，请检查：\n1. ROI区域是否正确框选边缘\n2. 边缘类型（极性）是否匹配\n3. 边缘阈值是否合适\n4. Metrology参数是否正确",
                        "测量失败", MessageBoxIcon.Error, true, 8000);
                    return;
                }

                // 计算比例：真实尺寸(mm) / 测量像素尺寸(pixel) * 1000 = um/pixel
                // Source: 方案A完整测量校准 - 基于实际测量值计算校准系数
                t.DimensionK = (t.CalibrationRealSize / pixelSize) * 1000.0;
                t.CalibrationPixelSize = pixelSize;

                NoticeBox.Show($"校准成功！\n测量像素值: {pixelSize:F2} pixel\n真实尺寸: {t.CalibrationRealSize:F2} mm\n比例: {t.DimensionK:F2} um/pixel",
                    "校准成功", MessageBoxIcon.Success, true, 8000);
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"校准失败: {ex.Message}", "错误", MessageBoxIcon.Error, true, 8000);
            }
        }

        private void DimensionMeasureApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (t.Image == null)
                {
                    NoticeBox.Show("请先选择图片", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                // 验证ROI设置（根据ROI类型检查）
                if (!IsROIValid(t.MeasureObject1ROI))
                {
                    NoticeBox.Show("请先选择测量对象1区域", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                if (!IsROIValid(t.MeasureObject2ROI))
                {
                    NoticeBox.Show("请先选择测量对象2区域", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                // 检查是否已校准：DimensionK应该是通过校准计算得到的，如果还是默认值1000且CalibrationRealSize为0，说明未校准
                if (t.DimensionK <= 0 || (t.DimensionK == 1000 && t.CalibrationRealSize == 0))
                {
                    NoticeBox.Show("请先进行世界坐标校准：\n1. 选择测量对象1区域\n2. 输入真实尺寸（mm）\n3. 点击校准按钮", 
                        "需要校准", MessageBoxIcon.Warning, true, 8000);
                    return;
                }

                // 执行测量
                double measureValue = vml.Main.MeasureDimension(t.Image, t, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.HWindow, true);
                
                if (measureValue < 0)
                {
                    NoticeBox.Show("测量失败，请检查：\n1. ROI区域是否正确框选\n2. 边缘类型是否匹配\n3. 边缘灵敏度是否合适\n4. 是否已进行世界坐标校准", 
                        "测量失败", MessageBoxIcon.Error, true, 8000);
                }
                else
                {
                    string status = (measureValue >= t.MinMeasureValue && measureValue <= t.MaxMeasureValue) ? "OK" : "NG";
                    NoticeBox.Show($"测量结果: {measureValue:F3} mm\n状态: {status}\n范围: {t.MinMeasureValue} - {t.MaxMeasureValue} mm", 
                        "测量结果", MessageBoxIcon.Info, true, 10000);
                }
            }
            catch (Exception ex)
            {
                // 显示详细的错误信息
                string errorMsg = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMsg += "\n" + ex.InnerException.Message;
                }
                NoticeBox.Show($"测量失败: {errorMsg}\n\n建议：\n1. 检查ROI区域是否正确\n2. 调整边缘检测参数（展开卡尺参数）\n3. 确认已进行世界坐标校准",
                    "测量失败", MessageBoxIcon.Error, true, 10000);
            }
        }

        /// <summary>
        /// Metrology参数变更事件处理器（300ms防抖）
        /// 实现参数变更时的防抖预览刷新
        /// </summary>
        private void MetrologyParameter_Changed(object sender, RoutedEventArgs e)
        {
            // 初始化定时器（仅第一次）
            if (_metrologyPreviewTimer == null)
            {
                _metrologyPreviewTimer = new System.Windows.Threading.DispatcherTimer();
                _metrologyPreviewTimer.Interval = TimeSpan.FromMilliseconds(300);
                _metrologyPreviewTimer.Tick += (s, args) =>
                {
                    _metrologyPreviewTimer.Stop();

                    // 执行预览刷新
                    try
                    {
                        if (t.Image != null && t.TestMode == TestModes.尺寸测量)
                        {
                            // 调用MainViewModel的预览方法
                            vml.Main.PreviewDimensionMeasurement(t.Image, t, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.HWindow);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Metrology预览刷新失败: {ex.Message}");
                    }
                };
            }

            // 重置定时器（防抖）
            _metrologyPreviewTimer.Stop();
            _metrologyPreviewTimer.Start();
        }

        #endregion

        private void move_to_center(TransformGroup group, double scale, double w1, double h1)
        {
            double w3 = show_image_ctr.ActualWidth * scale, h3 = show_image_ctr.ActualHeight * scale;
            double distanceX = (w1 - w3) / 2;
            double distanceY = (h1 - h3) / 2;
            var position = show_image_ctr.TranslatePoint(new System.Windows.Point(0, 0), (UIElement)show_image_ctr.Parent);
            var transform1 = group.Children[1] as TranslateTransform;
            transform1.X = distanceX;
            transform1.Y = distanceY;
        }

        #region 图片加载与状态管理

        /// <summary>
        /// 加载图片并转换为BitmapSource（避免重复读取，优化资源管理）
        /// 
        /// 业务场景：
        /// - 用户在参数配置界面选择图片，用于ROI绘制、测量参数调试、校准等操作
        /// - 图片需要同时用于HALCON图像处理（t.Image）和WPF界面显示（BitmapSource）
        /// 
        /// 核心逻辑：
        /// 1. 加载HALCON图像到工具模型（t.Image），供后续测量、校准、边缘检测等算法使用
        /// 2. 生成缩略图用于工具列表预览（CurrentBitmapSource）
        /// 3. 转换原图为WPF格式用于参数配置界面显示（ShowBitmapSource）
        /// 4. 只读取图片文件一次，避免旧版本重复读取造成的资源浪费
        /// 5. 使用try-finally确保GDI句柄和HALCON对象正确释放，防止内存泄漏
        /// 
        /// 与其他模块关联：
        /// - t.Image：被校准（CalibrateDimensionK_Click）、测量（DimensionMeasureApply_Click）、预览（PreviewDimensionMeasurement）等方法使用
        /// - ShowBitmapSource：绑定到界面Image控件，显示原图并叠加ROI绘制层
        /// </summary>
        /// <param name="filename">图片文件完整路径（支持.jpg格式）</param>
        /// <returns>成功返回true，失败时弹出错误提示并返回false</returns>
        private bool LoadAndConvertImage(string filename)
        {
            try
            {
                // 1. 加载HALCON图像到工具模型
                t.Image?.Dispose();
                HOperatorSet.GenEmptyObj(out t.Image);
                HOperatorSet.ReadImage(out t.Image, filename);

                // 2. 转换为缩略图和原图的BitmapSource
                HObject reducedImage = null;
                Bitmap bmpReduced = null;
                Bitmap bmpOriginal = null;
                
                try
                {
                    // 生成缩略图
                    reducedImage = vml.Main.GetReducedImage(
                        vml.Main.DataModel.FaraVisionDataModel.Settingmodel.ImageSize,
                        vml.Main.DataModel.FaraVisionDataModel.Settingmodel.ImageSize,
                        t.Image);
                    
                    // 转换为Bitmap
                    Hobject2Bitmap.HobjectToBitmap24(reducedImage, out bmpReduced);
                    Hobject2Bitmap.HobjectToBitmap24(t.Image, out bmpOriginal);

                    // 3. 更新CurrentBitmapSource（缩略图）
                    IntPtr hBitmap1 = bmpReduced.GetHbitmap();
                    try
                    {
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.CurrentBitmapSource = null;
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.CurrentBitmapSource = 
                            Imaging.CreateBitmapSourceFromHBitmap(hBitmap1, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                    finally
                    {
                        DeleteObject(hBitmap1);
                    }

                    // 4. 更新ShowBitmapSource（原图）
                    IntPtr hBitmap2 = bmpOriginal.GetHbitmap();
                    try
                    {
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = 
                            Imaging.CreateBitmapSourceFromHBitmap(hBitmap2, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                    finally
                    {
                        DeleteObject(hBitmap2);
                    }

                    return true;
                }
                finally
                {
                    // 清理临时资源
                    reducedImage?.Dispose();
                    bmpReduced?.Dispose();
                    bmpOriginal?.Dispose();
                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"图片加载失败: {ex.Message}", "错误", MessageBoxIcon.Error, true, 5000);
                return false;
            }
        }

        /// <summary>
        /// 重置所有绘制状态（用于切换图片、测量类型等场景）
        /// 
        /// 业务场景：
        /// - 用户切换图片后，需要清空之前的绘制状态，允许在新图片上重新绘制ROI
        /// - 用户切换测量类型（直线到直线 → 圆心到圆心）时，需要清理不适用的ROI绘制状态
        /// - 用户取消当前ROI绘制操作时，需要恢复初始状态
        /// 
        /// 核心逻辑：
        /// 1. 重置6个ROI选择标志（条码/定位/尺寸/测量对象1/测量对象2），确保只有一个ROI处于激活状态
        /// 2. 重置3个绘制进度标志（线段绘制/圆形绘制/圆心点击），避免绘制流程卡在中间状态
        /// 3. 恢复5个选择按钮背景色为默认灰色，提供视觉反馈
        /// 4. 清理临时预览控件（圆形预览Ellipse），避免残留UI元素
        /// 
        /// 调用时机：
        /// - Button_Click（选择图片）：切换图片后立即调用，确保新图片可以重新绘制ROI
        /// - MeasureType_SelectionChanged（切换测量类型）：清理不兼容的ROI状态
        /// - 未来可扩展：取消ROI绘制按钮、重置配置按钮等
        /// 
        /// 与其他模块关联：
        /// - refreshrectangle()：负责刷新已保存ROI的显示，本方法负责清理绘制状态，两者互补
        /// - RemoveCirclePreview()：清理WPF层的临时圆形预览控件
        /// </summary>
        private void ResetDrawingStates()
        {
            // 重置矩形ROI选择状态
            selectbroi = false;
            selectproi = false;
            selectdroi = false;
            
            // 重置测量对象ROI选择状态
            selectmeasureobject1roi = false;
            selectmeasureobject2roi = false;
            
            // 重置绘制进度标志
            _lineDrawingInProgress = false;
            _circleDrawingInProgress = false;
            _isFirstCircleClick = true;
            
            // 恢复按钮背景色
            SelectBarcodeROI.Background = System.Windows.Media.Brushes.Gray;
            SelectPositionROI.Background = System.Windows.Media.Brushes.Gray;
            SelectDimensionROI.Background = System.Windows.Media.Brushes.Gray;
            SelectMeasureObject1ROI.Background = System.Windows.Media.Brushes.Gray;
            SelectMeasureObject2ROI.Background = System.Windows.Media.Brushes.Gray;
            
            // 清理临时预览控件
            RemoveCirclePreview();
        }

        #endregion

        #region 圆形ROI绘制辅助方法

        /// <summary>
        /// 检查ROI是否有效（根据ROI类型检查对应参数）
        /// </summary>
        private bool IsROIValid(ROI roi)
        {
            if (roi == null) return false;
            
            switch (roi.Type)
            {
                case ROIType.Circle:
                    // 圆形ROI：检查半径
                    return roi.CircleRadius > 0;
                    
                case ROIType.Line:
                    // 线段ROI：检查是否有起点和终点
                    return !(roi.Row1 == 0 && roi.Row2 == 0 && roi.Col1 == 0 && roi.Col2 == 0);
                    
                case ROIType.Rectangle:
                default:
                    // 矩形ROI：检查是否有坐标
                    return !(roi.Row1 == 0 && roi.Row2 == 0 && roi.Col1 == 0 && roi.Col2 == 0);
            }
        }

        /// <summary>
        /// 初始化圆形预览控件
        /// </summary>
        private void InitializeCirclePreview()
        {
            if (_previewCircle == null)
            {
                _previewCircle = new System.Windows.Shapes.Ellipse
                {
                    Stroke = System.Windows.Media.Brushes.Cyan,
                    StrokeThickness = 2,
                    Fill = System.Windows.Media.Brushes.Transparent
                };
                show_image_canvas.Children.Add(_previewCircle);
            }
            _previewCircle.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 更新圆形预览
        /// </summary>
        /// <param name="center">圆心位置</param>
        /// <param name="radius">圆半径</param>
        private void UpdateCirclePreview(System.Windows.Point center, double radius)
        {
            if (_previewCircle != null)
            {
                _previewCircle.Width = radius * 2;
                _previewCircle.Height = radius * 2;
                _previewCircle.Margin = new Thickness(
                    center.X - radius, 
                    center.Y - radius, 
                    0, 0
                );
                _previewCircle.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 移除圆形预览
        /// </summary>
        private void RemoveCirclePreview()
        {
            if (_previewCircle != null)
            {
                _previewCircle.Visibility = Visibility.Collapsed;
            }
        }

        #endregion



    }
}
