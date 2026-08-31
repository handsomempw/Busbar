using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.FaraVision;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.Record;
using GalaSoft.MvvmLight;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        // 日志相关方法拆分到独立文件，便于维护
        #region 通用数据日志
        private object writeLog_Locker = new object();

        private object writeBug_Locker = new object();

        /// <summary>
         /// 写入日志信息到文件和界面显示
         /// </summary>
        /// <param name="LogContent">日志内容</param>
        /// <param name="showdatarecord">是否在界面上显示日志记录，默认为 true</param>
        /// <remarks>
        /// 该方法执行以下操作：
        /// 1. 创建带时间戳的日志项
        /// 2. 如果 showdatarecord 为 true，则在 UI 线程中更新界面日志列表（最多保留 200 条）
        /// 3. 将日志写入到按日期命名的文本文件中（格式：yyyyMMdd.txt）
        /// 4. 日志文件保存路径：程序目录\日志\日志\
        /// </remarks>
        internal void writeLog(string LogContent, bool showdatarecord = true)
        {
            if (string.IsNullOrEmpty(LogContent)) { return; }

            //lock (writeLog_Locker)
            //{
            try
            {

                logitem logitem = new logitem()
                {
                    log = LogContent
                };
                string logstr = $"[{logitem.DateTime.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{LogContent}";
                if (showdatarecord)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        int num = 200;
                        if (DataModel.Recordmodel.workLog.Count > num)
                        {
                            //DataModel.Recordmodel.workLog.RemoveAt(DataModel.Recordmodel.workLog.Count - 1);
                            DataModel.Recordmodel.workLog.Clear();
                        }
                        DataModel.Recordmodel.workLog.Insert(0, logitem);
                    });
                }

                // 【日志落盘加锁】现场存在多线程写同一日志文件的情况，不加锁容易产生 txt 写入异常并刷屏
                lock (writeLog_Locker)
                {
                    string filename = $"{Environment.CurrentDirectory}\\日志\\日志\\{DateTime.Now.ToString("yyyyMMdd")}.txt";
                    string dir = Path.GetDirectoryName(filename);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    using (StreamWriter sw = new StreamWriter(filename, true))
                    {
                        sw.WriteLine(logstr);
                        sw.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                writeError(ex.Message + Environment.NewLine + ex.StackTrace);
            }
            //}
        }

        #region 尺寸测量日志（结果 + 诊断）
        /// <summary>
        /// 尺寸测量日志统一入口：在同一把锁内写入两份文件（结果日志 + 诊断日志）。
        /// 说明：
        /// - 结果日志保持简洁，便于现场/报表解析：日志\尺寸测量结果\{yyyyMMdd}.txt
        /// - 诊断日志用于排查一致性问题：日志\尺寸测量诊断\{yyyyMMdd}.txt
        /// - 同一把锁/同一时间戳：避免并发写入导致两份日志顺序难对齐
        /// </summary>
        private void WriteMeasurementLogs(ToolModel tool, Productinfo productInfo, DateTime measureTime)
        {
            lock (_measurementLogLock)
            {
                AppendDailyLogLine("尺寸测量结果", measureTime, BuildMeasurementResultLine(tool, productInfo, measureTime), "写入尺寸测量日志失败");
                AppendDailyLogLine("尺寸测量诊断", measureTime, BuildMeasurementDiagnosticLine(tool, productInfo, measureTime), "写入尺寸测量诊断日志失败");
            }
        }

        private string BuildMeasurementResultLine(ToolModel tool, Productinfo productInfo, DateTime measureTime)
        {
            string partNo = productInfo?.PartNOID ?? string.Empty;
            string woCode = productInfo?.WOCODE ?? string.Empty;
            string sn = productInfo?.SN ?? string.Empty;
            string toolName = tool?.Name ?? string.Empty;
            string measureType = tool?.MeasureType.ToString() ?? string.Empty;
            double min = tool?.MinMeasureValue ?? 0;
            double max = tool?.MaxMeasureValue ?? 0;
            double actual = tool?.ActualMeasureValue ?? 0;
            ToolStatus status = tool?.ToolStatus ?? ToolStatus.NG2;

            return $"[{measureTime:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"{partNo}|" +
                $"{woCode}|" +
                $"{sn}|" +
                $"{toolName}|" +
                $"{measureType}|" +
                $"{min:F3}~{max:F3}|" +
                $"{actual:F3}|" +
                $"{GetMeasurementStatusText(status)}";
        }

        private string BuildMeasurementDiagnosticLine(ToolModel tool, Productinfo productInfo, DateTime measureTime)
        {
            string partNo = productInfo?.PartNOID ?? string.Empty;
            string woCode = productInfo?.WOCODE ?? string.Empty;
            string sn = productInfo?.SN ?? string.Empty;
            string toolName = tool?.Name ?? string.Empty;
            string measureType = tool?.MeasureType.ToString() ?? string.Empty;

            double min = tool?.MinMeasureValue ?? 0;
            double max = tool?.MaxMeasureValue ?? 0;
            double actual = tool?.ActualMeasureValue ?? 0;
            double rawActual = tool?.LastRawMeasureValue ?? -1;
            ToolStatus status = tool?.ToolStatus ?? ToolStatus.NG2;

            // 图片只记录文件名，便于阅读；目录结构固定时可直接定位。
            string imageName = string.IsNullOrWhiteSpace(tool?.LastResultImagePath)
                ? string.Empty
                : Path.GetFileName(tool.LastResultImagePath);

            double calibratedUmPerPixel = tool?.DimensionK ?? 0;
            double measuredPixel = tool?.LastMeasurePixelValue ?? -1;
            string remeasureImageName = string.IsNullOrWhiteSpace(tool?.DimensionRemeasureImagePath)
                ? string.Empty
                : Path.GetFileName(tool.DimensionRemeasureImagePath);
            string remeasureMessage = SanitizeMeasurementLogText(tool?.DimensionRemeasureMessage);
            string remeasureOriginalError = SanitizeMeasurementLogText(tool?.DimensionRemeasureOriginalError);
            string remeasureError = SanitizeMeasurementLogText(tool?.DimensionRemeasureError);

            IMetrologyLineParameters primaryParameters = tool;
            string separateLineParameterTrace = string.Empty;
            if (tool != null &&
                tool.TestMode == TestModes.尺寸测量 &&
                tool.MeasureType == DimensionMeasureType.直线到直线)
            {
                DimensionLineMetrologyParameters line1Parameters = tool.GetDimensionLineMetrologyParameters(1);
                DimensionLineMetrologyParameters line2Parameters = tool.GetDimensionLineMetrologyParameters(2);
                primaryParameters = line1Parameters;
                separateLineParameterTrace =
                    $"直线1参数={FormatMetrologyLineParametersForLog(line1Parameters, tool.LastDimensionLine1FitScore, tool.LastDimensionLine1EdgePointCount)}|" +
                    $"直线2参数={FormatMetrologyLineParametersForLog(line2Parameters, tool.LastDimensionLine2FitScore, tool.LastDimensionLine2EdgePointCount)}|";
            }

            int threshold = primaryParameters?.MetrologyMeasureThreshold ?? 0;
            string select = primaryParameters?.MetrologyMeasureSelect ?? string.Empty;
            int numMeasures = primaryParameters?.MetrologyNumMeasures ?? 0;
            double minScore = primaryParameters?.MetrologyMinScore ?? 0;

            return $"[{measureTime:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"{partNo}|" +
                $"{woCode}|" +
                $"{sn}|" +
                $"{toolName}|" +
                $"{measureType}|" +
                $"{min:F3}~{max:F3}|" +
                $"{actual:F3}|" +
                $"{GetMeasurementStatusText(status)}|" +
                $"原始测量值(mm)={rawActual:F3}|" +
                $"图片={imageName}|" +
                $"标定像素尺寸(um/pixel)={calibratedUmPerPixel:F2}|" +
                $"原始测量像素值(px)={measuredPixel:F2}|" +
                $"边缘阈值={threshold}|" +
                $"边缘选择={select}|" +
                $"卡尺数量={numMeasures}|" +
                $"最小得分={minScore:F2}|" +
                separateLineParameterTrace +
                $"ROI1={FormatRoiForLog(tool?.MeasureObject1ROI)}|" +
                $"ROI2={FormatRoiForLog(tool?.MeasureObject2ROI)}|" +
                $"复测触发={(tool?.DimensionRemeasureAttempted == true ? "是" : "否")}|" +
                $"复测成功={(tool?.DimensionRemeasureSucceeded == true ? "是" : "否")}|" +
                $"复测说明={remeasureMessage}|" +
                $"复测图片={remeasureImageName}|" +
                $"复测值(mm)={(tool?.DimensionRemeasureMeasureValue ?? -1):F3}|" +
                $"复测像素(px)={(tool?.DimensionRemeasurePixelValue ?? -1):F2}|" +
                $"首次失败={remeasureOriginalError}|" +
                $"复测失败={remeasureError}";
        }

        /// <summary>
        /// 生成尺寸测量单侧 Metrology 参数与本次拟合结果的紧凑诊断文本。
        /// 两侧分别写入同一条尺寸诊断日志，便于现场对照亮暗过渡、采样密度和拟合质量；
        /// 该文本只服务追溯，不参与结果日志、尺寸判定或外部通信。
        /// </summary>
        /// <param name="parameters">当前侧实际使用的工程参数。</param>
        /// <param name="fitScore">当前侧最近一次拟合分数，范围 0～1。</param>
        /// <param name="edgePointCount">当前侧最近一次有效边缘点数量。</param>
        /// <returns>不含竖线分隔符的单侧参数摘要，可安全嵌入现有尺寸诊断日志字段。</returns>
        private static string FormatMetrologyLineParametersForLog(
            IMetrologyLineParameters parameters,
            double fitScore,
            int edgePointCount)
        {
            if (parameters == null)
            {
                return string.Empty;
            }

            return $"阈值:{parameters.MetrologyMeasureThreshold}," +
                $"过渡:{parameters.MetrologyMeasureTransition}," +
                $"选择:{parameters.MetrologyMeasureSelect}," +
                $"卡尺:{parameters.MetrologyNumMeasures}," +
                $"Sigma:{parameters.MetrologyMeasureSigma:F1}," +
                $"最小分:{parameters.MetrologyMinScore:F2}," +
                $"搜索半长:{parameters.MetrologyMeasureLength1}px," +
                $"沿线半宽:{parameters.MetrologyMeasureLength2}px," +
                $"拟合分:{fitScore:F2}," +
                $"边缘点:{edgePointCount}";
        }

        /// <summary>
        /// 规整尺寸测量诊断日志中的自由文本。
        /// 现场日志使用竖线分隔字段；异常文本写入前替换分隔符和换行，保证后续按列查看时不会错位。
        /// </summary>
        /// <param name="value">来自测量异常或复测诊断的文本。</param>
        /// <returns>可安全写入单行诊断日志的文本。</returns>
        private string SanitizeMeasurementLogText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("|", "/")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private void AppendDailyLogLine(string subDirName, DateTime measureTime, string line, string errorPrefix)
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\日志\\{subDirName}\\{measureTime:yyyyMMdd}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                {
                    sw.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                writeError($"{errorPrefix}: {ex.Message}\r\n{ex.StackTrace}");
            }
        }
        #endregion

        #region 模板匹配追溯日志
        /// <summary>
        /// 模板匹配专用追溯落盘，记录模型加载与 AOI 在线匹配结果，便于对照 shm、ROI 与 NG/NG2 判定。
        /// 输出路径：日志\模板匹配追溯\{yyyyMMdd}.txt
        /// </summary>
        /// <param name="eventKind">事件类型，如模型加载、在线匹配。</param>
        /// <param name="tool">模板匹配工具；读取 ROI、阈值与运行态测量值。</param>
        /// <param name="shmPath">形状模型完整路径；加载事件与匹配事件共用。</param>
        /// <param name="detail">补充说明，如加载失败原因、Match 返回的 ErrorInfo 或异常摘要。</param>
        /// <param name="productInfo">AOI 产品信息；可为 null（工程加载阶段）。</param>
        /// <param name="traceTime">追溯时间戳；默认取当前时间。</param>
        internal void WriteTemplateMatchTraceLog(
            string eventKind,
            ToolModel tool,
            string shmPath,
            string detail,
            Productinfo productInfo = null,
            DateTime? traceTime = null)
        {
            DateTime time = traceTime ?? DateTime.Now;
            lock (_templateMatchLogLock)
            {
                AppendDailyLogLine(
                    "模板匹配追溯",
                    time,
                    BuildTemplateMatchTraceLine(eventKind, tool, shmPath, detail, productInfo, time),
                    "写入模板匹配追溯日志失败");
            }
        }

        /// <summary>
        /// 组装模板匹配追溯单行文本，字段以竖线分隔便于现场检索与 Excel 分列。
        /// </summary>
        private string BuildTemplateMatchTraceLine(
            string eventKind,
            ToolModel tool,
            string shmPath,
            string detail,
            Productinfo productInfo,
            DateTime traceTime)
        {
            string partNo = productInfo?.PartNOID ?? string.Empty;
            string woCode = productInfo?.WOCODE ?? string.Empty;
            string sn = productInfo?.SN ?? string.Empty;
            string toolName = tool?.Name ?? string.Empty;
            int toolIndex = tool?.Index ?? 0;
            string roi = FormatRoiForLog(tool?.PositionROI);
            string imageName = string.IsNullOrWhiteSpace(tool?.LastResultImagePath)
                ? string.Empty
                : Path.GetFileName(tool.LastResultImagePath);
            double minScore = tool?.MinScore ?? 0;
            double candidateMinScore = tool?.CandidateMinScore ?? 0;
            double score = tool?.ActualScore ?? 0;
            double deltaX = tool?.DeltaX ?? 0;
            double deltaY = tool?.DeltaY ?? 0;
            double angle = tool?.ActualAngle ?? 0;
            ToolStatus status = tool?.ToolStatus ?? ToolStatus.NG2;
            double allowX = tool?.Allow_X_Delta ?? 0;
            double allowY = tool?.Allow_Y_Delta ?? 0;
            double allowAngle = tool?.AllowAngleDelta ?? 0;
            bool modelLoaded = tool?.ShapeMatch?.ModelLoaded ?? false;

            return $"[{traceTime:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"事件={eventKind}|" +
                $"{partNo}|{woCode}|{sn}|" +
                $"工具={toolIndex:00}-{toolName}|" +
                $"模型已加载={modelLoaded}|" +
                $"模型路径={shmPath}|" +
                $"ROI={roi}|" +
                $"CandidateMinScore={candidateMinScore:F3}|MinScore={minScore:F3}|Score={score:F3}|" +
                $"ΔXmm={deltaX:F3}|ΔYmm={deltaY:F3}|角度deg={angle:F2}|" +
                $"允许ΔXmm={allowX:F3}|允许ΔYmm={allowY:F3}|允许角度deg={allowAngle:F2}|" +
                $"判定={GetMeasurementStatusText(status)}|" +
                $"图片={imageName}|" +
                $"说明={detail}";
        }
        #endregion

        #region 定位矫正追溯日志
        /// <summary>
        /// 定位与 ROI 矫正统一追溯落盘。
        /// 输出路径：日志\定位矫正追溯\{yyyyMMdd}.txt
        /// </summary>
        internal void WriteLocatorCorrectionTraceLog(
            string eventKind,
            ToolModel tool,
            Productinfo productInfo,
            string detail,
            string originalRoiText = "",
            string effectiveRoiText = "",
            bool correctionApplied = false,
            DateTime? traceTime = null)
        {
            DateTime time = traceTime ?? DateTime.Now;
            lock (_locatorCorrectionLogLock)
            {
                AppendDailyLogLine(
                    "定位矫正追溯",
                    time,
                    BuildLocatorCorrectionTraceLine(
                        eventKind,
                        tool,
                        productInfo,
                        detail,
                        originalRoiText,
                        effectiveRoiText,
                        correctionApplied,
                        time),
                    "写入定位矫正追溯日志失败");
            }
        }

        private string BuildLocatorCorrectionTraceLine(
            string eventKind,
            ToolModel tool,
            Productinfo productInfo,
            string detail,
            string originalRoiText,
            string effectiveRoiText,
            bool correctionApplied,
            DateTime traceTime)
        {
            string partNo = productInfo?.PartNOID ?? string.Empty;
            string woCode = productInfo?.WOCODE ?? string.Empty;
            string sn = productInfo?.SN ?? string.Empty;
            string command = tool?.Command ?? string.Empty;
            string toolName = tool?.Name ?? string.Empty;
            int toolIndex = tool?.Index ?? 0;
            var state = DataModel.FaraVisionDataModel.Processmodel.LocatorCorrection;
            double refRow = state?.RefRow ?? 0;
            double refCol = state?.RefCol ?? 0;
            double refAngleDeg = (state?.RefAngleRad ?? 0) * 180.0 / Math.PI;
            double actRow = state?.ActRow ?? 0;
            double actCol = state?.ActCol ?? 0;
            double actAngleDeg = (state?.ActAngleRad ?? 0) * 180.0 / Math.PI;

            return $"[{traceTime:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"事件={eventKind}|Command={command}|{partNo}|{woCode}|{sn}|" +
                $"工具={toolIndex:00}-{toolName}|模式={tool?.TestMode}|" +
                $"跟随启用={(state?.FollowEnabled == true ? "是" : "否")}|" +
                $"定位生效={(state?.TransformActive == true ? "是" : "否")}|" +
                $"参考pose=({refRow:F1},{refCol:F1},{refAngleDeg:F2}deg)|" +
                $"实测pose=({actRow:F1},{actCol:F1},{actAngleDeg:F2}deg)|" +
                $"Score={(tool?.ActualScore ?? state?.MatchScore ?? 0):F3}|" +
                $"已应用矫正={(correctionApplied ? "是" : "否")}|" +
                $"原ROI={originalRoiText}|生效ROI={effectiveRoiText}|" +
                $"辅助状态={GetMeasurementStatusText(tool?.ToolStatus ?? ToolStatus.等待中)}|" +
                $"说明={detail}";
        }
        #endregion

        /// <summary>
        /// 将ROI格式化为可落盘的一行文本，便于现场快速比对是否“同一套ROI”。
        /// </summary>
        private static string FormatRoiForLog(ROI roi)
        {
            if (roi == null)
            {
                return "";
            }

            if (roi.Type == ROIType.Circle)
            {
                return $"C({roi.CircleCenterRow:F1},{roi.CircleCenterCol:F1},r={roi.CircleRadius:F1})";
            }

            // Rectangle / Line：记录四点坐标（现场足够用）
            return $"{roi.Type}({roi.Row1},{roi.Col1},{roi.Row2},{roi.Col2})";
        }

        /// <summary>
        /// 获取测量状态的文本描述
        /// </summary>
        /// <param name="status">工具状态</param>
        /// <returns>状态文本（OK/NG/NG2）</returns>
        private string GetMeasurementStatusText(ToolStatus status)
        {
            switch (status)
            {
                case ToolStatus.OK:
                    return "OK";
                case ToolStatus.NG:
                    return "NG";
                case ToolStatus.NG2:
                    return "NG2";
                case ToolStatus.定位未生效:
                    return "定位未生效";
                default:
                    return status.ToString();
            }
        }
        #endregion

        internal void writeError(string Content)
        {

            try
            {
                int num = 200;
                string bugstr = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{Content}";
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (DataModel.Recordmodel.ErrorLog.Count > num)
                    {
                        //DataModel.Recordmodel.workLog.RemoveAt(DataModel.Recordmodel.workLog.Count - 1);
                        DataModel.Recordmodel.ErrorLog.Clear();
                    }
                    DataModel.Recordmodel.ErrorLog.Insert(0, bugstr);
                });
                // 【错误落盘加锁】避免多线程同时写入导致“txt 相关异常”反复出现
                lock (writeBug_Locker)
                {
                    string filename = $"{Environment.CurrentDirectory}\\日志\\错误\\{DateTime.Now.ToString("yyyyMMdd")}.txt";
                    string dir = Path.GetDirectoryName(filename);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    using (StreamWriter sw = new StreamWriter(filename, true))
                    {
                        sw.WriteLine(bugstr);
                        sw.Close();
                    }
                }
            }
            catch (Exception)
            {
            }

        }

        /// <summary>
        /// PLC 相关错误专用落盘：避免高频 PLC 异常刷屏污染“日志\\错误”。
        /// 输出路径：日志\\PLC异常\\{yyyyMMdd}.txt
        /// 说明：UI 错误列表仍会显示该条目（便于现场快速看到），但落盘独立归档。
        /// </summary>
        internal void writePlcError(string Content)
        {
            try
            {
                int num = 200;
                string bugstr = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{Content}";
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (DataModel.Recordmodel.ErrorLog.Count > num)
                    {
                        DataModel.Recordmodel.ErrorLog.Clear();
                    }
                    DataModel.Recordmodel.ErrorLog.Insert(0, bugstr);
                });

                lock (writeBug_Locker)
                {
                    string filename = $"{Environment.CurrentDirectory}\\日志\\PLC异常\\{DateTime.Now.ToString("yyyyMMdd")}.txt";
                    string dir = Path.GetDirectoryName(filename);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    using (StreamWriter sw = new StreamWriter(filename, true))
                    {
                        sw.WriteLine(bugstr);
                        sw.Close();
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
