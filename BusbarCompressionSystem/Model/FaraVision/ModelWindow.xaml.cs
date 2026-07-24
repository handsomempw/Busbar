using BusbarCompressionSystem.Model.FaraVision;
using BusbarCompressionSystem.ViewModel;
using HalconDotNet;
using Microsoft.Win32;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BusbarCompressionSystem.Model.FaraVision
{
    /// <summary>
    /// 模板匹配模型设置窗口，负责模板特征圈选、模型生成、shm 保存、模型加载与基准点写入。
    /// </summary>
    public partial class ModelWindow : WindowX
    {

        ViewModelLocator vml = null;
        private string _modelStatus = "未导出";

        public ModelWindow(HObject image, string Modelfilename)
        {
            InitializeComponent();
            vml = (ViewModelLocator)this.FindResource("Locator");
            vml.PositionDetectViewModel.DATA.image?.Dispose();
            HOperatorSet.GenEmptyObj(out vml.PositionDetectViewModel.DATA.image);
            HOperatorSet.CopyImage(image, out vml.PositionDetectViewModel.DATA.image);
            vml.PositionDetectViewModel.DATA.modelfilename = Modelfilename;
            UpdateModelStatus(File.Exists(Modelfilename) ? $"已导出 {System.IO.Path.GetFileName(Modelfilename)}" : "未导出");
            ConfigureBasePointButtons();
        }


        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (vml?.PositionDetectViewModel?.DATA != null)
            {
                vml.PositionDetectViewModel.DATA.ROImode = false;
            }
        }

        private void UpdateModelStatus(string status)
        {
            _modelStatus = status;
            ModelStatusText.Text = $"模型状态：{_modelStatus}";
        }

        /// <summary>
        /// 按工具模式配置基准入口。模板定位只从模板图保存参考位姿，避免现场图基准入口写入模板匹配用的 InitX/InitY 后误导在线 ROI 跟随矫正。
        /// </summary>
        private void ConfigureBasePointButtons()
        {
            var tool = vml?.Main?.DataModel?.FaraVisionDataModel?.Processmodel?.tool;
            if (tool?.TestMode == TestModes.模板定位)
            {
                getinitpositionbymodelimage.Content = "5 保存定位参考位姿";
                getinitpositionbymodelimage.ToolTip = "用当前模板图匹配一次，并保存定位参考位姿；在线 ROI 跟随矫正读取这组 Row/Col/Angle。";
                getinitpositonfromimagefile.Visibility = Visibility.Collapsed;
                return;
            }

            getinitpositionbymodelimage.Content = "5 用模板图设基准点";
            getinitpositionbymodelimage.ToolTip = "用当前模板图片匹配一次，并把找到的位置写入基准X/Y。";
            getinitpositonfromimagefile.Visibility = Visibility.Visible;
            getinitpositonfromimagefile.ToolTip = "选择一张现场图片匹配，并把该图片中的位置写入基准X/Y。";
        }

        private void selectFeature_Click(object sender, RoutedEventArgs e)
        {
            if (!vml.PositionDetectViewModel.DATA.ROImode)
            {
                vml.PositionDetectViewModel.DATA.Objects.Clear();
            }
            vml.PositionDetectViewModel.DATA.ROImode = true;
            UpdateModelStatus("正在圈选模板特征");
        }

        private void previewGenerateModel_Click(object sender, RoutedEventArgs e)
        {
            bool success = vml.PositionDetectViewModel.showdetect();
            UpdateModelStatus(success ? "已预览生成，待保存模型文件" : "预览生成失败");
        }

        /// <summary>
        /// 发布当前预览模型，并让当前工程工具立即切换到已校验的 .shm。
        /// 工程快照在模型发布成功后同步，工程切换、工具重排或现场恢复时可按同一 Tool 序号取得 XML、示教图和模型文件。
        /// </summary>
        /// <param name="sender">模型保存按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void exportModel_Click(object sender, RoutedEventArgs e)
        {
            bool success = vml.PositionDetectViewModel.OutputModel();
            if (success)
            {
                var tool = vml?.Main?.DataModel?.FaraVisionDataModel?.Processmodel?.tool;
                bool loaded = tool != null && vml.Main.EnsureShapeModelLoaded(tool, "模型保存后加载", true);
                bool snapshotSaved = vml.Main.BackupCurrentAoiProjectFilesAfterSave();

                if (loaded && snapshotSaved)
                {
                    UpdateModelStatus($"已保存并加载 {System.IO.Path.GetFileName(vml.PositionDetectViewModel.DATA.modelfilename)}");
                }
                else if (loaded)
                {
                    UpdateModelStatus("模型文件已保存并加载，工程快照未确认，请检查运行日志");
                }
                else
                {
                    UpdateModelStatus("模型文件已保存，内存重载失败，请检查运行日志");
                }
            }
            else
            {
                UpdateModelStatus("保存模型文件失败");
            }
        }

        private bool EnsurePositionRoiReady()
        {
            var roi = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI;
            if (roi == null || !roi.IsValidRectangle())
            {
                string message = "模板匹配ROI未设置，请先在工具设置界面点击“模板匹配ROI-选择”，框选搜索范围后再设基准点。";
                UpdateModelStatus("请先设置模板匹配ROI");
                NoticeBox.Show(message, "提示", MessageBoxIcon.Warning, true, 6000);
                return false;
            }

            return true;
        }

        private void ShowBasePointResult(string message, MessageBoxIcon icon = MessageBoxIcon.Warning)
        {
            UpdateModelStatus(message);
            NoticeBox.Show(message, "提示", icon, true, 6000);
        }

        private void getinitpositionbymodelimage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsurePositionRoiReady())
                {
                    return;
                }

                //int w = (int)vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.BitmapSource.Width;
                //int h = (int)vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.BitmapSource.Height;
                //var r = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.ShapeMatch.Match(vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.Image, 1, 0, h - 1, 0, w - 1);
                //vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.InitX = r.points[0].column;
                //vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.InitY = r.points[1].row;

                var shapmatchresult = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.ShapeMatch.Match(
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.Image, 1,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Row1,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Col1,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Row2,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Col2);

                var Result_Data = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.ShapeMatch.Analysis_Result(shapmatchresult);
                if (!shapmatchresult.IsSuccess)
                {
                    ShowBasePointResult(string.IsNullOrWhiteSpace(shapmatchresult.ErrorInfo)
                        ? "模板图设基准点失败：匹配结果为空"
                        : $"模板图设基准点失败：{shapmatchresult.ErrorInfo}");
                    return;
                }

                var tool = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool;
                if (tool.TestMode == TestModes.模板定位
                    && shapmatchresult.points != null
                    && shapmatchresult.points.Count > 0)
                {
                    var rp = shapmatchresult.points[0];
                    tool.ReferenceMatchRow = rp.row;
                    tool.ReferenceMatchCol = rp.column;
                    tool.ReferenceMatchAngleDeg = rp.angle * 180.0 / Math.PI;
                    tool.ReferencePoseConfigured = true;
                    ShowBasePointResult(
                        $"参考位姿已保存：Row={rp.row:F1}, Col={rp.column:F1}, Angle={tool.ReferenceMatchAngleDeg:F2}deg",
                        MessageBoxIcon.Success);
                    return;
                }

                if (Result_Data != null)
                {
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.InitX = Result_Data.X_actual;
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.InitY = Result_Data.Y_actual;
                    ShowBasePointResult($"模板图基准点已更新：X={Result_Data.X_actual:F2}, Y={Result_Data.Y_actual:F2}", MessageBoxIcon.Success);
                }
                else
                {
                    ShowBasePointResult("模板图设基准点失败：匹配结果为空");
                }
            }
            catch (Exception ex)
            {
                ShowBasePointResult($"模板图设基准点异常：{ex.Message}");
            }
        }

        private void getinitpositonfromimagefile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tool = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool;
                if (tool.TestMode == TestModes.模板定位)
                {
                    ShowBasePointResult("模板定位参考位姿请使用模板图保存，现场图入口仅用于模板匹配基准X/Y。");
                    return;
                }

                if (!EnsurePositionRoiReady())
                {
                    return;
                }

                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "图片文件|*.jpg;*.jpeg;*.bmp;*.png|JPG文件|*.jpg;*.jpeg|BMP文件|*.bmp|PNG文件|*.png";
                if (ofd.ShowDialog() == true)
                {

                    HObject image = null;
                    HOperatorSet.GenEmptyObj(out image);
                    HOperatorSet.ReadImage(out image, ofd.FileName);

                    var shapmatchresult = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.ShapeMatch.Match(
                   image, 1,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Row1,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Col1,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Row2,
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.PositionROI.Col2);

                    var Result_Data = vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.ShapeMatch.Analysis_Result(shapmatchresult);
                    if (!shapmatchresult.IsSuccess)
                    {
                        ShowBasePointResult(string.IsNullOrWhiteSpace(shapmatchresult.ErrorInfo)
                            ? "现场图设基准点失败：匹配结果为空"
                            : $"现场图设基准点失败：{shapmatchresult.ErrorInfo}");
                        return;
                    }

                    if (Result_Data != null)
                    {
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.InitX = Result_Data.X_actual;
                        vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool.InitY = Result_Data.Y_actual;
                        ShowBasePointResult($"现场图基准点已更新：X={Result_Data.X_actual:F2}, Y={Result_Data.Y_actual:F2}", MessageBoxIcon.Success);
                    }
                    else
                    {
                        ShowBasePointResult("现场图设基准点失败：匹配结果为空");
                    }
                }

            }
            catch (Exception ex)
            {
                ShowBasePointResult($"现场图设基准点异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 由操作员手动重载当前工具的工程模型。
        /// 手动入口跳过文件时间判断；文件缺失或损坏时保留当前可用内存模型，并把现场可执行状态明确反馈给操作员。
        /// </summary>
        /// <param name="sender">加载当前模型按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void loadshmfromfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tool = vml?.Main?.DataModel?.FaraVisionDataModel?.Processmodel?.tool;
                if (tool == null)
                {
                    UpdateModelStatus("当前工具未就绪");
                    return;
                }

                string shmfilename = vml.Main.GetShapeModelPath(tool);
                bool fileExists = File.Exists(shmfilename);
                bool loaded = vml.Main.EnsureShapeModelLoaded(tool, "手动重载", true);
                if (fileExists && loaded)
                {
                    UpdateModelStatus($"已重载 {System.IO.Path.GetFileName(shmfilename)}");
                }
                else if (!fileExists && tool.ShapeMatch.ModelLoaded)
                {
                    UpdateModelStatus("模型文件不存在，当前继续使用已加载模型");
                }
                else
                {
                    UpdateModelStatus("重载失败，请检查模型文件和运行日志");
                }
            }
            catch (Exception ex)
            {
                UpdateModelStatus($"重载异常：{ex.Message}");
                vml?.Main?.writeLog($"[模板模型重载] 异常：{ex.Message}", true);
            }

        }



    }
}
