using GalaSoft.MvvmLight;
using HalconDotNet;
using Panuon.WPF.UI;
using System.Windows.Media;
using System.Windows;
using System;
using Microsoft.Win32;
using System.IO;

namespace PositionDetect.ViewModel
{
    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// <para>
    /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
    /// </para>
    /// <para>
    /// You can also use Blend to data bind with the tool's support.
    /// </para>
    /// <para>
    /// See http://www.galasoft.ch/mvvm
    /// </para>
    /// </summary>
    public class PositionDetectViewModel : ViewModelBase
    {
        //HObject image = null;
        public DATA DATA { get; set; } = new DATA();
        public void showtest()
        {
            HTuple hv_width, hv_height;

            try
            {
                //HOperatorSet.ReadImage(out DATA.image, "E:\\0.测试图片\\模板匹配\\6999\\1.bmp");
                HOperatorSet.GetImageSize(DATA.image, out hv_width, out hv_height);
                DATA.HWindow.HalconWindow.SetPart(0, 0, (int)hv_height - 1, (int)hv_width - 1);
                DATA.HWindow.HalconWindow.DispObj(DATA.image);

            }
            catch
            {; }


            //DATA.image?.Dispose();

        }
        public void showregion()
        {
            try
            {
                HObject ROI = null;
                HOperatorSet.GenEmptyObj(out ROI);
                foreach (var r in DATA.Objects)
                {
                    HOperatorSet.Union2(ROI, r, out ROI);
                }
                DATA.HWindow.HalconWindow.SetRgba(0, 255, 0, 100);
                DATA.HWindow.HalconWindow.DispObj(ROI);

                if (DATA.Objects.Count > 0 && DATA.selectedindex >= 0 && DATA.selectedindex < DATA.Objects.Count)
                {
                    //HObject ROI1 = null;
                    DATA.HWindow.HalconWindow.SetRgba(255, 0, 0, 100);
                    DATA.HWindow.HalconWindow.DispObj(DATA.Objects[DATA.selectedindex]);
                }
            }catch { ; }

        }
        public void drawrectangle()
        {
            DATA.CircleMode = Brushes.White;
            DATA.RectangleMmode = Brushes.Orange;
            double row1, col1, row2, col2;
            HObject rect = null;
            HOperatorSet.GenEmptyObj(out rect);
            DATA.HWindow.HalconWindow.DrawRectangle1(out row1, out col1, out row2, out col2);
            HOperatorSet.GenRectangle1(out rect, row1, col1, row2, col2);
            DATA.Objects.Add(rect);
            DATA.RectangleMmode = Brushes.White;
        }
        public void drawcircle()
        {
            DATA.RectangleMmode = Brushes.White;
            DATA.CircleMode = Brushes.OrangeRed;
            double row, col, radius;
            HObject cir = null;
            HOperatorSet.GenEmptyObj(out cir);
            DATA.HWindow.HalconWindow.DrawCircle(out row, out col, out radius);
            HOperatorSet.GenCircle(out cir, row, col, radius);
            DATA.Objects.Add(cir);
            DATA.CircleMode = Brushes.White;
        }
        public void showreduce()
        {
            //HObject image = null;
            HTuple hv_width, hv_height;
            HObject ReduceImage = null;

            try
            {
                DATA.HWindow.HalconWindow.ClearWindow();
                //HOperatorSet.ReadImage(out DATA.image, "E:\\0.测试图片\\模板匹配\\6999\\1.bmp");
                //DATA.hSmartWindow.HalconWindow.DispObj(image);
                HOperatorSet.GetImageSize(DATA.image, out hv_width, out hv_height);


                HObject ROI = null;
                HOperatorSet.GenEmptyObj(out ROI);
                foreach (var r in DATA.Objects)
                {
                    HOperatorSet.Union2(ROI, r, out ROI);
                }
                //DATA.HWindow.HalconWindow.SetRgba(0, 255, 0, 100);
                //DATA.HWindow.HalconWindow.DispObj(ROI);

                HOperatorSet.GenEmptyObj(out ReduceImage);
                HOperatorSet.ReduceDomain(DATA.image, ROI, out ReduceImage);
                DATA.HWindow.HalconWindow.SetPart(0, 0, (int)hv_width - 1, (int)hv_height - 1);
                DATA.HWindow.HalconWindow.DispObj(ReduceImage);

            }
            catch
            {; }


            //DATA.image?.Dispose();
        }
        public bool showdetect()
        {
            if (!DATA.HasTemplateFeatures)
            {
                MessageBoxX.Show("请先画矩形或圆形特征，并在图像窗口点鼠标右键完成当前特征。", "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return false;
            }

            DATA.modelID = null;
            //HObject image = null;
            HTuple hv_width, hv_height;
            HObject ReduceImage = null;

            HObject ho_ModelContours, ho_ContoursAffinTrans;

            HTuple hv_Row = new HTuple(), hv_Column = new HTuple(), hv_Angle = new HTuple(), hv_Score = new HTuple();

            HOperatorSet.GenEmptyObj(out ho_ModelContours);
            HOperatorSet.GenEmptyObj(out ho_ContoursAffinTrans);
            try
            {
                DATA.HWindow.HalconWindow.ClearWindow();
                //HOperatorSet.ReadImage(out DATA.image, "E:\\0.测试图片\\模板匹配\\6999\\1.bmp");
                //DATA.hSmartWindow.HalconWindow.DispObj(image);
                HOperatorSet.GetImageSize(DATA.image, out hv_width, out hv_height);


                HObject ROI = null;
                HOperatorSet.GenEmptyObj(out ROI);
                foreach (var r in DATA.Objects)
                {
                    HOperatorSet.Union2(ROI, r, out ROI);
                }
                //DATA.HWindow.HalconWindow.SetRgba(0, 255, 0, 100);
                //DATA.HWindow.HalconWindow.DispObj(ROI);

                HOperatorSet.GenEmptyObj(out ReduceImage);
                HOperatorSet.ReduceDomain(DATA.image, ROI, out ReduceImage);
                DATA.HWindow.HalconWindow.SetPart(0, 0, (int)hv_height - 1, (int)hv_width - 1);
                DATA.HWindow.HalconWindow.DispObj(ReduceImage);
                HTuple modelID = null;
                HOperatorSet.CreateShapeModel(ReduceImage, "auto", (new HTuple(-181)).TupleRad()
                        , (new HTuple(181)).TupleRad(), "auto", "auto", "use_polarity", "auto", "auto", out modelID);
                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                    hv_Row.Dispose(); hv_Column.Dispose(); hv_Angle.Dispose(); hv_Score.Dispose();
                    HOperatorSet.FindShapeModel(DATA.image, modelID, (new HTuple(-181)).TupleRad()
                        , (new HTuple(181)).TupleRad(), 0.5, 1, 0.5, "least_squares", 0, 0.9, out hv_Row,
                        out hv_Column, out hv_Angle, out hv_Score);
                }

                ho_ModelContours.Dispose();
                HOperatorSet.GetShapeModelContours(out ho_ModelContours, modelID, 1);
                //if (HDevWindowStack.IsOpen())
                //{
                //    //HOperatorSet.SetLineWidth(HDevWindowStack.GetActive(), 2);
                //}

                DATA.HWindow.HalconWindow.SetLineWidth(2);
                DATA.HWindow.HalconWindow.DispObj(DATA.image);
                HTuple hv_HomMat2D = new HTuple();
                //将模板映射到目标上
                hv_HomMat2D.Dispose();
                HOperatorSet.VectorAngleToRigid(0, 0, 0, hv_Row, hv_Column, hv_Angle, out hv_HomMat2D);
                ho_ContoursAffinTrans.Dispose();
                HOperatorSet.AffineTransContourXld(ho_ModelContours, out ho_ContoursAffinTrans,
                    hv_HomMat2D);
                DATA.HWindow.HalconWindow.SetColor("red");
                DATA.HWindow.HalconWindow.DispObj(ho_ContoursAffinTrans);
                DATA.HWindow.HalconWindow.SetDraw("margin");
                DATA.HWindow.HalconWindow.SetColor("yellow");
                DATA.HWindow.HalconWindow.DispObj(ROI);

                DATA.modelID = modelID;
                return true;
            }
            catch (Exception ex)
            {
                DATA.modelID = null;
                return false;
            }


            //DATA.image?.Dispose();
        }
        public void deletelistitem()
        {
            if (DATA.selectedindex < 0 || DATA.selectedindex >= DATA.Objects.Count)
            {
                MessageBoxX.Show("请先选择需要删除的模板特征", "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBoxX.Show("是否确定删除选择ROI区域?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == MessageBoxResult.Yes)
            {
                try
                {
                    DATA.Objects.RemoveAt(DATA.selectedindex);

                }
                catch {; }
            }
        }

        public bool OutputModel()
        {
            if (!DATA.HasTemplateFeatures)
            {
                MessageBoxX.Show("请先完成模板特征圈选，再预览生成模型。", "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (DATA.modelID == null)
            {
                MessageBoxX.Show("请先预览再保存模型");
                return false;
            }

            if (string.IsNullOrEmpty(DATA.modelfilename))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "*.shm|*.shm";
                if (saveFileDialog.ShowDialog() == true)
                {
                    string filename = saveFileDialog.FileName;
                    HOperatorSet.WriteShapeModel(DATA.modelID, filename);
                    DATA.modelfilename = filename;
                    NoticeBox.Show($"{filename}", $"模型导出成功",  MessageBoxIcon.Success, true, 3000);
                    return true;
                }
                return false;
            }
            else
            {
                string dir = Path.GetDirectoryName(DATA.modelfilename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                HOperatorSet.WriteShapeModel(DATA.modelID, DATA.modelfilename);
                NoticeBox.Show($"{DATA.modelfilename}", $"模型导出成功",  MessageBoxIcon.Success, true, 3000);
                return true;
            }
        }


    }
}
