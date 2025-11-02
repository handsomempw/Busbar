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

        public SettingForm()
        {
            InitializeComponent();
            vml = this.FindResource("Locator") as ViewModelLocator;
            t = (ToolModel)DataContext;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "*.jpg|*.jpg";
                if (ofd.ShowDialog() == true)
                {
                    string filename = ofd.FileName;

                    #region 加载模型图片
                    t.Image?.Dispose();
                    HOperatorSet.GenEmptyObj(out t.Image);
                    HOperatorSet.ReadImage(out t.Image, filename);
                    #endregion



                    Bitmap bmp;
                    Bitmap src;
                    HObject Image;
                    HOperatorSet.GenEmptyObj(out Image);

                    try
                    {
                        HOperatorSet.ReadImage(out Image, filename);
                        var dst = vml.Main.GetReducedImage(vml.Main.DataModel.FaraVisionDataModel.Settingmodel.ImageSize, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.ImageSize, Image);
                        Hobject2Bitmap.HobjectToBitmap24(Image, out src);
                        Hobject2Bitmap.HobjectToBitmap24(dst, out bmp);

                        // 修复GDI句柄泄漏：第一个BitmapSource
                        IntPtr hBitmap1 = bmp.GetHbitmap();
                        try
                        {
                            vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.CurrentBitmapSource = null;
                            vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap1, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                        finally
                        {
                            DeleteObject(hBitmap1); // 释放GDI句柄
                        }

                        // 修复GDI句柄泄漏：第二个BitmapSource
                        IntPtr hBitmap2 = src.GetHbitmap();
                        try
                        {
                            vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                            vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap2, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                        finally
                        {
                            DeleteObject(hBitmap2); // 释放GDI句柄
                        }

                        bmp?.Dispose();
                        dst?.Dispose();
                        src?.Dispose();

                    }
                    catch (Exception ex)
                    {; }



                    //using (Bitmap bmp = (Bitmap)Bitmap.FromFile(filename))
                    //{
                    //    using (Bitmap bmp1 = vml.Main.GetReducedImage(vml.Main.DataModel.FaraVisionDataModel.Settingmodel.ImageSize, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.ImageSize, bmp))
                    //    {
                    //        //t.BitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bmp1.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    //        vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.BitmapSource = null;
                    //        vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.BitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bmp1.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                    //    }
                    //    vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                    //    vml.Main.DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bmp.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    //}

                    string newfilename = $"{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{t.Index}.jpg";
                    string dir = System.IO.Path.GetDirectoryName(newfilename);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.Copy(filename, newfilename, true);


                    zoom_all();
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
            if (selectproi || selectbroi || selectdroi)
            {
                _downPoint = e.GetPosition(show_image_canvas);
                _started = true;
            }
        }

        private void rectange_MouseUp(object sender, MouseButtonEventArgs e)
        {

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

        private void DimensionApply_Pic_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "*.jpg|*.jpg";
            if (ofd.ShowDialog() == true)
            {
                string filename = ofd.FileName;

                HObject image;
                HOperatorSet.GenEmptyObj(out image);
                #region 加载模型图片
                HOperatorSet.GenEmptyObj(out image);
                HOperatorSet.ReadImage(out image, filename);
                #endregion
                int area = vml.Main.CoculateDimension(image, vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool, vml.Main.DataModel.FaraVisionDataModel.Settingmodel.HWindow, true);
                NoticeBox.Show($"识别面积大小:{area}", "提示", MessageBoxIcon.Info, true, 10000);
            }
        }

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



    }
}
