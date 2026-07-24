// ==========================================
// 文件: MainViewModel_FaraVision.cs
// 描述: FaraVision 相关视图模型，负责工程管理、工具增删改、
//       图像/模型读写以及基于 HALCON 的面积计算等核心逻辑。
// ==========================================
using BusbarCompressionSystem.FaraVision;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.FaraVision;
using BusbarCompressionSystem.Utils;
using Faratronic.EquipUtils.Authentication.Models;
using Faratronic.EquipUtils.Authentication;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight;
using HalconDotNet;
using Panuon.WPF.UI;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices; // 用于GDI句柄管理
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Xml.Serialization;
using System;


namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        /// <summary>
        /// 用于释放GDI句柄，防止句柄泄漏
        /// </summary>
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// 初始化界面命令绑定（RelayCommand）。
        /// </summary>
        public void Initrelaycommand()
        {
            DeleteToolCMD = new RelayCommand(DeleteTool);
            AddToolCMD = new RelayCommand(AddTool);
            ClearToolCMD = new RelayCommand(ClearTool);
            CopyToolCMD = new RelayCommand(CopyTool);
            InsertToolCMD = new RelayCommand(InsertTool);

            NEW_PRJCMD = new RelayCommand(NEW_PRJCMD_process);
            SAVE_PRJCMD = new RelayCommand(SAVE_PRJ_process);
        }



        /// <summary>
        /// 初始化 HALCON 显示窗口，使所有工具共享相同的 HWindow。
        /// </summary>
        /// <param name="_HWindow">HALCON 的 HWindow 对象</param>
        public void InitHwindow(HWindow _HWindow)
        {
            DataModel.FaraVisionDataModel.Settingmodel.HWindow = _HWindow;
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                DataModel.FaraVisionDataModel.Processmodel.Tools[i].Reg.Hwindow = _HWindow;
                DataModel.FaraVisionDataModel.Processmodel.Tools[i].ShapeMatch.HWindow = _HWindow;
            }
        }
        /// <summary>
        /// 初始化工程内所有模板工具的形状模型；软件启动和切换工程后由 <see cref="Load_Prj()"/> 调用。
        /// 模型句柄只保存在内存中，工程 XML 仍只保存工具参数，.shm 文件作为模板匹配与模板定位的现场模型来源。
        /// </summary>
        /// <returns>
        /// 未能从工程目录准备的模板模型数量；大于 0 时由 <see cref="Load_Prj()"/> 标记模型未就绪，
        /// 供工程切换拒绝持久化工程名称，不进入配方保存保护，也不拦截生产检测主流程。
        /// </returns>
        public int InitShm()
        {
            int shapeToolCount = 0;
            int unavailableModelCount = 0;
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                ToolModel tool = DataModel.FaraVisionDataModel.Processmodel.Tools[i];
                if (IsShapeModelTool(tool))
                {
                    shapeToolCount++;
                    if (!EnsureShapeModelLoaded(tool, "模型加载"))
                    {
                        unavailableModelCount++;
                    }
                }
            }

            if (shapeToolCount > 0)
            {
                writeLog(
                    $"[模板模型加载] 工程={DataModel.FaraVisionDataModel.Settingmodel.Name}，模板工具={shapeToolCount}，已就绪={shapeToolCount - unavailableModelCount}，待处理={unavailableModelCount}",
                    unavailableModelCount > 0);
            }

            return unavailableModelCount;
        }

        /// <summary>
        /// 在 AOI 执行或照片测试前确认模板模型已加载，避免重启软件后 XML 已恢复但 .shm 仍未读入内存。
        /// 当磁盘 .shm 比内存模型更新时会自动重载；文件缺失且内存中已有同路径模型时，当前进程继续使用已加载句柄。手动重载可跳过文件时间判断，读取失败时由 ShapeMatch 保留原模型句柄。
        /// </summary>
        /// <param name="tool">模板匹配或模板定位工具。</param>
        /// <param name="eventKind">追溯事件类型，如执行前加载、照片测试加载。</param>
        /// <param name="forceReload">true 时直接读取当前工程的 .shm，用于模型保存后和操作员手动重载；false 时沿用文件时间一致的内存模型。</param>
        /// <returns>true 表示可进入模板匹配算子；false 表示模型文件缺失或读取失败。</returns>
        internal bool EnsureShapeModelLoaded(ToolModel tool, string eventKind, bool forceReload = false)
        {
            if (!IsShapeModelTool(tool))
            {
                return true;
            }

            string shmfilename = GetShapeModelPath(tool);
            try
            {
                if (!forceReload && tool.ShapeMatch != null && tool.ShapeMatch.IsModelFileCurrent(shmfilename))
                {
                    return true;
                }

                if (File.Exists(shmfilename))
                {
                    bool loaded = tool.ShapeMatch.init(shmfilename);
                    if (loaded)
                    {
                        WriteTemplateMatchTraceLog(eventKind, tool, shmfilename, "成功");
                        return true;
                    }

                    writeLog($"模型文件加载失败:{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{tool.Index}.shm");
                    WriteTemplateMatchTraceLog(eventKind, tool, shmfilename, "ReadShapeModel失败");
                    return false;
                }

                if (tool.ShapeMatch != null && tool.ShapeMatch.ModelLoaded)
                {
                    WriteTemplateMatchTraceLog(eventKind, tool, shmfilename, "文件不存在，继续使用内存模型");
                    return true;
                }

                writeLog($"模型文件不存在:{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{tool.Index}.shm");
                WriteTemplateMatchTraceLog(eventKind, tool, shmfilename, "文件不存在");
                return false;
            }
            catch (Exception ex)
            {
                writeLog($"模型文件加载失败:{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{tool.Index}.shm;{ex.Message}");
                WriteTemplateMatchTraceLog(eventKind, tool, shmfilename, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 判断工具是否依赖工程目录下的 Tool 序号 .shm 形状模型。
        /// </summary>
        private static bool IsShapeModelTool(ToolModel tool)
        {
            return tool != null && (tool.TestMode == TestModes.模板匹配 || tool.TestMode == TestModes.模板定位);
        }

        /// <summary>
        /// 按工程目录和工具序号生成形状模型路径；XML 中的模型文件名字段保持兼容，不改变现有 Tool 序号文件约定。
        /// </summary>
        internal string GetShapeModelPath(ToolModel tool)
        {
            return $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{tool.Index}.shm";
        }
        /// <summary>
        /// 工程校验
        /// </summary>
        public void CheckPrj()
        {

            try
            {
                bool checkprj = false;
                DataModel.FaraVisionDataModel.Settingmodel.Prjs.Clear();
                if (!Directory.Exists(DataModel.FaraVisionDataModel.Settingmodel.Prjdir))
                {
                    Directory.CreateDirectory(DataModel.FaraVisionDataModel.Settingmodel.Prjdir);
                }
                string[] dirs = Directory.GetDirectories(DataModel.FaraVisionDataModel.Settingmodel.Prjdir);

                for (int i = 0; i < dirs.Length; i++)
                {
                    DirectoryInfo di = new DirectoryInfo(dirs[i]);
                    DataModel.FaraVisionDataModel.Settingmodel.Prjs.Add(di.Name);
                    if (di.Name.ToUpper() == DataModel.FaraVisionDataModel.Settingmodel.Name.ToUpper())
                    {
                        checkprj = true;
                        //break;
                    }
                }

                if (checkprj)
                {
                    NoticeBox.Show($"工程校验成功:{DataModel.FaraVisionDataModel.Settingmodel.Name}", "成功", MessageBoxIcon.Success, true, 5000);
                }
                else
                {
                    NoticeBox.Show($"工程校验失败:{DataModel.FaraVisionDataModel.Settingmodel.Name}", "失败", MessageBoxIcon.Error, true, 5000);
                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"工程目录加载失败:{ex.Message}", "失败", MessageBoxIcon.Error, true, 5000);

            }
        }

        /// <summary>
        /// 刷新工程选择项
        /// </summary>
        public void Prjs_selectedindex()
        {
            for (int i = 0; i < DataModel.FaraVisionDataModel.Settingmodel.Prjs.Count; i++)
            {
                if (DataModel.FaraVisionDataModel.Settingmodel.Prjs[i] == DataModel.FaraVisionDataModel.Settingmodel.Name)
                {
                    DataModel.FaraVisionDataModel.Settingmodel.prjselected = i;
                    break;
                }
            }
        }


        /// <summary>
        /// 加载当前名称对应的 AOI 工程，并在工具 XML 成功恢复后准备其中的模板模型。
        /// Tool XML 损坏或序号缺口时返回 false，并进入配方保存保护；模板 .shm 未就绪只置 <see cref="AoiShapeModelNotReady"/>，
        /// 不返回 false，也不禁止保存与生产检测，由工程切换入口决定是否持久化工程名称。
        /// </summary>
        /// <returns>工具 XML 通过完整性校验时返回 true；与模板模型是否就绪无关。</returns>
        public bool Load_Prj()
        {
            LoadPrjXmls();
            _aoiShapeModelNotReady = false;
            if (!_aoiProjectLoadFailed)
            {
                int unavailableModelCount = InitShm();
                if (unavailableModelCount > 0)
                {
                    _aoiShapeModelNotReady = true;
                    writeLog(
                        $"[AOI工程加载] 有 {unavailableModelCount} 个模板工具模型未就绪；工程可继续保存和检测，工程切换成功前不会写入该工程名称",
                        true);
                }
            }

            CheckPrj();
            Prjs_selectedindex();
            //ClearToolStatus();
            return !_aoiProjectLoadFailed;
        }

        /// <summary>
        /// 切换到指定工程并执行工具 XML、示教图和 .shm 的加载入口。
        /// </summary>
        /// <param name="PrjName">操作员选择的 AOI 工程名称。</param>
        /// <returns>指定工程的工具 XML 通过完整性校验时返回 true；模板模型就绪状态见 <see cref="AoiShapeModelNotReady"/>。</returns>
        public bool Load_Prj(string PrjName)
        {
            DataModel.FaraVisionDataModel.Settingmodel.Name = PrjName;
            return Load_Prj();
        }

        /// <summary>
        /// 当前 AOI 工程中模板匹配或模板定位工具的形状模型是否未全部就绪。
        /// 工程切换在持久化工程名称前检查该状态；为 true 时不写入视觉配置，但不阻止工程保存和生产检测。
        /// </summary>
        internal bool AoiShapeModelNotReady => _aoiShapeModelNotReady;

        /// <summary>
        /// 在工程切换、工具移除或工程清空前释放当前工具持有的运行时资源。
        /// 工程目录中的 XML、示教图和 .shm 文件保持原样；释放范围覆盖 HALCON 模型、图像句柄和 WPF 图像引用，避免异常加载后继续累积进程资源。
        /// </summary>
        private void ReleaseLoadedToolResources()
        {
            foreach (ToolModel tool in DataModel.FaraVisionDataModel.Processmodel.Tools)
            {
                ReleaseToolRuntimeResources(tool);
            }
        }

        /// <summary>
        /// 释放单个工具的 HALCON 模型、示教图像和界面图像引用。
        /// 工具从工程列表移除或工程加载中止时调用；磁盘上的 XML、JPG 和 SHM 文件不受影响，便于后续按快照恢复。
        /// </summary>
        /// <param name="tool">待释放运行时资源的工具对象。</param>
        private void ReleaseToolRuntimeResources(ToolModel tool)
        {
            if (tool == null)
            {
                return;
            }

            try
            {
                tool.ShapeMatch?.ReleaseModel();
            }
            catch (Exception ex)
            {
                writeLog($"[模板模型释放] Tool{tool.Index} 释放异常：{ex.Message}", false);
            }

            if (tool.Image != null)
            {
                try
                {
                    tool.Image.Dispose();
                }
                catch (Exception ex)
                {
                    writeLog($"[工程图像释放] Tool{tool.Index} 释放异常：{ex.Message}", false);
                }
                finally
                {
                    tool.Image = null;
                }
            }

            tool.BitmapSource = null;
            tool.CurrentBitmapSource = null;
        }

        /// <summary>
        /// 从当前 AOI 工程目录读取 <c>Tool*.xml</c> 构建工具列表，同时加载对应的示教图像缩略图显示。
        /// 工具 XML 只读取工程内主文件；保存成功后维护的单份工程快照供人工整批恢复 XML、图片和模型。
        /// 工具文件按 Tool 序号枚举并校验连续性；任一工具 XML 无法加载或序号存在缺口时标记本轮工程保存保护，避免关闭软件时压缩工具顺序并覆盖现场配方。
        /// </summary>
        public void LoadPrjXmls()
        {

            _aoiProjectLoadFailed = false;
            _aoiShapeModelNotReady = false;
            ReleaseLoadedToolResources();
            DataModel.FaraVisionDataModel.Processmodel.Tools.Clear();
            DataModel.FaraVisionDataModel.Processmodel.tool = new ToolModel();
            DataModel.FaraVisionDataModel.Processmodel.selectedindex = -1;
            string dir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}";
            if (!Directory.Exists(dir))
            {
                _aoiProjectLoadFailed = true;
                writeLog($"[AOI工程加载] 工程目录不存在：{dir}；工程保存已保护", true);
                return;
            }
            var toolFiles = new SortedDictionary<int, string>();
            foreach (string filename in Directory.GetFiles(dir, "Tool*.xml"))
            {
                string name = Path.GetFileNameWithoutExtension(filename);
                string indexText = name != null && name.StartsWith("Tool", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(4)
                    : string.Empty;

                int toolIndex;
                if (int.TryParse(indexText, out toolIndex) && toolIndex > 0)
                {
                    toolFiles[toolIndex] = filename;
                }
            }

            int expectedIndex = 1;
            foreach (int toolIndex in toolFiles.Keys)
            {
                if (toolIndex != expectedIndex)
                {
                    _aoiProjectLoadFailed = true;
                    writeLog($"[AOI工程加载] Tool 文件序号不连续，期望 Tool{expectedIndex}.xml，实际发现 Tool{toolIndex}.xml；工程保存已保护", true);
                    return;
                }

                expectedIndex++;
            }

            foreach (int toolIndex in toolFiles.Keys)
            {
                ToolModel tool = LoadPrjXml(toolIndex);
                if (tool == null)
                {
                    _aoiProjectLoadFailed = true;
                    ReleaseLoadedToolResources();
                    DataModel.FaraVisionDataModel.Processmodel.Tools.Clear();
                    writeLog($"[AOI工程加载] Tool{toolIndex}.xml 未生成有效工具对象，工程保存已保护", true);
                    return;
                }

                tool.Index = toolIndex;
                DataModel.FaraVisionDataModel.Processmodel.Tools.Add(tool);

                string jpgfilename = Path.Combine(dir, $"Tool{toolIndex}.jpg");
                try
                {
                    tool.Image?.Dispose();
                    HOperatorSet.GenEmptyObj(out tool.Image);
                    HOperatorSet.ReadImage(out tool.Image, jpgfilename);

                    HObject reducedImage = null;
                    Bitmap bitmap = null;
                    try
                    {
                        reducedImage = GetReducedImage(
                            DataModel.FaraVisionDataModel.Settingmodel.ImageSize,
                            DataModel.FaraVisionDataModel.Settingmodel.ImageSize,
                            tool.Image);
                        Hobject2Bitmap.HobjectToBitmap24(reducedImage, out bitmap);

                        IntPtr currentBitmapHandle = bitmap.GetHbitmap();
                        try
                        {
                            tool.CurrentBitmapSource = null;
                            tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(currentBitmapHandle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                        finally
                        {
                            DeleteObject(currentBitmapHandle);
                        }

                        IntPtr previewBitmapHandle = bitmap.GetHbitmap();
                        try
                        {
                            tool.BitmapSource = null;
                            tool.BitmapSource = Imaging.CreateBitmapSourceFromHBitmap(previewBitmapHandle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                        finally
                        {
                            DeleteObject(previewBitmapHandle);
                        }
                    }
                    finally
                    {
                        bitmap?.Dispose();
                        reducedImage?.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    writeLog($"[AOI工程加载] Tool{toolIndex}.jpg 读取失败：{ex.Message}", true);
                }

                GC.Collect();
            }
        }

        /// <summary>
        /// 读取指定序号的 AOI 工具 XML。该方法用于工程打开阶段，仅加载工程目录内的主文件。
        /// </summary>
        /// <param name="index">工具在工程目录中的文件序号；1 表示 <c>Tool1.xml</c>。</param>
        /// <returns>返回可用于界面和检测流程的工具配置；文件缺失或无法反序列化时返回 <c>null</c>。</returns>
        private ToolModel LoadPrjXml(int index)
        {
            string dir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}";
            string filename = $"{dir}\\Tool{index}.xml";

            ConfigLoadResult<ToolModel> result = ConfigXmlSaveHelper.TryLoad<ToolModel>(
                filename,
                () => null,
                message => writeLog(message));

            if (result.LoadFailed)
            {
                _aoiProjectLoadFailed = true;
                writeLog($"[AOI工程加载] Tool{index}.xml损坏或无法读取，工程保存已保护");
            }

            return result.Data;
        }

        public KColor GetPixelData(int X, int Y)
        {
            try
            {
                int w = DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.PixelWidth;
                int h = DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.PixelHeight;
                int stride = (w * DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.Format.BitsPerPixel + 7) / 8;

                byte[] pixels = new byte[h * stride];
                DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.CopyPixels(pixels, stride, 0);

                int pixelindex = Y * stride + X * (DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.Format.BitsPerPixel / 8);
                byte r = pixels[pixelindex + 2];
                byte g = pixels[pixelindex + 1];
                byte b = pixels[pixelindex + 0];

                return new KColor() { R = r, G = g, B = b };
            }catch {  return null; }
        }



        /// <summary>
        /// 保存当前 AOI 工程的工具 XML，并在授权会话内按参数差异审计口径记录变更。
        /// 工具 XML 采用临时文件校验和正式文件原子替换；保存成功后按工程名维护一份完整快照供人工恢复。
        /// 工程加载存在无法读取的工具时返回失败，保留现场工程文件和参数审计上下文。
        /// </summary>
        /// <returns>全部工具 XML 保存完成返回 <c>true</c>；目录、序列化、文件替换或审计流程抛出异常时返回 <c>false</c>。</returns>
        public bool SavePrjXmls()
        {
            try
            {
                if (_aoiProjectLoadFailed)
                {
                    writeLog("[AOI工程保存] AOI工程加载或工具文件操作存在失败，当前工程完整性未确认，已跳过保存以保留现场工程文件", true);
                    return false;
                }

                if (_faraVisionSettingConfigLoadFailed)
                {
                    writeLog("[AOI工程保存] 视觉配置数据加载失败，工程目录和工程名称不适合作为保存依据，已跳过工程保存", true);
                    return false;
                }

                // ==================== 动态密码审计（P1）：保存工程时上报参数差异 ====================
                // 仅在“动态密码验证通过且已开启编辑权限”时，记录本次保存涉及的参数变化（old/new）
                bool shouldReport = CanReportDynamicPasswordAudit(
                    out string authorizerName,
                    out string authorizerNo,
                    out string auditReason,
                    out int privilegeLevel,
                    out int periodMinutes,
                    out string requestedReceivers);

                // 快照模式：oldTool 来自“打开编辑界面时的工具快照”，避免依赖磁盘旧xml并减少运行时噪声
                int editingIndex = DataModel.FaraVisionDataModel.Processmodel.EditingToolIndex;
                ToolModel oldTool = DataModel.FaraVisionDataModel.Processmodel.EditingToolSnapshot;
                ToolModel newTool = null;
                int toolFileIndex = -1;

                if (shouldReport
                    && oldTool != null
                    && editingIndex >= 0
                    && editingIndex < DataModel.FaraVisionDataModel.Processmodel.Tools.Count)
                {
                    toolFileIndex = editingIndex + 1;
                    newTool = DataModel.FaraVisionDataModel.Processmodel.Tools[editingIndex];
                }

                string prjDir = GetCurrentProjectDirectory();
                if (!Directory.Exists(prjDir))
                {
                    Directory.CreateDirectory(prjDir);
                }

                for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
                {
                    SavePrjXml(DataModel.FaraVisionDataModel.Processmodel.Tools[i], i + 1);
                }

                DeleteExtraPrjXmls(prjDir, DataModel.FaraVisionDataModel.Processmodel.Tools.Count);

                if (shouldReport && toolFileIndex > 0 && newTool != null && oldTool != null)
                {
                    ReportToolParameterDiff(
                        oldTool,
                        newTool,
                        toolFileIndex,
                        DataModel.FaraVisionDataModel.Settingmodel.Name,
                        authorizerName,
                        authorizerNo,
                        auditReason,
                        privilegeLevel,
                        periodMinutes,
                        requestedReceivers);

                }

                if (!BackupCurrentAoiProjectFilesAfterSave())
                {
                    writeLog("[AOI工程保存] 工程快照未确认，已拒绝报告保存成功", true);
                    return false;
                }

                if (shouldReport && toolFileIndex > 0 && newTool != null && oldTool != null)
                {
                    // 工程 XML 和完整快照都成功后更新审计基线，避免备份失败时吞掉下一次重试的参数差异
                    DataModel.FaraVisionDataModel.Processmodel.EditingToolSnapshot = CloneToolModelSnapshot(newTool);
                    DataModel.FaraVisionDataModel.Processmodel.EditingToolSnapshotTime = DateTime.Now;
                }
                return true;
            }
            catch (Exception ex)
            {
                writeLog($"[AOI工程保存] 工具配置保存失败：{ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// 获取当前 AOI 工程目录，供工具 XML 保存和旧文件清理共用。
        /// 目录来自工程配置，直接对应现场 <c>工程文件\工程名称</c> 下的一组 Tool XML/JPG/SHM 文件。
        /// </summary>
        /// <returns>当前 AOI 工程的绝对目录路径。</returns>
        private string GetCurrentProjectDirectory()
        {
            return $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}";
        }

        /// <summary>
        /// 在模型发布、工程保存完成以及工具文件重排前同步当前 AOI 工程的单份完整快照。工程内容无变化时跳过复制。
        /// 快照覆盖 XML、示教图和 .shm，供现场从一次工具文件操作异常中人工恢复；备份流程不改变工程选择、模型初始化、检测执行、参数审计、相机取图或 PLC/MES 通信。
        /// </summary>
        /// <returns>快照已同步或当前工程没有需要备份的文件时返回 <c>true</c>；快照目录或文件复制失败时返回 <c>false</c>。</returns>
        internal bool BackupCurrentAoiProjectFilesAfterSave()
        {
            return BackupAoiProjectFiles(
                GetCurrentProjectDirectory(),
                DataModel.FaraVisionDataModel.Settingmodel.Name);
        }

        /// <summary>
        /// 将指定 AOI 工程目录同步到按工程名归档的人工恢复快照。
        /// 同名工程覆盖前使用该入口保留原目录内容，当前工程的模型保存和工具重排通过 <see cref="BackupCurrentAoiProjectFilesAfterSave"/> 使用相同备份口径。
        /// </summary>
        /// <param name="projectDir">待备份的 AOI 工程绝对目录。</param>
        /// <param name="projectName">用于生成备份分类目录的工程名称。</param>
        /// <returns>快照已同步或源目录没有需要备份的文件时返回 <c>true</c>；备份过程中存在文件失败时返回 <c>false</c>。</returns>
        private bool BackupAoiProjectFiles(string projectDir, string projectName)
        {
            string backupRoot = Path.Combine(Environment.CurrentDirectory, "配置备份");
            string categoryName = $"AOI工程_{projectName}";
            return ConfigXmlSaveHelper.BackupProjectFilesIfChanged(projectDir, backupRoot, categoryName, message => writeLog(message));
        }

        /// <summary>
        /// 清理当前工具数量之外的旧 Tool XML。
        /// 清理动作位于全部工具 XML 成功替换之后；保存失败时不会提前删除旧工具文件，便于现场继续使用上一次完整配置。
        /// </summary>
        /// <param name="prjDir">当前 AOI 工程目录。</param>
        /// <param name="toolCount">当前工程内有效工具数量。</param>
        private void DeleteExtraPrjXmls(string prjDir, int toolCount)
        {
            foreach (string file in Directory.GetFiles(prjDir, "Tool*.xml"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string indexText = name != null && name.StartsWith("Tool", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(4)
                    : string.Empty;

                if (int.TryParse(indexText, out int index) && index > toolCount)
                {
                    File.Delete(file);
                }
            }
        }

        private bool CanReportDynamicPasswordAudit(
            out string authorizerName,
            out string authorizerNo,
            out string reason,
            out int privilegeLevel,
            out int periodMinutes,
            out string requestedReceivers)
        {
            authorizerName = DataModel.FaraVisionDataModel.Settingmodel.PermissionAuthorizerName ?? string.Empty;
            authorizerNo = DataModel.FaraVisionDataModel.Settingmodel.PermissionAuthorizerNo ?? string.Empty;
            reason = DataModel.FaraVisionDataModel.Settingmodel.PermissionReason ?? string.Empty;
            privilegeLevel = DataModel.FaraVisionDataModel.Settingmodel.PermissionPrivilegeLevel;
            periodMinutes = DataModel.FaraVisionDataModel.Settingmodel.PermissionPeriodMinutes;
            requestedReceivers = DataModel.FaraVisionDataModel.Settingmodel.PermissionRequestedReceivers ?? string.Empty;

            // 必须满足“权限已开启 + 授权人工号存在”，否则不做审计上报
            return DataModel.FaraVisionDataModel.Settingmodel.permission
                && !string.IsNullOrWhiteSpace(authorizerNo);
        }

        private ToolModel CloneToolModelSnapshot(ToolModel tool)
        {
            try
            {
                if (tool == null)
                {
                    return null;
                }

                var serializer = new XmlSerializer(typeof(ToolModel));
                using (var ms = new MemoryStream())
                {
                    serializer.Serialize(ms, tool);
                    ms.Position = 0;
                    return serializer.Deserialize(ms) as ToolModel;
                }
            }
            catch
            {
                return null;
            }
        }

        private void ReportToolParameterDiff(
            ToolModel oldTool,
            ToolModel newTool,
            int toolFileIndex,
            string prjName,
            string authorizerName,
            string authorizerNo,
            string reason,
            int privilegeLevel,
            int periodMinutes,
            string requestedReceivers)
        {
            try
            {
                var datas = new List<OperationData>();

                // 上下文信息（不做 old/new 对比，仅用于追溯）
                datas.Add(new OperationData { DataName = "工程名称", DataOldValue = string.Empty, DataNewValue = prjName ?? string.Empty, DataType = "上下文" });
                datas.Add(new OperationData { DataName = "工具文件序号", DataOldValue = string.Empty, DataNewValue = toolFileIndex.ToString(CultureInfo.InvariantCulture), DataType = "上下文" });
                datas.Add(new OperationData { DataName = "工具名称", DataOldValue = string.Empty, DataNewValue = newTool?.Name ?? string.Empty, DataType = "上下文" });
                datas.Add(new OperationData { DataName = "申请原因", DataOldValue = string.Empty, DataNewValue = reason ?? string.Empty, DataType = "上下文" });
                datas.Add(new OperationData { DataName = "权限等级", DataOldValue = string.Empty, DataNewValue = privilegeLevel.ToString(CultureInfo.InvariantCulture), DataType = "上下文" });
                if (periodMinutes > 0)
                {
                    datas.Add(new OperationData { DataName = "有效期分钟", DataOldValue = string.Empty, DataNewValue = periodMinutes.ToString(CultureInfo.InvariantCulture), DataType = "上下文" });
                }
                if (!string.IsNullOrWhiteSpace(requestedReceivers))
                {
                    datas.Add(new OperationData { DataName = "通知接收人", DataOldValue = string.Empty, DataNewValue = requestedReceivers, DataType = "上下文" });
                }

                // 参数差异明细
                string dataType = string.IsNullOrWhiteSpace(newTool?.Name)
                    ? $"AOI工具{toolFileIndex}"
                    : $"AOI工具{toolFileIndex}({newTool.Name})";

                if (oldTool == null)
                {
                    // 旧快照不存在时不全量展开（字段太多），仅记录关键字段用于追溯
                    datas.AddRange(new[]
                    {
                        new OperationData { DataName = "工具模式", DataOldValue = "无快照", DataNewValue = newTool.TestMode.ToString(), DataType = dataType },
                        new OperationData { DataName = "触发指令", DataOldValue = "无快照", DataNewValue = newTool.Command ?? string.Empty, DataType = dataType },
                        new OperationData { DataName = "相机序号", DataOldValue = "无快照", DataNewValue = newTool.CameraIndex.ToString(CultureInfo.InvariantCulture), DataType = dataType },
                        new OperationData { DataName = "相机曝光时间", DataOldValue = "无快照", DataNewValue = newTool.ExposureTime.ToString(CultureInfo.InvariantCulture), DataType = dataType },
                    });
                }
                else
                {
                    var diffs = BuildObjectDiff(oldTool, newTool, dataType, 2);
                    if (diffs.Count > 0)
                    {
                        datas.AddRange(diffs);
                    }
                }

                // 如果没有任何差异，则不发送
                bool hasRealDiff = datas.Any(d => d.DataType != "上下文");
                if (!hasRealDiff)
                {
                    return;
                }

                // 防止一次保存产生过多字段（ToolModel字段非常多）
                const int maxDatas = 200;
                if (datas.Count > maxDatas)
                {
                    datas = datas.Take(maxDatas).ToList();
                    datas.Add(new OperationData
                    {
                        DataName = "提示",
                        DataOldValue = string.Empty,
                        DataNewValue = "变更项过多，已截断（请联系开发扩展上报策略）",
                        DataType = "系统"
                    });
                }

                string equipNo = DataModel.Settingmodel.SETTING_DATA.MachineID ?? string.Empty;

                var record = new OperationRecord
                {
                    AuthorizerName = authorizerName ?? string.Empty,
                    AuthorizerNo = authorizerNo ?? string.Empty,
                    EquipNo = equipNo,
                    OperateStartTime = DateTime.Now,
                    OperateEndTime = DateTime.Now,
                    Datas = datas
                };

                int contextCount = datas.Count(d => d.DataType == "上下文");
                int diffCount = datas.Count(d => d.DataType != "上下文");
                int totalCount = datas.Count;

                // UI仅提示概要信息，详细变更写入日志文件
                writeLog($"[动态密码][参数审计] 触发上报：工程={prjName}, 工具序号={toolFileIndex}, 参数变更={diffCount}, 上下文={contextCount}, 合计={totalCount}", true);
                WriteParameterAuditDetailToFile(prjName, toolFileIndex, authorizerName, authorizerNo, reason, requestedReceivers, datas);

                Task.Run(() =>
                {
                    try
                    {
                        // 参数审计与权限验证使用同一生产服务边界，确保现场变更记录进入正式审计链路。
                        const bool useTestAuthentication = false;
                        OperationLog opLog = new OperationLog(useTestAuthentication);
                        opLog.Log(record);
                        writeLog($"[动态密码][参数审计] 已上报：工程={prjName}, 工具序号={toolFileIndex}, 参数变更={diffCount}, 上下文={contextCount}, 合计={totalCount}", true);
                    }
                    catch (Exception ex)
                    {
                        writeLog($"[动态密码][参数审计] 上报失败：{ex.Message}", true);
                    }
                });
            }
            catch
            {
                // 忽略所有异常，避免影响保存工程主流程
            }
        }

        private List<OperationData> BuildObjectDiff(object oldObj, object newObj, string dataType, int maxDepth)
        {
            var diffs = new List<OperationData>();
            AppendObjectDiff(diffs, oldObj, newObj, string.Empty, dataType, maxDepth);
            return diffs;
        }

        private void WriteParameterAuditDetailToFile(
            string prjName,
            int toolFileIndex,
            string authorizerName,
            string authorizerNo,
            string reason,
            string requestedReceivers,
            List<OperationData> datas)
        {
            try
            {
                // 明细仅写入日志文件，界面不展示（showdatarecord=false）
                writeLog($"[动态密码][参数审计][明细] 工程={prjName}, 工具序号={toolFileIndex}, 授权人={authorizerName}({authorizerNo}), 原因={reason}", false);
                if (!string.IsNullOrWhiteSpace(requestedReceivers))
                {
                    writeLog($"[动态密码][参数审计][明细] 通知接收人={requestedReceivers}", false);
                }

                if (datas == null || datas.Count == 0)
                {
                    writeLog("[动态密码][参数审计][明细] 无明细数据", false);
                    return;
                }

                const int maxValueLength = 300;
                foreach (var d in datas)
                {
                    string oldValue = TruncateForAuditLog(d?.DataOldValue, maxValueLength);
                    string newValue = TruncateForAuditLog(d?.DataNewValue, maxValueLength);
                    string dataType = d?.DataType ?? string.Empty;
                    string name = d?.DataName ?? string.Empty;

                    writeLog($"[动态密码][参数审计][明细] {dataType}|{name}: {oldValue} -> {newValue}", false);
                }
            }
            catch
            {
                // 忽略所有异常，避免影响保存工程主流程
            }
        }

        private static string TruncateForAuditLog(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string v = value.Replace("\r", "\\r").Replace("\n", "\\n");
            if (v.Length <= maxLength)
            {
                return v;
            }
            return v.Substring(0, maxLength) + "...(已截断)";
        }

        private void AppendObjectDiff(List<OperationData> diffs, object oldObj, object newObj, string prefix, string dataType, int depth)
        {
            if (depth < 0)
            {
                return;
            }

            if (oldObj == null && newObj == null)
            {
                return;
            }

            Type type = (newObj ?? oldObj).GetType();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite)
                {
                    continue;
                }

                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (Attribute.IsDefined(prop, typeof(XmlIgnoreAttribute)))
                {
                    continue;
                }

                // 过滤运行时结果字段，避免把“测量结果/识别结果”等噪声上报为参数修改
                if (!IsAuditRelevantProperty(prop.Name))
                {
                    continue;
                }

                object oldValue = oldObj != null ? prop.GetValue(oldObj) : null;
                object newValue = newObj != null ? prop.GetValue(newObj) : null;

                string displayName = GetDisplayName(prop);
                string path = string.IsNullOrWhiteSpace(prefix) ? displayName : $"{prefix}.{displayName}";

                Type propType = prop.PropertyType;

                if (IsSimpleType(propType))
                {
                    if (!AreEqual(oldValue, newValue, propType))
                    {
                        diffs.Add(new OperationData
                        {
                            DataName = path,
                            DataOldValue = FormatValue(oldValue),
                            DataNewValue = FormatValue(newValue),
                            DataType = dataType
                        });
                    }
                    continue;
                }

                // 跳过集合/大对象，避免爆炸式上报
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(propType) && propType != typeof(string))
                {
                    continue;
                }

                // 递归比较子对象（例如 ROI）
                if (depth > 0)
                {
                    AppendObjectDiff(diffs, oldValue, newValue, path, dataType, depth - 1);
                }
            }
        }

        private bool IsAuditRelevantProperty(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            // 运行时结果字段常见前缀（ToolModel中较多），不作为“参数修改”审计
            if (propertyName.StartsWith("Actual", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (propertyName.StartsWith("Delta", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 状态/过程字段，不作为参数审计
            if (propertyName.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            // 运行时扫码内容，不作为参数审计
            if (propertyName.Equals("BarcodeStr", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Equals("Barcodes", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private string GetDisplayName(PropertyInfo prop)
        {
            var xmlElement = prop.GetCustomAttributes(typeof(XmlElementAttribute), true)
                .OfType<XmlElementAttribute>()
                .FirstOrDefault();
            if (xmlElement != null && !string.IsNullOrWhiteSpace(xmlElement.ElementName))
            {
                return xmlElement.ElementName;
            }
            return prop.Name;
        }

        private bool IsSimpleType(Type t)
        {
            if (t.IsEnum)
            {
                return true;
            }

            return t == typeof(string)
                || t == typeof(bool)
                || t == typeof(byte)
                || t == typeof(short)
                || t == typeof(int)
                || t == typeof(long)
                || t == typeof(float)
                || t == typeof(double)
                || t == typeof(decimal)
                || t == typeof(DateTime);
        }

        private bool AreEqual(object oldValue, object newValue, Type t)
        {
            if (oldValue == null && newValue == null)
            {
                return true;
            }
            if (oldValue == null || newValue == null)
            {
                return false;
            }

            if (t == typeof(double))
            {
                double a = (double)oldValue;
                double b = (double)newValue;
                return Math.Abs(a - b) < 1e-9;
            }
            if (t == typeof(float))
            {
                float a = (float)oldValue;
                float b = (float)newValue;
                return Math.Abs(a - b) < 1e-6f;
            }

            if (t == typeof(string))
            {
                return string.Equals((string)oldValue, (string)newValue, StringComparison.Ordinal);
            }

            return Equals(oldValue, newValue);
        }

        private string FormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is DateTime dt)
            {
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }

        /// <summary>
        /// 保存单个 AOI 工具 XML。
        /// 文件先写入同目录临时文件并校验，再原子替换正式 Tool XML；保存成功后的单份工程快照承担整批人工恢复。
        /// </summary>
        /// <param name="td">当前工程内待持久化的工具模型，内容会写入对应的 Tool XML。</param>
        /// <param name="index">工具文件序号；1 表示 <c>Tool1.xml</c>，与界面工具顺序一致。</param>
        private void SavePrjXml(ToolModel td, int index)
        {
            string filename = $"{GetCurrentProjectDirectory()}\\Tool{index}.xml";
            if (!SaveProjectXmlSafely(filename, td, _aoiProjectLoadFailed))
            {
                throw new IOException($"Tool{index}.xml保存失败");
            }
        }

        public bool LoadBitmapSource()
        {

            try
            {
                string jpgfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{DataModel.FaraVisionDataModel.Processmodel.selectedindex + 1}.jpg";
                using (Bitmap bmp = (Bitmap)Bitmap.FromFile(jpgfilename))
                {
                    // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = bs;
                    }
                    finally
                    {
                        DeleteObject(hBitmap); // 释放GDI句柄
                    }
                }
                GC.Collect();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }

            GC.Collect();


        }

        #region Command
        public RelayCommand DeleteToolCMD { set; get; } = null;
        public RelayCommand AddToolCMD { set; get; } = null;
        public RelayCommand ClearToolCMD { set; get; } = null;
        public RelayCommand CopyToolCMD { set; get; } = null;
        public RelayCommand InsertToolCMD { set; get; } = null;


        public void DeleteTool()
        {
            int index = DataModel.FaraVisionDataModel.Processmodel.selectedindex;
            DeleteTool(index);

        }
        /// <summary>
        /// 删除指定索引的工具，并同步删除对应 jpg/xml/shm 文件。
        /// 操作前保留完整工程快照；若其后仍有工具，则将后续文件整体前移保持序号连续，模型文件始终随同 XML 与示教图保持相同 Tool 序号。
        /// </summary>
        public void DeleteTool(int index)
        {
            bool snapshotReady = false;
            bool fileMutationStarted = false;
            bool fileMutationCompleted = false;
            string rollbackDirectory = null;
            int rollbackFirstToolIndex = 0;
            int rollbackLastToolIndex = 0;
            try
            {
                if (index < 0 || index >= DataModel.FaraVisionDataModel.Processmodel.Tools.Count)
                {

                    NoticeBox.Show($"请先选择需要删除的工具", "失败", MessageBoxIcon.Error, true, 5000);
                    return;
                }
                if (MessageBoxX.Show("是否确定删除工具？", "提示", System.Windows.MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == System.Windows.MessageBoxResult.Yes)
                {
                    snapshotReady = BackupCurrentAoiProjectFilesAfterSave();
                    if (!snapshotReady)
                    {
                        _aoiProjectLoadFailed = true;
                        writeLog("[AOI工具删除] 工程快照未确认，已停止文件整理并保护工程保存", true);
                        NoticeBox.Show("工程快照未确认，工具删除未执行，工程已进入保存保护", "提示", MessageBoxIcon.Warning, true, 6000);
                        return;
                    }

                    rollbackFirstToolIndex = index + 1;
                    rollbackLastToolIndex = DataModel.FaraVisionDataModel.Processmodel.Tools.Count;
                    string rollbackPrepareError;
                    if (!TryCreateToolFileRollbackBackup(
                        rollbackFirstToolIndex,
                        rollbackLastToolIndex,
                        out rollbackDirectory,
                        out rollbackPrepareError))
                    {
                        writeLog($"[AOI工具删除] 文件整理准备失败，工程文件保持原样：{rollbackPrepareError}", true);
                        NoticeBox.Show("工具删除未执行，工程文件保持原样，请检查运行日志", "提示", MessageBoxIcon.Warning, true, 7000);
                        return;
                    }

                    fileMutationStarted = true;

                    string jpgsrc1 = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index + 1}.jpg";
                    string xmlsrc1 = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index + 1}.xml";
                    string shmsrc1 = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index + 1}.shm";

                    DeleteFile(jpgsrc1);
                    DeleteFile(xmlsrc1);
                    DeleteFile(shmsrc1);

                    if (index != DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1)
                    {

                        // 将后续工具的 Tool{i+2}.xxx 前移到 Tool{i+1}.xxx
                        for (int i = index; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1; i++)
                        {
                            string moveError;
                            if (!TryMoveToolFiles(i + 2, i + 1, out moveError))
                            {
                                HandleToolFileReorderFailure(
                                    "AOI工具删除",
                                    "工具删除",
                                    moveError,
                                    rollbackDirectory,
                                    rollbackFirstToolIndex,
                                    rollbackLastToolIndex);
                                return;
                            }
                        }
                    }
                    fileMutationCompleted = true;
                    if (index >= 0)
                    {
                        ReleaseToolRuntimeResources(DataModel.FaraVisionDataModel.Processmodel.Tools[index]);
                        DataModel.FaraVisionDataModel.Processmodel.Tools.RemoveAt(index);
                    }
                    ReindexToolsInMemory();
                }
            }
            catch (Exception ex)
            {
                if (fileMutationStarted && !fileMutationCompleted && !string.IsNullOrWhiteSpace(rollbackDirectory))
                {
                    HandleToolFileReorderFailure(
                        "AOI工具删除",
                        "工具删除",
                        ex.Message,
                        rollbackDirectory,
                        rollbackFirstToolIndex,
                        rollbackLastToolIndex);
                    return;
                }

                _aoiProjectLoadFailed = true;
                writeLog($"[AOI工具删除] 文件整理失败：{ex.Message}", true);
                NoticeBox.Show(
                    snapshotReady
                        ? "工具删除未完成，工程快照已保留，工程已进入保存保护，请检查运行日志"
                        : "工具删除未完成，工程快照未确认，工程已进入保存保护，请检查运行日志",
                    "提示",
                    MessageBoxIcon.Warning,
                    true,
                    6000);
            }
            finally
            {
                TryDeleteToolFileRollbackBackup(rollbackDirectory);
            }
        }


        public void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static readonly string[] ToolReorderFileExtensions = { "xml", "jpg", "shm" };

        /// <summary>
        /// 在工具增删、插入或复制前暂存本次会受影响的 Tool 文件。
        /// 临时副本只服务于当前操作的自动回滚，不参与工程加载、模型初始化、生产检测或长期工程快照。
        /// </summary>
        /// <param name="firstToolIndex">受影响的首个 Tool 文件序号，从 1 开始。</param>
        /// <param name="lastToolIndex">受影响的最后一个 Tool 文件序号，包含该序号。</param>
        /// <param name="rollbackDirectory">成功时返回本次操作专用的临时回滚目录。</param>
        /// <param name="errorMessage">准备失败时返回文件系统原因。</param>
        /// <returns>全部现有 XML、示教图和模型文件完成暂存时返回 <c>true</c>。</returns>
        private bool TryCreateToolFileRollbackBackup(
            int firstToolIndex,
            int lastToolIndex,
            out string rollbackDirectory,
            out string errorMessage)
        {
            rollbackDirectory = null;
            errorMessage = string.Empty;
            string temporaryDirectory = null;
            try
            {
                temporaryDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "FaraVisionToolReorder",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);

                for (int index = firstToolIndex; index <= lastToolIndex; index++)
                {
                    foreach (string extension in ToolReorderFileExtensions)
                    {
                        string sourceFile = GetToolFilePath(index, extension);
                        if (File.Exists(sourceFile))
                        {
                            File.Copy(sourceFile, Path.Combine(temporaryDirectory, Path.GetFileName(sourceFile)), true);
                        }
                    }
                }

                rollbackDirectory = temporaryDirectory;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                TryDeleteToolFileRollbackBackup(temporaryDirectory);
                return false;
            }
        }

        /// <summary>
        /// 将工具文件整理前的临时副本恢复到当前工程目录。
        /// 恢复范围只覆盖本次增删或复制涉及的 Tool 序号，其他工程文件和工具参数保持原样。
        /// </summary>
        /// <param name="rollbackDirectory">由 <see cref="TryCreateToolFileRollbackBackup"/> 生成的临时目录。</param>
        /// <param name="firstToolIndex">受影响的首个 Tool 文件序号。</param>
        /// <param name="lastToolIndex">受影响的最后一个 Tool 文件序号。</param>
        /// <param name="errorMessage">恢复失败时返回文件系统原因。</param>
        /// <returns>受影响范围恢复到操作前文件状态时返回 <c>true</c>。</returns>
        private bool TryRestoreToolFilesFromRollbackBackup(
            string rollbackDirectory,
            int firstToolIndex,
            int lastToolIndex,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(rollbackDirectory) || !Directory.Exists(rollbackDirectory))
                {
                    errorMessage = "工具文件临时回滚副本不存在";
                    return false;
                }

                for (int index = firstToolIndex; index <= lastToolIndex; index++)
                {
                    foreach (string extension in ToolReorderFileExtensions)
                    {
                        string targetFile = GetToolFilePath(index, extension);
                        if (File.Exists(targetFile))
                        {
                            File.Delete(targetFile);
                        }
                    }
                }

                Directory.CreateDirectory(GetCurrentProjectDirectory());
                foreach (string backupFile in Directory.GetFiles(rollbackDirectory))
                {
                    File.Copy(
                        backupFile,
                        Path.Combine(GetCurrentProjectDirectory(), Path.GetFileName(backupFile)),
                        true);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 收尾单次工具文件整理使用的临时回滚目录。
        /// 清理失败只记录诊断日志，正式工程目录和长期工程快照保持可用。
        /// </summary>
        /// <param name="rollbackDirectory">待清理的临时回滚目录。</param>
        private void TryDeleteToolFileRollbackBackup(string rollbackDirectory)
        {
            if (string.IsNullOrWhiteSpace(rollbackDirectory) || !Directory.Exists(rollbackDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(rollbackDirectory, true);
            }
            catch (Exception ex)
            {
                writeLog($"[AOI工具整理] 临时回滚目录清理失败：{rollbackDirectory}；{ex.Message}", true);
            }
        }

        /// <summary>
        /// 处理工具文件整理失败并尝试恢复操作前状态。
        /// 自动恢复成功时保持当前工程可继续保存和使用；恢复失败时进入工程保存保护，避免半整理目录继续落盘。
        /// </summary>
        /// <param name="logCategory">现场日志分类，例如 AOI工具删除。</param>
        /// <param name="operationName">界面提示使用的操作名称，例如工具删除。</param>
        /// <param name="failureDetail">触发回滚的文件系统原因。</param>
        /// <param name="rollbackDirectory">本次操作的临时回滚目录。</param>
        /// <param name="firstToolIndex">受影响的首个 Tool 文件序号。</param>
        /// <param name="lastToolIndex">受影响的最后一个 Tool 文件序号。</param>
        private void HandleToolFileReorderFailure(
            string logCategory,
            string operationName,
            string failureDetail,
            string rollbackDirectory,
            int firstToolIndex,
            int lastToolIndex)
        {
            string restoreError;
            if (TryRestoreToolFilesFromRollbackBackup(
                rollbackDirectory,
                firstToolIndex,
                lastToolIndex,
                out restoreError))
            {
                writeLog($"[{logCategory}] 文件整理未完成，工程文件已自动恢复：{failureDetail}", true);
                NoticeBox.Show($"{operationName}未完成，工程文件已自动恢复，可继续使用", "提示", MessageBoxIcon.Warning, true, 7000);
                return;
            }

            _aoiProjectLoadFailed = true;
            writeLog($"[{logCategory}] 文件整理失败且自动恢复未完成，工程保存已保护：{failureDetail}；恢复错误：{restoreError}", true);
            NoticeBox.Show($"{operationName}未完成，自动恢复失败，工程已进入保存保护，请检查运行日志", "提示", MessageBoxIcon.Error, true, 9000);
        }

        /// <summary>
        /// 生成当前工程目录下指定 Tool 序号与扩展名的文件路径。
        /// 路径约定与现场工程目录一致，供工具重排时把 XML、示教图和模型作为同一组处理。
        /// </summary>
        /// <param name="toolFileIndex">工程目录中的 Tool 文件序号，从 1 开始。</param>
        /// <param name="extension">文件扩展名，不含点，例如 xml、jpg、shm。</param>
        /// <returns>对应 Tool 文件的绝对路径。</returns>
        private string GetToolFilePath(int toolFileIndex, string extension)
        {
            return Path.Combine(
                DataModel.FaraVisionDataModel.Settingmodel.Prjdir,
                DataModel.FaraVisionDataModel.Settingmodel.Name,
                $"Tool{toolFileIndex}.{extension}");
        }

        /// <summary>
        /// 将同一 Tool 序号下的 XML、示教图和模型文件作为一组移动到目标序号。
        /// XML 始终必需；示教图和 .shm 保持源文件的存在状态，源文件缺失时同步清理目标残留，避免后移工具继承其他工具的图片或模型。
        /// </summary>
        /// <param name="sourceIndex">源 Tool 文件序号。</param>
        /// <param name="destinationIndex">目标 Tool 文件序号。</param>
        /// <param name="errorMessage">失败时返回可供日志和弹窗使用的原因。</param>
        /// <returns>文件组按规则全部处理完成时返回 true。</returns>
        private bool TryMoveToolFiles(int sourceIndex, int destinationIndex, out string errorMessage)
        {
            return TryTransferToolFiles(sourceIndex, destinationIndex, move: true, out errorMessage);
        }

        /// <summary>
        /// 将同一 Tool 序号下的 XML、示教图和模型文件作为一组复制到目标序号。
        /// 校验口径与移动相同，保证复制工具后新旧序号各自仍能组成完整工具文件组。
        /// </summary>
        /// <param name="sourceIndex">源 Tool 文件序号。</param>
        /// <param name="destinationIndex">目标 Tool 文件序号。</param>
        /// <param name="errorMessage">失败时返回可供日志和弹窗使用的原因。</param>
        /// <returns>文件组按规则全部复制完成时返回 true。</returns>
        private bool TryCopyToolFiles(int sourceIndex, int destinationIndex, out string errorMessage)
        {
            return TryTransferToolFiles(sourceIndex, destinationIndex, move: false, out errorMessage);
        }

        /// <summary>
        /// 按工程文件完整性口径迁移或复制 Tool 文件组。
        /// XML 缺失或文件系统操作失败时立即返回；示教图和 .shm 缺失表示该工具尚未保存对应文件，目标序号同步保持缺失状态。
        /// </summary>
        /// <param name="sourceIndex">源 Tool 文件序号。</param>
        /// <param name="destinationIndex">目标 Tool 文件序号。</param>
        /// <param name="move">true 表示移动，false 表示复制。</param>
        /// <param name="errorMessage">失败原因。</param>
        /// <returns>文件组处理成功时返回 true。</returns>
        private bool TryTransferToolFiles(int sourceIndex, int destinationIndex, bool move, out string errorMessage)
        {
            if (!TryTransferFile(GetToolFilePath(sourceIndex, "xml"), GetToolFilePath(destinationIndex, "xml"), required: true, "XML", move, out errorMessage))
            {
                return false;
            }

            // 示教图不是所有工具的硬门禁：缺失时跳过，存在则必须完成迁移，避免半移后序号错位。
            if (!TryTransferFile(GetToolFilePath(sourceIndex, "jpg"), GetToolFilePath(destinationIndex, "jpg"), required: false, "示教图", move, out errorMessage))
            {
                return false;
            }

            return TryTransferFile(
                GetToolFilePath(sourceIndex, "shm"),
                GetToolFilePath(destinationIndex, "shm"),
                required: false,
                "模型",
                move,
                out errorMessage);
        }

        /// <summary>
        /// 迁移或复制单个工程文件，并按是否必需区分缺失处理。
        /// 必需文件缺失视为整理失败；非必需源文件缺失时清理目标残留，若源文件存在则必须完成文件系统操作。
        /// </summary>
        /// <param name="sourceFile">源文件绝对路径。</param>
        /// <param name="destinationFile">目标文件绝对路径。</param>
        /// <param name="required">true 表示该文件属于当前工具的必需组成。</param>
        /// <param name="fileKind">用于日志的文件业务名称，例如 XML、示教图、模型。</param>
        /// <param name="move">true 表示移动，false 表示复制。</param>
        /// <param name="errorMessage">失败原因。</param>
        /// <returns>按规则处理完成时返回 true。</returns>
        private static bool TryTransferFile(
            string sourceFile,
            string destinationFile,
            bool required,
            string fileKind,
            bool move,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!File.Exists(sourceFile))
            {
                if (required)
                {
                    errorMessage = $"{fileKind}文件不存在：{Path.GetFileName(sourceFile)}";
                    return false;
                }

                try
                {
                    if (File.Exists(destinationFile))
                    {
                        File.Delete(destinationFile);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = $"{fileKind}目标残留清理失败：{Path.GetFileName(destinationFile)}，{ex.Message}";
                    return false;
                }
            }

            try
            {
                string directory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (move)
                {
                    if (File.Exists(destinationFile))
                    {
                        File.Delete(destinationFile);
                    }

                    File.Move(sourceFile, destinationFile);
                }
                else
                {
                    File.Copy(sourceFile, destinationFile, true);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"{fileKind}文件{(move ? "移动" : "复制")}失败：{Path.GetFileName(sourceFile)}，{ex.Message}";
                return false;
            }
        }




        public void AddTool()
        {
            int index = DataModel.FaraVisionDataModel.Processmodel.Tools.Count;
            DataModel.FaraVisionDataModel.Processmodel.selectedindex = 0;
            InsertTool(index);

        }
        /// <summary>
        /// 在指定位置插入工具，同时从后向前移动文件避免覆盖。
        /// 移动前同步工程快照，XML、示教图和 .shm 以同一 Tool 序号整体迁移。
        /// </summary>
        public void InsertTool(int index, ToolModel tool = null)
        {
            bool snapshotReady = false;
            bool fileMutationStarted = false;
            bool fileMutationCompleted = false;
            string rollbackDirectory = null;
            int rollbackFirstToolIndex = 0;
            int rollbackLastToolIndex = 0;
            try
            {

                if (DataModel.FaraVisionDataModel.Processmodel.selectedindex < 0
                    || index < 0
                    || index > DataModel.FaraVisionDataModel.Processmodel.Tools.Count)
                {

                    NoticeBox.Show($"请先选择需要插入工具的位置", "失败", MessageBoxIcon.Error, true, 5000);
                    return;
                }

                snapshotReady = BackupCurrentAoiProjectFilesAfterSave();
                if (!snapshotReady)
                {
                    _aoiProjectLoadFailed = true;
                    writeLog("[AOI工具插入] 工程快照未确认，已停止文件整理并保护工程保存", true);
                    NoticeBox.Show("工程快照未确认，工具插入未执行，工程已进入保存保护", "提示", MessageBoxIcon.Warning, true, 6000);
                    return;
                }

                rollbackFirstToolIndex = index + 1;
                rollbackLastToolIndex = DataModel.FaraVisionDataModel.Processmodel.Tools.Count + 1;
                string rollbackPrepareError;
                if (!TryCreateToolFileRollbackBackup(
                    rollbackFirstToolIndex,
                    rollbackLastToolIndex,
                    out rollbackDirectory,
                    out rollbackPrepareError))
                {
                    writeLog($"[AOI工具插入] 文件整理准备失败，工程文件保持原样：{rollbackPrepareError}", true);
                    NoticeBox.Show("工具插入未执行，工程文件保持原样，请检查运行日志", "提示", MessageBoxIcon.Warning, true, 7000);
                    return;
                }

                fileMutationStarted = true;

                // 从后向前移动 Tool 文件，避免文件名冲突覆盖
                for (int i = DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1; i >= index; i--)
                {
                    string moveError;
                    if (!TryMoveToolFiles(i + 1, i + 2, out moveError))
                    {
                        HandleToolFileReorderFailure(
                            "AOI工具插入",
                            "工具插入",
                            moveError,
                            rollbackDirectory,
                            rollbackFirstToolIndex,
                            rollbackLastToolIndex);
                        return;
                    }
                }
                fileMutationCompleted = true;

                if (tool == null)
                {
                    tool = new ToolModel();
                }
                DataModel.FaraVisionDataModel.Processmodel.Tools.Insert(index, tool);
                DataModel.FaraVisionDataModel.Processmodel.selectedindex = index;
                ReindexToolsInMemory();

            }
            catch (Exception ex)
            {
                if (fileMutationStarted && !fileMutationCompleted && !string.IsNullOrWhiteSpace(rollbackDirectory))
                {
                    HandleToolFileReorderFailure(
                        "AOI工具插入",
                        "工具插入",
                        ex.Message,
                        rollbackDirectory,
                        rollbackFirstToolIndex,
                        rollbackLastToolIndex);
                    return;
                }

                _aoiProjectLoadFailed = true;
                writeLog($"[AOI工具插入] 文件整理失败：{ex.Message}", true);
                NoticeBox.Show(
                    snapshotReady
                        ? "工具插入未完成，工程快照已保留，工程已进入保存保护，请检查运行日志"
                        : "工具插入未完成，工程快照未确认，工程已进入保存保护，请检查运行日志",
                    "提示",
                    MessageBoxIcon.Warning,
                    true,
                    6000);
            }
            finally
            {
                TryDeleteToolFileRollbackBackup(rollbackDirectory);
            }
        }


        /// <summary>
        /// 清空当前工程的全部工具及其 XML、示教图和 .shm 文件。
        /// 操作员确认后先同步完整工程快照；单个文件删除失败会保留到运行日志并在界面提示，避免目录半清理被误判为完成。
        /// </summary>
        public void ClearTool()
        {
            bool snapshotReady = false;
            try
            {
                if (MessageBoxX.Show("是否确定清空所有工具？", "提示", System.Windows.MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == System.Windows.MessageBoxResult.Yes)
                {
                    snapshotReady = BackupCurrentAoiProjectFilesAfterSave();
                    if (!snapshotReady)
                    {
                        _aoiProjectLoadFailed = true;
                        writeLog("[AOI工具清空] 工程快照未确认，已停止删除并保护工程保存", true);
                        NoticeBox.Show("工程快照未确认，工具清空未执行，工程已进入保存保护", "提示", MessageBoxIcon.Warning, true, 6000);
                        return;
                    }

                    ReleaseLoadedToolResources();
                    DataModel.FaraVisionDataModel.Processmodel.Tools.Clear();
                    #region 删除对应目录下面的工具文件
                    string dir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}";
                    var files = Directory.GetFiles(dir);
                    int failedCount = 0;
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            //if (files[i].ToUpper().EndsWith(".XML"))
                            //{
                            //    File.Delete(files[i]);
                            //}

                            File.Delete(files[i]);
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            writeLog($"[AOI工具清空] 删除 {Path.GetFileName(files[i])} 失败：{ex.Message}", true);
                        }
                        #endregion
                    }

                    if (failedCount > 0)
                    {
                        _aoiProjectLoadFailed = true;
                        writeLog($"[AOI工具清空] 有 {failedCount} 个工程文件删除失败，工程保存已保护", true);
                        NoticeBox.Show($"有 {failedCount} 个工程文件保留，工程快照已保存，请检查运行日志", "提示", MessageBoxIcon.Warning, true, 6000);
                    }
                }
            }
            catch (Exception ex)
            {
                _aoiProjectLoadFailed = true;
                writeLog($"[AOI工具清空] 操作异常：{ex.Message}", true);
                NoticeBox.Show(
                    snapshotReady
                        ? "工具清空未完成，工程快照已保留，工程已进入保存保护，请检查运行日志"
                        : "工具清空未完成，工程快照未确认，工程已进入保存保护，请检查运行日志",
                    "提示",
                    MessageBoxIcon.Warning,
                    true,
                    6000);
            }
        }
        /// <summary>
        /// 响应界面复制工具命令，并在创建内存副本前确认当前工程快照。
        /// 内存副本创建失败时工程文件保持原样；文件复制阶段由内部回滚副本恢复，只有自动恢复失败才进入工程保存保护。
        /// </summary>
        public void CopyTool()
        {
            bool snapshotReady = false;
            try
            {
                if (DataModel.FaraVisionDataModel.Processmodel.selectedindex < 0)
                {

                    NoticeBox.Show($"请先选择需要复制的工具", "失败", MessageBoxIcon.Error, true, 5000);
                    return;
                }

                if (MessageBoxX.Show("是否确定复制选中的工具？", "提示", System.Windows.MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == System.Windows.MessageBoxResult.Yes)
                {
                    snapshotReady = BackupCurrentAoiProjectFilesAfterSave();
                    if (!snapshotReady)
                    {
                        _aoiProjectLoadFailed = true;
                        writeLog("[AOI工具复制] 工程快照未确认，已停止文件整理并保护工程保存", true);
                        NoticeBox.Show("工程快照未确认，工具复制未执行，工程已进入保存保护", "提示", MessageBoxIcon.Warning, true, 6000);
                        return;
                    }

                    ToolModel copiedTool = New_Tool_Model(DataModel.FaraVisionDataModel.Processmodel.Tools[DataModel.FaraVisionDataModel.Processmodel.selectedindex]);
                    if (!CopyTool(DataModel.FaraVisionDataModel.Processmodel.selectedindex + 1, copiedTool))
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                writeLog($"[AOI工具复制] 内存副本创建失败，工程文件保持原样：{ex.Message}", true);
                NoticeBox.Show("工具复制未完成，工程文件保持原样，请检查运行日志", "提示", MessageBoxIcon.Warning, true, 6000);
            }
        }
        /// <summary>
        /// 在指定位置插入拷贝的工具，并自后向前复制文件避免覆盖。
        /// 复制前同步工程快照，原工具和副本的 XML、示教图与 .shm 使用连续 Tool 序号保持一一对应。
        /// </summary>
        /// <param name="index">副本插入位置，使用当前内存工具列表的零基索引。</param>
        /// <param name="tool">已从原工具复制出的内存对象；该对象只有在文件整理和序号重排完成后才加入工具列表。</param>
        /// <returns>文件整理、内存插入和序号重排均完成时返回 <c>true</c>；快照、文件操作或序号重排失败时返回 <c>false</c>。</returns>
        public bool CopyTool(int index, ToolModel tool)
        {
            bool snapshotReady = false;
            bool fileMutationStarted = false;
            bool fileMutationCompleted = false;
            string rollbackDirectory = null;
            int rollbackFirstToolIndex = 0;
            int rollbackLastToolIndex = 0;
            try
            {

                if (DataModel.FaraVisionDataModel.Processmodel.selectedindex < 0
                    || index < 1
                    || index > DataModel.FaraVisionDataModel.Processmodel.Tools.Count
                    || tool == null)
                {

                    NoticeBox.Show($"请先选择需要插入工具的位置", "失败", MessageBoxIcon.Error, true, 5000);
                    return false;
                }

                snapshotReady = BackupCurrentAoiProjectFilesAfterSave();
                if (!snapshotReady)
                {
                    _aoiProjectLoadFailed = true;
                    writeLog("[AOI工具复制] 工程快照未确认，已停止文件整理并保护工程保存", true);
                    NoticeBox.Show("工程快照未确认，工具复制未执行，工程已进入保存保护", "提示", MessageBoxIcon.Warning, true, 6000);
                    return false;
                }

                rollbackFirstToolIndex = index + 1;
                rollbackLastToolIndex = DataModel.FaraVisionDataModel.Processmodel.Tools.Count + 1;
                string rollbackPrepareError;
                if (!TryCreateToolFileRollbackBackup(
                    rollbackFirstToolIndex,
                    rollbackLastToolIndex,
                    out rollbackDirectory,
                    out rollbackPrepareError))
                {
                    writeLog($"[AOI工具复制] 文件整理准备失败，工程文件保持原样：{rollbackPrepareError}", true);
                    NoticeBox.Show("工具复制未执行，工程文件保持原样，请检查运行日志", "提示", MessageBoxIcon.Warning, true, 7000);
                    return false;
                }

                fileMutationStarted = true;

                // 从后向前复制，避免目标文件被覆盖
                for (int i = DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1; i >= index - 1; i--)
                {
                    string copyError;
                    if (!TryCopyToolFiles(i + 1, i + 2, out copyError))
                    {
                        HandleToolFileReorderFailure(
                            "AOI工具复制",
                            "工具复制",
                            copyError,
                            rollbackDirectory,
                            rollbackFirstToolIndex,
                            rollbackLastToolIndex);
                        return false;
                    }
                }
                fileMutationCompleted = true;

                DataModel.FaraVisionDataModel.Processmodel.Tools.Insert(index, tool);
                DataModel.FaraVisionDataModel.Processmodel.selectedindex = index;
                ReindexToolsInMemory();

                return true;
            }
            catch (Exception ex)
            {
                if (fileMutationStarted && !fileMutationCompleted && !string.IsNullOrWhiteSpace(rollbackDirectory))
                {
                    HandleToolFileReorderFailure(
                        "AOI工具复制",
                        "工具复制",
                        ex.Message,
                        rollbackDirectory,
                        rollbackFirstToolIndex,
                        rollbackLastToolIndex);
                    return false;
                }

                _aoiProjectLoadFailed = true;
                writeLog($"[AOI工具复制] 文件整理失败：{ex.Message}", true);
                NoticeBox.Show(
                    snapshotReady
                        ? "工具复制未完成，工程快照已保留，工程已进入保存保护，请检查运行日志"
                        : "工具复制未完成，工程快照未确认，工程已进入保存保护，请检查运行日志",
                    "提示",
                    MessageBoxIcon.Warning,
                    true,
                    6000);
                return false;
            }
            finally
            {
                TryDeleteToolFileRollbackBackup(rollbackDirectory);
            }
        }

        public ROI New_ROI(ROI roi)
        {
            ROI r = new ROI
            {
                Type = roi.Type,
                Row1 = roi.Row1,
                Row2 = roi.Row2,
                Col1 = roi.Col1,
                Col2 = roi.Col2,
                CircleCenterRow = roi.CircleCenterRow,
                CircleCenterCol = roi.CircleCenterCol,
                CircleRadius = roi.CircleRadius
            };
            return r;
        }

        public ToolModel New_Tool_Model(ToolModel tool)
        {
            ToolModel t = new ToolModel();
            t.Index = tool.Index;
            t.Command = tool.Command;
            t.Name = tool.Name;
            //t.DecodeBarcode = tool.DecodeBarcode;
            t.BarcodeStr = tool.BarcodeStr;
            t.BarCodeROI = New_ROI(tool.BarCodeROI);
            //t.PositionDetect = tool.PositionDetect;
            t.ModelFileName = tool.ModelFileName;
            t.MinScore = tool.MinScore;
            t.ActualScore = tool.ActualScore;
            t.K = tool.K;
            t.InitX = tool.InitX;
            t.InitY = tool.InitY;
            t.ActualX = tool.ActualX;
            t.ActualY = tool.ActualY;
            t.ActualAngle = tool.ActualAngle;
            t.AllowAngleDelta = tool.AllowAngleDelta;
            t.DeltaX = tool.DeltaX;
            t.DeltaY = tool.DeltaY;
            //t.DirectX = tool.DirectX;
            //t.DirectY = tool.DirectY;
            //t.InvertXY = tool.InvertXY;
            t.SendStatus = tool.SendStatus;
            //t.SendBarcodeData = tool.SendBarcodeData;
            //t.SendPositionData = tool.SendPositionData;
            //t.InvertResult = tool.InvertResult;
            t.Allow_X_Delta = tool.Allow_X_Delta;
            t.Allow_Y_Delta = tool.Allow_Y_Delta;
            //t.StatusColor = tool.StatusColor;
            t.PositionROI = New_ROI(tool.PositionROI);
            t.ReferenceMatchRow = tool.ReferenceMatchRow;
            t.ReferenceMatchCol = tool.ReferenceMatchCol;
            t.ReferenceMatchAngleDeg = tool.ReferenceMatchAngleDeg;
            t.ReferencePoseConfigured = tool.ReferencePoseConfigured;
            t.EnableFollowCorrectionForCommand = tool.EnableFollowCorrectionForCommand;

            t.DimensionROI = New_ROI(tool.DimensionROI);
            //t.DimensionDetect = tool.DimensionDetect;
            t.ActualDimension = tool.ActualDimension;
            t.MinDimension = tool.MinDimension;
            t.MaxDimension = tool.MaxDimension;
            t.GrayMode = tool.GrayMode;
            t.RedChannelEnabled = tool.RedChannelEnabled;
            t.GreenChannelEnabled = tool.GreenChannelEnabled;
            t.BlueChannelEnabled = tool.BlueChannelEnabled;
            t.MinGray = tool.MinGray;
            t.MaxGray = tool.MaxGray;
            t.MinRed = tool.MinRed;
            t.MaxRed = tool.MaxRed;
            t.MinGreen = tool.MinGreen;
            t.MaxGreen = tool.MaxGreen;
            t.MinBlue = tool.MinBlue;
            t.MaxBlue = tool.MaxBlue;
            t.MinAreaFilter = tool.MinAreaFilter;
            t.MaxAreaFilter = tool.MaxAreaFilter;
            t.TestMode = tool.TestMode;
            t.ExposureTime = tool.ExposureTime;
            t.ProductPositionNO = tool.ProductPositionNO;
            t.CameraIndex = tool.CameraIndex;
            t.MeasureType = tool.MeasureType;
            t.MeasureObject1ROI = New_ROI(tool.MeasureObject1ROI);
            t.MeasureObject2ROI = New_ROI(tool.MeasureObject2ROI);
            t.DimensionK = tool.DimensionK;
            t.CalibrationRealSize = tool.CalibrationRealSize;
            t.CalibrationPixelSize = tool.CalibrationPixelSize;
            t.MinMeasureValue = tool.MinMeasureValue;
            t.MaxMeasureValue = tool.MaxMeasureValue;
            t.MetrologyTolerance = tool.MetrologyTolerance;
            t.MetrologyNumMeasures = tool.MetrologyNumMeasures;
            t.MetrologyMeasureSigma = tool.MetrologyMeasureSigma;
            t.MetrologyMeasureThreshold = tool.MetrologyMeasureThreshold;
            t.MetrologyMeasureTransition = tool.MetrologyMeasureTransition;
            t.MetrologyMeasureSelect = tool.MetrologyMeasureSelect;
            t.MetrologyMinScore = tool.MetrologyMinScore;
            t.MetrologyMeasureLength1 = tool.MetrologyMeasureLength1;
            t.MetrologyMeasureLength2 = tool.MetrologyMeasureLength2;
            t.LineDistanceExtendRatio = tool.LineDistanceExtendRatio;
            t.ShowMetrologyDebugInfo = tool.ShowMetrologyDebugInfo;
            t.LineDetectROI = New_ROI(tool.LineDetectROI);
            t.LineDetectAllowAngleDelta = tool.LineDetectAllowAngleDelta;
            t.LineDetectMinEdgeHitRatio = tool.LineDetectMinEdgeHitRatio;
            t.ExpectLinePresent = tool.ExpectLinePresent;
            t.LineDetectAbsentMaxEdgePoints = tool.LineDetectAbsentMaxEdgePoints;
            t.BitmapSource = null;
            t.CurrentBitmapSource = null;
            if (tool.BitmapSource != null)
            {
                t.BitmapSource = tool.BitmapSource.Clone();
            }
            if (tool.CurrentBitmapSource != null)
            {
                t.CurrentBitmapSource = tool.CurrentBitmapSource.Clone();
            }
            t.CurrentBitmapFileName = tool.CurrentBitmapFileName;
            if (tool.Image != null)
            {
                t.Image = tool.Image.Clone();
            }

            return t;
        }
        public void InsertTool()
        {
            int index = DataModel.FaraVisionDataModel.Processmodel.selectedindex;
            InsertTool(index);
        }

        /// <summary>
        /// 在工具文件整理完成后，按界面顺序刷新内存工具序号。
        /// 磁盘 XML、示教图和 .shm 已由增删复制流程一次性处理，此处只更新运行期索引，避免同一批文件被重复重命名。
        /// </summary>
        private void ReindexToolsInMemory()
        {
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                DataModel.FaraVisionDataModel.Processmodel.Tools[i].Index = i + 1;
            }
        }

        #endregion


        #region 界面menuitem
        public RelayCommand NEW_PRJCMD { set; get; } = null;

        /// <summary>
        /// 创建 AOI 工程目录并切换当前编辑上下文。
        /// 同名工程覆盖前同步原工程快照；当前进程中的模板句柄在清空工具列表前释放，工程目录与 HALCON 内存资源分别按确认流程收尾。
        /// </summary>
        public void NEW_PRJCMD_process()
        {
            try
            {
                if (DataModel.FaraVisionDataModel.Settingmodel.permission)
                {
                    if (MessageBoxX.Show("是否确定新建工程?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == MessageBoxResult.Yes)
                    {
                        BusbarCompressionSystem.Model.newPrj newPrj = new BusbarCompressionSystem.Model.newPrj();
                        if (newPrj.ShowDialog() == true)
                        {
                            string prjname = newPrj.Prj_Name;
                            string prjdir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{prjname}";
                            if (Directory.Exists(prjdir))
                            {
                                if (MessageBoxX.Show("工程已经存在，是否删除旧工程?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == MessageBoxResult.Yes)
                                {
                                    if (!BackupAoiProjectFiles(prjdir, prjname))
                                    {
                                        writeLog($"[AOI工程新建] 同名工程快照未确认，已停止删除：{prjdir}", true);
                                        NoticeBox.Show("同名工程快照未确认，未删除原工程", "提示", MessageBoxIcon.Warning, true, 6000);
                                        return;
                                    }
                                    Directory.Delete(prjdir, true);
                                }
                                else
                                {
                                    return;
                                }
                            }
                            else
                            {
                                DataModel.FaraVisionDataModel.Settingmodel.Prjs.Add(prjname);
                                DataModel.FaraVisionDataModel.Settingmodel.prjselected = DataModel.FaraVisionDataModel.Settingmodel.Prjs.Count - 1;
                            }

                            #region 新建工程
                            Directory.CreateDirectory(prjdir);
                            DataModel.FaraVisionDataModel.Settingmodel.Name = prjname;
                            ReleaseLoadedToolResources();
                            DataModel.FaraVisionDataModel.Processmodel.Tools.Clear();
                            //ClearToolStatus();
                            #endregion

                        }
                    }

                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"新建工程失败\r\n{ex.Message}", "错误", MessageBoxIcon.Error, true, 5000);
            }
        }


        public RelayCommand SAVE_PRJCMD { set; get; } = null;
        public void SAVE_PRJ_process()
        {
            //SaveAPPXml();
            SaveProcessmodel();
            SaveSettingModel();
            bool saved = SavePrjXmls();
            if (saved)
            {
                NoticeBox.Show($"工程保存完成", "成功", MessageBoxIcon.Success, true, 5000);
            }
            else
            {
                NoticeBox.Show($"工程保存失败，请检查日志", "错误", MessageBoxIcon.Error, true, 5000);
            }

        }

        #endregion



        #region 数据保存加载

        #region 过程数据
        public void Faravision_SaveProcessmodel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
            SaveXmlSafely(filename, DataModel.Processmodel);
        }
        public void Faravision_LoadProcessmodel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(filename))
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var serializer = new XmlSerializer(typeof(Processmodel));
                        DataModel.FaraVisionDataModel.Processmodel = serializer.Deserialize(stream) as Processmodel;
                    }
                }
                else
                {
                    DataModel.FaraVisionDataModel.Processmodel = new Processmodel();
                }
            }
            catch (Exception ex)
            {
                DataModel.FaraVisionDataModel.Processmodel = new Processmodel();

            }
        }
        #endregion

        #region 配置数据
        public void Faravision_SaveSettingModel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\视觉配置数据.xml";
            SaveXmlSafely(filename, DataModel.FaraVisionDataModel.Settingmodel, _faraVisionSettingConfigLoadFailed);
        }
        public void Faravision_LoadSettingModel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\视觉配置数据.xml";
                ConfigLoadResult<VisionSettingModel> result = ConfigXmlSaveHelper.TryLoad(
                    filename,
                    () => new VisionSettingModel(),
                    message => writeLog(message));

                DataModel.FaraVisionDataModel.Settingmodel = result.Data ?? new VisionSettingModel();
                _faraVisionSettingConfigLoadFailed = result.LoadFailed;

                if (result.RestoredFromBackup)
                {
                    writeLog($"视觉配置数据.xml已从备份恢复：{Path.GetFileName(result.RestoredFrom)}");
                }
                else if (result.LoadFailed)
                {
                    MessageBox.Show("视觉配置数据.xml加载失败且无法从备份恢复，软件本轮使用默认内存配置；关闭软件时会跳过该文件保存，现场 XML 文件会保留供维护排查。");
                }
            }
            catch (Exception ex)
            {
                DataModel.FaraVisionDataModel.Settingmodel = new VisionSettingModel();
                _faraVisionSettingConfigLoadFailed = true;

                MessageBox.Show($"视觉配置数据.xml加载异常，软件本轮使用默认内存配置；关闭软件时会跳过该文件保存，现场 XML 文件会保留供维护排查:\r\n{ex.Message}");

            }
        }
        #endregion

        #region 日志数据
        public void Faravision_SaveRecordModel()
        {
            string filename = $"{Environment.CurrentDirectory}\\配置\\视觉日志数据.xml";
            SaveXmlSafely(filename, DataModel.FaraVisionDataModel.Recordmodel, _faraVisionRecordConfigLoadFailed);
        }
        public void Faravision_LoadRecordModel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\视觉日志数据.xml";
                ConfigLoadResult<RecordModel> result = ConfigXmlSaveHelper.TryLoad(
                    filename,
                    () => new RecordModel(),
                    message => writeLog(message));

                DataModel.FaraVisionDataModel.Recordmodel = result.Data ?? new RecordModel();
                _faraVisionRecordConfigLoadFailed = result.LoadFailed;
            }
            catch (Exception ex)
            {
                DataModel.FaraVisionDataModel.Recordmodel = new RecordModel();
                _faraVisionRecordConfigLoadFailed = true;
                writeLog($"[配置加载] 视觉日志数据.xml加载异常：{ex.Message}");
                //MessageBox.Show($"日志数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");

            }
        }
        #endregion
        #region 配方保存加载




        public bool Faravision_LoadBitmapSource()
        {

            try
            {
                string jpgfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{DataModel.FaraVisionDataModel.Processmodel.selectedindex + 1}.jpg";
                using (Bitmap bmp = (Bitmap)Bitmap.FromFile(jpgfilename))
                {
                    // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = bs;
                    }
                    finally
                    {
                        DeleteObject(hBitmap); // 释放GDI句柄
                    }
                }
                GC.Collect();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }

            GC.Collect();


        }


        #endregion
        #endregion



        #region 面积计算

        /// <summary>
        /// 面积计算（支持灰度/颜色阈值），并可在窗口重绘 ROI 与结果区域。
        /// </summary>
        /// <returns>面积，失败返回 -1，无区域返回 0</returns>
        public int CoculateDimension(HObject image, ToolModel tool, HWindow hwindow, bool redraw = true, ROI dimensionRoiOverride = null)
        {
            try
            {
                ROI dimensionRoi = dimensionRoiOverride ?? tool.DimensionROI;

                HTuple Area = new HTuple(), Row = new HTuple(), Column = new HTuple();

                HObject ROI, ReduceImage;

                HObject Region, ConnectedRegions, SelectedRegions, RegionUnion;
                HOperatorSet.GenEmptyObj(out Region);
                HOperatorSet.GenEmptyObj(out ConnectedRegions);
                HOperatorSet.GenEmptyObj(out SelectedRegions);
                HOperatorSet.GenEmptyObj(out RegionUnion);

                HOperatorSet.GenEmptyObj(out ROI);
                HOperatorSet.GenEmptyObj(out ReduceImage);

                HObject ImageR, ImageG, ImageB;
                HObject RegionR, RegionG, RegionB;
                HObject RegionIntersection;

                HOperatorSet.GenEmptyObj(out ImageR);
                HOperatorSet.GenEmptyObj(out ImageG);
                HOperatorSet.GenEmptyObj(out ImageB);

                HOperatorSet.GenEmptyObj(out RegionR);
                HOperatorSet.GenEmptyObj(out RegionG);
                HOperatorSet.GenEmptyObj(out RegionB);

                HOperatorSet.GenEmptyObj(out RegionIntersection);

                try
                {

                    // 1) 生成 ROI 并缩小运算域
                    HOperatorSet.GenRectangle1(out ROI, dimensionRoi.Row1, dimensionRoi.Col1, dimensionRoi.Row2, dimensionRoi.Col2);
                    HOperatorSet.ReduceDomain(image, ROI, out ReduceImage);

                    // 2) 阈值分割：灰度 or RGB 三通道交集
                    if (tool.GrayMode)
                    {
                        HOperatorSet.Threshold(ReduceImage, out Region, tool.MinGray, tool.MaxGray);
                    }
                    else
                    {
                        HOperatorSet.Decompose3(ReduceImage, out ImageR, out ImageG, out ImageB);

                        HOperatorSet.Threshold(ImageR, out RegionR, tool.MinRed, tool.MaxRed);
                        HOperatorSet.Threshold(ImageG, out RegionG, tool.MinGreen, tool.MaxGreen);
                        HOperatorSet.Threshold(ImageB, out RegionB, tool.MinBlue, tool.MaxBlue);

                        HOperatorSet.Intersection(RegionR, RegionG, out RegionIntersection);
                        HOperatorSet.Intersection(RegionB, RegionIntersection, out Region);
                    }


                    // 3) 连通域、按面积筛选并合并
                    HOperatorSet.Connection(Region, out ConnectedRegions);
                    HOperatorSet.SelectShape(ConnectedRegions, out SelectedRegions, "area", "and", tool.MinAreaFilter, tool.MaxAreaFilter);
                    HOperatorSet.Union1(SelectedRegions, out RegionUnion);
                    HOperatorSet.AreaCenter(RegionUnion, out Area, out Row, out Column);

                    HTuple width, height;
                    HOperatorSet.GetImageSize(image, out width, out height);
                    //HWindow.HalconWindow.SetPart(0, 0, (int)height - 1, (int)width - 1);

                    if (redraw)
                    {

                        hwindow.ClearWindow();
                        //hwindow.SetPart(0, 0, (int)height - 1, (int)width - 1);
                        hwindow.DispObj(image);
                    }
                    hwindow.SetLineWidth(2);
                    hwindow.SetDraw("margin");
                    hwindow.SetColor("orange");
                    hwindow.DispObj(ROI);
                    hwindow.SetDraw("fill");
                    hwindow.SetColor("red");
                    hwindow.DispObj(SelectedRegions);

                }
                catch (Exception ex) { }

                // 统一释放 HObject 资源，避免句柄泄漏
                ROI?.Dispose();
                ReduceImage?.Dispose();
                Region?.Dispose();
                ConnectedRegions?.Dispose();
                SelectedRegions?.Dispose();
                RegionUnion?.Dispose();
                ImageR?.Dispose();
                ImageG?.Dispose();
                ImageB?.Dispose();
                RegionR?.Dispose();
                RegionG?.Dispose();
                RegionB?.Dispose();
                RegionIntersection?.Dispose();

                //tool.ActualDimension = (int)Area.L;
                if (Area.Length > 0)
                {
                    return (int)Area.L;
                }
                else
                {
                    return 0;
                }
            }
            catch (Exception e)
            {
                //tool.ActualDimension = -1;
                return -1;
            }
        }
        #endregion

        #region 尺寸测量

        /// <summary>
        /// 为手动尺寸测量结果准备主界面 HALCON 画面。
        /// 参数配置页的校准、模板测试和检测照片按钮都使用主界面 FaraVision 结果窗口显示完整测量图；
        /// 本方法只负责清空目标窗口并显示当前测量图像，后续拟合线、距离线和调试图层仍由尺寸测量流程绘制。
        /// 它不更新 WPF 的 ShowBitmapSource，不改变 ROI、校准系数、测量值或工程配置，避免影响配置页坐标映射。
        /// </summary>
        /// <param name="image">将作为测量结果背景显示的 HALCON 图像；模板测试使用工具模板图，检测照片使用临时测试图。</param>
        /// <param name="hwindow">主界面 FaraVision/AOI 结果窗口，当前由主窗口初始化为 HWindow4。</param>
        /// <returns>true 表示目标窗口已显示当前图像，可继续绘制测量层；false 表示窗口或图像不可用。</returns>
        public bool PrepareDimensionResultDisplay(HObject image, HWindow hwindow)
        {
            try
            {
                if (image == null || hwindow == null)
                {
                    return false;
                }

                HTuple width, height;
                HOperatorSet.GetImageSize(image, out width, out height);
                if (width == null || height == null || width.Length <= 0 || height.Length <= 0 || width.I <= 0 || height.I <= 0)
                {
                    return false;
                }

                hwindow.ClearWindow();
                hwindow.DispObj(image);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"尺寸测量结果画面准备失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 尺寸测量核心方法，根据测量类型调用相应的子方法。
        /// redraw 为 true 时只在传入的 HALCON 窗口上绘制拟合线、距离线和调试图层；
        /// 调用方负责在需要独立结果画面时先清空窗口并显示当前图像，避免在线多工具流程中途擦除同一张图上的其他结果。
        /// </summary>
        /// <param name="image">输入图像；作为 Metrology 找边和尺寸计算的来源。</param>
        /// <param name="tool">尺寸测量工具配置，包含 ROI、校准系数、测量类型、调试显示和 Metrology 参数。</param>
        /// <param name="hwindow">HALCON 显示窗口；redraw 为 true 时接收测量图层，但本方法不负责清屏或铺底图。</param>
        /// <param name="redraw">true 表示绘制测量图层；false 表示只计算尺寸，不更新窗口显示。</param>
        /// <param name="calibrationMode">校准模式；true 返回原始像素距离用于计算 um/pixel，false 返回毫米值用于判定。</param>
        /// <returns>校准模式返回像素值，正常模式返回毫米值；失败时通过异常向调用方报告。</returns>
        public double MeasureDimension(HObject image, ToolModel tool, HWindow hwindow, bool redraw = true, bool calibrationMode = false, ROI measureObject1Override = null, ROI measureObject2Override = null)
        {
            try
            {
                double result = -1;
                ROI measureRoi1 = measureObject1Override ?? tool.MeasureObject1ROI;
                ROI measureRoi2 = measureObject2Override ?? tool.MeasureObject2ROI;

                // 验证基本参数（校准模式下跳过DimensionK验证）
                if (!calibrationMode)
                {
                    if (tool.DimensionK <= 0 || (tool.DimensionK == 1000 && tool.CalibrationRealSize == 0))
                    {
                        throw new Exception("世界坐标未校准：请先进行校准");
                    }
                }

                switch (tool.MeasureType)
                {
                    case DimensionMeasureType.直线到直线:
                        result = MeasureLineToLine(image, tool, hwindow, redraw, measureRoi1, measureRoi2);
                        break;
                    case DimensionMeasureType.直线到圆心:
                        result = MeasureLineToCircle(image, tool, hwindow, redraw, measureRoi1, measureRoi2);
                        break;
                    case DimensionMeasureType.圆心到圆心:
                        result = MeasureCircleToCircle(image, tool, hwindow, redraw, measureRoi1, measureRoi2);
                        break;
                }

                // 运行态追溯：记录本次算法输出的原始像素测量值（单位：px，失败为-1）
                tool.LastMeasurePixelValue = result;

                // 校准模式：返回原始像素值
                if (calibrationMode)
                {
                    return result; // 返回像素值，用于计算DimensionK
                }

                // 正常测量模式：转换为实际尺寸（mm）
                // DimensionK的单位是um/pixel，需要转换为mm/pixel
                if (result > 0 && tool.DimensionK > 0)
                {
                    return result * tool.DimensionK / 1000.0; // 转换为mm
                }

                return result;
            }
            catch (Exception e)
            {
                // 运行态追溯：异常时明确为失败值，避免残留导致诊断误判
                if (tool != null)
                {
                    tool.LastMeasurePixelValue = -1;
                }
                // 重新抛出异常，让UI层显示详细错误信息
                throw new Exception($"测量失败: {e.Message}", e);
            }
        }

        /// <summary>
        /// 校验测量 ROI 是否具备进入尺寸测量流程的基本几何条件。
        /// 该校验在找边、预览绘制和报警判定前执行；线段 ROI 必须达到最小像素长度，
        /// 以避免误触或近似点输入进入 Metrology 后产生方向不稳定的测量结果。
        /// </summary>
        /// <param name="roi">工具配置界面保存的测量 ROI；坐标单位为像素，可为空。</param>
        /// <returns>true 表示 ROI 可进入测量流程；false 表示应提示用户重新绘制或补齐 ROI。</returns>
        private bool IsROIValid(ROI roi)
        {
            if (roi == null)
            {
                return false;
            }

            // 根据ROI类型选择验证逻辑
            if (roi.Type == ROIType.Circle)
            {
                // 圆形ROI：检查半径是否大于0
                return roi.CircleRadius > 0;
            }
            else if (roi.Type == ROIType.Line)
            {
                // 【规则说明】线段 ROI 过短时：
                // - 方向向量难以稳定估计（近似“点”），会放大后续距离计算的误差；
                // - 对于可视化与报警而言，短线更像是“误触”而不是有效测量输入。
                // 因此设置最小像素长度门槛，把无效输入提前挡掉，减少“看起来能测但结果不可信”的工况。
                return CalculateDistance(roi.Row1, roi.Col1, roi.Row2, roi.Col2) >= MinLineRoiLengthPixels;
            }
            else
            {
                // 矩形/线段ROI：检查坐标是否相同（避免区域太小）
                return !(roi.Row1 == roi.Row2 && roi.Col1 == roi.Col2);
            }
        }

        /// <summary>
        /// 执行直线到直线尺寸测量。
        /// 该流程先通过 Metrology 获取两条边缘拟合线，再按工具配置选择默认测距或计算线测距口径；
        /// 返回值始终为像素距离，毫米换算和报警判定由外层尺寸测量流程继续处理。
        /// </summary>
        /// <param name="image">当前待测图像，作为 Metrology 找边输入。</param>
        /// <param name="tool">当前尺寸测量工具配置，包含 ROI、Metrology 参数、调试显示和计算线延长系数。</param>
        /// <param name="hwindow">用于预览绘制拟合线、计算线和距离线的 HALCON 窗口。</param>
        /// <param name="redraw">true 表示同步刷新预览画面；false 表示只计算距离，不更新窗口显示。</param>
        /// <returns>两条测量边之间的像素距离；后续流程负责按校准系数换算为毫米。</returns>
        /// <exception cref="Exception">ROI 无效或边缘检测失败时抛出，调用方负责转为操作者可见提示。</exception>
        private double MeasureLineToLine(HObject image, ToolModel tool, HWindow hwindow, bool redraw, ROI measureObject1Roi = null, ROI measureObject2Roi = null)
        {
            try
            {
                ROI roi1 = measureObject1Roi ?? tool.MeasureObject1ROI;
                ROI roi2 = measureObject2Roi ?? tool.MeasureObject2ROI;

                // 验证ROI有效性（支持矩形、线段和圆形ROI）
                if (!IsROIValid(roi1))
                {
                    throw new Exception("测量对象1 ROI无效：ROI区域太小或未正确绘制");
                }
                if (!IsROIValid(roi2))
                {
                    throw new Exception("测量对象2 ROI无效：ROI区域太小或未正确绘制");
                }

                // 使用Metrology模型检测第一条直线
                double line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd;
                HTuple edge1Rows, edge1Cols;
                if (!DetectEdgeWithMetrology(image, tool, roi1,
                    out line1RowBegin, out line1ColBegin, out line1RowEnd, out line1ColEnd,
                    out edge1Rows, out edge1Cols, out _))
                {
                    throw new Exception("测量对象1边缘检测失败：请检查ROI位置、Metrology参数设置");
                }

                // 使用Metrology模型检测第二条直线
                double line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd;
                HTuple edge2Rows, edge2Cols;
                if (!DetectEdgeWithMetrology(image, tool, roi2,
                    out line2RowBegin, out line2ColBegin, out line2RowEnd, out line2ColEnd,
                    out edge2Rows, out edge2Cols, out _))
                {
                    throw new Exception("测量对象2边缘检测失败：请检查ROI位置、Metrology参数设置");
                }

                LineSegment2D fittedLine1 = new LineSegment2D(line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd);
                LineSegment2D fittedLine2 = new LineSegment2D(line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd);

                double lineDistanceExtendRatio = NormalizeLineDistanceExtendRatio(tool.LineDistanceExtendRatio);
                bool useCalculationLine = lineDistanceExtendRatio > 1.0 + LineDistanceRatioEpsilon;

                LineSegment2D calculationLine1 = null;
                LineSegment2D calculationLine2 = null;
                LineDistanceResult distanceResult;

                if (useCalculationLine)
                {
                    // 【使用场景】当需要“更稳定的距离定义/更清晰的红线展示”时，使用“计算线”参与距离计算。
                    // 【规则说明】
                    // - 拟合线（cyan）来自真实找边结果，代表“算法识别到的边缘”；
                    // - 计算线（yellow, 可选）在 ROI 主方向上按倍数做延长，仅用于“距离如何定义”；
                    // - 这样可以在两线近似平行、ROI 较短或端点不对齐时，让距离更接近“垂直距离”的直觉表达。
                    calculationLine1 = BuildCalculationLineFromRoi(fittedLine1, roi1, lineDistanceExtendRatio);
                    calculationLine2 = BuildCalculationLineFromRoi(fittedLine2, roi2, lineDistanceExtendRatio);
                    distanceResult = CalculateDistanceBetweenCalculationLines(calculationLine1, calculationLine2);
                }
                else
                {
                    // Ratio=1.0 时不启用计算线延长，但距离值和红线端点仍保持同源。
                    distanceResult = CalculateShortestDistanceBetweenSegments(fittedLine1, fittedLine2, LineDistanceBranchLegacy);
                }

                writeLog($"[尺寸测量] 分支={distanceResult.Branch}, Ratio={lineDistanceExtendRatio:F1}, 距离={distanceResult.Distance:F3}px", false);

                // 可视化绘制
                if (redraw)
                {
                    // 绘制拟合直线（青色）
                    hwindow.SetLineWidth(2);
                    hwindow.SetColor("cyan");
                    hwindow.DispLine(line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd);
                    hwindow.DispLine(line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd);

                    if (tool.ShowMetrologyDebugInfo && useCalculationLine)
                    {
                        hwindow.SetColor("yellow");
                        hwindow.SetLineStyle(new HTuple(new int[] { 6, 4 }));
                        hwindow.DispLine(calculationLine1.RowBegin, calculationLine1.ColBegin, calculationLine1.RowEnd, calculationLine1.ColEnd);
                        hwindow.DispLine(calculationLine2.RowBegin, calculationLine2.ColBegin, calculationLine2.RowEnd, calculationLine2.ColEnd);
                        hwindow.SetLineStyle(new HTuple());
                    }

                    // 绘制测量距离线（红色虚线）- 连接两条线段的最近点
                    hwindow.SetColor("red");
                    hwindow.SetLineStyle(new HTuple(new int[] { 10, 5 })); // 虚线样式
                    hwindow.DispLine(distanceResult.Row1, distanceResult.Col1, distanceResult.Row2, distanceResult.Col2);
                    hwindow.SetLineStyle(new HTuple()); // 恢复实线

                    // 如果启用调试信息，绘制边缘点、卡尺位置和最近点标记
                    if (tool.ShowMetrologyDebugInfo)
                    {
                        // 绘制边缘点（春绿色十字标记）
                        hwindow.SetColor("spring green");
                        hwindow.SetLineWidth(1);
                        for (int i = 0; i < edge1Rows.Length; i++)
                        {
                            double row = edge1Rows[i].D;
                            double col = edge1Cols[i].D;
                            hwindow.DispCross(row, col, 6, 0); // 绘制十字
                        }
                        for (int i = 0; i < edge2Rows.Length; i++)
                        {
                            double row = edge2Rows[i].D;
                            double col = edge2Cols[i].D;
                            hwindow.DispCross(row, col, 6, 0);
                        }

                        // 绘制最近点标记（橙色圆圈）
                        hwindow.SetColor("orange");
                        hwindow.SetLineWidth(2);
                        hwindow.DispCircle(distanceResult.Row1, distanceResult.Col1, 8);
                        hwindow.DispCircle(distanceResult.Row2, distanceResult.Col2, 8);

                        // 显示距离验证信息（白色文字）
                        hwindow.SetColor("white");
                        string debugInfo = $"分支: {distanceResult.Branch}\nRatio: {lineDistanceExtendRatio:F1}\nDistance: {distanceResult.Distance:F2}px";
                        hwindow.DispText(debugInfo, "image", distanceResult.Row1 - 30, distanceResult.Col1, "white", "box", "false");
                    }
                }

                return distanceResult.Distance;
            }
            catch (Exception ex)
            {
                // 记录详细错误信息，便于调试
                System.Diagnostics.Debug.WriteLine($"MeasureLineToLine错误: {ex.Message}");
                throw; // 重新抛出异常，让上层捕获并显示
            }
        }

        /// <summary>
        /// 直线到圆心距离测量（使用统一Metrology框架）
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型</param>
        /// <param name="hwindow">HALCON窗口</param>
        /// <param name="redraw">是否重绘</param>
        /// <returns>测量距离（像素），失败抛出异常</returns>
        private double MeasureLineToCircle(HObject image, ToolModel tool, HWindow hwindow, bool redraw, ROI measureObject1Roi = null, ROI measureObject2Roi = null)
        {
            try
            {
                ROI roi1 = measureObject1Roi ?? tool.MeasureObject1ROI;
                ROI roi2 = measureObject2Roi ?? tool.MeasureObject2ROI;

                // 验证ROI有效性（支持矩形、线段和圆形ROI）
                if (!IsROIValid(roi1))
                {
                    throw new Exception("测量对象1（直线）ROI无效：ROI区域太小或未正确绘制");
                }
                if (!IsROIValid(roi2))
                {
                    throw new Exception("测量对象2（圆）ROI无效：ROI区域太小或未正确绘制");
                }

                // 1. 使用Metrology检测直线
                double lineRowBegin, lineColBegin, lineRowEnd, lineColEnd;
                HTuple lineEdgeRows, lineEdgeCols;
                if (!DetectEdgeWithMetrology(image, tool, roi1,
                    out lineRowBegin, out lineColBegin, out lineRowEnd, out lineColEnd,
                    out lineEdgeRows, out lineEdgeCols, out _))
                {
                    throw new Exception("直线检测失败：请检查ROI位置、Metrology参数设置");
                }

                // 2. 使用Metrology检测圆心
                double centerRow, centerCol, radiusValue;
                HTuple circleEdgeRows, circleEdgeCols;
                if (!DetectCircleWithMetrology(image, tool, roi2,
                    out centerRow, out centerCol, out radiusValue,
                    out circleEdgeRows, out circleEdgeCols))
                {
                    throw new Exception("圆心检测失败：请检查ROI位置、Metrology参数设置");
                }

                // 3. 计算圆心到直线的距离
                // 使用HALCON的DistancePl算子（点到直线的距离）
                HTuple distance;
                HOperatorSet.DistancePl(
                    centerRow, centerCol,           // 圆心坐标
                    lineRowBegin, lineColBegin,     // 直线起点
                    lineRowEnd, lineColEnd,         // 直线终点
                    out distance);

                // 4. 可视化绘制
                if (redraw)
                {
                    // 绘制拟合直线（青色）
                    hwindow.SetLineWidth(2);
                    hwindow.SetColor("cyan");
                    hwindow.DispLine(lineRowBegin, lineColBegin, lineRowEnd, lineColEnd);

                    // 绘制拟合圆（青色）
                    hwindow.DispCircle(centerRow, centerCol, radiusValue);

                    // 计算圆心在直线上的投影点（垂足）
                    HTuple projRow, projCol;
                    HOperatorSet.ProjectionPl(centerRow, centerCol,
                                             lineRowBegin, lineColBegin,
                                             lineRowEnd, lineColEnd,
                                             out projRow, out projCol);

                    // 绘制测量距离线（红色虚线）- 从圆心到垂足
                    hwindow.SetColor("red");
                    hwindow.SetLineStyle(new HTuple(new int[] { 10, 5 })); // 虚线样式
                    hwindow.DispLine(centerRow, centerCol, projRow.D, projCol.D);
                    hwindow.SetLineStyle(new HTuple()); // 恢复实线

                    // 如果启用调试信息，绘制边缘点和标记
                    if (tool.ShowMetrologyDebugInfo)
                    {
                        // 绘制直线边缘点（春绿色十字标记）
                        hwindow.SetColor("spring green");
                        hwindow.SetLineWidth(1);
                        for (int i = 0; i < lineEdgeRows.Length; i++)
                        {
                            double row = lineEdgeRows[i].D;
                            double col = lineEdgeCols[i].D;
                            hwindow.DispCross(row, col, 6, 0);
                        }

                        // 绘制圆边缘点（春绿色十字标记）
                        for (int i = 0; i < circleEdgeRows.Length; i++)
                        {
                            double row = circleEdgeRows[i].D;
                            double col = circleEdgeCols[i].D;
                            hwindow.DispCross(row, col, 6, 0);
                        }

                        // 绘制圆心标记（橙色圆圈）
                        hwindow.SetColor("orange");
                        hwindow.SetLineWidth(2);
                        hwindow.DispCircle(centerRow, centerCol, 8);

                        // 绘制垂足标记（橙色圆圈）
                        hwindow.DispCircle(projRow.D, projCol.D, 8);

                        // 显示距离数值（白色文字）
                        hwindow.SetColor("white");
                        string debugInfo = $"Distance: {distance.D:F2}px";
                        hwindow.DispText(debugInfo, "image", centerRow - 30, centerCol, "white", "box", "false");
                    }
                }

                return distance.D;
            }
            catch (Exception ex)
            {
                // 记录详细错误信息，便于调试
                System.Diagnostics.Debug.WriteLine($"MeasureLineToCircle错误: {ex.Message}");
                throw; // 重新抛出异常，让上层捕获并显示
            }
        }

        /// <summary>
        /// 圆心到圆心距离测量（统一Metrology框架）
        /// </summary>
        private double MeasureCircleToCircle(HObject image, ToolModel tool, HWindow hwindow, bool redraw, ROI measureObject1Roi = null, ROI measureObject2Roi = null)
        {
            try
            {
                ROI roi1 = measureObject1Roi ?? tool.MeasureObject1ROI;
                ROI roi2 = measureObject2Roi ?? tool.MeasureObject2ROI;

                // 验证ROI有效性（支持矩形、线段和圆形ROI）
                if (!IsROIValid(roi1))
                {
                    throw new Exception("测量对象1（圆）ROI无效：ROI区域太小或未正确绘制");
                }
                if (!IsROIValid(roi2))
                {
                    throw new Exception("测量对象2（圆）ROI无效：ROI区域太小或未正确绘制");
                }

                // 1. 使用Metrology检测第一个圆心
                double center1Row, center1Col, radius1;
                HTuple edge1Rows, edge1Cols;
                if (!DetectCircleWithMetrology(image, tool, roi1,
                    out center1Row, out center1Col, out radius1,
                    out edge1Rows, out edge1Cols))
                {
                    throw new Exception("第一个圆心检测失败：请检查ROI位置、Metrology参数设置");
                }

                // 2. 使用Metrology检测第二个圆心
                double center2Row, center2Col, radius2;
                HTuple edge2Rows, edge2Cols;
                if (!DetectCircleWithMetrology(image, tool, roi2,
                    out center2Row, out center2Col, out radius2,
                    out edge2Rows, out edge2Cols))
                {
                    throw new Exception("第二个圆心检测失败：请检查ROI位置、Metrology参数设置");
                }

                // 3. 计算两圆心间距离
                HTuple distance;
                HOperatorSet.DistancePp(
                    center1Row, center1Col,
                    center2Row, center2Col,
                    out distance);

                // 4. 可视化绘制
                if (redraw)
                {
                    // 绘制两个拟合圆（青色）
                    hwindow.SetLineWidth(2);
                    hwindow.SetColor("cyan");
                    hwindow.DispCircle(center1Row, center1Col, radius1);
                    hwindow.DispCircle(center2Row, center2Col, radius2);

                    // 绘制圆心连线（红色虚线）
                    hwindow.SetColor("red");
                    hwindow.SetLineStyle(new HTuple(new int[] { 10, 5 })); // 虚线样式
                    hwindow.DispLine(center1Row, center1Col, center2Row, center2Col);
                    hwindow.SetLineStyle(new HTuple()); // 恢复实线

                    // 如果启用调试信息，绘制边缘点和标记
                    if (tool.ShowMetrologyDebugInfo)
                    {
                        // 绘制第一个圆的边缘点（春绿色十字标记）
                        hwindow.SetColor("spring green");
                        hwindow.SetLineWidth(1);
                        for (int i = 0; i < edge1Rows.Length; i++)
                        {
                            double row = edge1Rows[i].D;
                            double col = edge1Cols[i].D;
                            hwindow.DispCross(row, col, 6, 0);
                        }

                        // 绘制第二个圆的边缘点（春绿色十字标记）
                        for (int i = 0; i < edge2Rows.Length; i++)
                        {
                            double row = edge2Rows[i].D;
                            double col = edge2Cols[i].D;
                            hwindow.DispCross(row, col, 6, 0);
                        }

                        // 绘制两个圆心标记（橙色圆圈）
                        hwindow.SetColor("orange");
                        hwindow.SetLineWidth(2);
                        hwindow.DispCircle(center1Row, center1Col, 8);
                        hwindow.DispCircle(center2Row, center2Col, 8);

                        // 显示距离数值（白色文字）
                        hwindow.SetColor("white");
                        double midRow = (center1Row + center2Row) / 2.0;
                        double midCol = (center1Col + center2Col) / 2.0;
                        string distanceInfo = $"距离: {distance.D:F2}px";
                        hwindow.DispText(distanceInfo, "image", midRow - 20, midCol, "white", "box", "false");
                    }
                }

                return distance.D;
            }
            catch (Exception ex)
            {
                // 记录详细错误信息，便于调试
                System.Diagnostics.Debug.WriteLine($"MeasureCircleToCircle错误: {ex.Message}");
                throw; // 重新抛出异常，让上层捕获并显示
            }
        }

        /// <summary>
        /// 使用卡尺工具检测边缘（用于直线检测）
        /// </summary>
        private bool DetectEdgeWithCaliper(HObject image, ToolModel tool, ROI roi, out HTuple row, out HTuple col)
        {
            row = null;
            col = null;
            HTuple measureHandle = null;

            try
            {
                // 计算ROI中心点和角度
                double centerRow = (roi.Row1 + roi.Row2) / 2.0;
                double centerCol = (roi.Col1 + roi.Col2) / 2.0;

                // 如果未设置角度，根据ROI自动计算
                double angle = tool.CaliperAngle;
                if (angle == 0)
                {
                    double deltaRow = roi.Row2 - roi.Row1;
                    double deltaCol = roi.Col2 - roi.Col1;
                    angle = Math.Atan2(deltaRow, deltaCol) * 180.0 / Math.PI;
                }

                // 获取图像尺寸
                HTuple width, height;
                HOperatorSet.GetImageSize(image, out width, out height);

                // 生成卡尺工具
                HOperatorSet.GenMeasureRectangle2(
                    centerRow,
                    centerCol,
                    angle * Math.PI / 180.0,
                    tool.CaliperLength / 2.0,
                    tool.CaliperWidth / 2.0,
                    width,
                    height,
                    tool.SubPixelAccuracy ? "bicubic" : "nearest_neighbor",
                    out measureHandle
                );

                // 检测边缘
                HTuple rowEdge, columnEdge, amplitude, distance;
                HOperatorSet.MeasurePos(
                    image,
                    measureHandle,
                    1.0, // sigma
                    tool.EdgeThreshold,
                    tool.EdgePolarity,
                    tool.EdgeSelection,
                    out rowEdge,
                    out columnEdge,
                    out amplitude,
                    out distance
                );

                // 释放资源
                HOperatorSet.CloseMeasure(measureHandle);
                measureHandle = null;

                // 返回边缘位置（如果检测到边缘，返回第一个）
                if (rowEdge.Length > 0)
                {
                    row = rowEdge[0];
                    col = columnEdge[0];
                    return true;
                }

                // 边缘检测失败，返回false
                return false;
            }
            catch (Exception ex)
            {
                if (measureHandle != null)
                {
                    try { HOperatorSet.CloseMeasure(measureHandle); } catch { }
                }
                return false;
            }
        }

        private const double LineDistanceRatioEpsilon = 1e-9;
        private const double MinLineRoiLengthPixels = 5.0;
        private const double ParallelAngleThresholdDegrees = 5.0;
        private const double GeometryEpsilon = 1e-9;
        private const string LineDistanceBranchLegacy = "旧逻辑";
        private const string LineDistanceBranchSegment = "线段最短距";
        private const string LineDistanceBranchParallel = "平行垂距";

        /// <summary>
        /// 表示一次直线测距中参与距离计算或画面展示的二维线段。
        /// 坐标单位为 HALCON 图像坐标像素，Row/Col 顺序与现有 ROI、拟合线和绘图接口保持一致。
        /// </summary>
        private class LineSegment2D
        {
            public LineSegment2D(double rowBegin, double colBegin, double rowEnd, double colEnd)
            {
                RowBegin = rowBegin;
                ColBegin = colBegin;
                RowEnd = rowEnd;
                ColEnd = colEnd;
            }

            public double RowBegin { get; private set; }
            public double ColBegin { get; private set; }
            public double RowEnd { get; private set; }
            public double ColEnd { get; private set; }
        }

        /// <summary>
        /// 封装直线到直线测量的距离结果与红色距离线端点。
        /// Branch 用于调试画面和测量日志区分默认测距、计算线最短距和平行垂距口径；
        /// Distance 与端点坐标均为像素单位，不直接写入工程标定参数。
        /// </summary>
        private class LineDistanceResult
        {
            public LineDistanceResult(string branch, double distance, double row1, double col1, double row2, double col2)
            {
                Branch = branch;
                Distance = distance;
                Row1 = row1;
                Col1 = col1;
                Row2 = row2;
                Col2 = col2;
            }

            public string Branch { get; private set; }
            public double Distance { get; private set; }
            public double Row1 { get; private set; }
            public double Col1 { get; private set; }
            public double Row2 { get; private set; }
            public double Col2 { get; private set; }
        }

        /// <summary>
        /// 归一化直线测距延长系数。
        /// 非法配置按 1.0 处理，保证已保存工程即使出现空值、无穷大或非正数，也回到默认测距口径，
        /// 不影响 Metrology 找边范围、ROI 本身或工程 XML 中其他测量参数。
        /// </summary>
        /// <param name="ratio">来自工具配置的线段距离计算延长系数；单位为倍数，1.0 表示默认测距口径。</param>
        /// <returns>可用于直线测距分支判断的倍数；返回 1.0 时保持兼容口径。</returns>
        private double NormalizeLineDistanceExtendRatio(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
            {
                return 1.0;
            }

            return Math.Max(1.0, Math.Min(3.0, ratio));
        }

        /// <summary>
        /// 根据 ROI 主方向与拟合直线生成用于距离定义的计算线。
        /// 计算线只参与直线到直线的像素距离和调试画面红/黄线展示，不扩大 Metrology 卡尺找边范围，
        /// 也不改变拟合线本身；ROI 过短或拟合线无方向时抛出业务提示，避免继续产出不可信测量值。
        /// </summary>
        /// <param name="fittedLine">Metrology 找边得到的拟合线，提供真实边缘方向；坐标单位为像素。</param>
        /// <param name="roi">用户在工具配置界面绘制的线段 ROI，决定计算线中心与基础长度；坐标单位为像素。</param>
        /// <param name="ratio">线段距离计算延长系数；1.0 以上按 ROI 长度等比例延长计算线。</param>
        /// <returns>沿拟合线方向生成的计算线；坐标单位为像素。</returns>
        /// <exception cref="Exception">ROI 或拟合线过短，无法形成稳定测距方向时抛出。</exception>
        private LineSegment2D BuildCalculationLineFromRoi(LineSegment2D fittedLine, ROI roi, double ratio)
        {
            double roiLength = CalculateDistance(roi.Row1, roi.Col1, roi.Row2, roi.Col2);
            if (roiLength < MinLineRoiLengthPixels)
            {
                throw new Exception("ROI 太短，请重新绘制");
            }

            double dirRow, dirCol;
            if (!TryGetNormalizedDirection(fittedLine, out dirRow, out dirCol))
            {
                throw new Exception("拟合线段太短，无法计算距离");
            }

            double roiMidRow = (roi.Row1 + roi.Row2) / 2.0;
            double roiMidCol = (roi.Col1 + roi.Col2) / 2.0;
            double centerProjection = ProjectPointToAxis(
                roiMidRow,
                roiMidCol,
                fittedLine.RowBegin,
                fittedLine.ColBegin,
                dirRow,
                dirCol);

            double centerRow = fittedLine.RowBegin + centerProjection * dirRow;
            double centerCol = fittedLine.ColBegin + centerProjection * dirCol;
            double halfLength = roiLength * ratio / 2.0;

            return new LineSegment2D(
                centerRow - halfLength * dirRow,
                centerCol - halfLength * dirCol,
                centerRow + halfLength * dirRow,
                centerCol + halfLength * dirCol);
        }

        /// <summary>
        /// 按计算线测距口径生成直线到直线的距离结果。
        /// 近似平行且投影重叠时优先返回垂直距离，便于现场按两条边的间距理解红线；
        /// 其他角度或无重叠时回退到线段最短距离，避免非平行工况被强行解释为垂距。
        /// </summary>
        /// <param name="line1">测量对象1的计算线；坐标单位为像素。</param>
        /// <param name="line2">测量对象2的计算线；坐标单位为像素。</param>
        /// <returns>距离值、测距分支和红色距离线端点；距离单位为像素。</returns>
        private LineDistanceResult CalculateDistanceBetweenCalculationLines(LineSegment2D line1, LineSegment2D line2)
        {
            LineDistanceResult parallelResult;
            if (AreLinesNearlyParallel(line1, line2, ParallelAngleThresholdDegrees) &&
                TryCalculateParallelPerpendicularDistance(line1, line2, out parallelResult))
            {
                return parallelResult;
            }

            return CalculateShortestDistanceBetweenSegments(line1, line2, LineDistanceBranchSegment);
        }

        /// <summary>
        /// 判断两条计算线是否进入平行垂距口径。
        /// 角度阈值用于保护近似平行的母排边缘测距场景，超出阈值时继续使用线段最短距，
        /// 避免倾斜或交叉工况在预览和判定中被误读为平行间距。
        /// </summary>
        /// <param name="line1">测量对象1的计算线；坐标单位为像素。</param>
        /// <param name="line2">测量对象2的计算线；坐标单位为像素。</param>
        /// <param name="maxAngleDegrees">允许进入平行垂距口径的最大夹角；单位为度。</param>
        /// <returns>true 表示两线夹角在阈值内，可尝试按平行垂距计算；false 表示使用其他测距口径。</returns>
        private bool AreLinesNearlyParallel(LineSegment2D line1, LineSegment2D line2, double maxAngleDegrees)
        {
            double dir1Row, dir1Col, dir2Row, dir2Col;
            if (!TryGetNormalizedDirection(line1, out dir1Row, out dir1Col) ||
                !TryGetNormalizedDirection(line2, out dir2Row, out dir2Col))
            {
                return false;
            }

            double cosAngle = Math.Abs(Dot(dir1Row, dir1Col, dir2Row, dir2Col));
            cosAngle = Math.Max(0, Math.Min(1, cosAngle));
            double angleDegrees = Math.Acos(cosAngle) * 180.0 / Math.PI;
            return angleDegrees <= maxAngleDegrees;
        }

        /// <summary>
        /// 尝试计算近似平行计算线的垂直距离。
        /// 只有两条计算线在主方向投影存在重叠时才返回结果；无重叠时交由线段最短距处理，
        /// 保证红线端点仍落在有效计算线范围内，避免预览给出超出 ROI 语义的距离连接。
        /// </summary>
        /// <param name="line1">测量对象1的计算线；坐标单位为像素。</param>
        /// <param name="line2">测量对象2的计算线；坐标单位为像素。</param>
        /// <param name="result">成功时输出平行垂距结果，包含距离值和红线端点；距离单位为像素。</param>
        /// <returns>true 表示可按平行垂距口径返回；false 表示需要回退到线段最短距。</returns>
        private bool TryCalculateParallelPerpendicularDistance(LineSegment2D line1, LineSegment2D line2, out LineDistanceResult result)
        {
            result = null;

            double dir1Row, dir1Col, dir2Row, dir2Col;
            if (!TryGetNormalizedDirection(line1, out dir1Row, out dir1Col) ||
                !TryGetNormalizedDirection(line2, out dir2Row, out dir2Col))
            {
                return false;
            }

            double line1Start = ProjectPointToAxis(line1.RowBegin, line1.ColBegin, line1.RowBegin, line1.ColBegin, dir1Row, dir1Col);
            double line1End = ProjectPointToAxis(line1.RowEnd, line1.ColEnd, line1.RowBegin, line1.ColBegin, dir1Row, dir1Col);
            double line2Start = ProjectPointToAxis(line2.RowBegin, line2.ColBegin, line1.RowBegin, line1.ColBegin, dir1Row, dir1Col);
            double line2End = ProjectPointToAxis(line2.RowEnd, line2.ColEnd, line1.RowBegin, line1.ColBegin, dir1Row, dir1Col);

            double overlapStart = Math.Max(Math.Min(line1Start, line1End), Math.Min(line2Start, line2End));
            double overlapEnd = Math.Min(Math.Max(line1Start, line1End), Math.Max(line2Start, line2End));
            if (overlapEnd < overlapStart - GeometryEpsilon)
            {
                return false;
            }

            double overlapMid = (overlapStart + overlapEnd) / 2.0;
            double point1Row = line1.RowBegin + overlapMid * dir1Row;
            double point1Col = line1.ColBegin + overlapMid * dir1Col;

            double footProjection = ProjectPointToAxis(point1Row, point1Col, line2.RowBegin, line2.ColBegin, dir2Row, dir2Col);
            double point2Row = line2.RowBegin + footProjection * dir2Row;
            double point2Col = line2.ColBegin + footProjection * dir2Col;
            double distance = CalculateDistance(point1Row, point1Col, point2Row, point2Col);

            result = new LineDistanceResult(
                LineDistanceBranchParallel,
                distance,
                point1Row,
                point1Col,
                point2Row,
                point2Col);
            return true;
        }

        /// <summary>
        /// 计算两条线段在指定业务分支下的最短距离。
        /// 相交或重叠时返回 0 像素，并把红线端点落在交点或重叠中点；否则复用现有最近点计算，
        /// 使调试画面中的红线与最终像素距离保持一致。
        /// </summary>
        /// <param name="line1">测量对象1的线段；坐标单位为像素。</param>
        /// <param name="line2">测量对象2的线段；坐标单位为像素。</param>
        /// <param name="branch">写入调试信息的测距分支名称，用于区分默认测距和计算线最短距。</param>
        /// <returns>距离值、测距分支和红色距离线端点；距离单位为像素。</returns>
        private LineDistanceResult CalculateShortestDistanceBetweenSegments(LineSegment2D line1, LineSegment2D line2, string branch)
        {
            double intersectionRow, intersectionCol;
            if (TryGetSegmentIntersection(line1, line2, out intersectionRow, out intersectionCol))
            {
                return new LineDistanceResult(branch, 0, intersectionRow, intersectionCol, intersectionRow, intersectionCol);
            }

            double closestRow1, closestCol1, closestRow2, closestCol2;
            CalculateClosestPointsBetweenSegments(
                line1.RowBegin, line1.ColBegin, line1.RowEnd, line1.ColEnd,
                line2.RowBegin, line2.ColBegin, line2.RowEnd, line2.ColEnd,
                out closestRow1, out closestCol1,
                out closestRow2, out closestCol2);

            double distance = CalculateDistance(closestRow1, closestCol1, closestRow2, closestCol2);
            return new LineDistanceResult(branch, distance, closestRow1, closestCol1, closestRow2, closestCol2);
        }

        /// <summary>
        /// 尝试求两条线段的交点或重叠中点。
        /// 该判断用于测距前置分流，确保相交边缘不会继续显示非零距离；输出坐标仅用于距离结果和预览红线，
        /// 不参与 Metrology 找边、工程参数保存或毫米标定换算。
        /// </summary>
        /// <param name="line1">测量对象1的线段；坐标单位为像素。</param>
        /// <param name="line2">测量对象2的线段；坐标单位为像素。</param>
        /// <param name="row">成功时输出交点或重叠中点的 Row 坐标；单位为像素。</param>
        /// <param name="col">成功时输出交点或重叠中点的 Col 坐标；单位为像素。</param>
        /// <returns>true 表示两线段相交或重叠；false 表示需要继续计算最近点距离。</returns>
        private bool TryGetSegmentIntersection(LineSegment2D line1, LineSegment2D line2, out double row, out double col)
        {
            row = 0;
            col = 0;

            double pRow = line1.RowBegin;
            double pCol = line1.ColBegin;
            double rRow = line1.RowEnd - line1.RowBegin;
            double rCol = line1.ColEnd - line1.ColBegin;

            double qRow = line2.RowBegin;
            double qCol = line2.ColBegin;
            double sRow = line2.RowEnd - line2.RowBegin;
            double sCol = line2.ColEnd - line2.ColBegin;

            double denominator = Cross(rRow, rCol, sRow, sCol);
            double qmpRow = qRow - pRow;
            double qmpCol = qCol - pCol;

            if (Math.Abs(denominator) < GeometryEpsilon)
            {
                if (Math.Abs(Cross(qmpRow, qmpCol, rRow, rCol)) > GeometryEpsilon)
                {
                    return false;
                }

                double rLengthSquared = Dot(rRow, rCol, rRow, rCol);
                if (rLengthSquared < GeometryEpsilon)
                {
                    return false;
                }

                double t0 = Dot(qmpRow, qmpCol, rRow, rCol) / rLengthSquared;
                double t1 = Dot(qmpRow + sRow, qmpCol + sCol, rRow, rCol) / rLengthSquared;
                double overlapStart = Math.Max(0, Math.Min(t0, t1));
                double overlapEnd = Math.Min(1, Math.Max(t0, t1));
                if (overlapEnd < overlapStart - GeometryEpsilon)
                {
                    return false;
                }

                double overlapMid = (overlapStart + overlapEnd) / 2.0;
                row = pRow + overlapMid * rRow;
                col = pCol + overlapMid * rCol;
                return true;
            }

            double t = Cross(qmpRow, qmpCol, sRow, sCol) / denominator;
            double u = Cross(qmpRow, qmpCol, rRow, rCol) / denominator;
            if (t >= -GeometryEpsilon && t <= 1 + GeometryEpsilon &&
                u >= -GeometryEpsilon && u <= 1 + GeometryEpsilon)
            {
                t = Math.Max(0, Math.Min(1, t));
                row = pRow + t * rRow;
                col = pCol + t * rCol;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取线段在图像坐标系中的单位方向向量。
        /// 返回 false 时表示线段长度不足以支撑 ROI 主方向、计算线延长或平行角度判断，
        /// 调用方应回退或给出业务提示，避免继续产生不稳定测距结果。
        /// </summary>
        /// <param name="line">待取方向的线段；坐标单位为像素。</param>
        /// <param name="dirRow">成功时输出 Row 方向分量。</param>
        /// <param name="dirCol">成功时输出 Col 方向分量。</param>
        /// <returns>true 表示方向向量有效；false 表示线段过短。</returns>
        private bool TryGetNormalizedDirection(LineSegment2D line, out double dirRow, out double dirCol)
        {
            double length = CalculateDistance(line.RowBegin, line.ColBegin, line.RowEnd, line.ColEnd);
            if (length < GeometryEpsilon)
            {
                dirRow = 0;
                dirCol = 0;
                return false;
            }

            dirRow = (line.RowEnd - line.RowBegin) / length;
            dirCol = (line.ColEnd - line.ColBegin) / length;
            return true;
        }

        /// <summary>
        /// 将图像坐标点投影到指定方向轴上。
        /// 投影值用于计算线中心、平行重叠区间和垂足位置，单位为像素；该 helper 只服务测距几何，
        /// 不改变 ROI、找边结果或工程持久化数据。
        /// </summary>
        /// <param name="row">待投影点的 Row 坐标；单位为像素。</param>
        /// <param name="col">待投影点的 Col 坐标；单位为像素。</param>
        /// <param name="originRow">投影轴原点的 Row 坐标；单位为像素。</param>
        /// <param name="originCol">投影轴原点的 Col 坐标；单位为像素。</param>
        /// <param name="dirRow">投影轴单位方向的 Row 分量。</param>
        /// <param name="dirCol">投影轴单位方向的 Col 分量。</param>
        /// <returns>点到投影轴原点的有符号距离；单位为像素。</returns>
        private double ProjectPointToAxis(double row, double col, double originRow, double originCol, double dirRow, double dirCol)
        {
            return Dot(row - originRow, col - originCol, dirRow, dirCol);
        }

        private double Dot(double row1, double col1, double row2, double col2)
        {
            return row1 * row2 + col1 * col2;
        }

        private double Cross(double row1, double col1, double row2, double col2)
        {
            return col1 * row2 - row1 * col2;
        }

        /// <summary>
        /// 使用 HALCON DistanceSs 计算两条线段的默认最短距离。
        /// 该口径服务既有直线到直线测量工程，返回像素距离；计算线延长系数为 1.0 时继续使用该口径，
        /// 以避免已标定工程的判定基准被配置默认值改变。
        /// </summary>
        /// <param name="line1RowBegin">线段1起点 Row 坐标；单位为像素。</param>
        /// <param name="line1ColBegin">线段1起点 Col 坐标；单位为像素。</param>
        /// <param name="line1RowEnd">线段1终点 Row 坐标；单位为像素。</param>
        /// <param name="line1ColEnd">线段1终点 Col 坐标；单位为像素。</param>
        /// <param name="line2RowBegin">线段2起点 Row 坐标；单位为像素。</param>
        /// <param name="line2ColBegin">线段2起点 Col 坐标；单位为像素。</param>
        /// <param name="line2RowEnd">线段2终点 Row 坐标；单位为像素。</param>
        /// <param name="line2ColEnd">线段2终点 Col 坐标；单位为像素。</param>
        /// <returns>两条线段间的最短距离；单位为像素。</returns>
        private double CalculateDistanceBetweenLines(
            double line1RowBegin, double line1ColBegin, double line1RowEnd, double line1ColEnd,
            double line2RowBegin, double line2ColBegin, double line2RowEnd, double line2ColEnd)
        {
            // 使用HALCON的DistanceSs算子计算两条线段之间的最短距离
            // 该算子会精确计算线段间的真实最短距离，处理所有情况（平行、相交、斜线、端点距离等）
            HTuple distanceMin, distanceMax;
            HOperatorSet.DistanceSs(
                line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,  // 线段1的起点和终点
                line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd,  // 线段2的起点和终点
                out distanceMin,  // 最短距离
                out distanceMax   // 最大距离（未使用）
            );

            return distanceMin.D;  // 返回最短距离
        }

        /// <summary>
        /// 计算两条线段之间最近点对的坐标。
        /// 该结果用于默认测距口径的红色距离线展示，使预览连线与 DistanceSs 返回的像素距离保持一致；
        /// 不影响 Metrology 找边结果、工程配置保存或毫米标定系数。
        /// </summary>
        /// <param name="line1RowBegin">线段1起点 Row 坐标；单位为像素。</param>
        /// <param name="line1ColBegin">线段1起点 Col 坐标；单位为像素。</param>
        /// <param name="line1RowEnd">线段1终点 Row 坐标；单位为像素。</param>
        /// <param name="line1ColEnd">线段1终点 Col 坐标；单位为像素。</param>
        /// <param name="line2RowBegin">线段2起点 Row 坐标；单位为像素。</param>
        /// <param name="line2ColBegin">线段2起点 Col 坐标；单位为像素。</param>
        /// <param name="line2RowEnd">线段2终点 Row 坐标；单位为像素。</param>
        /// <param name="line2ColEnd">线段2终点 Col 坐标；单位为像素。</param>
        /// <param name="closestRow1">线段1上最近点的 Row 坐标；单位为像素。</param>
        /// <param name="closestCol1">线段1上最近点的 Col 坐标；单位为像素。</param>
        /// <param name="closestRow2">线段2上最近点的 Row 坐标；单位为像素。</param>
        /// <param name="closestCol2">线段2上最近点的 Col 坐标；单位为像素。</param>
        private void CalculateClosestPointsBetweenSegments(
            double line1RowBegin, double line1ColBegin, double line1RowEnd, double line1ColEnd,
            double line2RowBegin, double line2ColBegin, double line2RowEnd, double line2ColEnd,
            out double closestRow1, out double closestCol1,
            out double closestRow2, out double closestCol2)
        {
            // 使用HALCON的projection_pl算子和枚举法找到真实的最近点对
            // 策略：枚举所有可能的候选点对，找到距离最小的一对

            double minDistance = double.MaxValue;
            closestRow1 = line1RowBegin;
            closestCol1 = line1ColBegin;
            closestRow2 = line2RowBegin;
            closestCol2 = line2ColBegin;

            // 候选1: 线段1的起点投影到线段2
            HTuple projRow, projCol;
            HOperatorSet.ProjectionPl(line1RowBegin, line1ColBegin,
                                     line2RowBegin, line2ColBegin,
                                     line2RowEnd, line2ColEnd,
                                     out projRow, out projCol);
            // 裁剪投影点到线段2范围内
            double clampedRow2, clampedCol2;
            ClampPointToSegment(projRow.D, projCol.D,
                               line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd,
                               out clampedRow2, out clampedCol2);
            double dist = CalculateDistance(line1RowBegin, line1ColBegin, clampedRow2, clampedCol2);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = line1RowBegin;
                closestCol1 = line1ColBegin;
                closestRow2 = clampedRow2;
                closestCol2 = clampedCol2;
            }

            // 候选2: 线段1的终点投影到线段2
            HOperatorSet.ProjectionPl(line1RowEnd, line1ColEnd,
                                     line2RowBegin, line2ColBegin,
                                     line2RowEnd, line2ColEnd,
                                     out projRow, out projCol);
            ClampPointToSegment(projRow.D, projCol.D,
                               line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd,
                               out clampedRow2, out clampedCol2);
            dist = CalculateDistance(line1RowEnd, line1ColEnd, clampedRow2, clampedCol2);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = line1RowEnd;
                closestCol1 = line1ColEnd;
                closestRow2 = clampedRow2;
                closestCol2 = clampedCol2;
            }

            // 候选3: 线段2的起点投影到线段1
            HOperatorSet.ProjectionPl(line2RowBegin, line2ColBegin,
                                     line1RowBegin, line1ColBegin,
                                     line1RowEnd, line1ColEnd,
                                     out projRow, out projCol);
            double clampedRow1, clampedCol1;
            ClampPointToSegment(projRow.D, projCol.D,
                               line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,
                               out clampedRow1, out clampedCol1);
            dist = CalculateDistance(clampedRow1, clampedCol1, line2RowBegin, line2ColBegin);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = clampedRow1;
                closestCol1 = clampedCol1;
                closestRow2 = line2RowBegin;
                closestCol2 = line2ColBegin;
            }

            // 候选4: 线段2的终点投影到线段1
            HOperatorSet.ProjectionPl(line2RowEnd, line2ColEnd,
                                     line1RowBegin, line1ColBegin,
                                     line1RowEnd, line1ColEnd,
                                     out projRow, out projCol);
            ClampPointToSegment(projRow.D, projCol.D,
                               line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,
                               out clampedRow1, out clampedCol1);
            dist = CalculateDistance(clampedRow1, clampedCol1, line2RowEnd, line2ColEnd);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = clampedRow1;
                closestCol1 = clampedCol1;
                closestRow2 = line2RowEnd;
                closestCol2 = line2ColEnd;
            }
        }

        /// <summary>
        /// 将投影点裁剪到线段范围内
        /// 如果投影点在线段外，返回最近的端点
        /// </summary>
        private void ClampPointToSegment(
            double projRow, double projCol,
            double segRowBegin, double segColBegin, double segRowEnd, double segColEnd,
            out double clampedRow, out double clampedCol)
        {
            // 计算线段的方向向量
            double dirRow = segRowEnd - segRowBegin;
            double dirCol = segColEnd - segColBegin;
            double segmentLengthSquared = dirRow * dirRow + dirCol * dirCol;

            // 处理退化线段（起点和终点重合）
            if (segmentLengthSquared < 1e-10)
            {
                clampedRow = segRowBegin;
                clampedCol = segColBegin;
                return;
            }

            // 计算投影点在线段上的参数t（0表示起点，1表示终点）
            double t = ((projRow - segRowBegin) * dirRow + (projCol - segColBegin) * dirCol) / segmentLengthSquared;

            // 裁剪t到[0, 1]范围
            t = Math.Max(0, Math.Min(1, t));

            // 计算裁剪后的点坐标
            clampedRow = segRowBegin + t * dirRow;
            clampedCol = segColBegin + t * dirCol;
        }

        /// <summary>
        /// 计算两点之间的欧氏距离
        /// </summary>
        private double CalculateDistance(double row1, double col1, double row2, double col2)
        {
            double dRow = row2 - row1;
            double dCol = col2 - col1;
            return Math.Sqrt(dRow * dRow + dCol * dCol);
        }

        /// <summary>
        /// 使用HALCON Metrology模型检测边缘并拟合直线
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型（包含Metrology参数）</param>
        /// <param name="roi">线段ROI（起点和终点定义搜索区域）</param>
        /// <param name="lineRowBegin">输出：拟合直线起点Row坐标</param>
        /// <param name="lineColBegin">输出：拟合直线起点Col坐标</param>
        /// <param name="lineRowEnd">输出：拟合直线终点Row坐标</param>
        /// <param name="lineColEnd">输出：拟合直线终点Col坐标</param>
        /// <param name="edgeRows">输出：检测到的边缘点Row坐标数组</param>
        /// <param name="edgeCols">输出：检测到的边缘点Col坐标数组</param>
        /// <param name="fitScore">输出：Metrology 拟合分数，范围 0~1；找边失败时为 0。</param>
        /// <returns>是否检测成功</returns>
        private bool DetectEdgeWithMetrology(
            HObject image, ToolModel tool, ROI roi,
            out double lineRowBegin, out double lineColBegin,
            out double lineRowEnd, out double lineColEnd,
            out HTuple edgeRows, out HTuple edgeCols,
            out double fitScore)
        {
            // 初始化输出参数
            lineRowBegin = lineColBegin = lineRowEnd = lineColEnd = 0;
            edgeRows = new HTuple();
            edgeCols = new HTuple();
            fitScore = 0;

            HTuple metrologyHandle = null;
            HTuple width = null, height = null;

            try
            {
                // 1. 获取图像尺寸
                HOperatorSet.GetImageSize(image, out width, out height);

                // 2. 创建Metrology模型
                HOperatorSet.CreateMetrologyModel(out metrologyHandle);
                HOperatorSet.SetMetrologyModelImageSize(metrologyHandle, width, height);

                // 3. 构建线段参数 [Row1, Col1, Row2, Col2]
                HTuple shapeParam = new HTuple();
                shapeParam[0] = roi.Row1;
                shapeParam[1] = roi.Col1;
                shapeParam[2] = roi.Row2;
                shapeParam[3] = roi.Col2;

                // 4. 添加线对象到Metrology模型
                HTuple index;
                // 注意：这里必须按 HALCON 接口定义传入测量窗口参数，避免语义错位
                HOperatorSet.AddMetrologyObjectGeneric(
                    metrologyHandle,
                    "line",                           // 对象类型：线段
                    shapeParam,                       // 线段参数
                    tool.MetrologyMeasureLength1,     // 测量方向半长度
                    tool.MetrologyMeasureLength2,     // 垂直测量方向半宽度
                    tool.MetrologyMeasureSigma,       // 高斯平滑
                    tool.MetrologyMeasureThreshold,   // 边缘阈值
                    new HTuple(),                     // GenParamName（空）
                    new HTuple(),                     // GenParamValue（空）
                    out index
                );

                // 5. 设置Metrology对象参数
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_transition", tool.MetrologyMeasureTransition);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "num_measures", tool.MetrologyNumMeasures);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_sigma", tool.MetrologyMeasureSigma);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_threshold", tool.MetrologyMeasureThreshold);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_select", tool.MetrologyMeasureSelect);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "min_score", tool.MetrologyMinScore);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_length1", tool.MetrologyMeasureLength1);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_length2", tool.MetrologyMeasureLength2);

                // 6. 执行测量
                HOperatorSet.ApplyMetrologyModel(image, metrologyHandle);

                // 7. 获取拟合结果（直线参数）
                HTuple parameter;
                HOperatorSet.GetMetrologyObjectResult(
                    metrologyHandle,
                    0,              // 对象索引（第一个对象）
                    "all",          // 实例（全部）
                    "result_type",  // 结果类型
                    "all_param",    // 参数名称
                    out parameter
                );

                // 检查是否获取到有效结果
                if (parameter == null || parameter.Length < 4)
                {
                    return false;
                }

                // 8. 解析直线参数 [Row1, Col1, Row2, Col2]
                lineRowBegin = parameter[0].D;
                lineColBegin = parameter[1].D;
                lineRowEnd = parameter[2].D;
                lineColEnd = parameter[3].D;

                try
                {
                    HTuple scoreTuple;
                    HOperatorSet.GetMetrologyObjectResult(
                        metrologyHandle,
                        0,
                        "all",
                        "result_type",
                        "score",
                        out scoreTuple);
                    if (scoreTuple != null && scoreTuple.Length > 0)
                    {
                        fitScore = scoreTuple[0].D;
                    }
                }
                catch
                {
                    fitScore = 1.0;
                }

                // 9. 获取边缘点坐标（用于可视化）
                HObject contours;
                HOperatorSet.GetMetrologyObjectMeasures(
                    out contours,
                    metrologyHandle,
                    "all",      // 对象索引
                    "all",      // 实例
                    out edgeRows,
                    out edgeCols
                );
                contours?.Dispose();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DetectEdgeWithMetrology错误: {ex.Message}");
                return false;
            }
            finally
            {
                // 释放Metrology模型资源
                if (metrologyHandle != null)
                {
                    try
                    {
                        HOperatorSet.ClearMetrologyObject(metrologyHandle, "all");
                        HOperatorSet.ClearMetrologyModel(metrologyHandle);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 检测圆（用于圆心检测）- 传统边缘检测方法（已弃用，保留作为备用）
        /// </summary>
        private bool DetectCircle(HObject image, ToolModel tool, ROI roi, out HTuple row, out HTuple col, out HTuple radius)
        {
            row = null;
            col = null;
            radius = null;
            HObject reduceImage = null, edges = null, contours = null, selectedContours = null;

            try
            {
                // 生成ROI并缩小运算域
                HObject roiRegion;
                HOperatorSet.GenRectangle1(out roiRegion, roi.Row1, roi.Col1, roi.Row2, roi.Col2);
                HOperatorSet.ReduceDomain(image, roiRegion, out reduceImage);
                roiRegion.Dispose();

                // 边缘检测
                HOperatorSet.EdgesSubPix(reduceImage, out edges, "canny", 1.0, 20, 40);

                // 选择圆形轮廓
                HOperatorSet.SelectShapeXld(edges, out selectedContours, "circularity", "and", 0.7, 1.0);

                if (selectedContours.CountObj() == 0)
                {
                    reduceImage?.Dispose();
                    edges?.Dispose();
                    selectedContours?.Dispose();
                    return false;
                }

                // 拟合圆
                HTuple rowCenter, colCenter, radiusCenter, startPhi, endPhi, pointOrder;
                HOperatorSet.FitCircleContourXld(selectedContours, "algebraic", -1, 0, 0, 3, 2, 
                    out rowCenter, out colCenter, out radiusCenter, out startPhi, out endPhi, out pointOrder);

                if (rowCenter.Length > 0)
                {
                    row = rowCenter[0];
                    col = colCenter[0];
                    radius = radiusCenter[0];

                    reduceImage?.Dispose();
                    edges?.Dispose();
                    selectedContours?.Dispose();
                    return true;
                }

                reduceImage?.Dispose();
                edges?.Dispose();
                selectedContours?.Dispose();
                return false;
            }
            catch (Exception ex)
            {
                reduceImage?.Dispose();
                edges?.Dispose();
                selectedContours?.Dispose();
                return false;
            }
        }

        /// <summary>
        /// 使用HALCON Metrology模型检测圆心
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型（包含Metrology参数）</param>
        /// <param name="roi">圆ROI（矩形框，自动计算圆心和半径）</param>
        /// <param name="centerRow">输出：拟合圆心Row坐标</param>
        /// <param name="centerCol">输出：拟合圆心Col坐标</param>
        /// <param name="radius">输出：拟合圆半径</param>
        /// <param name="edgeRows">输出：检测到的边缘点Row坐标数组</param>
        /// <param name="edgeCols">输出：检测到的边缘点Col坐标数组</param>
        /// <returns>是否检测成功</returns>
        private bool DetectCircleWithMetrology(
            HObject image, ToolModel tool, ROI roi,
            out double centerRow, out double centerCol, out double radius,
            out HTuple edgeRows, out HTuple edgeCols)
        {
            // 初始化输出参数
            centerRow = centerCol = radius = 0;
            edgeRows = new HTuple();
            edgeCols = new HTuple();

            HTuple metrologyHandle = null;
            HTuple width = null, height = null;

            try
            {
                // 1. 获取图像尺寸
                HOperatorSet.GetImageSize(image, out width, out height);

                // 2. 创建Metrology模型
                HOperatorSet.CreateMetrologyModel(out metrologyHandle);
                HOperatorSet.SetMetrologyModelImageSize(metrologyHandle, width, height);

                // 3. 计算圆的初始参数
                double initCenterRow, initCenterCol, initRadius;
                
                if (roi.Type == ROIType.Circle && roi.CircleRadius > 0)
                {
                    // 使用圆形ROI参数（新模式：点击圆心 + 拖动半径）
                    initCenterRow = roi.CircleCenterRow;
                    initCenterCol = roi.CircleCenterCol;
                    initRadius = roi.CircleRadius;
                }
                else
                {
                    // 从矩形ROI自动计算（兼容旧模式）
                    // 圆心：ROI矩形的中心点
                    initCenterRow = (roi.Row1 + roi.Row2) / 2.0;
                    initCenterCol = (roi.Col1 + roi.Col2) / 2.0;
                    // 初始半径：ROI矩形宽高的较小值的一半
                    initRadius = Math.Min(
                        Math.Abs(roi.Row2 - roi.Row1),
                        Math.Abs(roi.Col2 - roi.Col1)
                    ) / 2.0;
                }
                
                // 3.1 参数验证：确保半径 > Tolerance，避免搜索范围出现负值
                if (initRadius <= tool.MetrologyTolerance)
                {
                    string errorMsg = $"圆形ROI半径 ({initRadius:F1}px) 必须大于Metrology搜索范围 ({tool.MetrologyTolerance}px)\n" +
                                     $"建议：\n" +
                                     $"1. 绘制更大的圆形ROI（半径 > {tool.MetrologyTolerance}px）\n" +
                                     $"2. 或在设置中减小'Metrology搜索范围'参数（当前{tool.MetrologyTolerance}px）";
                    System.Diagnostics.Debug.WriteLine($"[圆检测失败] {errorMsg}");
                    throw new Exception(errorMsg);
                }

                // 4. 添加圆对象到Metrology模型
                // 参考官方示例apply_metrology_model.cs第846行
                HTuple circleIndex;
                HOperatorSet.AddMetrologyObjectCircleMeasure(
                    metrologyHandle,
                    initCenterRow,                    // 圆心初始Row坐标
                    initCenterCol,                    // 圆心初始Column坐标
                    initRadius,                       // 初始半径
                    tool.MetrologyTolerance,          // 半径容差（搜索范围）
                    tool.MetrologyNumMeasures,        // 卡尺数量（沿圆周分布）
                    tool.MetrologyMeasureLength1,     // 卡尺长度（径向搜索长度）
                    tool.MetrologyMeasureSigma,       // 高斯平滑参数
                    new HTuple(),                     // GenParamName（空）
                    new HTuple(),                     // GenParamValue（空）
                    out circleIndex                   // 输出圆对象索引
                );

                // 5. 设置Metrology对象参数
                // 参考官方示例apply_metrology_model.cs第852-863行
                HOperatorSet.SetMetrologyObjectParam(
                    metrologyHandle, circleIndex,
                    "measure_transition", tool.MetrologyMeasureTransition);  // 边缘极性
                HOperatorSet.SetMetrologyObjectParam(
                    metrologyHandle, circleIndex,
                    "measure_threshold", tool.MetrologyMeasureThreshold);    // 边缘阈值
                HOperatorSet.SetMetrologyObjectParam(
                    metrologyHandle, circleIndex,
                    "min_score", tool.MetrologyMinScore);                    // 最小分数
                HOperatorSet.SetMetrologyObjectParam(
                    metrologyHandle, circleIndex,
                    "num_instances", 1);                                     // 只检测一个圆实例

                // 6. 执行测量
                // 参考官方示例apply_metrology_model.cs第867行
                HOperatorSet.ApplyMetrologyModel(image, metrologyHandle);

                // 7. 获取拟合结果（圆参数）
                // 参考官方示例apply_metrology_model.cs第912行
                HTuple circleParameter;
                HOperatorSet.GetMetrologyObjectResult(
                    metrologyHandle,
                    circleIndex,     // 圆对象索引
                    "all",           // 所有实例
                    "result_type",   // 结果类型
                    "all_param",     // 所有参数
                    out circleParameter
                );

                // 检查是否获取到有效结果
                if (circleParameter == null || circleParameter.Length < 3)
                {
                    return false;
                }

                // 8. 解析圆参数 [CenterRow, CenterColumn, Radius]
                // 参考官方示例apply_metrology_model.cs第921-938行
                centerRow = circleParameter[0].D;
                centerCol = circleParameter[1].D;
                radius = circleParameter[2].D;

                // 9. 获取边缘点坐标（用于可视化）
                HObject contours;
                HOperatorSet.GetMetrologyObjectMeasures(
                    out contours,
                    metrologyHandle,
                    circleIndex,
                    "all",           // 所有实例
                    out edgeRows,
                    out edgeCols
                );
                contours?.Dispose();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DetectCircleWithMetrology错误: {ex.Message}");
                return false;
            }
            finally
            {
                // 释放Metrology模型资源
                if (metrologyHandle != null)
                {
                    try
                    {
                        HOperatorSet.ClearMetrologyObject(metrologyHandle, "all");
                        HOperatorSet.ClearMetrologyModel(metrologyHandle);
                    }
                    catch { }
                }
            }
        }

        #endregion

        #region 直线检测

        /// <summary>
        /// 单次直线存在性检测的业务结果。
        /// 供配置界面测试与 AOI 运行分支映射 ToolStatus，不参与尺寸测量与毫米换算。
        /// </summary>
        public class LinePresenceResult
        {
            /// <summary>
            /// 找边失败或 ROI 无效时为 true，运行分支映射为 ToolStatus.NG2。
            /// </summary>
            public bool DetectFailed { get; set; }

            /// <summary>
            /// 判定口径满足时为 true，运行分支映射为 ToolStatus.OK。
            /// </summary>
            public bool JudgementOk { get; set; }

            /// <summary>
            /// 拟合线相对 ROI 的无方向夹角偏差；单位为度。
            /// </summary>
            public double AngleDeviation { get; set; }

            /// <summary>
            /// Metrology 拟合直线方向角；单位为度，相对图像坐标系。
            /// </summary>
            public double FittedLineAngle { get; set; }

            /// <summary>
            /// 有效边缘卡尺数占 Metrology 卡尺总数的比例；范围 0~1。
            /// </summary>
            public double EdgeHitRatio { get; set; }

            /// <summary>
            /// Metrology 拟合质量分数；范围 0~1，与 MetrologyMinScore 比较。
            /// </summary>
            public double FitScore { get; set; }

            /// <summary>
            /// 判定失败说明，写入日志与 LastLineDetectFailReason 供主界面追溯。
            /// </summary>
            public string FailReason { get; set; } = string.Empty;
        }

        /// <summary>
        /// 表示一次直线检测中可参与业务判定的线状边缘。
        /// 该对象把 Metrology 的找边结果转换为直线检测工具的判定指标，供有线与无线两种口径共用。
        /// </summary>
        private class LinePresenceCandidate
        {
            public bool Found { get; set; }
            public bool IsEffective { get; set; }
            public double RowBegin { get; set; }
            public double ColBegin { get; set; }
            public double RowEnd { get; set; }
            public double ColEnd { get; set; }
            public HTuple EdgeRows { get; set; } = new HTuple();
            public HTuple EdgeCols { get; set; } = new HTuple();
            public int EdgePointCount { get; set; }
            public double EdgeHitRatio { get; set; }
            public double FitScore { get; set; }
            public double FittedLineAngle { get; set; }
            public double AngleDeviation { get; set; }
            public string RejectReason { get; set; } = string.Empty;
        }

        /// <summary>
        /// 直线存在性检测：判断 ROI 沿线是否形成满足方向与质量的线状边缘。
        ///
        /// 业务边界：
        /// - 仅输出 OK/NG/NG2 判定依据，不参与尺寸测量、毫米标定与距离计算。
        /// - 期望方向取自 LineDetectROI 线段走向；拟合方向来自 Metrology 输出。
        /// </summary>
        /// <param name="image">待测图像，作为 Metrology 找边输入。</param>
        /// <param name="tool">直线检测工具配置，含 ROI、角度阈值、命中率与 Metrology 参数。</param>
        /// <param name="hwindow">HALCON 窗口；非空且 redraw 为 true 时绘制拟合线与调试图层。</param>
        /// <param name="redraw">是否在窗口叠加检测结果。</param>
        /// <returns>检测与判定结果；DetectFailed 为 true 时表示 NG2。</returns>
        public LinePresenceResult DetectLinePresence(HObject image, ToolModel tool, HWindow hwindow, bool redraw)
        {
            var result = new LinePresenceResult();
            ROI roi = tool?.LineDetectROI;
            if (roi == null)
            {
                roi = new ROI { Type = ROIType.Line };
            }

            if (!IsROIValid(roi))
            {
                result.DetectFailed = true;
                result.FailReason = "直线检测 ROI 无效或未绘制";
                tool.LastLineDetectFailReason = result.FailReason;
                tool.ActualAngleDeviation = 0;
                tool.LastEdgeHitRatio = 0;
                tool.LastLineDetectScore = 0;
                return result;
            }

            LinePresenceCandidate candidate = EvaluateLinePresenceCandidate(image, tool, roi);
            ApplyLinePresenceCandidateToResult(result, tool, candidate);

            if (tool.ExpectLinePresent)
            {
                if (!candidate.Found)
                {
                    result.DetectFailed = true;
                    result.FailReason = "未找到可拟合的线状边缘";
                    ApplyLinePresenceResultToTool(tool, result);
                    DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, candidate);
                    return result;
                }

                if (!candidate.IsEffective)
                {
                    result.FailReason = candidate.RejectReason;
                    ApplyLinePresenceResultToTool(tool, result);
                    DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, candidate);
                    return result;
                }

                result.JudgementOk = true;
                ApplyLinePresenceResultToTool(tool, result);
                DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, candidate);
                return result;
            }

            if (candidate.IsEffective)
            {
                result.FailReason = "检测到有效边缘直线，与“期望无线”配置冲突";
                ApplyLinePresenceResultToTool(tool, result);
                DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, candidate);
                return result;
            }

            if (candidate.Found)
            {
                result.FailReason = string.IsNullOrWhiteSpace(candidate.RejectReason)
                    ? "检测到边缘但未满足无线口径，不能判为无线OK"
                    : candidate.RejectReason;
                ApplyLinePresenceResultToTool(tool, result);
                DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, candidate);
                return result;
            }

            LinePresenceCandidate weakCandidate = EvaluateWeakLinePresenceCandidate(image, tool, roi);
            if (weakCandidate.IsEffective)
            {
                ApplyLinePresenceCandidateToResult(result, tool, weakCandidate);
                result.FailReason = "低阈值复检发现有效边缘直线，与“期望无线”配置冲突";
                ApplyLinePresenceResultToTool(tool, result);
                DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, weakCandidate);
                return result;
            }

            if (weakCandidate.Found || weakCandidate.EdgePointCount > tool.LineDetectAbsentMaxEdgePoints)
            {
                ApplyLinePresenceCandidateToResult(result, tool, weakCandidate);
                result.DetectFailed = true;
                result.FailReason = string.IsNullOrWhiteSpace(weakCandidate.RejectReason)
                    ? "低阈值复检仍可见边缘散点，搜索条件无法确认无线"
                    : $"低阈值复检可见边缘散点：{weakCandidate.RejectReason}";
                ApplyLinePresenceResultToTool(tool, result);
                DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, weakCandidate);
                return result;
            }

            result.JudgementOk = true;
            result.FailReason = string.Empty;
            ApplyLinePresenceResultToTool(tool, result);
            DrawLinePresenceResultIfNeeded(redraw, hwindow, tool, roi, candidate);
            return result;
        }

        /// <summary>
        /// 按直线检测工具的判定口径评估一次 Metrology 找边结果。
        /// 该评估同时服务有线与无线模式，使“有效直线”的定义在两种业务口径下保持一致。
        /// </summary>
        /// <param name="image">当前待测图像，作为 Metrology 找边输入。</param>
        /// <param name="tool">直线检测工具配置，提供 ROI 之外的 Metrology 参数和判定阈值。</param>
        /// <param name="roi">直线检测 ROI；坐标单位为像素。</param>
        /// <returns>包含找边、角度、命中率和有效性判断的候选结果。</returns>
        private LinePresenceCandidate EvaluateLinePresenceCandidate(HObject image, ToolModel tool, ROI roi)
        {
            var candidate = new LinePresenceCandidate();

            double rowBegin, colBegin, rowEnd, colEnd;
            HTuple edgeRows, edgeCols;
            double fitScore;
            candidate.Found = DetectEdgeWithMetrology(
                image, tool, roi,
                out rowBegin, out colBegin, out rowEnd, out colEnd,
                out edgeRows, out edgeCols, out fitScore);

            candidate.RowBegin = rowBegin;
            candidate.ColBegin = colBegin;
            candidate.RowEnd = rowEnd;
            candidate.ColEnd = colEnd;
            candidate.EdgeRows = edgeRows ?? new HTuple();
            candidate.EdgeCols = edgeCols ?? new HTuple();
            candidate.EdgePointCount = candidate.EdgeRows.Length;
            candidate.FitScore = fitScore;
            candidate.EdgeHitRatio = CalculateLineDetectEdgeHitRatio(candidate.EdgePointCount, tool.MetrologyNumMeasures);

            if (!candidate.Found)
            {
                candidate.RejectReason = "未形成可拟合直线";
                return candidate;
            }

            candidate.FittedLineAngle = Math.Atan2(rowEnd - rowBegin, colEnd - colBegin) * 180.0 / Math.PI;
            candidate.AngleDeviation = CalculateUndirectedLineAngleDeviationDegrees(
                roi.Row1, roi.Col1, roi.Row2, roi.Col2,
                rowBegin, colBegin, rowEnd, colEnd);

            if (candidate.FitScore < tool.MetrologyMinScore)
            {
                candidate.RejectReason = $"拟合分数不足({candidate.FitScore:F2} < {tool.MetrologyMinScore:F2})";
                return candidate;
            }

            if (candidate.EdgeHitRatio < tool.LineDetectMinEdgeHitRatio)
            {
                candidate.RejectReason = $"边缘命中率不足({candidate.EdgeHitRatio:P0} < {tool.LineDetectMinEdgeHitRatio:P0})";
                return candidate;
            }

            if (candidate.AngleDeviation > tool.LineDetectAllowAngleDelta)
            {
                candidate.RejectReason = $"角度偏差超限({candidate.AngleDeviation:F2}° > {tool.LineDetectAllowAngleDelta:F2}°)";
                return candidate;
            }

            candidate.IsEffective = true;
            return candidate;
        }

        /// <summary>
        /// 无线判定前使用较低阈值复检 ROI，识别原阈值可能漏掉的弱边缘。
        /// 复检只改变本次临时计算参数，完成后恢复工具配置，工程 XML 与界面参数保持原值。
        /// </summary>
        /// <param name="image">当前待测图像。</param>
        /// <param name="tool">直线检测工具配置；方法内部会临时降低边缘阈值与最小分数。</param>
        /// <param name="roi">直线检测 ROI；坐标单位为像素。</param>
        /// <returns>低阈值复检得到的候选结果。</returns>
        private LinePresenceCandidate EvaluateWeakLinePresenceCandidate(HObject image, ToolModel tool, ROI roi)
        {
            int savedThreshold = tool.MetrologyMeasureThreshold;
            double savedMinScore = tool.MetrologyMinScore;
            try
            {
                tool.MetrologyMeasureThreshold = Math.Max(1, savedThreshold / 2);
                tool.MetrologyMinScore = Math.Min(savedMinScore, 0.15);
                return EvaluateLinePresenceCandidate(image, tool, roi);
            }
            finally
            {
                tool.MetrologyMeasureThreshold = savedThreshold;
                tool.MetrologyMinScore = savedMinScore;
            }
        }

        /// <summary>
        /// 将边缘点数量换算为直线检测使用的命中率显示值。
        /// 该值用于现场调参和日志追溯；正式有效性仍由拟合分数、命中率与角度共同决定。
        /// </summary>
        /// <param name="edgePointCount">Metrology 返回的边缘点数量。</param>
        /// <param name="numMeasures">工具配置的卡尺数量。</param>
        /// <returns>0~1 的命中率估计值。</returns>
        private double CalculateLineDetectEdgeHitRatio(int edgePointCount, int numMeasures)
        {
            int safeNumMeasures = Math.Max(1, numMeasures);
            return Math.Min(1.0, (double)Math.Min(edgePointCount, safeNumMeasures) / safeNumMeasures);
        }

        /// <summary>
        /// 将候选线指标写入直线检测结果对象，供运行分支和配置测试提示使用。
        /// </summary>
        /// <param name="result">本次检测的业务结果。</param>
        /// <param name="tool">当前工具配置；用于保持调用口径一致。</param>
        /// <param name="candidate">Metrology 找边候选结果。</param>
        private void ApplyLinePresenceCandidateToResult(LinePresenceResult result, ToolModel tool, LinePresenceCandidate candidate)
        {
            result.AngleDeviation = candidate.AngleDeviation;
            result.FittedLineAngle = candidate.FittedLineAngle;
            result.EdgeHitRatio = candidate.EdgeHitRatio;
            result.FitScore = candidate.FitScore;
        }

        /// <summary>
        /// 将直线检测结果写回工具运行态字段，刷新主界面显示并保存最近失败原因。
        /// 运行态字段不写入工程 XML，只服务当次 AOI 结果展示和日志追溯。
        /// </summary>
        /// <param name="tool">当前工具实例。</param>
        /// <param name="result">本次直线检测业务结果。</param>
        private void ApplyLinePresenceResultToTool(ToolModel tool, LinePresenceResult result)
        {
            tool.ActualLineAngle = result.FittedLineAngle;
            tool.ActualAngleDeviation = result.AngleDeviation;
            tool.LastEdgeHitRatio = result.EdgeHitRatio;
            tool.LastLineDetectScore = result.FitScore;
            tool.LastLineDetectFailReason = result.JudgementOk ? string.Empty : result.FailReason;
        }

        /// <summary>
        /// 按当前候选结果绘制直线检测预览图层。
        /// 找到拟合线时绘制拟合线；仅完成无线确认或搜索失败时只绘制 ROI 与卡尺框，避免操作者误读为检测到有效线。
        /// </summary>
        /// <param name="redraw">true 表示调用方需要刷新 HALCON 预览。</param>
        /// <param name="hwindow">接收图层的 HALCON 窗口。</param>
        /// <param name="tool">当前工具配置，提供调试显示开关。</param>
        /// <param name="roi">直线检测 ROI。</param>
        /// <param name="candidate">本次候选线结果。</param>
        private void DrawLinePresenceResultIfNeeded(bool redraw, HWindow hwindow, ToolModel tool, ROI roi, LinePresenceCandidate candidate)
        {
            if (!redraw || hwindow == null)
            {
                return;
            }

            DrawLinePresenceOverlay(
                hwindow,
                tool,
                roi,
                candidate.Found ? candidate.RowBegin : roi.Row1,
                candidate.Found ? candidate.ColBegin : roi.Col1,
                candidate.Found ? candidate.RowEnd : roi.Row2,
                candidate.Found ? candidate.ColEnd : roi.Col2,
                candidate.EdgeRows,
                candidate.EdgeCols,
                candidate.Found);
        }

        /// <summary>
        /// 计算两条线段方向的无方向夹角偏差；0° 与 180° 等价，单位为度。
        /// </summary>
        private static double CalculateUndirectedLineAngleDeviationDegrees(
            double roiRow1, double roiCol1, double roiRow2, double roiCol2,
            double fitRow1, double fitCol1, double fitRow2, double fitCol2)
        {
            double dir1Row = roiRow2 - roiRow1;
            double dir1Col = roiCol2 - roiCol1;
            double dir2Row = fitRow2 - fitRow1;
            double dir2Col = fitCol2 - fitCol1;
            double len1 = Math.Sqrt(dir1Row * dir1Row + dir1Col * dir1Col);
            double len2 = Math.Sqrt(dir2Row * dir2Row + dir2Col * dir2Col);
            if (len1 < GeometryEpsilon || len2 < GeometryEpsilon)
            {
                return 180.0;
            }

            dir1Row /= len1;
            dir1Col /= len1;
            dir2Row /= len2;
            dir2Col /= len2;
            double cosAngle = Math.Abs(dir1Row * dir2Row + dir1Col * dir2Col);
            cosAngle = Math.Max(0, Math.Min(1, cosAngle));
            return Math.Acos(cosAngle) * 180.0 / Math.PI;
        }

        /// <summary>
        /// 在 HALCON 窗口绘制直线检测的 ROI、拟合线与边缘点，供配置测试与运行追溯。
        /// </summary>
        private void DrawLinePresenceOverlay(
            HWindow hwindow, ToolModel tool, ROI roi,
            double lineRowBegin, double lineColBegin, double lineRowEnd, double lineColEnd,
            HTuple edgeRows, HTuple edgeCols,
            bool drawFittedLine = true)
        {
            hwindow.SetLineWidth(2);
            hwindow.SetColor("green");
            hwindow.SetLineStyle(new HTuple(new int[] { 10, 5 }));
            hwindow.DispLine((double)roi.Row1, (double)roi.Col1, (double)roi.Row2, (double)roi.Col2);
            hwindow.SetLineStyle(new HTuple());

            if (drawFittedLine)
            {
                hwindow.SetColor("cyan");
                hwindow.DispLine(lineRowBegin, lineColBegin, lineRowEnd, lineColEnd);
            }

            if (tool.ShowMetrologyDebugInfo)
            {
                if (edgeRows != null && edgeCols != null)
                {
                    hwindow.SetColor("spring green");
                    hwindow.SetLineWidth(1);
                    for (int i = 0; i < edgeRows.Length; i++)
                    {
                        hwindow.DispCross(edgeRows[i].D, edgeCols[i].D, 6, 0);
                    }
                }

                DrawMetrologyCaliperFramesOnRoi(hwindow, tool, roi);
            }
        }

        /// <summary>
        /// 沿 ROI 线段绘制 Metrology 卡尺框，供直线检测与尺寸测量预览共用。
        /// </summary>
        private void DrawMetrologyCaliperFramesOnRoi(HWindow hwindow, ToolModel tool, ROI roi)
        {
            int numMeasures = Math.Max(0, tool.MetrologyNumMeasures);
            if (numMeasures <= 0)
            {
                return;
            }

            double roiRowBegin = roi.Row1;
            double roiColBegin = roi.Col1;
            double roiRowEnd = roi.Row2;
            double roiColEnd = roi.Col2;
            double dirRow = roiRowEnd - roiRowBegin;
            double dirCol = roiColEnd - roiColBegin;
            double lineLength = Math.Sqrt(dirRow * dirRow + dirCol * dirCol);
            if (lineLength <= 1e-6)
            {
                return;
            }

            dirRow /= lineLength;
            dirCol /= lineLength;
            double perpRow = -dirCol;
            double perpCol = dirRow;
            double halfLen1 = tool.MetrologyMeasureLength1;
            double halfLen2 = tool.MetrologyMeasureLength2;

            hwindow.SetColor("orange");
            hwindow.SetLineWidth(1);
            hwindow.SetDraw("margin");

            for (int i = 0; i < numMeasures; i++)
            {
                double t = (numMeasures == 1) ? 0.5 : (double)i / (numMeasures - 1);
                double centerRow = roiRowBegin + t * (roiRowEnd - roiRowBegin);
                double centerCol = roiColBegin + t * (roiColEnd - roiColBegin);

                double row1 = centerRow - halfLen1 * perpRow - halfLen2 * dirRow;
                double col1 = centerCol - halfLen1 * perpCol - halfLen2 * dirCol;
                double row2 = centerRow + halfLen1 * perpRow - halfLen2 * dirRow;
                double col2 = centerCol + halfLen1 * perpCol - halfLen2 * dirCol;
                double row3 = centerRow + halfLen1 * perpRow + halfLen2 * dirRow;
                double col3 = centerCol + halfLen1 * perpCol + halfLen2 * dirCol;
                double row4 = centerRow - halfLen1 * perpRow + halfLen2 * dirRow;
                double col4 = centerCol - halfLen1 * perpCol + halfLen2 * dirCol;

                hwindow.DispLine(row1, col1, row2, col2);
                hwindow.DispLine(row2, col2, row3, col3);
                hwindow.DispLine(row3, col3, row4, col4);
                hwindow.DispLine(row4, col4, row1, col1);
            }
        }

        #endregion

        #region 边缘预览

        /// <summary>
        /// 预览指定ROI区域内的边缘轮廓
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型</param>
        /// <param name="roi">要预览的ROI区域</param>
        /// <param name="hwindow">HALCON窗口</param>
        /// <param name="color">显示颜色</param>
        /// <returns>是否预览成功</returns>
        public bool PreviewEdgesForROI(HObject image, ToolModel tool, ROI roi, HWindow hwindow, string color = "green")
        {
            try
            {
                // 验证ROI有效性（支持圆形、线段、矩形）
                if (!IsROIValid(roi))
                {
                    return false; // ROI无效，跳过预览
                }

                if (tool.TestMode == TestModes.直线检测)
                {
                    if (roi.Type == ROIType.Line)
                    {
                        return PreviewEdgesWithMetrology(image, tool, roi, hwindow, color);
                    }

                    return false;
                }

                // 判断测量类型，选择合适的预览方法
                if (tool.TestMode == TestModes.尺寸测量)
                {
                    // 尺寸测量模式：根据测量类型选择预览方法
                    if (tool.MeasureType == DimensionMeasureType.直线到直线 ||
                        tool.MeasureType == DimensionMeasureType.直线到圆心)
                    {
                        // 直线相关测量：使用Metrology直线预览
                        if (roi.Type == ROIType.Line)
                        {
                            return PreviewEdgesWithMetrology(image, tool, roi, hwindow, color);
                        }
                        else
                        {
                            // 圆相关：使用Metrology圆预览
                            return PreviewCircleEdgesWithMetrology(image, tool, roi, hwindow, color);
                        }
                    }
                    else if (tool.MeasureType == DimensionMeasureType.圆心到圆心)
                    {
                        // 圆心到圆心测量：使用Metrology圆预览
                        return PreviewCircleEdgesWithMetrology(image, tool, roi, hwindow, color);
                    }
                }

                // 其他模式：使用传统卡尺工具预览
                return PreviewEdgesWithCaliper(image, tool, roi, hwindow, color);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"边缘预览失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用Metrology模型预览圆形边缘（完整调试信息）
        /// </summary>
        private bool PreviewCircleEdgesWithMetrology(HObject image, ToolModel tool, ROI roi, HWindow hwindow, string color)
        {
            try
            {
                // 使用Metrology检测圆
                double centerRow, centerCol, radiusValue;
                HTuple edgeRows, edgeCols;
                if (!DetectCircleWithMetrology(image, tool, roi,
                    out centerRow, out centerCol, out radiusValue,
                    out edgeRows, out edgeCols))
                {
                    return false;
                }

                // 1. 绘制拟合圆（青色）
                hwindow.SetLineWidth(2);
                hwindow.SetColor("cyan");
                hwindow.DispCircle(centerRow, centerCol, radiusValue);

                // 2. 绘制圆心十字标记（提高可见性）
                hwindow.SetColor("cyan");
                hwindow.SetLineWidth(1);
                double crossSize = 12; // 十字大小
                hwindow.DispLine(centerRow - crossSize, centerCol, centerRow + crossSize, centerCol);
                hwindow.DispLine(centerRow, centerCol - crossSize, centerRow, centerCol + crossSize);

                // 3. 如果是圆形ROI，绘制初始ROI圆（绿色虚线，表示用户绘制的ROI）
                if (roi.Type == ROIType.Circle && roi.CircleRadius > 0)
                {
                    hwindow.SetColor("green");
                    hwindow.SetLineWidth(1);
                    HTuple dashStyle = new HTuple(new int[] { 10, 5 }); // 虚线样式：10像素实线，5像素空白
                    hwindow.SetLineStyle(dashStyle);
                    hwindow.DispCircle(roi.CircleCenterRow, roi.CircleCenterCol, roi.CircleRadius);
                    hwindow.SetLineStyle(new HTuple()); // 恢复实线样式
                    
                    // 绘制初始圆心标记（绿色小十字）
                    hwindow.SetColor("green");
                    double initCrossSize = 8;
                    hwindow.DispLine(roi.CircleCenterRow - initCrossSize, roi.CircleCenterCol, 
                                   roi.CircleCenterRow + initCrossSize, roi.CircleCenterCol);
                    hwindow.DispLine(roi.CircleCenterRow, roi.CircleCenterCol - initCrossSize, 
                                   roi.CircleCenterRow, roi.CircleCenterCol + initCrossSize);
                }

                // 4. 绘制边缘点（春绿色十字标记）
                hwindow.SetColor("spring green");
                hwindow.SetLineWidth(1);
                for (int i = 0; i < edgeRows.Length; i++)
                {
                    double row = edgeRows[i].D;
                    double col = edgeCols[i].D;
                    hwindow.DispCross(row, col, 6, 0);
                }

                // 5. 如果启用调试信息，绘制卡尺位置和详细信息
                if (tool.ShowMetrologyDebugInfo)
                {
                    // 绘制圆心标记（橙色圆圈）
                    hwindow.SetColor("orange");
                    hwindow.SetLineWidth(2);
                    hwindow.DispCircle(centerRow, centerCol, 8);

                    // 绘制圆心十字
                    hwindow.SetLineWidth(1);
                    hwindow.DispLine(centerRow - 15, centerCol, centerRow + 15, centerCol);
                    hwindow.DispLine(centerRow, centerCol - 15, centerRow, centerCol + 15);

                    // 绘制径向卡尺方向示意（黄色）
                    // 在圆周上均匀分布的几个位置绘制径向线，表示卡尺位置
                    hwindow.SetColor("yellow");
                    hwindow.SetLineWidth(1);
                    int numIndicators = Math.Min(tool.MetrologyNumMeasures, 8); // 最多显示8个方向
                    for (int i = 0; i < numIndicators; i++)
                    {
                        double angle = (2.0 * Math.PI * i) / numIndicators;
                        double innerRow = centerRow + (radiusValue - tool.MetrologyMeasureLength1) * Math.Cos(angle);
                        double innerCol = centerCol + (radiusValue - tool.MetrologyMeasureLength1) * Math.Sin(angle);
                        double outerRow = centerRow + (radiusValue + tool.MetrologyMeasureLength1) * Math.Cos(angle);
                        double outerCol = centerCol + (radiusValue + tool.MetrologyMeasureLength1) * Math.Sin(angle);
                        hwindow.DispLine(innerRow, innerCol, outerRow, outerCol);
                    }

                    // 显示圆参数信息（白色文字）
                    hwindow.SetColor("white");
                    string circleInfo = $"圆心: ({centerRow:F1}, {centerCol:F1})\n半径: {radiusValue:F2}px";
                    hwindow.DispText(circleInfo, "image", centerRow - radiusValue - 40, centerCol - 50, "white", "box", "false");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Metrology圆预览失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用Metrology模型预览边缘（完整调试信息）
        /// </summary>
        private bool PreviewEdgesWithMetrology(HObject image, ToolModel tool, ROI roi, HWindow hwindow, string color)
        {
            try
            {
                // 使用Metrology检测边缘
                double lineRowBegin, lineColBegin, lineRowEnd, lineColEnd;
                HTuple edgeRows, edgeCols;
                if (!DetectEdgeWithMetrology(image, tool, roi,
                    out lineRowBegin, out lineColBegin, out lineRowEnd, out lineColEnd,
                    out edgeRows, out edgeCols, out _))
                {
                    return false;
                }

                // 1. 绘制拟合直线（青色）
                hwindow.SetLineWidth(2);
                hwindow.SetColor("cyan");
                hwindow.DispLine(lineRowBegin, lineColBegin, lineRowEnd, lineColEnd);

                // 2. 绘制边缘点（春绿色十字标记）
                hwindow.SetColor("spring green");
                hwindow.SetLineWidth(1);
                for (int i = 0; i < edgeRows.Length; i++)
                {
                    double row = edgeRows[i].D;
                    double col = edgeCols[i].D;
                    hwindow.DispCross(row, col, 6, 0);
                }

                // 3. 如果启用调试信息，绘制卡尺位置
                if (tool.ShowMetrologyDebugInfo)
                {
                    // 重要：调试“卡尺矩形框”以用户绘制的ROI线段为基准（更符合“预览=配置”的直觉），
                    // 避免跟着拟合结果漂移导致ROI中心看起来“不在画的线的中心”。
                    int numMeasures = Math.Max(0, tool.MetrologyNumMeasures);
                    if (numMeasures <= 0)
                    {
                        return true;
                    }

                    // 计算ROI线段方向向量（Row/Col坐标系）
                    double roiRowBegin = roi.Row1;
                    double roiColBegin = roi.Col1;
                    double roiRowEnd = roi.Row2;
                    double roiColEnd = roi.Col2;
                    double dirRow = roiRowEnd - roiRowBegin;
                    double dirCol = roiColEnd - roiColBegin;
                    double lineLength = Math.Sqrt(dirRow * dirRow + dirCol * dirCol);
                    if (lineLength <= 1e-6)
                    {
                        return true; // 线段过短：跳过卡尺矩形框绘制，避免除0/NaN
                    }
                    dirRow /= lineLength;
                    dirCol /= lineLength;

                    // 垂直方向向量
                    double perpRow = -dirCol;
                    double perpCol = dirRow;

                    // 绘制卡尺位置（橙色矩形，避免与ROI(绿/黄)冲突）
                    hwindow.SetColor("orange");
                    hwindow.SetLineWidth(1);
                    hwindow.SetDraw("margin");

                    for (int i = 0; i < numMeasures; i++)
                    {
                        // 计算卡尺中心位置
                        double t = (numMeasures == 1) ? 0.5 : (double)i / (numMeasures - 1);
                        double centerRow = roiRowBegin + t * (roiRowEnd - roiRowBegin);
                        double centerCol = roiColBegin + t * (roiColEnd - roiColBegin);

                        // 计算卡尺矩形的四个角点
                        double halfLen1 = tool.MetrologyMeasureLength1; // 测量方向半长度
                        double halfLen2 = tool.MetrologyMeasureLength2; // 垂直测量方向半宽度

                        // 测量方向应垂直于ROI线方向，因此Length1沿perp，Length2沿dir
                        double row1 = centerRow - halfLen1 * perpRow - halfLen2 * dirRow;
                        double col1 = centerCol - halfLen1 * perpCol - halfLen2 * dirCol;
                        double row2 = centerRow + halfLen1 * perpRow - halfLen2 * dirRow;
                        double col2 = centerCol + halfLen1 * perpCol - halfLen2 * dirCol;
                        double row3 = centerRow + halfLen1 * perpRow + halfLen2 * dirRow;
                        double col3 = centerCol + halfLen1 * perpCol + halfLen2 * dirCol;
                        double row4 = centerRow - halfLen1 * perpRow + halfLen2 * dirRow;
                        double col4 = centerCol - halfLen1 * perpCol + halfLen2 * dirCol;

                        // 绘制矩形
                        hwindow.DispLine(row1, col1, row2, col2);
                        hwindow.DispLine(row2, col2, row3, col3);
                        hwindow.DispLine(row3, col3, row4, col4);
                        hwindow.DispLine(row4, col4, row1, col1);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Metrology预览失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用传统卡尺工具预览边缘
        /// </summary>
        private bool PreviewEdgesWithCaliper(HObject image, ToolModel tool, ROI roi, HWindow hwindow, string color)
        {
            HTuple measureHandle = null;
            try
            {
                // 计算ROI中心点和角度（用于卡尺工具）
                double centerRow = (roi.Row1 + roi.Row2) / 2.0;
                double centerCol = (roi.Col1 + roi.Col2) / 2.0;

                // 根据ROI形状自动计算角度
                double angle = 0;
                double deltaRow = roi.Row2 - roi.Row1;
                double deltaCol = roi.Col2 - roi.Col1;
                if (deltaRow != 0 || deltaCol != 0)
                {
                    angle = Math.Atan2(deltaRow, deltaCol) * 180.0 / Math.PI;
                }

                // 获取图像尺寸
                HTuple width, height;
                HOperatorSet.GetImageSize(image, out width, out height);

                // 生成卡尺工具（使用较小的尺寸以提高检测密度）
                HOperatorSet.GenMeasureRectangle2(
                    centerRow,
                    centerCol,
                    angle * Math.PI / 180.0,
                    tool.CaliperLength / 2.0,
                    tool.CaliperWidth / 2.0,
                    width,
                    height,
                    tool.SubPixelAccuracy ? "bicubic" : "nearest_neighbor",
                    out measureHandle
                );

                // 检测边缘
                HTuple rowEdge, columnEdge, amplitude, distance;
                HOperatorSet.MeasurePos(
                    image,
                    measureHandle,
                    1.0, // sigma
                    tool.EdgeThreshold,
                    tool.EdgePolarity,
                    tool.EdgeSelection,
                    out rowEdge,
                    out columnEdge,
                    out amplitude,
                    out distance
                );

                // 在图像上绘制检测到的边缘点
                if (rowEdge.Length > 0)
                {
                    hwindow.SetLineWidth(1);
                    hwindow.SetDraw("margin");
                    hwindow.SetColor(color);

                    // 绘制边缘点为小十字
                    for (int i = 0; i < rowEdge.Length; i++)
                    {
                        double row = rowEdge[i].D;
                        double col = columnEdge[i].D;

                        // 绘制小十字标记
                        hwindow.DispLine((double)(row - 2), (double)col, (double)(row + 2), (double)col); // 垂直线
                        hwindow.DispLine((double)row, (double)(col - 2), (double)row, (double)(col + 2)); // 水平线
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"卡尺预览失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (measureHandle != null)
                {
                    try { HOperatorSet.CloseMeasure(measureHandle); } catch { }
                }
            }
        }

        /// <summary>
        /// 按 ROI 维度逐侧绘制找边结果，不包含两侧之间的测距图层。
        ///
        /// 显示口径：
        /// - 清空窗口，重绘底图；
        /// - 对象1 ROI 框（绿色）→ 拟合线/圆（青色）→ 边缘点（调试时春绿色十字）→ 卡尺框（调试时橙色）；
        /// - 对象2 ROI 框（黄色）→ 同上；
        /// - 不绘制两侧之间的红色距离线和黄色计算延长线；这些属于测距图层，
        ///   由 MeasureLineToLine / MeasureLineToCircle / MeasureCircleToCircle 在 redraw=true 时绘制。
        ///
        /// 调用时机：
        /// - PreviewDimensionMeasurement：供单独排障场景使用。
        /// - DimensionMeasureApply_Click 不在测量后调用本方法，以保留测量时已绘制的完整测距图层。
        /// - CalibrateDimensionK_Click 不调用本方法；校准画面先准备主界面底图，再由 MeasureDimension 绘制测量层。
        ///
        /// 注意：本方法调用后会完全刷新 HALCON 窗口，若上游已绘制测距图层则会被覆盖。
        /// </summary>
        /// <param name="image">输入图像；调用后作为 HALCON 窗口底图重新显示。</param>
        /// <param name="tool">工具配置；ROI、测量类型和调试开关决定绘制内容。</param>
        /// <param name="hwindow">传入的 HALCON 显示窗口；当前配置页手动预览使用主界面 FaraVision/AOI 结果窗口。</param>
        /// <returns>true 表示两侧 ROI 均预览成功；false 表示任一侧找边失败或参数无效。</returns>
        public bool PreviewEdges(HObject image, ToolModel tool, HWindow hwindow)
        {
            try
            {
                if (image == null || hwindow == null)
                {
                    return false;
                }

                // 清空窗口并重新显示图像
                hwindow.ClearWindow();
                hwindow.DispObj(image);

                // 绘制ROI（注意：尺寸测量的ROI可能是线段/圆，不能一律当矩形画）
                hwindow.SetLineWidth(2);
                hwindow.SetDraw("margin");

                // 绘制测量对象1的ROI（绿色）
                if (IsROIValid(tool.MeasureObject1ROI))
                {
                    hwindow.SetColor("green");
                    if (tool.MeasureObject1ROI.Type == ROIType.Circle && tool.MeasureObject1ROI.CircleRadius > 0)
                    {
                        hwindow.DispCircle(tool.MeasureObject1ROI.CircleCenterRow, tool.MeasureObject1ROI.CircleCenterCol, tool.MeasureObject1ROI.CircleRadius);
                    }
                    else if (tool.MeasureObject1ROI.Type == ROIType.Line)
                    {
                        hwindow.DispLine((double)tool.MeasureObject1ROI.Row1, (double)tool.MeasureObject1ROI.Col1,
                                         (double)tool.MeasureObject1ROI.Row2, (double)tool.MeasureObject1ROI.Col2);
                    }
                    else
                    {
                        hwindow.DispRectangle1((double)tool.MeasureObject1ROI.Row1, (double)tool.MeasureObject1ROI.Col1,
                                             (double)tool.MeasureObject1ROI.Row2, (double)tool.MeasureObject1ROI.Col2);
                    }

                    // 预览测量对象1的边缘
                    PreviewEdgesForROI(image, tool, tool.MeasureObject1ROI, hwindow, "lime");
                }

                // 绘制测量对象2的ROI（黄色）
                if (IsROIValid(tool.MeasureObject2ROI))
                {
                    hwindow.SetColor("yellow");
                    if (tool.MeasureObject2ROI.Type == ROIType.Circle && tool.MeasureObject2ROI.CircleRadius > 0)
                    {
                        hwindow.DispCircle(tool.MeasureObject2ROI.CircleCenterRow, tool.MeasureObject2ROI.CircleCenterCol, tool.MeasureObject2ROI.CircleRadius);
                    }
                    else if (tool.MeasureObject2ROI.Type == ROIType.Line)
                    {
                        hwindow.DispLine((double)tool.MeasureObject2ROI.Row1, (double)tool.MeasureObject2ROI.Col1,
                                         (double)tool.MeasureObject2ROI.Row2, (double)tool.MeasureObject2ROI.Col2);
                    }
                    else
                    {
                        hwindow.DispRectangle1((double)tool.MeasureObject2ROI.Row1, (double)tool.MeasureObject2ROI.Col1,
                                             (double)tool.MeasureObject2ROI.Row2, (double)tool.MeasureObject2ROI.Col2);
                    }

                    // 预览测量对象2的边缘
                    PreviewEdgesForROI(image, tool, tool.MeasureObject2ROI, hwindow, "yellow");
                }

                // - 如需恢复WPF同步显示，取消下方注释：
                // UpdateWpfDisplayFromHWindow(hwindow);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"边缘预览失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从HALCON窗口获取当前显示内容并更新WPF的ShowBitmapSource
        /// 
        /// ⚠️ 重要提示：本方法已停用，不应再被调用
        /// 
        /// 停用原因：
        /// 1. **尺寸不确定性**：DumpWindowImage获取的图像尺寸取决于HALCON窗口大小
        ///    - 测试电脑和生产电脑的窗口尺寸可能不同
        ///    - 不同分辨率/DPI设置导致窗口尺寸差异
        ///    - 导致ShowBitmapSource尺寸变化不可预测
        /// </summary>
        /// <param name="hwindow">HALCON窗口</param>
        private void UpdateWpfDisplayFromHWindow(HWindow hwindow)
        {
            try
            {
                // 验证HALCON窗口状态
                if (hwindow == null)
                {
                    System.Diagnostics.Debug.WriteLine("HALCON窗口为空，跳过显示更新");
                    return;
                }

                // 强制刷新HALCON窗口，确保所有绘制操作已完成
                HOperatorSet.SetWindowParam(hwindow, "flush", "true");

                // 短暂延迟确保窗口刷新完成
                System.Threading.Thread.Sleep(50);

                // 从HALCON窗口获取当前显示的图像
                HObject windowImage = null;
                HOperatorSet.GenEmptyObj(out windowImage);
                HOperatorSet.DumpWindowImage(out windowImage, hwindow);

                // 验证获取的图像是否有效
                HTuple width, height;
                HOperatorSet.GetImageSize(windowImage, out width, out height);
                if (width.I <= 0 || height.I <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("获取的窗口图像无效");
                    windowImage?.Dispose();
                    return;
                }

                // 转换为WPF BitmapSource
                Hobject2Bitmap.HobjectToBitmap24(windowImage, out System.Drawing.Bitmap bmp);

                // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                IntPtr hBitmap = bmp.GetHbitmap();
                try
                {
                    BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                    // 更新WPF显示（先置null再赋值，确保绑定更新）
                    DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                    DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = bs;

                    // 强制触发UI更新
                    RaisePropertyChanged(() => DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource);
                }
                finally
                {
                    DeleteObject(hBitmap); // 释放GDI句柄
                }

                bmp?.Dispose();
                windowImage?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新WPF显示失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 尺寸测量的独立预览入口（仅显示找边结果，不包含测距图层）。
        ///
        /// 业务场景：
        /// - 供需要单独查看找边结果、而不执行完整测量流程的场景使用。
        /// - 典型调用方：ROI 调整后手动查看边缘分布，或排障时单独验证 Metrology 参数。
        ///
        /// 显示口径：
        /// - 委托 PreviewEdges 执行：原图 → ROI 框 → 每侧拟合线/圆 → 边缘点 → 卡尺框（调试勾选时）。
        /// - 不包含两侧之间的红色距离线、黄色计算延长线等测距图层；
        ///   完整测距图层由 MeasureDimension（redraw=true）在测量时同步绘制。
        /// - 不更新 WPF 的 ShowBitmapSource，只写 HALCON 窗口，不影响 ROI 绘制坐标映射。
        ///
        /// 调用时机：
        /// - DimensionMeasureApply_Click 不再在测量后调用本方法，避免其 ClearWindow 覆盖测量图层。
        /// - CalibrateDimensionK_Click 本身不调用本方法；校准画面先准备主界面底图，再由 MeasureDimension 绘制测量层。
        /// </summary>
        /// <param name="image">输入图像，通常是 tool.Image，决定 HALCON 窗口的底图。</param>
        /// <param name="tool">工具配置，包含 ROI、Metrology 参数和调试开关，决定预览内容。</param>
        /// <param name="hwindow">传入的 HALCON 显示窗口；当前配置页手动预览使用主界面 FaraVision/AOI 结果窗口。</param>
        public void PreviewDimensionMeasurement(HObject image, ToolModel tool, HWindow hwindow)
        {
            try
            {
                if (image == null || hwindow == null)
                {
                    return;
                }

                // 独立找边预览会重绘底图和 ROI 层；完整测距显示应走 MeasureDimension。
                PreviewEdges(image, tool, hwindow);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"尺寸测量预览失败: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// 将 Bitmap 等比缩放至不超过 W×H 的尺寸。
        /// </summary>
        public Bitmap GetReducedImage(double W, double H, Bitmap src)
        {
            try
            {
                double _wscale = W / (double)src.Width;
                double _Hscale = H / (double)src.Height;
                double _scale = Math.Min(_wscale, _Hscale);
                W = src.Width * _scale;
                H = src.Height * _scale;
                Bitmap r = new Bitmap((int)W, (int)H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                Graphics g = Graphics.FromImage(r);
                g.Clear(Color.Transparent);
                // 按比例绘制缩放图像
                g.DrawImage(src, new Rectangle(0, 0, (int)W, (int)H));
                g.Save();
                g.Dispose();
                return r;
            }
            catch (Exception e)
            {
                return null;
            }

        }

        /// <summary>
        /// 使用 HALCON 将 HObject 图像等比缩放至不超过 W×H 的尺寸。
        /// </summary>
        public HObject GetReducedImage(double W, double H, HObject src)
        {
            HObject dst;
            HOperatorSet.GenEmptyObj(out dst);

            try
            {
                HTuple width = new HTuple();
                HTuple height = new HTuple();

                HOperatorSet.GetImageSize(src, out width, out height);
                double _wscale = W / width;
                double _Hscale = H / height;
                double _scale = Math.Min(_wscale, _Hscale);

                W = width * _scale;
                H = height * _scale;
                HOperatorSet.ZoomImageSize(src, out dst, W, H, "constant");
                //Bitmap r = new Bitmap((int)W, (int)H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                //Graphics g = Graphics.FromImage(r);
                //g.Clear(Color.Transparent);
                //g.DrawImage(src, new Rectangle(0, 0, (int)W, (int)H));
                //g.Save();
                //g.Dispose();
                return dst;
            }
            catch (Exception e)
            {
                return null;
            }

        }

    }
}
