using BusbarCompressionSystem.Model.FaraVision;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.ViewModel;
using HalconDotNet;
using Microsoft.Win32;
using Panuon.WPF.UI;
using PositionDetect;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace BusbarCompressionSystem.Model.FaraVision
{
    /// <summary>
    /// 模板匹配模型设置窗口，负责模板特征圈选、模型生成、shm 保存、模型加载与基准点写入。
    /// </summary>
    public partial class ModelWindow : WindowX
    {

        ViewModelLocator vml = null;
        private string _modelStatus = "未导出";
        private readonly ToolModel _tool;
        private List<TemplateFeatureDefinition> _publishedTemplateFeatures = new List<TemplateFeatureDefinition>();
        private int _publishedTemplateImageWidth;
        private int _publishedTemplateImageHeight;
        private int _restoredFeatureCount;
        private string _sessionRestoreMessage = string.Empty;

        /// <summary>
        /// 打开当前工具的模板示教会话，复制示教图、恢复 Tool XML 中的可编辑区域并同步生产搜索参数。
        /// 窗口内维护 HALCON Region 和临时模型句柄；几何配方、正式 .shm 与在线模型在“保存并发布”成功后按同一 Tool 序号更新。
        /// </summary>
        /// <param name="image">当前工具的示教图像；窗口复制后独立管理，调用方仍保留原图所有权。</param>
        /// <param name="modelFilename">当前工具正式 .shm 的完整路径。</param>
        /// <param name="tool">当前工具配置，提供持久化特征配方、PositionROI、角域、分值和基准数据。</param>
        public ModelWindow(HObject image, string modelFilename, ToolModel tool)
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            InitializeComponent();
            vml = (ViewModelLocator)this.FindResource("Locator");
            vml.PositionDetectViewModel.DATA.image?.Dispose();
            HOperatorSet.CopyImage(image, out vml.PositionDetectViewModel.DATA.image);
            vml.PositionDetectViewModel.DATA.modelfilename = modelFilename;
            CapturePublishedRecipeBaseline();
            RestoreTemplateFeatureSession();
            bool settingsReady = TrySyncPreviewMatchSettings(out string settingsError);
            UpdateModelStatus(settingsReady
                ? (File.Exists(modelFilename) ? $"已发布 {System.IO.Path.GetFileName(modelFilename)}" : "当前工具尚未发布模型")
                : settingsError);
            ConfigureBasePointButtons();
        }

        /// <summary>
        /// 记录当前 Tool 已发布的模板特征基线，供本次会话放弃修改或模型发布失败时恢复。
        /// 基线只复制可序列化几何参数，HALCON Region 始终由模型窗口独立创建和释放。
        /// </summary>
        private void CapturePublishedRecipeBaseline()
        {
            _publishedTemplateFeatures = CloneTemplateFeatures(_tool.TemplateFeatures);
            _publishedTemplateImageWidth = _tool.TemplateFeatureImageWidth;
            _publishedTemplateImageHeight = _tool.TemplateFeatureImageHeight;
        }

        /// <summary>
        /// 深拷贝模板特征定义集合，隔离 ToolModel 已发布配方与窗口编辑会话。
        /// </summary>
        /// <param name="features">待复制的 Tool XML 特征集合；旧工程允许为空。</param>
        /// <returns>按原顺序排列的独立几何定义集合。</returns>
        private static List<TemplateFeatureDefinition> CloneTemplateFeatures(IEnumerable<TemplateFeatureDefinition> features)
        {
            return (features ?? Enumerable.Empty<TemplateFeatureDefinition>())
                .Where(feature => feature != null)
                .Select(feature => feature.Clone())
                .ToList();
        }

        /// <summary>
        /// 根据当前示教图尺寸恢复可编辑模板区域。
        /// 配方图像尺寸与当前图片不一致时保留 ToolModel 原配方并进入重新示教视图，避免旧坐标覆盖不同尺寸图片。
        /// </summary>
        private void RestoreTemplateFeatureSession()
        {
            if (!TryGetSessionImageSize(out int imageWidth, out int imageHeight, out string imageError))
            {
                _restoredFeatureCount = vml.PositionDetectViewModel.RestoreTemplateFeatures(
                    Enumerable.Empty<TemplateFeatureDefinition>(),
                    out _);
                _sessionRestoreMessage = $"示教图尺寸读取失败：{imageError}";
                return;
            }

            bool hasSavedFeatures = _publishedTemplateFeatures.Count > 0;
            bool hasSavedImageSize = _publishedTemplateImageWidth > 0 && _publishedTemplateImageHeight > 0;
            if (hasSavedFeatures
                && hasSavedImageSize
                && (_publishedTemplateImageWidth != imageWidth || _publishedTemplateImageHeight != imageHeight))
            {
                _restoredFeatureCount = vml.PositionDetectViewModel.RestoreTemplateFeatures(
                    Enumerable.Empty<TemplateFeatureDefinition>(),
                    out _);
                _sessionRestoreMessage =
                    $"示教图尺寸已变化（配方 {_publishedTemplateImageWidth}x{_publishedTemplateImageHeight}px，当前 {imageWidth}x{imageHeight}px），请重新示教";
                return;
            }

            _restoredFeatureCount = vml.PositionDetectViewModel.RestoreTemplateFeatures(
                _publishedTemplateFeatures,
                out _sessionRestoreMessage);
        }

        /// <summary>
        /// 读取模型设置会话复制图的原始尺寸，供配方兼容校验和发布时写入 Tool XML。
        /// </summary>
        /// <param name="width">图像宽度，单位 px。</param>
        /// <param name="height">图像高度，单位 px。</param>
        /// <param name="errorMessage">图像为空或 HALCON 读取失败时的原因。</param>
        /// <returns>尺寸可用于配方校验时返回 true。</returns>
        private bool TryGetSessionImageSize(out int width, out int height, out string errorMessage)
        {
            width = 0;
            height = 0;
            errorMessage = string.Empty;
            HTuple imageWidth = null;
            HTuple imageHeight = null;
            try
            {
                HObject image = vml?.PositionDetectViewModel?.DATA?.image;
                if (image == null)
                {
                    errorMessage = "示教图为空";
                    return false;
                }

                HOperatorSet.GetImageSize(image, out imageWidth, out imageHeight);
                width = imageWidth.I;
                height = imageHeight.I;
                return width > 0 && height > 0;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                imageWidth?.Dispose();
                imageHeight?.Dispose();
            }
        }

        /// <summary>
        /// 窗口完成加载后显示示教图、已恢复区域和已发布模型轮廓。
        /// 轮廓显示只提供操作员复核，生产模型句柄和在线判定保持当前状态。
        /// </summary>
        /// <param name="sender">模板模型设置窗口。</param>
        /// <param name="e">WPF 加载事件参数。</param>
        private void WindowX_Loaded(object sender, RoutedEventArgs e)
        {
            string modelFilename = vml.PositionDetectViewModel.DATA.modelfilename;
            vml.PositionDetectViewModel.DisplayPublishedModelContours(modelFilename, out string contourSummary);

            var statusParts = new List<string>();
            if (_restoredFeatureCount > 0)
            {
                statusParts.Add($"已恢复 {_restoredFeatureCount} 个可编辑特征");
            }
            else if (_publishedTemplateFeatures.Count > 0)
            {
                statusParts.Add(File.Exists(modelFilename)
                    ? "已发布模型继续运行，原配方保持在 Tool XML"
                    : "原配方保持在 Tool XML，当前工具等待重新示教并发布");
            }
            else if (File.Exists(modelFilename))
            {
                statusParts.Add("已发布模型可继续运行，当前工程缺少可编辑特征配方");
            }
            else
            {
                statusParts.Add("请开始圈选模板特征");
            }

            if (!string.IsNullOrWhiteSpace(_sessionRestoreMessage))
            {
                statusParts.Add(_sessionRestoreMessage);
            }
            if (!string.IsNullOrWhiteSpace(contourSummary))
            {
                statusParts.Add(contourSummary);
            }

            UpdateModelStatus(string.Join("；", statusParts));
        }

        /// <summary>
        /// 结束当前模板示教会话并释放临时图像、区域和模型句柄。
        /// 存在未发布特征修改时先由操作员确认放弃；已保存的 .shm、工具 XML、在线模型句柄和基准数据保持当前状态。
        /// </summary>
        /// <param name="sender">模型设置窗口。</param>
        /// <param name="e">窗口关闭事件参数。</param>
        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var data = vml?.PositionDetectViewModel?.DATA;
            if (data?.IsTemplateRecipeDirty == true)
            {
                MessageBoxResult result = MessageBoxX.Show(
                    "当前特征修改尚未保存并发布。关闭窗口会放弃本次编辑，已发布模型继续生效，确认关闭？",
                    "未发布修改",
                    MessageBoxButton.YesNo,
                    MessageBoxIcon.Question,
                    DefaultButton.NoCancel);
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            vml?.PositionDetectViewModel?.DisposeTemplateFeatureSession();
        }

        /// <summary>
        /// 把当前工具的 PositionROI、角域与分值参数同步到模型设置预览会话。
        /// 同步前只做显式校验，不修改 ToolModel；预览、设基准与在线检测由此共用同一搜索区域和判定口径。
        /// </summary>
        /// <param name="errorMessage">工具、ROI 或参数未就绪时返回操作员可直接处理的原因。</param>
        /// <returns>true 表示预览参数已同步；false 表示应保持预览与保存入口关闭。</returns>
        private bool TrySyncPreviewMatchSettings(out string errorMessage)
        {
            if (_tool == null || vml?.PositionDetectViewModel?.DATA == null)
            {
                errorMessage = "当前工具未就绪";
                return false;
            }

            if (!_tool.TryValidateShapeMatchParameters(out errorMessage))
            {
                errorMessage = $"模板匹配参数无效：{errorMessage}";
                return false;
            }

            if (_tool.PositionROI == null || !_tool.PositionROI.IsValidRectangle())
            {
                errorMessage = "模板匹配ROI未设置，请返回工具设置重新选择";
                return false;
            }

            var data = vml.PositionDetectViewModel.DATA;
            data.PreviewAllowAngleDelta = _tool.AllowAngleDelta;
            data.PreviewCandidateMinScore = _tool.CandidateMinScore;
            data.PreviewMinScore = _tool.MinScore;
            data.PreviewRoiRow1 = _tool.PositionROI.Row1;
            data.PreviewRoiCol1 = _tool.PositionROI.Col1;
            data.PreviewRoiRow2 = _tool.PositionROI.Row2;
            data.PreviewRoiCol2 = _tool.PositionROI.Col2;
            errorMessage = null;
            return true;
        }

        /// <summary>
        /// 更新当前示教会话的模型状态栏，向操作员反馈圈选、预览、保存和重载结果。
        /// 状态文字只用于本窗口诊断，不参与在线检测判定或工程持久化。
        /// </summary>
        /// <param name="status">当前操作结果或可执行的修正提示。</param>
        private void UpdateModelStatus(string status)
        {
            _modelStatus = status;
            ModelStatusText.Text = $"模型状态：{_modelStatus}";
        }

        /// <summary>
        /// 同步模型发布结果到状态栏和提示框，使操作员在事务结束后直接看到生效范围与下一步处理。
        /// 该提示只反馈 Tool XML、.shm、在线句柄和工程快照状态，不改变发布结果。
        /// </summary>
        /// <param name="message">发布结果、回退状态和现场处理建议。</param>
        /// <param name="icon">成功、警告或错误级别，决定提示框的视觉优先级。</param>
        private void ShowModelPublishResult(string message, MessageBoxIcon icon)
        {
            UpdateModelStatus(message);
            NoticeBox.Show(message, "模型发布", icon, true, 8000);
        }

        /// <summary>
        /// 按工具模式配置基准入口。模板定位只从模板图保存参考位姿，避免现场图基准入口写入模板匹配用的 InitX/InitY 后误导在线 ROI 跟随矫正。
        /// </summary>
        private void ConfigureBasePointButtons()
        {
            if (_tool.TestMode == TestModes.模板定位)
            {
                getinitpositionbymodelimage.Content = "保存定位参考位姿";
                getinitpositionbymodelimage.ToolTip = "用当前模板图匹配一次；分值须达到合格下限后才保存参考位姿，供在线 ROI 跟随矫正。";
                getinitpositonfromimagefile.Visibility = Visibility.Collapsed;
                return;
            }

            getinitpositionbymodelimage.Content = "用模板图设基准点";
            getinitpositionbymodelimage.ToolTip = "用当前模板图片匹配一次；分值须达到合格下限后才写入基准X/Y。";
            getinitpositonfromimagefile.Visibility = Visibility.Visible;
            getinitpositonfromimagefile.ToolTip = "选择一张现场图片匹配；分值须达到合格下限后才写入基准X/Y。";
        }

        /// <summary>
        /// 按当前工具的生产搜索参数预览模板模型。
        /// 参数或 ROI 无效时保留原有正式模型并给出修正提示；预览达到合格分后才开放保存入口。
        /// </summary>
        /// <param name="sender">预览生成模型按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void previewGenerateModel_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySyncPreviewMatchSettings(out string settingsError))
            {
                UpdateModelStatus(settingsError);
                NoticeBox.Show(settingsError, "提示", MessageBoxIcon.Warning, true, 6000);
                return;
            }

            bool success = vml.PositionDetectViewModel.showdetect();
            string summary = vml.PositionDetectViewModel.DATA.LastPreviewSummary;
            if (success)
            {
                UpdateModelStatus(string.IsNullOrWhiteSpace(summary) ? "已预览生成，待保存模型文件" : summary);
            }
            else
            {
                UpdateModelStatus(string.IsNullOrWhiteSpace(summary) ? "预览生成失败" : summary);
            }
        }

        /// <summary>
        /// 发布当前预览模型，并让当前工程工具立即切换到已校验的 .shm。
        /// 工程快照在模型发布成功后同步，工程切换、工具重排或现场恢复时可按同一 Tool 序号取得 XML、示教图和模型文件。
        /// </summary>
        /// <param name="sender">模型保存按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void exportModel_Click(object sender, RoutedEventArgs e)
        {
            var data = vml.PositionDetectViewModel.DATA;
            if (!data.CanSaveModel)
            {
                ShowModelPublishResult("请先完成特征圈选并预览生成模型", MessageBoxIcon.Warning);
                return;
            }

            if (!TryStageSessionRecipe(out string recipeError))
            {
                ShowModelPublishResult(
                    $"模板配方准备失败：{recipeError}；已发布模型继续生效",
                    MessageBoxIcon.Warning);
                return;
            }

            if (!vml.Main.TrySaveCurrentToolRecipeForModelPublish(_tool, out string xmlError))
            {
                RestorePublishedRecipeToTool();
                ShowModelPublishResult(
                    $"配方 XML 保存失败：{xmlError}；已发布模型继续生效",
                    MessageBoxIcon.Error);
                return;
            }

            if (!vml.PositionDetectViewModel.OutputModel(out string modelError))
            {
                bool rollbackSaved = TryRestorePublishedRecipeXml(out string rollbackError);
                ShowModelPublishResult(
                    rollbackSaved
                        ? $"模型文件发布失败：{modelError}；Tool XML 已恢复，上一次发布模型继续生效"
                        : $"模型文件发布失败：{modelError}；Tool XML 回退失败：{rollbackError}，当前发布版本需要人工核对",
                    rollbackSaved ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
                return;
            }

            CapturePublishedRecipeBaseline();
            data.IsTemplateRecipeDirty = false;
            data.ROImode = false;

            bool loaded = vml.Main.EnsureShapeModelLoaded(_tool, "模型保存后加载", true);
            bool snapshotSaved = vml.Main.BackupCurrentAoiProjectFilesAfterSave();
            string filename = System.IO.Path.GetFileName(data.modelfilename);
            if (loaded && snapshotSaved)
            {
                ShowModelPublishResult($"配方与 {filename} 已发布并加载，可继续编辑", MessageBoxIcon.Success);
            }
            else if (loaded)
            {
                ShowModelPublishResult(
                    $"配方与 {filename} 已发布并加载；工程快照同步失败，请检查运行日志",
                    MessageBoxIcon.Warning);
            }
            else
            {
                ShowModelPublishResult(
                    $"配方与 {filename} 已发布；在线模型重载失败，请检查运行日志并点击“加载已发布模型”重试",
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 将当前会话区域转换为 ToolModel 配方，作为 XML 与 .shm 发布事务的候选版本。
        /// 此时只更新内存工具；XML 原子保存和模型发布由调用方依次确认。
        /// </summary>
        /// <param name="errorMessage">示教图尺寸或特征集合无效时的可读原因。</param>
        /// <returns>候选配方已写入当前 ToolModel 时返回 true。</returns>
        private bool TryStageSessionRecipe(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!TryGetSessionImageSize(out int imageWidth, out int imageHeight, out errorMessage))
            {
                return false;
            }

            List<TemplateFeatureDefinition> features = vml.PositionDetectViewModel.ExportTemplateFeatureDefinitions();
            if (features.Count == 0)
            {
                errorMessage = "当前会话没有可发布的模板特征";
                return false;
            }

            _tool.TemplateFeatures = CloneTemplateFeatures(features);
            _tool.TemplateFeatureImageWidth = imageWidth;
            _tool.TemplateFeatureImageHeight = imageHeight;
            return true;
        }

        /// <summary>
        /// 将当前 ToolModel 恢复为本窗口最近一次发布成功的模板配方。
        /// 用于候选 XML 保存失败或 .shm 发布失败后的内存回退，不改变在线模型句柄。
        /// </summary>
        private void RestorePublishedRecipeToTool()
        {
            _tool.TemplateFeatures = CloneTemplateFeatures(_publishedTemplateFeatures);
            _tool.TemplateFeatureImageWidth = _publishedTemplateImageWidth;
            _tool.TemplateFeatureImageHeight = _publishedTemplateImageHeight;
        }

        /// <summary>
        /// 在 .shm 发布失败后把 Tool XML 恢复到最近一次发布配方，使工程 XML 与继续生效的模型文件保持同一版本。
        /// </summary>
        /// <param name="errorMessage">恢复保存失败时的工程保护或文件系统原因。</param>
        /// <returns>ToolModel 内存与对应 Tool XML 均恢复完成时返回 true。</returns>
        private bool TryRestorePublishedRecipeXml(out string errorMessage)
        {
            RestorePublishedRecipeToTool();
            return vml.Main.TrySaveCurrentToolRecipeForModelPublish(_tool, out errorMessage);
        }

        /// <summary>
        /// 检查当前工具是否已有可用的 PositionROI，作为模板图和现场图设基准的共同搜索范围。
        /// </summary>
        /// <returns>ROI 可用于匹配返回 true；未设置时显示操作提示并返回 false。</returns>
        private bool EnsurePositionRoiReady()
        {
            var roi = _tool.PositionROI;
            if (roi == null || !roi.IsValidRectangle())
            {
                string message = "模板匹配ROI未设置，请先在工具设置界面点击“模板匹配ROI-选择”，框选搜索范围后再设基准点。";
                UpdateModelStatus("请先设置模板匹配ROI");
                NoticeBox.Show(message, "提示", MessageBoxIcon.Warning, true, 6000);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 同步显示设基准结果到状态栏和提示框，便于操作员在当前窗口完成确认与修正。
        /// </summary>
        /// <param name="message">设基准结果或失败原因。</param>
        /// <param name="icon">提示框图标；成功流程传入 Success。</param>
        private void ShowBasePointResult(string message, MessageBoxIcon icon = MessageBoxIcon.Warning)
        {
            UpdateModelStatus(message);
            NoticeBox.Show(message, "提示", icon, true, 6000);
        }

        /// <summary>
        /// 使用当前工具的角域与候选搜索下限执行一次匹配，供设基准/参考位姿写入。
        /// 搜索范围与在线检测共用 PositionROI 和 ShapeMatch 固定参数；合格线门禁由调用方在写入前单独检查。
        /// </summary>
        /// <param name="image">待匹配图像，坐标单位为 px；该方法不接管图像生命周期。</param>
        /// <param name="tool">当前 AOI 工具配置，提供 PositionROI、角域和分值参数。</param>
        /// <returns>HALCON 匹配结果，含候选点、耗时与失败原因。</returns>
        /// <exception cref="InvalidOperationException">当前工具的角域或分值参数无效时抛出，由设基准入口显示操作员提示。</exception>
        private ShapeMatch.Result MatchWithToolSettings(HObject image, ToolModel tool)
        {
            if (!tool.TryValidateShapeMatchParameters(out string parameterError))
            {
                throw new InvalidOperationException($"模板匹配参数无效：{parameterError}");
            }

            ShapeMatch.GetFindShapeModelAngles(tool.AllowAngleDelta, out double angleStartDeg, out double angleExtentDeg);
            return tool.ShapeMatch.Match(
                image,
                1,
                tool.PositionROI.Row1,
                tool.PositionROI.Col1,
                tool.PositionROI.Row2,
                tool.PositionROI.Col2,
                angleStartDeg,
                angleExtentDeg,
                true,
                tool.CandidateMinScore);
        }

        /// <summary>
        /// 检查匹配候选是否达到合格分值，达到才允许写入基准或参考位姿。
        /// 位置偏差不在此判定：设基准本身用于定义零点。
        /// </summary>
        /// <param name="tool">当前工具，读取工程已校验的合格分值下限。</param>
        /// <param name="score">候选分值（0~1）。</param>
        /// <param name="errorMessage">未达合格线时的操作员提示。</param>
        /// <returns>达到合格线返回 true。</returns>
        private static bool TryAcceptScoreForBasePoint(ToolModel tool, double score, out string errorMessage)
        {
            if (score < tool.MinScore)
            {
                errorMessage = $"分值低于合格下限({score:F3} < {tool.MinScore:F3})，未写入基准/参考位姿";
                return false;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// 使用当前模板图执行匹配并写入业务基准。
        /// 模板定位模式保存参考行列与角度，模板匹配模式保存基准 X/Y；两条路径共用 PositionROI、角域、候选分和合格分门禁。
        /// </summary>
        /// <param name="sender">模板图设基准或保存定位参考位姿按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void getinitpositionbymodelimage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsurePositionRoiReady())
                {
                    return;
                }

                var tool = _tool;
                var shapmatchresult = MatchWithToolSettings(tool.Image, tool);

                var Result_Data = tool.ShapeMatch.Analysis_Result(shapmatchresult);
                if (!shapmatchresult.IsSuccess)
                {
                    ShowBasePointResult(string.IsNullOrWhiteSpace(shapmatchresult.ErrorInfo)
                        ? "模板图设基准点失败：匹配结果为空"
                        : $"模板图设基准点失败：{shapmatchresult.ErrorInfo}");
                    return;
                }

                if (shapmatchresult.points == null || shapmatchresult.points.Count == 0)
                {
                    ShowBasePointResult(string.IsNullOrWhiteSpace(shapmatchresult.ErrorInfo)
                        ? "模板图设基准点失败：未找到候选"
                        : $"模板图设基准点失败：{shapmatchresult.ErrorInfo}");
                    return;
                }

                var firstPoint = shapmatchresult.points[0];
                if (!TryAcceptScoreForBasePoint(tool, firstPoint.score, out string scoreError))
                {
                    ShowBasePointResult($"模板图设基准点失败：{scoreError}");
                    return;
                }

                if (tool.TestMode == TestModes.模板定位)
                {
                    tool.ReferenceMatchRow = firstPoint.row;
                    tool.ReferenceMatchCol = firstPoint.column;
                    tool.ReferenceMatchAngleDeg = firstPoint.angle * 180.0 / Math.PI;
                    tool.ReferencePoseConfigured = true;
                    ShowBasePointResult(
                        $"参考位姿已保存：Row={firstPoint.row:F1}, Col={firstPoint.column:F1}, Angle={tool.ReferenceMatchAngleDeg:F2}deg, 分值={firstPoint.score:F3}, 耗时={shapmatchresult.ElapsedMs}ms",
                        MessageBoxIcon.Success);
                    return;
                }

                if (Result_Data != null)
                {
                    tool.InitX = Result_Data.X_actual;
                    tool.InitY = Result_Data.Y_actual;
                    ShowBasePointResult(
                        $"模板图基准点已更新：X={Result_Data.X_actual:F2}, Y={Result_Data.Y_actual:F2}, 分值={Result_Data.score:F3}, 角度={Result_Data.angle * 180.0 / Math.PI:F2}°, 耗时={shapmatchresult.ElapsedMs}ms",
                        MessageBoxIcon.Success);
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

        /// <summary>
        /// 使用操作员选择的现场图片写入模板匹配基准 X/Y。
        /// 图片只服务本次设基准并在完成后释放；模板定位参考位姿继续由模板图入口维护。
        /// </summary>
        /// <param name="sender">现场图设基准按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void getinitpositonfromimagefile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tool = _tool;
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
                    try
                    {
                        HOperatorSet.ReadImage(out image, ofd.FileName);

                        var shapmatchresult = MatchWithToolSettings(image, tool);

                        var Result_Data = tool.ShapeMatch.Analysis_Result(shapmatchresult);
                        if (!shapmatchresult.IsSuccess)
                        {
                            ShowBasePointResult(string.IsNullOrWhiteSpace(shapmatchresult.ErrorInfo)
                                ? "现场图设基准点失败：匹配结果为空"
                                : $"现场图设基准点失败：{shapmatchresult.ErrorInfo}");
                            return;
                        }

                        if (shapmatchresult.points == null || shapmatchresult.points.Count == 0)
                        {
                            ShowBasePointResult(string.IsNullOrWhiteSpace(shapmatchresult.ErrorInfo)
                                ? "现场图设基准点失败：未找到候选"
                                : $"现场图设基准点失败：{shapmatchresult.ErrorInfo}");
                            return;
                        }

                        if (!TryAcceptScoreForBasePoint(tool, shapmatchresult.points[0].score, out string scoreError))
                        {
                            ShowBasePointResult($"现场图设基准点失败：{scoreError}");
                            return;
                        }

                        if (Result_Data != null)
                        {
                            tool.InitX = Result_Data.X_actual;
                            tool.InitY = Result_Data.Y_actual;
                            ShowBasePointResult(
                                $"现场图基准点已更新：X={Result_Data.X_actual:F2}, Y={Result_Data.Y_actual:F2}, 分值={Result_Data.score:F3}, 角度={Result_Data.angle * 180.0 / Math.PI:F2}°, 耗时={shapmatchresult.ElapsedMs}ms",
                                MessageBoxIcon.Success);
                        }
                        else
                        {
                            ShowBasePointResult("现场图设基准点失败：匹配结果为空");
                        }
                    }
                    finally
                    {
                        image?.Dispose();
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
                var tool = _tool;
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
                    vml.PositionDetectViewModel.DisplayPublishedModelContours(shmfilename, out string contourSummary);
                    UpdateModelStatus($"已重载 {System.IO.Path.GetFileName(shmfilename)}；{contourSummary}");
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
