/*
 * 主要业务逻辑控制器
 *
 * MVVM架构中的ViewModel层核心，负责所有业务逻辑：
 * 1. 硬件设备控制：相机、PLC、测试仪器、机器人协调工作
 * 2. 生产流程管理：产品检测、测试、数据记录的完整流程
 * 3. 数据处理：XML序列化保存/加载配置，数据库交互
 * 4. 算法集成：Halcon视觉算法、位置检测算法等
 * 5. 多线程管理：并发处理多工位测试和检测任务
 *
 * 核心业务流程：
 * 产品进站 → 视觉检测 → 耐压测试 → 数据记录 → MES报工
 */

using BusbarCompressionSystem.Model;
using GalaSoft.MvvmLight;
using System.IO;
using System.Windows;
using System.Xml.Serialization;
using System;
using System.Globalization;
using AT9620;
using HslCommunication.ModBus;
using System.Data;
using System.Threading;
using System.Text;
using HalconDotNet;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Camera;
using TcpServerHelper;
using Panuon.WPF.UI;
using BusbarCompressionSystem.Model.Record;
using SQLITEDATABASE;
using BusbarCompressionSystem.Model.FaraVision.Tool.QRCode;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.FaraVision;
using PositionDetect;
using System.Drawing;
using System.Linq;
using Honeywell;
using System.Runtime.InteropServices; // 用于GDI句柄管理
using BusbarCompressionSystem.Utils;

namespace BusbarCompressionSystem.ViewModel
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
    public partial class MainViewModel : ViewModelBase
    {
        #region 静态锁对象
        /// <summary>
        /// 尺寸测量日志写入锁
        /// </summary>
        private static readonly object _measurementLogLock = new object();

        /// <summary>
        /// 模板匹配追溯日志写入锁
        /// </summary>
        private static readonly object _templateMatchLogLock = new object();

        /// <summary>
        /// 定位矫正追溯日志写入锁
        /// </summary>
        private static readonly object _locatorCorrectionLogLock = new object();
        #endregion

        /// <summary>
        /// AOI 整轮外观结果锁。
        /// 相机回调、机器人 CHECK 与界面记录可能处在不同线程，该锁保证同一 SN 的外观结果按检测轮次顺序折算。
        /// </summary>
        private readonly object _aoiInspectionResultLock = new object();

        /// <summary>
        /// 按 SN 保存当前 AOI 轮次的累计外观结果。
        /// 同一轮检测内任一参与判定的 AOI 指令 NG 后，最终出站与点检 OK 口径保持 NG；下一次拍照留底建档时重置该 SN 的轮次状态。
        /// </summary>
        private readonly Dictionary<string, bool> _aoiInspectionOverallResultBySn = new Dictionary<string, bool>();

        /// <summary>
        /// 耐压工位流程占用锁。PLC 触发、参数下发、启动测试和结果写回属于同一业务会话，
        /// 同台 AT9620 在会话结束前拒绝重复入口，避免 Download 与 Start 之间插入额外通信。
        /// </summary>
        private readonly object _tvProcessSessionLock = new object();

        /// <summary>
        /// 按 AT9620 实例记录正在执行的耐压工位流程，用于现场日志确认重复触发被哪个工位会话占用。
        /// </summary>
        private readonly Dictionary<AT9620.AT9620, string> _activeTvProcessByMeter = new Dictionary<AT9620.AT9620, string>();

        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public MainViewModel()
        {
            // 注入SQLite模块的UI日志回调：当底层检测到关键文件缺失等情况时，也能通过 writeLog 在界面提示
            sqlite.UiLog = msg => writeLog(msg);

            ////if (IsInDesignMode)
            ////{
            ////    // Code runs in Blend --> create design time data.
            ////}
            ////else
            ////{
            ////    // Code runs "for real"
            ////}
            ///
            Initrelaycommand();
        }

        public DataModel DataModel { get; set; } = new DataModel();

        /// <summary>
        /// 量产模式每个工单、每个 SN 的复测入口次数上限。
        /// 该口径固定为 5 次，不写入配置 XML；AOI 独立累计，ACW、DCW、IR 共用电测次数。
        /// </summary>
        public const int ProductionRetestWarningLimit = 5;

        private const string AoiRetestScope = "AOI";
        private const string ElectricalRetestScope = "ELECTRICAL";

        /// <summary>
        /// 判断当前电测触发是否代表产品进入本轮电测工序的首项。
        /// ACW、DCW、IR 属于同一个电测入口；当前测试模式只决定 ACW/DCW 的首项顺序，
        /// 计数表仍按 SN 和 ELECTRICAL 统计。仅有 IR 可用时，IR 作为本轮唯一入口承担计数。
        /// </summary>
        /// <param name="testType">本次 PLC 触发的子测试类型，取值为 ACW、DCW 或 IR。</param>
        /// <returns>true 表示本次 SN 校验应增加一次电测入口；false 表示属于本轮后续子测试。</returns>
        private bool IsFirstElectricalEntryTest(string testType)
        {
            string normalizedTestType = (testType ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizedTestType == "IR")
            {
                TVAvailable available = DataModel?.Processmodel?.TVAvailable;
                return available != null
                    && !available.TV1Available
                    && !available.TV2Available
                    && !available.TV3Available
                    && available.IRAvailable;
            }

            switch (DataModel.Settingmodel.CurrentTestMode)
            {
                case AT9620.ElectricalTestMode.ACWOnly:
                case AT9620.ElectricalTestMode.ACWThenDCW:
                    return normalizedTestType == "ACW";
                case AT9620.ElectricalTestMode.DCWOnly:
                case AT9620.ElectricalTestMode.DCWThenACW:
                    return normalizedTestType == "DCW";
                default:
                    return false;
            }
        }

        /// <summary>
        /// 切换 AOI 与电测入口次数的运行模式。
        /// 调机模式享有无限次数；量产模式按工单、SN 和检测类别持久化累计。该状态只影响次数记录和界面报警，
        /// 产品检测、PLC 回执、机器人交互、过程数据和 MES 业务保持现有流程。
        /// </summary>
        /// <param name="adjustmentMode">true 表示调机模式；false 表示量产模式。</param>
        /// <param name="reason">模式切换来源，用于操作员日志追溯授权、到期和手动选择。</param>
        public void SetRetestAdjustmentMode(bool adjustmentMode, string reason)
        {
            DataModel.FaraVisionDataModel.Settingmodel.IsRetestAdjustmentMode = adjustmentMode;
            string modeText = adjustmentMode ? "调机" : "量产";
            string policyText = adjustmentMode
                ? "AOI和电测入口次数不受限制"
                : $"AOI和电测分别按SN累计，上限{ProductionRetestWarningLimit}次，第{ProductionRetestWarningLimit + 1}次扫码拦截并报警";
            writeLog($"[复测模式] 已进入{modeText}模式，{policyText}，原因={reason}");
        }

        /// <summary>
        /// 在 AOI 或电测入口取得有效 SN 后记录一次环节入口并判断是否允许启动检测。
        /// AOI 在 CHECK1 前置条件全部通过后调用；电测只在当前测试模式首项调用，ACW、DCW、IR 后续子测试沿用本次入口。
        /// 调机模式写入操作日志；量产模式通过工单 SQLite 原子累加。第 6 次入口在启动检测前拦截，
        /// 只输出界面报警并由调用方回写当前工位的既有失败完成信号；计数和表结构异常继续沿用原流程。
        /// </summary>
        /// <param name="processScope">持久化统计类别；AOI 使用 AOI，ACW、DCW、IR 共用 ELECTRICAL。</param>
        /// <param name="stageName">操作员日志中的当前入口名称，例如 AOI、ACW、DCW 或 IR。</param>
        /// <param name="sn">环节开始前从 PLC 产品码校验得到的产品 SN。</param>
        /// <param name="wocode">与 SN 同时取得的工单号，用于定位本地工单数据库。</param>
        /// <param name="partnoid">当前产品规格编码，用于保持本地数据库定位接口一致。</param>
        /// <returns>true 表示当前入口可以继续检测；false 表示已完成 5 次放行入口，本次扫码在检测启动前拦截。</returns>
        private bool RecordRetestEntry(string processScope, string stageName, string sn, string wocode, string partnoid)
        {
            string normalizedSn = (sn ?? string.Empty).Trim();
            string normalizedWoCode = (wocode ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedSn) || string.IsNullOrEmpty(normalizedWoCode))
            {
                writeLog($"[复测次数][{stageName}] SN或工单为空，次数记录失败，当前流程继续", true);
                return true;
            }

            if (DataModel.FaraVisionDataModel.Settingmodel.IsRetestAdjustmentMode)
            {
                writeLog($"[调机复测扫码][{stageName}] SN={normalizedSn}，次数不限，当前流程继续");
                return true;
            }

            int entryCount;
            bool allowed;
            bool recorded = sqlite.TryIncrementRetestEntryCount(
                normalizedWoCode,
                partnoid,
                normalizedSn,
                processScope,
                ProductionRetestWarningLimit,
                out entryCount,
                out allowed);
            if (!recorded)
            {
                writeLog($"[量产复测扫码][{stageName}] SN={normalizedSn}，次数记录失败，当前流程继续", true);
                return true;
            }

            if (!allowed)
            {
                writeLog(
                    $"[量产复测扫码拦截][{stageName}] SN={normalizedSn}，当前已完成{entryCount}/{ProductionRetestWarningLimit}次，本次扫码超限，已拦截检测",
                    true);
                return false;
            }

            writeLog($"[量产复测扫码][{stageName}] SN={normalizedSn}，第{entryCount}/{ProductionRetestWarningLimit}次，剩余{ProductionRetestWarningLimit - entryCount}次，已进入检测流程");
            return true;
        }

        /// <summary>
        /// 将当前过程参数绑定到三台耐压仪，并把各工位运行态初始化为交流测试。
        /// 标准窗口和双Y专用窗口在设备连接前共用该入口；后续 PLC 触发仍可按产品测试模式切换 ACW/DCW 参数。
        /// </summary>
        public void InitializeElectricalRuntimeParameters()
        {
            DataModel.Processmodel.ACWParameter.TestMode = AT9620.TestMode.ACW;
            DataModel.Processmodel.DCWParameter.TestMode = AT9620.TestMode.DCW;

            DataModel.Settingmodel.AT9620_1.TVParameter = DataModel.Processmodel.ACWParameter;
            DataModel.Settingmodel.AT9620_2.TVParameter = DataModel.Processmodel.ACWParameter;
            DataModel.Settingmodel.AT9620_3.TVParameter = DataModel.Processmodel.ACWParameter;

            DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.ACW;
            DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.ACW;
            DataModel.Processmodel.LastTV3TestMode = AT9620.TestMode.ACW;
        }

        #region 操作
        /// <summary>
        /// 处理扫码数据，完成产品SN的解码、验证和数据库记录创建
        /// </summary>
        /// <param name="snstr">扫码原始数据字符串</param>
        /// <returns>
        /// 返回错误信息字符串，如果处理成功则返回空字符串
        /// 可能的错误信息包括：
        /// - 标签解码失败
        /// - 工单混批错误（扫码工单与参数下发批号不一致）
        /// - 混批错误（不同规格产品）
        /// - 工单号查询失败
        /// - 物料编码查询失败
        /// </returns>
        /// <remarks>
        /// 处理流程：
        /// 1. 解码扫码数据获取产品SN
        /// 2. 根据SN查询MES系统获取工单号和物料编码
        /// 3. 验证数据完整性（工单号和物料编码不能为空）
        /// 4. 验证扫码工单是否与参数下发批号一致（防止不同工单产品混测）
        /// 5. 验证物料编码是否与当前产线规格一致（防止不同规格混测）
        /// 6. 将SN和工单号写入PLC
        /// 7. 在本地数据库创建生产记录
        /// 业务逻辑：
        /// 1. 点检SN码（耐压、IR、AOI 的 OK/NG 标准件）：保留点检旁路，从 MES 取得工单和料号后创建本地记录
        /// 2. 普通SN码：调用MES解析获取产品信息，校验参数批号和规格一致性后创建本地记录
        /// </remarks>
        public string _ScanSN(string snstr)
        {
            if (IsDualYElectricalTestModeActive())
            {
                return ProcessScanSnToPlc(snstr, DataModel.DualYConfiguration.Station1ScanSnAddress, 1, true, true, "双Y-工位1扫码");
            }

            return ProcessScanSnToPlc(snstr, DataModel.Settingmodel.AddressSN, 1, false, false, "扫码");
        }

        /// <summary>
        /// 保存一次点检扫码经 MES 确认的工单、料号和本轮 IR 实测结果。
        /// 固定点检 SN 每次扫码都会覆盖同一键值，使 CHECK 只消费当前标准件的测试结果。
        /// </summary>
        private sealed class InspectionRunContext
        {
            /// <summary>本次点检扫码对应的 MES 工单号。</summary>
            public string WorkOrderCode { get; set; }

            /// <summary>本次点检扫码对应的 MES 料号。</summary>
            public string PartNoId { get; set; }

            /// <summary>本轮 IR 仪器实测结果；null 表示本轮测试尚未完成。</summary>
            public bool? IrActualOk { get; set; }

            /// <summary>本轮 IR 测试读取的接触电阻，单位沿用 PLC 当前标定。</summary>
            public float ContactResistance { get; set; }

            /// <summary>本轮 IR 仪器返回的绝缘电阻，单位沿用 AT6835FL 参数配置。</summary>
            public float IrResistance { get; set; }

            /// <summary>本轮 IR 仪器返回的漏电流，单位沿用 AT6835FL 参数配置。</summary>
            public float IrLeakCurrent { get; set; }

            /// <summary>带 [IR] 前缀的本轮仪器状态，用于 SQLite 与 MES 识别电测类型。</summary>
            public string IrInfo { get; set; }

            /// <summary>本轮 IR 测试使用的仪表编号。</summary>
            public string IrMeterId { get; set; }

            /// <summary>本轮 IR 过程行已经写入 SQLite 时为 true，供 CHECK 日志呈现本地追溯状态。</summary>
            public bool IrProcessRowSaved { get; set; }
        }

        /// <summary>
        /// 保护点检扫码上下文的跨线程读写。
        /// 扫码、IR 测试和机器人 CHECK 分别运行在设备事件线程，锁仅覆盖内存字段访问。
        /// </summary>
        private readonly object _inspectionRunContextSync = new object();

        /// <summary>
        /// 按点检 SN 保存最近一次扫码上下文。
        /// 配置最多包含耐压、IR、AOI 六个固定码，字典规模保持稳定。
        /// </summary>
        private readonly Dictionary<string, InspectionRunContext> _inspectionRunContexts =
            new Dictionary<string, InspectionRunContext>(StringComparer.Ordinal);

        /// <summary>
        /// 建立本次点检扫码上下文，并清空同一固定 SN 的上一轮 IR 结果。
        /// </summary>
        /// <param name="sn">已识别并归一化的点检 SN。</param>
        /// <param name="wocode">MES 返回的点检工单号。</param>
        /// <param name="partnoid">MES 返回的点检料号。</param>
        private void RememberInspectionRunContext(string sn, string wocode, string partnoid)
        {
            string normalizedSn = (sn ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedSn))
            {
                return;
            }

            lock (_inspectionRunContextSync)
            {
                _inspectionRunContexts[normalizedSn] = new InspectionRunContext
                {
                    WorkOrderCode = (wocode ?? string.Empty).Trim(),
                    PartNoId = (partnoid ?? string.Empty).Trim(),
                    IrActualOk = null,
                    IrProcessRowSaved = false
                };
            }
        }

        /// <summary>
        /// 取得点检扫码时由 MES 确认的料号，供 IR 落库和 CHECK 上传沿用同一产品上下文。
        /// 内存上下文缺失时再次查询 MES；查询失败时返回空值，由设备流程按 NG 处理并保留诊断日志。
        /// </summary>
        /// <param name="sn">当前点检 SN。</param>
        /// <param name="wocode">PLC 产品码携带的点检工单号。</param>
        /// <param name="fallbackPartNoId">正常产品使用的当前生产料号；点检产品始终使用 MES 料号。</param>
        /// <param name="stageTag">当前业务阶段，用于日志定位。</param>
        /// <returns>本次点检使用的料号。</returns>
        private string ResolveInspectionPartNo(string sn, string wocode, string fallbackPartNoId, string stageTag)
        {
            string fallback = (fallbackPartNoId ?? string.Empty).Trim();
            if (!IsInspectionSn(sn))
            {
                return fallback;
            }

            string normalizedSn = (sn ?? string.Empty).Trim();
            string normalizedWo = (wocode ?? string.Empty).Trim();
            lock (_inspectionRunContextSync)
            {
                InspectionRunContext context;
                if (_inspectionRunContexts.TryGetValue(normalizedSn, out context)
                    && string.Equals(context.WorkOrderCode, normalizedWo, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(context.PartNoId))
                {
                    return context.PartNoId;
                }
            }

            try
            {
                string mesPartNoId = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(normalizedSn);
                if (!string.IsNullOrWhiteSpace(mesPartNoId))
                {
                    mesPartNoId = mesPartNoId.Trim();
                    RememberInspectionRunContext(normalizedSn, normalizedWo, mesPartNoId);
                    writeLog($"[{stageTag}] 点检扫码上下文已通过MES恢复，SN={normalizedSn}, WO={normalizedWo}, PartNOID={mesPartNoId}");
                    return mesPartNoId;
                }
            }
            catch (Exception ex)
            {
                writeLog($"[{stageTag}] 点检料号恢复异常，SN={normalizedSn}, WO={normalizedWo}, 原因={ex.Message}", true);
            }

            writeLog($"[{stageTag}] 点检料号上下文缺失，SN={normalizedSn}, WO={normalizedWo}，已按点检数据不完整处理。", true);
            return string.Empty;
        }

        /// <summary>
        /// 保存本轮 IR 点检的仪器实测结果和 SQLite 落库状态。
        /// 该结果与 D1015 使用同一布尔值，CHECK 据此生成机器人分流回执。
        /// </summary>
        /// <param name="sn">当前 IR 点检 SN。</param>
        /// <param name="wocode">当前 IR 点检工单号。</param>
        /// <param name="partnoid">扫码时由 MES 确认的点检料号。</param>
        /// <param name="contactResistance">本轮 PLC 接触电阻。</param>
        /// <param name="irResistance">本轮 IR 仪器绝缘电阻。</param>
        /// <param name="irLeakCurrent">本轮 IR 仪器漏电流。</param>
        /// <param name="irInfo">本轮带 [IR] 前缀的仪器状态。</param>
        /// <param name="irMeterId">本轮 IR 仪表编号。</param>
        /// <param name="actualOk">IR 仪器实测结果。</param>
        /// <param name="processRowSaved">本轮 IR 过程行写入 SQLite 的结果。</param>
        private void RememberIrInspectionResult(
            string sn,
            string wocode,
            string partnoid,
            float contactResistance,
            float irResistance,
            float irLeakCurrent,
            string irInfo,
            string irMeterId,
            bool actualOk,
            bool processRowSaved)
        {
            if (!IsIrInspectionSn(sn))
            {
                return;
            }

            string normalizedSn = (sn ?? string.Empty).Trim();
            string normalizedWo = (wocode ?? string.Empty).Trim();
            lock (_inspectionRunContextSync)
            {
                InspectionRunContext context;
                if (!_inspectionRunContexts.TryGetValue(normalizedSn, out context)
                    || !string.Equals(context.WorkOrderCode, normalizedWo, StringComparison.Ordinal))
                {
                    context = new InspectionRunContext
                    {
                        WorkOrderCode = normalizedWo,
                        PartNoId = (partnoid ?? string.Empty).Trim()
                    };
                    _inspectionRunContexts[normalizedSn] = context;
                }

                context.IrActualOk = actualOk;
                context.ContactResistance = contactResistance;
                context.IrResistance = irResistance;
                context.IrLeakCurrent = irLeakCurrent;
                context.IrInfo = irInfo ?? string.Empty;
                context.IrMeterId = irMeterId ?? string.Empty;
                context.IrProcessRowSaved = processRowSaved;
            }
        }

        /// <summary>
        /// 读取本次扫码对应的 IR 测量快照，阻止固定点检 SN 复用上一轮缓存或 SQLite 历史结果。
        /// </summary>
        /// <param name="sn">当前 IR 点检 SN。</param>
        /// <param name="wocode">当前 IR 点检工单号。</param>
        /// <param name="resultContext">返回本轮 IR 结果和测量值的只读快照。</param>
        /// <param name="currentRunKnown">返回是否存在与当前 SN、工单匹配的扫码上下文。</param>
        /// <returns>本轮 IR 测试已完成并具有实测结果时返回 true。</returns>
        private bool TryGetIrInspectionResult(string sn, string wocode, out InspectionRunContext resultContext, out bool currentRunKnown)
        {
            resultContext = null;
            currentRunKnown = false;
            string normalizedSn = (sn ?? string.Empty).Trim();
            string normalizedWo = (wocode ?? string.Empty).Trim();

            lock (_inspectionRunContextSync)
            {
                InspectionRunContext context;
                if (!_inspectionRunContexts.TryGetValue(normalizedSn, out context)
                    || !string.Equals(context.WorkOrderCode, normalizedWo, StringComparison.Ordinal))
                {
                    return false;
                }

                currentRunKnown = true;
                if (!context.IrActualOk.HasValue)
                {
                    return false;
                }

                resultContext = new InspectionRunContext
                {
                    WorkOrderCode = context.WorkOrderCode,
                    PartNoId = context.PartNoId,
                    IrActualOk = context.IrActualOk,
                    ContactResistance = context.ContactResistance,
                    IrResistance = context.IrResistance,
                    IrLeakCurrent = context.IrLeakCurrent,
                    IrInfo = context.IrInfo,
                    IrMeterId = context.IrMeterId,
                    IrProcessRowSaved = context.IrProcessRowSaved
                };
                return true;
            }
        }

        /// <summary>
        /// 判断扫码值是否为已配置的点检标准件 SN。
        /// 点检包含耐压、IR 和 AOI 的 OK/NG 标准件；该判断只决定扫码旁路和 CHECK 分支，仪器实际结果仍决定 PLC 与机器人的分流回执。
        /// </summary>
        /// <param name="sn">扫码器或 PLC 提供的当前产品 SN。</param>
        /// <returns>命中任一非空点检配置码时返回 true。</returns>
        private bool IsInspectionSn(string sn)
        {
            return GetInspectionSnMatchCount(sn) > 0;
        }

        /// <summary>
        /// 统计当前扫码值命中的点检配置数量。
        /// 一个 SN 只能承担一种设备和一种期望结果；重复配置会在扫码及 CHECK 入口被拦截。
        /// </summary>
        /// <param name="sn">扫码器或 PLC 提供的当前产品 SN。</param>
        /// <returns>命中的非空点检配置数量。</returns>
        private int GetInspectionSnMatchCount(string sn)
        {
            int count = 0;
            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN)) count++;
            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN)) count++;
            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIROKSN)) count++;
            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIRNGSN)) count++;
            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN)) count++;
            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN)) count++;
            return count;
        }

        /// <summary>
        /// 判断当前 SN 是否属于耐压点检标准件，供双Y扫码放行和实测结算选择耐压点检口径。
        /// </summary>
        /// <param name="sn">扫码器或 PLC 提供的当前产品 SN。</param>
        /// <returns>耐压 OK/NG 任一点检码命中时返回 true。</returns>
        private bool IsTvInspectionSn(string sn)
        {
            return IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN)
                || IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN);
        }

        /// <summary>
        /// 判断耐压点检标准件的期望结果是否为 OK。
        /// 返回值只用于双Y点检日志和小屏展示；M3051/M3052 始终保留实测总结果语义。
        /// </summary>
        /// <param name="sn">已识别为耐压点检的当前 SN。</param>
        /// <returns>耐压 OK 点检码返回 true，耐压 NG 点检码返回 false。</returns>
        private bool IsTvOkInspectionSn(string sn)
        {
            return IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN);
        }

        /// <summary>
        /// 判断当前 SN 是否属于 AOI 点检标准件，供 CHECK 阶段选择 AOI 专用判定口径。
        /// </summary>
        /// <param name="sn">CHECK 从 PLC 产品码中解析的 SN。</param>
        /// <returns>AOI OK/NG 任一点检码命中时返回 true。</returns>
        private bool IsAoiInspectionSn(string sn)
        {
            return IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN)
                || IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN);
        }

        /// <summary>
        /// 判断当前 SN 是否属于 IR 绝缘电阻点检标准件，供 CHECK 阶段从本轮测量快照生成机器人分流回执。
        /// </summary>
        /// <param name="sn">CHECK 从 PLC 产品码中解析的 SN。</param>
        /// <returns>IR OK/NG 任一点检码命中时返回 true。</returns>
        private bool IsIrInspectionSn(string sn)
        {
            return IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIROKSN)
                || IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIRNGSN);
        }

        /// <summary>
        /// 判断 IR 点检标准件的期望结果是否为 OK。
        /// 返回值只用于点检命中留档；D1015 和 CHECK 回执始终保留仪器实测 OK/NG 语义。
        /// </summary>
        /// <param name="sn">已识别为 IR 点检的当前 SN。</param>
        /// <returns>IR OK 点检码返回 true，IR NG 点检码返回 false。</returns>
        private bool IsIrOkInspectionSn(string sn)
        {
            return IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIROKSN);
        }

        /// <summary>
        /// 将点检 SN 映射为操作员可识别的点检类型。
        /// 文本服务扫码日志，PLC、仪器和 CHECK 判定继续使用各自的实测结果口径；OK/NG 标准件属性一并展示，便于现场确认标准件身份。
        /// </summary>
        /// <param name="sn">已通过点检配置匹配的扫码 SN。</param>
        /// <returns>耐压、IR 或 AOI 点检类型及其 OK/NG 标准件属性。</returns>
        private string GetInspectionTypeText(string sn)
        {
            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN))
            {
                return "耐压点检（OK标准件）";
            }

            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN))
            {
                return "耐压点检（NG标准件）";
            }

            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIROKSN))
            {
                return "IR点检（OK标准件）";
            }

            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIRNGSN))
            {
                return "IR点检（NG标准件）";
            }

            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN))
            {
                return "AOI点检（OK标准件）";
            }

            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN))
            {
                return "AOI点检（NG标准件）";
            }

            return "未知点检类型";
        }

        /// <summary>
        /// 按精确文本匹配扫码值与点检配置码。
        /// 空配置不参与匹配，避免旧工程 XML 缺节点时把空扫码误识别为点检。
        /// </summary>
        /// <param name="sn">扫码或 PLC 解析后的产品 SN。</param>
        /// <param name="configuredSn">工程 XML 中配置的点检 SN。</param>
        /// <returns>两者均为非空文本且精确相等时返回 true。</returns>
        private static bool IsConfiguredInspectionSn(string sn, string configuredSn)
        {
            return !string.IsNullOrWhiteSpace(sn)
                && !string.IsNullOrWhiteSpace(configuredSn)
                && string.Equals(sn.Trim(), configuredSn.Trim(), StringComparison.Ordinal);
        }

        /// <summary>
        /// 双Y进站扫码业务入口。
        /// 2工位扫码与下料扫码器链路隔离，沿用标准扫码的 MES 解析、混批校验和本地建账规则，并按工位写入对应 PLC 产品码地址。
        /// </summary>
        /// <param name="snstr">扫码器读取到的原始条码。</param>
        /// <param name="dualYStationIndex">双Y工位号；1 写入 D800，2 写入 D950。</param>
        /// <returns>业务失败原因；空字符串表示扫码、PLC写码和本地建账已完成。</returns>
        internal string ScanDualYStationSn(string snstr, int dualYStationIndex)
        {
            int plcSnAddress = dualYStationIndex == 2
                ? DataModel.DualYConfiguration.Station2ScanSnAddress
                : DataModel.DualYConfiguration.Station1ScanSnAddress;

            return ProcessScanSnToPlc(snstr, plcSnAddress, dualYStationIndex, true, true, $"双Y-工位{dualYStationIndex}扫码");
        }

        /// <summary>
        /// 扫码进站共用业务链。
        /// 该方法负责 MES 解码、工单与规格校验、本地 SQLite 建账、PLC 产品码写入；双Y模式在同一链路上按工位选择产品码地址并开始独立测试会话。
        /// </summary>
        /// <param name="snstr">扫码器读取到的原始条码。</param>
        /// <param name="plcSnAddress">扫码成功后写入 PLC 的 D 寄存器地址，内容格式为 SN;WOCODE。</param>
        /// <param name="stationIndex">调用方所属工位，用于日志和双Y界面行归属。</param>
        /// <param name="beginDualYProductSession">双Y仅电测模式扫码成功后开始工位测试会话并刷新工位卡片。</param>
        /// <param name="plcWriteRequired">PLC 写码作为扫码放行条件时传 true，适用于 D950 等双Y进站链路。</param>
        /// <param name="contextTag">日志中的流程名称。</param>
        /// <returns>业务失败原因；空字符串表示扫码业务已完成。</returns>
        private string ProcessScanSnToPlc(string snstr, int plcSnAddress, int stationIndex, bool beginDualYProductSession, bool plcWriteRequired, string contextTag)
        {
            // 点检标准件跳过普通产品的工单一致性和混批校验，仍从 MES 取得工单、料号用于 PLC 移位和 SQLite 追溯。
            int inspectionMatchCount = GetInspectionSnMatchCount(snstr);
            if (inspectionMatchCount > 1)
            {
                writeLog($"[{contextTag}] 点检SN配置重复，扫码值={(snstr ?? string.Empty).Trim()}, 命中配置数={inspectionMatchCount}，已拦截进站。", true);
                return "点检SN配置重复，请检查耐压、IR、AOI点检码";
            }

            if (inspectionMatchCount == 1)
            {
                string inspectionSn = snstr.Trim();

                // 双Y仅电测不跑 IR/AOI；误扫对应点检码时在进站拦截，避免后续归档按错误点检类型处理。
                if (beginDualYProductSession && !IsTvInspectionSn(inspectionSn))
                {
                    writeLog($"[{contextTag}] 双Y仅支持耐压点检，已拦截非耐压点检码，类型={GetInspectionTypeText(inspectionSn)}, SN={inspectionSn}", true);
                    return "双Y仅支持耐压点检，请使用耐压OK/NG标准件";
                }

                writeLog($"[{contextTag}] 点检扫码原始数据: {inspectionSn}");
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(inspectionSn);
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(inspectionSn);

                if (string.IsNullOrWhiteSpace(wocode))
                {
                    writeLog($"[{contextTag}] 点检SN未从MES取得工单，SN={inspectionSn}", true);
                    return "点检关联工单读取失败";
                }
                if (string.IsNullOrWhiteSpace(partnoid))
                {
                    writeLog($"[{contextTag}] 点检SN未从MES取得料号，SN={inspectionSn}, WO={wocode}", true);
                    return "点检关联规格信息读取失败";
                }

                wocode = wocode.Trim();
                partnoid = partnoid.Trim();
                RememberInspectionRunContext(inspectionSn, wocode, partnoid);
                writeLog($"[{contextTag}] 点检类型：{GetInspectionTypeText(inspectionSn)}，SN={inspectionSn}");

                bool plcWriteOk = PLC_Writestring(plcSnAddress.ToString(), $"{inspectionSn};{wocode}");
                writeLog($"[{contextTag}] 写入PLC地址 D{plcSnAddress}: {(plcWriteOk ? "成功" : "失败")} | {inspectionSn};{wocode}");
                if (!plcWriteOk)
                {
                    // 【日志归置】PLC 写入异常属于设备/通讯类错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                    writePlcError($"[PLC写入异常]{contextTag}-写入产品码失败 | D{plcSnAddress}, 内容长度={(($"{inspectionSn};{wocode}")?.Length ?? 0)}, SN={inspectionSn}, WO={wocode}");
                    if (plcWriteRequired)
                    {
                        return "PLC产品码写入失败";
                    }
                }

                bool dbResult = PrepareScanPersistence(wocode, partnoid, inspectionSn, beginDualYProductSession, DateTime.Now);
                string persistenceAction = beginDualYProductSession ? "初始化本地测试数据库" : "创建数据库记录";
                writeLog($"[{contextTag}] {persistenceAction}: wocode={wocode}, partnoid={partnoid}, SN={inspectionSn}, 工位={DataModel.Settingmodel.SETTING_DATA.StationCode}, 设备={DataModel.Settingmodel.SETTING_DATA.MachineID}");
                if (!dbResult)
                {
                    writeLog($"[{contextTag}] 本地数据准备失败，wocode={wocode}, SN={inspectionSn}", true);
                    if (beginDualYProductSession)
                    {
                        return "本地测试数据库初始化失败";
                    }
                }

                if (beginDualYProductSession)
                {
                        BeginDualYProductSession(inspectionSn, wocode, partnoid, stationIndex);
                }

                writeLog($"[{contextTag}] 点检扫码处理成功");
                return string.Empty;
            }

            //if (string.IsNullOrEmpty(DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN))
            //{
            
            // 记录扫码原始数据
            writeLog($"[{contextTag}] 扫码原始数据: {snstr}");
            
            // 解码SN
            string sn = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.DecodeSN(snstr);
            writeLog($"[{contextTag}] 解码后的SN: {(string.IsNullOrEmpty(sn) ? "解码失败" : sn)}");
            
            if (!string.IsNullOrEmpty(sn))
            {
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(sn);
                writeLog($"[{contextTag}] 查询WOCODE: {(string.IsNullOrEmpty(wocode) ? "查询失败" : wocode)}");
                
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(sn);
                writeLog($"[{contextTag}] 查询PartNOID: {(string.IsNullOrEmpty(partnoid) ? "查询失败" : partnoid)}");
               
                // 数据完整性检查
                if (string.IsNullOrEmpty(wocode))
                {
                    return "关联批次号读取失败";
                }
                if (string.IsNullOrEmpty(partnoid))
                {
                    return "关联规格信息读取失败";
                }

                string workOrderError = ValidateScanWorkOrder(wocode, sn, partnoid);
                if (!string.IsNullOrEmpty(workOrderError))
                {
                    return workOrderError;
                }

                if (partnoid != DataModel.Processmodel.PartNOID)
                {
                    // 【日志】混批报错详细信息
                    writeLog($"[{contextTag}] 检测到混批，禁止不同规格产品混合作业", true);
                    writeLog($"  - 产线当前规格: {DataModel.Processmodel.PartNOID}", true);
                    writeLog($"  - 扫码产品规格: {partnoid}", true);
                    writeLog($"  - 产品SN: {sn}", true);
                    writeLog($"  - 工单号: {wocode}", true);
                    writeLog($"========== 扫码处理失败（混批错误）==========", true);
                    return $"混批错误！禁止不同规格产品混合作业\n当前规格: {DataModel.Processmodel.PartNOID}\n扫码规格: {partnoid}";
                }
                
                // 写入PLC和数据库
                bool plcWriteOk = PLC_Writestring(plcSnAddress.ToString(), $"{sn};{wocode}");
                writeLog($"[{contextTag}] 写入PLC地址 D{plcSnAddress}: {(plcWriteOk ? "成功" : "失败")} | {sn};{wocode}");
                if (!plcWriteOk)
                {
                    // 【日志归置】PLC 写入异常属于设备/通讯类错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                    writePlcError($"[PLC写入异常]{contextTag}-写入产品码失败 | D{plcSnAddress}, 内容长度={(($"{sn};{wocode}")?.Length ?? 0)}, SN={sn}, WO={wocode}");
                    if (plcWriteRequired)
                    {
                        return "PLC产品码写入失败";
                    }
                }
                
                string persistenceAction = beginDualYProductSession ? "初始化本地测试数据库" : "创建数据库记录";
                writeLog($"[{contextTag}] {persistenceAction}: wocode={wocode}, partnoid={partnoid}, SN={sn}, 工位={DataModel.Settingmodel.SETTING_DATA.StationCode}, 设备={DataModel.Settingmodel.SETTING_DATA.MachineID}");
                bool dbResult = PrepareScanPersistence(wocode, partnoid, sn, beginDualYProductSession, DateTime.Now);

                if (!dbResult)
                {
                    writeLog($"[{contextTag}] 本地数据准备失败，wocode={wocode}, SN={sn}", true);
                    if (beginDualYProductSession)
                    {
                        return "本地测试数据库初始化失败";
                    }
                }

                if (beginDualYProductSession)
                {
                    BeginDualYProductSession(sn, wocode, partnoid, stationIndex);
                }
                
                writeLog($"[{contextTag}] 扫码处理成功");
                return string.Empty;

            }
            else
            {
                return "标签读取失败,请确认该产品编号是否正常";
            }
            //}
            //else
            //{
            //    return "拍照位已经有产品编号，请勿重复扫码";
            //}
        }

        /// <summary>
        /// 准备扫码进站后的本地数据存储。
        /// 标准产线继续创建供拍照、电测和 CHECK 阶段逐步补齐的占位行；双Y只创建并校验工单数据库结构，
        /// 每次实际 ACW/DCW 测试完成后再独立插入过程行，保证数据库中的双Y电测行与仪器测试次数一一对应。
        /// </summary>
        /// <param name="wocode">MES 返回的当前产品工单号，用于定位本地工单数据库。</param>
        /// <param name="partnoid">MES 返回的当前产品规格编码。</param>
        /// <param name="sn">扫码确认后的产品序列号。</param>
        /// <param name="dualYSession">true 表示双Y仅电测会话；false 表示标准产线扫码流程。</param>
        /// <param name="scannedAt">标准产线占位行时间；双Y会话只用于保持统一调用口径。</param>
        /// <returns>工单数据库可用且对应模式的扫码持久化准备完成时返回 true。</returns>
        private bool PrepareScanPersistence(string wocode, string partnoid, string sn, bool dualYSession, DateTime scannedAt)
        {
            if (dualYSession)
            {
                return !string.IsNullOrWhiteSpace(sqlite.CheckDataBase(wocode, partnoid, sn));
            }

            return sqlite.CREATENEWLINE(
                wocode,
                partnoid,
                sn,
                DataModel.Settingmodel.SETTING_DATA.StationCode,
                DataModel.Settingmodel.SETTING_DATA.MachineID,
                scannedAt);
        }

        /// <summary>
        /// 校验普通产品扫码工单与参数下发批号的一致性。
        /// 该校验位于MES解析之后、PLC产品码写入之前，影响普通生产扫码放行；点检SN在扫码入口提前处理，保持原有点检路径。
        /// </summary>
        /// <param name="scanWorkOrder">扫码SN经MES查询得到的工单号，用于判定该产品归属批次。</param>
        /// <param name="sn">MES解码后的产品SN，用于异常日志追溯。</param>
        /// <param name="partnoid">扫码产品规格，用于异常日志追溯。</param>
        /// <returns>校验通过返回空字符串；返回非空文本时扫码失败，调用方停止PLC写入和本地记录创建。</returns>
        private string ValidateScanWorkOrder(string scanWorkOrder, string sn, string partnoid)
        {
            string currentWorkOrder = (DataModel.Processmodel.wocodeinputstr ?? string.Empty).Trim();
            string actualWorkOrder = (scanWorkOrder ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(currentWorkOrder))
            {
                writeLog($"[扫码工单校验失败] 当前参数工单为空，扫码工单={actualWorkOrder}, SN={sn}, 规格={partnoid}；已拦截扫码，未写入PLC产品码，未创建测试记录。", true);
                return "当前未下发工单参数，请先输入批号并下发参数";
            }

            if (!string.Equals(currentWorkOrder, actualWorkOrder, StringComparison.OrdinalIgnoreCase))
            {
                writeLog($"[扫码工单校验失败] 当前参数工单={currentWorkOrder}, 扫码工单={actualWorkOrder}, SN={sn}, 规格={partnoid}；已拦截扫码，未写入PLC产品码，未创建测试记录。", true);
                return $"工单混批错误！当前参数工单: {currentWorkOrder}\n扫码工单: {actualWorkOrder}\n产品SN: {sn}";
            }

            return string.Empty;
        }

        /// <summary>
        /// 扫描产品编号并进行MES校验
        /// </summary>
        /// <remarks>
        /// 触发方式：手动扫码（Enter键/按钮）或 自动扫码（PLC触发）
        /// 点检 SN 通过 MES 取得工单和料号，并跳过当前生产工单一致性及规格混批校验；普通 SN 执行完整进站校验。
        /// </remarks>
        /// <returns>true: 扫码成功; false: 扫码失败</returns>
        public bool ScanSN()
        {
            string s = _ScanSN(DataModel.Processmodel.sninputstr);
            if (!string.IsNullOrEmpty(s))
            {
                writeLog(s, true);
                return false;
            }
            else
            {
                DataModel.Processmodel.sninputstr = string.Empty;
                return true;
            }
            DataModel.Processmodel.TakePhotoTestModel.error = s;

        }

        /// <summary>
        /// 处理上一设备经 PLC 传入的已转换 SN。
        /// 该入口只替代扫码器取码和 MT 转 SN，后续仍执行 MES 工单/规格校验、旧产品码区写入和本地记录创建，保证后续 CHECK 流程读取到同一格式的 "SN;WOCODE"。
        /// </summary>
        /// <param name="resolvedSn">D725 提供的已转换产品 SN，按现场约定为最终 SN。</param>
        /// <returns>空字符串表示联动扫码业务完整成功；非空文本表示已中止且 M3047 保持未完成。</returns>
        private string ProcessLinkedScanResolvedSn(string resolvedSn)
        {
            string sn = (resolvedSn ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sn))
            {
                return "联动扫码读取到空SN，请确认上一设备已写入D725后再触发M3046";
            }

            writeLog($"[联动扫码] 读取上一设备已转换SN: {sn}");

            string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(sn);
            writeLog($"[联动扫码] 查询WOCODE: {(string.IsNullOrEmpty(wocode) ? "查询失败" : wocode)}");
            if (string.IsNullOrEmpty(wocode))
            {
                return "关联批次号读取失败";
            }

            string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(sn);
            writeLog($"[联动扫码] 查询PartNOID: {(string.IsNullOrEmpty(partnoid) ? "查询失败" : partnoid)}");
            if (string.IsNullOrEmpty(partnoid))
            {
                return "关联规格信息读取失败";
            }

            string workOrderError = ValidateScanWorkOrder(wocode, sn, partnoid);
            if (!string.IsNullOrEmpty(workOrderError))
            {
                return workOrderError;
            }

            if (partnoid != DataModel.Processmodel.PartNOID)
            {
                writeLog("[联动扫码] 检测到混批，已拦截上一设备SN进入本工位", true);
                writeLog($"  - 产线当前规格: {DataModel.Processmodel.PartNOID}", true);
                writeLog($"  - 联动SN规格: {partnoid}", true);
                writeLog($"  - 产品SN: {sn}", true);
                writeLog($"  - 工单号: {wocode}", true);
                return $"混批错误！禁止不同规格产品混合作业\n当前规格: {DataModel.Processmodel.PartNOID}\n联动SN规格: {partnoid}";
            }

            string productCode = $"{sn};{wocode}";
            bool plcWriteOk = PLC_Writestring(DataModel.Settingmodel.AddressSN.ToString(), productCode);
            writeLog($"[联动扫码] 写入PLC地址 {DataModel.Settingmodel.AddressSN}: {(plcWriteOk ? "成功" : "失败")} | {productCode}");
            if (!plcWriteOk)
            {
                writePlcError($"[PLC写入异常]联动扫码-写入AddressSN失败 | AddressSN={DataModel.Settingmodel.AddressSN}, 内容长度={(productCode?.Length ?? 0)}, SN={sn}, WO={wocode}");
                return "联动扫码写入PLC产品码失败";
            }

            writeLog($"[联动扫码] 创建数据库记录: wocode={wocode}, partnoid={partnoid}, SN={sn}, 工位={DataModel.Settingmodel.SETTING_DATA.StationCode}, 设备={DataModel.Settingmodel.SETTING_DATA.MachineID}");
            bool dbResult = sqlite.CREATENEWLINE(wocode, partnoid, sn, DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, DateTime.Now);
            if (!dbResult)
            {
                writeLog($"[联动扫码] 数据库记录创建失败！wocode={wocode}, SN={sn}", true);
                return "联动扫码数据库记录创建失败";
            }

            writeLog("[联动扫码] 扫码业务处理成功");
            return string.Empty;
        }
        #endregion
        #region 耐压测试

        public void InitAt9620()
        {
            DataModel.Settingmodel.AT9620_1.DataReceived += OnReceive1;
            DataModel.Settingmodel.AT9620_2.DataReceived += OnReceive2;
            DataModel.Settingmodel.AT9620_3.DataReceived += OnReceive3;
        }

        /// <summary>
        /// 初始化 AT6835FL 绝缘电阻测试仪：订阅串口日志事件
        /// </summary>
        public void InitAT6835FL()
        {
            DataModel.Settingmodel.AT6835FL_1.LogMessage += (s, e) =>
            {
                writeLog($"[IR测试] {e.Message}");
            };

            // 将 SETTING_DATA 中的 IR 串口调试开关同步到驱动运行时属性（避免“界面已勾选但驱动仍为非详细模式”）
            SyncIrAt6835RuntimeFlagsFromSettings("[启动]");
        }

        /// <summary>
        /// 同步 IR(AT6835FL) 驱动的运行时调试/通讯策略开关（来源：<see cref="SettingModel.SETTING_DATA"/>）。
        /// 说明：这些字段使用 XmlIgnore，不会随仪器串口 XML 一起持久化，因此每次通讯前同步最可靠。
        /// </summary>
        /// <param name="reason">简短原因标签，便于在主日志中定位调用点</param>
        public void SyncIrAt6835RuntimeFlagsFromSettings(string reason)
        {
            try
            {
                var sd = DataModel.Settingmodel?.SETTING_DATA;
                var ir = DataModel.Settingmodel?.AT6835FL_1;
                if (sd == null || ir == null)
                {
                    return;
                }

                // 仅保留一个 XML 开关：是否生成详细调试日志
                ir.VerboseSerialDebug = sd.IrDownloadDebugLog;

                // 固化生产策略：写前清空输入缓冲 + 下发时自检
                ir.DiscardInBufferBeforeWrite = true;
                ir.DownloadSelfCheck = true;

                string day = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                string dbgDir = Path.Combine(Environment.CurrentDirectory, "日志", "IR串口调试", day);
                writeLog($"{reason}[IR驱动] 运行时开关: Verbose={ir.VerboseSerialDebug}, DiscardIn={ir.DiscardInBufferBeforeWrite}, DownloadSelfCheck={ir.DownloadSelfCheck}");
                if (ir.VerboseSerialDebug)
                {
                    writeLog($"{reason}[IR驱动] 详细串口日志目录: {dbgDir}（文件名 IR_Serial_HHmmss_fff.txt，每次打开串口会新建会话）");
                }
            }
            catch
            {
                // 同步失败不影响主流程
            }
        }

        // 电测原始数据：按日期分子目录（yyyyMMdd），同目录下按 SN 分文件；诊断日志为 {SN}_diag.txt（不含工单料号），通信日志为 {SN}_comm.txt
        private readonly object _electricalRawLogLock = new object();
        private static readonly string ElectricalRawLogDir = Path.Combine(Environment.CurrentDirectory, "识别过程日志", "电测原始数据");
        private const int ElectricalLogRetainDays = 3;

        private static string GetElectricalLogDayDirectory()
        {
            return Path.Combine(ElectricalRawLogDir, DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 确保当天目录存在，并删除早于保留天数的日期子目录（仅匹配八位 yyyyMMdd 文件夹名）。
        /// </summary>
        private static void EnsureElectricalLogInfrastructure()
        {
            Directory.CreateDirectory(ElectricalRawLogDir);
            string todayDir = GetElectricalLogDayDirectory();
            Directory.CreateDirectory(todayDir);

            try
            {
                foreach (var dir in Directory.GetDirectories(ElectricalRawLogDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name != null && name.Length == 8 &&
                            DateTime.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        // 删除「距今已满 retain 天」的日期目录（含边界：满 7 天即删）
                        if ((DateTime.Today - d).Days >= ElectricalLogRetainDays)
                        {
                            try { Directory.Delete(dir, true); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // 清理失败不阻塞写日志
            }
        }

        private void WriteElectricalRawDataLog(int tvIndex, Productinfo productInfo, double voltage, double current, double time, string status)
        {
            try
            {
                if (productInfo == null || string.IsNullOrWhiteSpace(productInfo.SN))
                {
                    return;
                }

                lock (_electricalRawLogLock)
                {
                    EnsureElectricalLogInfrastructure();
                    string dayDir = GetElectricalLogDayDirectory();
                    string filePath = Path.Combine(dayDir, $"{productInfo.SN}.txt");
                    bool isNewFile = !File.Exists(filePath);

                    using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
                    {
                        if (isNewFile)
                        {
                            // 字段说明：时间戳、耐压机台、SN、工单、料号、实时电压/电流/时间、仪器状态（原始）
                            sw.WriteLine("时间\t机台\tSN\tWOCODE\tPartNOID\t电压\t电流\t时间\t状态");
                        }

                        sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\tTV{tvIndex}\t{productInfo.SN}\t{productInfo.WOCODE}\t{productInfo.PartNOID}\t{voltage}\t{current}\t{time}\t{status}");
                    }
                }
            }
            catch
            {
                // 原始数据日志不允许影响主流程，异常直接吞掉
            }
        }

        /// <summary>
        /// 电测诊断日志（一行一条，UTF-8 追加）。与原始数据同日期子目录，文件名为 {SN}_diag.txt。
        /// </summary>
        private void WriteElectricalDiagLog(string sn, string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sn) || string.IsNullOrEmpty(line))
                {
                    return;
                }

                lock (_electricalRawLogLock)
                {
                    EnsureElectricalLogInfrastructure();
                    string dayDir = GetElectricalLogDayDirectory();
                    string filePath = Path.Combine(dayDir, $"{sn.Trim()}_diag.txt");
                    using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
                    {
                        sw.WriteLine(line);
                    }
                }
            }
            catch
            {
                // 诊断日志不影响主流程
            }
        }

        /// <summary>
        /// 电测通信日志（一行一条，UTF-8 追加）。与原始数据同日期子目录，文件名为 {SN}_comm.txt。
        /// </summary>
        private void WriteElectricalCommLog(string sn, string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sn) || string.IsNullOrEmpty(line))
                {
                    return;
                }

                lock (_electricalRawLogLock)
                {
                    EnsureElectricalLogInfrastructure();
                    string dayDir = GetElectricalLogDayDirectory();
                    string filePath = Path.Combine(dayDir, $"{sn.Trim()}_comm.txt");
                    using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
                    {
                        sw.WriteLine(line);
                    }
                }
            }
            catch
            {
                // 通信日志不影响主流程
            }
        }

        /// <summary>
        /// 包一层耐压仪 Start：写入会话起止（不含工单/料号），并注入 AT9620 诊断回调。
        /// </summary>
        private global::AT9620.Result RunTvMeterStartWithDiagnostics(AT9620.AT9620 meter, string sn, string testModeLabel)
        {
            string normSn = (sn ?? string.Empty).Trim();
            if (meter == null)
            {
                return new global::AT9620.Result { Error = "仪器实例为空" };
            }

            if (string.IsNullOrWhiteSpace(normSn))
            {
                return meter.Start();
            }

            string sessionId = Guid.NewGuid().ToString("N");
            var tv = meter.TVParameter ?? new TVParameter();
            float total = tv.TestTime + tv.RiseTime + tv.FallTime;
            WriteElectricalDiagLog(normSn,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t【会话开始】会话ID={sessionId}\tSN={normSn}\t模式={testModeLabel}\t上升(s)={tv.RiseTime.ToString(CultureInfo.InvariantCulture)}\t保持(s)={tv.TestTime.ToString(CultureInfo.InvariantCulture)}\t下降(s)={tv.FallTime.ToString(CultureInfo.InvariantCulture)}\t理论总时长(s)={total.ToString(CultureInfo.InvariantCulture)}\t超时阈值(s)={(total + 2f).ToString(CultureInfo.InvariantCulture)}\t仪器IP={meter.IP}\t端口={meter.Port}");

            meter.DiagnosticLog = line => WriteElectricalDiagLog(normSn, line);
            meter.CommunicationLog = line => WriteElectricalCommLog(normSn, line);
            try
            {
                var r = meter.Start();
                string errDisplay = r.Error;
                if (string.IsNullOrEmpty(errDisplay) && r.Success)
                {
                    errDisplay = "合格";
                }
                WriteElectricalDiagLog(normSn,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t【会话结束】会话ID={sessionId}\t成功={(r.Success ? "是" : "否")}\t说明={errDisplay}\t过程串长度={r.Recordstr?.Length ?? 0}");
                return r;
            }
            finally
            {
                meter.DiagnosticLog = null;
                meter.CommunicationLog = null;
            }
        }

        private void OnReceive1(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel1.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel1.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel1.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel1.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel1.Voltage);
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel1.Current);
                DataModel.Processmodel.TVTestTestModel1.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 电测阶段原始数据落盘（按 SN、按日期子目录）
                WriteElectricalRawDataLog(1, DataModel.Processmodel.TVTestTestModel1.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);
            }
            catch (Exception ex)
            {
            }
        }
        private void OnReceive2(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel2.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel2.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel2.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel2.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel2.Voltage);
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel2.Current);
                DataModel.Processmodel.TVTestTestModel2.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 电测阶段原始数据落盘（按 SN、按日期子目录）
                WriteElectricalRawDataLog(2, DataModel.Processmodel.TVTestTestModel2.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);
            }
            catch (Exception ex)
            {
            }
        }
        private void OnReceive3(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel3.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel3.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel3.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel3.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel3.Voltage);
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel3.Current);
                DataModel.Processmodel.TVTestTestModel3.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 电测阶段原始数据落盘（按 SN、按日期子目录）
                WriteElectricalRawDataLog(3, DataModel.Processmodel.TVTestTestModel3.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);

            }
            catch (Exception ex)
            {
            }
        }

        #endregion
        #region PLC通讯

        /// <summary>
        /// 处理标准上料扫码器的 PLC 触发。标准产线沿用原扫码输入和结果反馈；
        /// 双Y模式将该设备固定为 Y1，并与 Y1 手动扫码及归档共享工位互斥和完整业务链。
        /// </summary>
        public void ScannerProcess()
        {
            bool dualYModeActive = IsDualYElectricalTestModeActive();
            bool dualYScanAcquired = false;
            if (dualYModeActive)
            {
                string busyReason;
                if (!TryBeginDualYScanProcessing(1, out busyReason))
                {
                    writeLog($"[双Y-工位1自动扫码] {busyReason}，忽略重复触发。", true);
                    return;
                }

                dualYScanAcquired = true;
            }

            try
            {
                string scannerMode = dualYModeActive
                    ? DataModel.DualYConfiguration.Station1ScannerMode
                    : DataModel.Settingmodel.ScannerMode;
                if (scannerMode == "HF800")
                {
                    var scanner = dualYModeActive
                        ? DataModel.DualYConfiguration.Station1HF800
                        : DataModel.Settingmodel.HF800;
                    var r = scanner.Scanner();
                    // 添加空值检查，防止r.value为null时崩溃
                    if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(r.value))
                    {
                        string s = r.value.Replace("\r", "").Replace("\n", "").Trim();
                        if (dualYModeActive)
                        {
                            ExecuteDualYStationScan(s, 1, "自动");
                        }
                        else
                        {
                            DataModel.Processmodel.sninputstr = s;
                            var businessOk = ScanSN();
                            PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(businessOk ? 1 : 2));
                        }
                    }
                    else
                    {
                        if (dualYModeActive)
                        {
                            CompleteDualYScanReadFailure(1, "自动", "扫码器未读取到有效产品编号");
                        }
                        else
                        {
                            PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                        }
                    }
                }
                else
                {
                    var scanner = dualYModeActive
                        ? DataModel.DualYConfiguration.Station1ScannerModel
                        : DataModel.Settingmodel.ScannerModel;
                    var r = scanner.Scanner();
                    string s = r.receivestring?.Replace("\r", "").Replace("\n", "").Trim() ?? "";
                    if (dualYModeActive)
                    {
                        if (r.IsSuccess && !string.IsNullOrEmpty(s))
                        {
                            ExecuteDualYStationScan(s, 1, "自动");
                        }
                        else
                        {
                            CompleteDualYScanReadFailure(1, "自动", "扫码器未读取到有效产品编号");
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(s))
                        {
                            DataModel.Processmodel.sninputstr = s;
                            var businessOk = ScanSN();
                            PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(r.IsSuccess && businessOk ? 1 : 2));
                        }
                        else
                        {
                            PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (dualYModeActive)
                {
                    CompleteDualYScanReadFailure(1, "自动", $"扫码处理异常：{ex.Message}");
                }
                else
                {
                    try
                    {
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                    }
                    catch { }
                }
            }
            finally
            {
                if (dualYScanAcquired)
                {
                    EndDualYScanProcessing(1);
                }
            }
        }

        /// <summary>
        /// 联动扫码流程入口。
        /// PLC 先将上一设备已转换 SN 写入 D725，再置位 M3046；上位机读取后复用本工位扫码放行链路，完整成功后向 M3047 写入完成信号。
        /// </summary>
        public void LinkedScannerProcess()
        {
            if (Interlocked.Exchange(ref _linkedScanProcessing, 1) == 1)
            {
                writeLog("[联动扫码] 上一笔联动扫码仍在处理中，忽略本次重复触发。", true);
                return;
            }

            try
            {
                PLC_WriteCoil(DataModel.Settingmodel.LinkedScanDoneAddress, false, "联动扫码完成信号");

                string linkedSn = (PLC_Readstring(DataModel.Settingmodel.LinkedScanSnAddress) ?? string.Empty)
                    .Replace("\0", string.Empty)
                    .Trim();

                if (string.IsNullOrWhiteSpace(linkedSn))
                {
                    writeLog($"[联动扫码] D{DataModel.Settingmodel.LinkedScanSnAddress}为空，已中止本次联动扫码，等待PLC超时或重试。", true);
                    return;
                }

                string result = ProcessLinkedScanResolvedSn(linkedSn);
                if (!string.IsNullOrEmpty(result))
                {
                    writeLog($"[联动扫码] {result}", true);
                    return;
                }

                bool doneOk = PLC_WriteCoil(DataModel.Settingmodel.LinkedScanDoneAddress, true, "联动扫码完成信号");
                if (doneOk)
                {
                    writeLog($"[联动扫码] 已写入M{DataModel.Settingmodel.LinkedScanDoneAddress}=1，通知PLC扫码业务完成");
                }
                else
                {
                    writePlcError($"[PLC写入异常]联动扫码-写入完成信号失败 | M{DataModel.Settingmodel.LinkedScanDoneAddress}=1, D{DataModel.Settingmodel.LinkedScanSnAddress}={linkedSn}");
                }
            }
            catch (Exception ex)
            {
                writeLog($"[联动扫码] 处理异常: {ex.Message}", true);
                writePlcError($"[PLC数据异常]联动扫码处理异常 | D{DataModel.Settingmodel.LinkedScanSnAddress}, M{DataModel.Settingmodel.LinkedScanTrigAddress}, 异常={ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _linkedScanProcessing, 0);
            }
        }

        public void SecondScannerProcess()
        {
            if (DataModel.Settingmodel.SecondScannerMode == "HF800")
            {
                var r = DataModel.Settingmodel.SecondHF800.Scanner();
                string s = r.value.Replace("\r", "").Replace("\n", "");
                DataModel.Processmodel.sninputstr = s;
                //SQLITEDATABASE.sqlite.CREATENEWLINE("1", "1", s);
                PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(r.Status == Honeywell.Status.OK ? 1 : 2));

                if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(s))
                {
                    // 写入SN到D1150
                    PLC_Writestring(DataModel.Settingmodel.SecondScanSNAddress.ToString(), s);
                }
            }
            else
            {
                var r = DataModel.Settingmodel.SecondScannerModel.Scanner();
                string s = r.receivestring.Replace("\r", "").Replace("\n", "");
                if (!String.IsNullOrEmpty(s))
                {
                    DataModel.Processmodel.sninputstr = s;

                    PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(r.IsSuccess ? 1 : 2));

                    if (r.IsSuccess)
                    {
                        // 写入SN到D1150
                        PLC_Writestring(DataModel.Settingmodel.SecondScanSNAddress.ToString(), s);
                    }
                }
                else
                {
                    PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(2));
                }
            }
        }
        /// <summary>
        /// 拍照留底工位处理流程：触发三个相机同时拍照并等待完成。
        /// </summary>
        /// <remarks>
        /// 触发条件：PLC拍照触发信号（M地址）由0变1时调用
        /// 
        /// 处理流程：
        /// 1. 从PLC读取产品编码（格式：SN;WOCODE），解析出SN和工单号
        /// 2. 重置三个相机的完成标志位
        /// 3. 同时触发三个相机执行拍照
        /// 4. 轮询等待三个相机全部完成（超时时间约3秒，每200ms检查一次，最多15次）
        /// 5. 根据拍照结果更新数据库记录，并创建内存中的产品过程记录
        /// 6. 向PLC写入完成信号：1=成功，2=超时失败
        /// 
        /// PLC交互地址：
        /// - 读取：D{AddressSN} - 产品编码字符串
        /// - 写入：D{AddressStart+3} - 拍照完成状态（1=OK, 2=NG）
        /// </remarks>
        public void TakePhoto1Process()
        {
            // 从PLC读取产品编码（协议格式：SN;WOCODE）。
            // PLC_Readstring() 本身已包含“通讯重试”，这里增加少量“重读”，用于缓解PLC尚未写入完成导致的瞬时异常（如读到空/0）。
            // 一旦最终仍读码失败：直接回写PLC=2并return，避免沿用上一次残留SN导致UI/照片/数据库被污染。
            const int maxReadRetry = 2;
            string s = string.Empty;
            string lastNonEmpty = string.Empty;
            string sn = string.Empty;
            string wocode = string.Empty;
            bool isValid = false;

            for (int attempt = 1; attempt <= maxReadRetry; attempt++)
            {
                s = (PLC_Readstring(DataModel.Settingmodel.AddressSN) ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    lastNonEmpty = s;
                }

                string[] parts = s.Split(';');
                if (parts.Length == 2)
                {
                    sn = (parts[0] ?? string.Empty).Trim();
                    wocode = (parts[1] ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(sn) && !string.IsNullOrWhiteSpace(wocode))
                    {
                        isValid = true;
                        break;
                    }
                }

                Thread.Sleep(80);
            }

            if (!isValid)
            {
                // 通讯问题通常表现为“读到空”，值问题通常表现为“读到0/不含;”。
                // 实际判断可结合 PLC_Readstring() 输出的 [PLC通讯] 日志进一步确认。
                string failureType = string.IsNullOrWhiteSpace(lastNonEmpty) ? "通讯/未写入" : "值/格式";
                string raw = string.IsNullOrWhiteSpace(lastNonEmpty) ? s : lastNonEmpty;

                writeLog($"[拍照留底] ❌ 产品编码读取错误({failureType})! 原始值=[{raw}], 期望格式=[SN;WOCODE], 重试次数={maxReadRetry}, PLC地址=D{DataModel.Settingmodel.AddressSN}", true);

                // 【日志归置】PLC 数据异常属于设备侧错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                writePlcError($"[PLC数据异常]TakePhoto1-产品编码读取失败 | 失败类型={failureType}, 原始值=[{raw}], 重试次数={maxReadRetry}, PLC地址=D{DataModel.Settingmodel.AddressSN}");

                // 清空当前产品信息，避免后续误用残留SN
                DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo()
                {
                    SN = string.Empty,
                    WOCODE = string.Empty,
                    PartNOID = DataModel.Processmodel.PartNOID
                };

                // 读码失败：直接回写NG(2)并中止本工位流程（不拍照/不写库/不插UI行）
                PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 2);
                return;
            }

            DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo()
            {
                SN = sn,
                WOCODE = wocode,
                PartNOID = DataModel.Processmodel.PartNOID
            };
            writeLog($"[拍照留底] 产品编码读取成功: SN={sn}, WOCODE={wocode}");

            // 开始拍照前，先清零三个相机的完成标志位；各相机在收到图片回调时会把 finished=true
            DataModel.Settingmodel.camedata1.CameraModel.finished = false;
            DataModel.Settingmodel.camedata2.CameraModel.finished = false;
            DataModel.Settingmodel.camedata3.CameraModel.finished = false;

            // 同时触发三个相机拍照；后续 while 循环轮询等待三个 finished 全部变为 true
            DataModel.Settingmodel.camedata1.CameraModel.camera.bnTriggerExec_Click();
            DataModel.Settingmodel.camedata2.CameraModel.camera.bnTriggerExec_Click();
            DataModel.Settingmodel.camedata3.CameraModel.camera.bnTriggerExec_Click();

            int i = 0;
            while (true)
            {
                Thread.Sleep(200);
                if ((DataModel.Settingmodel.camedata1.CameraModel.finished
                & DataModel.Settingmodel.camedata2.CameraModel.finished
                & DataModel.Settingmodel.camedata3.CameraModel.finished
                ))
                {
                    bool updateResult = SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, true);
                    if (!updateResult)
                    {
                        writeLog($"[拍照留底] ⚠ UpdateTakePhoto1更新true失败! SN={DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN}", true);
                    }
                    newline(true);
                    PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 1);
                    break;
                }
                if (i++ > 15)
                {
                    #region 超时未完成拍照

                    if (!DataModel.Settingmodel.camedata1.CameraModel.finished)
                    {
                        writeLog("相机1拍照超时");
                    }

                    if (!DataModel.Settingmodel.camedata2.CameraModel.finished)
                    {
                        writeLog("相机2拍照超时");
                    }

                    if (!DataModel.Settingmodel.camedata3.CameraModel.finished)
                    {
                        writeLog("相机3拍照超时");
                    }
                    bool updateResult = SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, false);
                    if (!updateResult)
                    {
                        writeLog($"[拍照留底] ⚠ UpdateTakePhoto1更新false(超时或者异常)失败! SN={DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN}", true);
                    }
                    newline(false);

                    PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 2);

                    break;
                    #endregion
                }

            }
        }

        /// <summary>
        /// 创建一条新的产品过程数据记录并加入到 DataModel.Recordmodel.ProductInfoRecords 集合中。
        /// 业务含义：
        /// - 在拍照留底完成后调用，将当前产品的基础信息、拍照结果缓存在内存集合里；
        /// - 后续耐压、压力、AOI 等过程都会通过 SN 在该集合中找到对应记录并补充数据，
        /// </summary>
        /// <param name="takephoto1">拍照留底是否成功</param>
        private void newline(bool takephoto1)
        {
            try
            {
                ProductInfoRecord p = new ProductInfoRecord()
                {
                    StationCode = DataModel.Settingmodel.SETTING_DATA.StationCode,
                    EQUIPMENTID = DataModel.Settingmodel.SETTING_DATA.MachineID,
                    Productinfo = DataModel.Processmodel.TakePhotoTestModel.Productinfo,
                    TakePhoto1 = takephoto1
                };
                ResetAoiInspectionOverallResult(p.Productinfo?.SN);
                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    DataModel.Recordmodel.ProductInfoRecords.Insert(0, p);
                }));
            }
            catch (Exception ex) { }
        }




        /// <summary>
        /// PLC 耐压工位入口互斥：同一台 AT9620 已在参数下发、启动测试或结果写回期间忽略重复触发，
        /// 不写入产品 NG，也不覆盖正在执行会话的 PLC/MES/SQLite 结果。
        /// </summary>
        /// <param name="meter">对应工位的 AT9620 实例。</param>
        /// <param name="stationLabel">工位日志前缀，例如“耐压1”。</param>
        /// <param name="testType">测试类型标识，例如 ACW 或 DCW。</param>
        /// <returns>仪器流程空闲且允许进入本流程时返回 true；已有会话时返回 false。</returns>
        private bool TryBeginTvProcessIfIdle(AT9620.AT9620 meter, string stationLabel, string testType)
        {
            if (meter == null)
            {
                return false;
            }

            string activeProcess = string.Empty;
            bool blockedByProcess = false;
            bool blockedByInstrument = false;
            lock (_tvProcessSessionLock)
            {
                if (_activeTvProcessByMeter.TryGetValue(meter, out activeProcess))
                {
                    blockedByProcess = true;
                }
                else if (meter.IsSessionActive)
                {
                    blockedByInstrument = true;
                }
                else
                {
                    _activeTvProcessByMeter[meter] = $"{stationLabel}-{testType}";
                    return true;
                }
            }

            if (blockedByProcess)
            {
                writeLog($"[{stationLabel}-{testType}] 仪器流程正在执行，忽略重复触发（当前流程={activeProcess}，仪器IP={meter.IP}，端口={meter.Port}）");
                return false;
            }

            if (blockedByInstrument)
            {
                writeLog($"[{stationLabel}-{testType}] 仪器正在参数下发或测试，忽略重复触发（仪器IP={meter.IP}，端口={meter.Port}）");
                return false;
            }

            return false;
        }

        /// <summary>
        /// 释放耐压工位流程占用。参数下发失败、测试异常和正常写回都经由该出口恢复 PLC 重复触发判断。
        /// </summary>
        /// <param name="meter">对应工位的 AT9620 实例。</param>
        private void EndTvProcessSession(AT9620.AT9620 meter)
        {
            if (meter == null)
            {
                return;
            }

            lock (_tvProcessSessionLock)
            {
                _activeTvProcessByMeter.Remove(meter);
            }
        }

        /// <summary>
        /// 识别 AT9620 会话互斥返回的忙碌结果。该结果只表示同台仪器通信链路已被占用，
        /// 上位机按 PLC 重复触发处理，不作为产品 NG、工艺参数失败或设备测试失败写回。
        /// </summary>
        /// <param name="error">AT9620 Download/Start 返回的错误说明。</param>
        /// <returns>错误说明属于仪器会话忙碌保护时返回 true。</returns>
        private bool IsTvMeterSessionBusyError(string error)
        {
            return error == AT9620.AT9620.ErrorDownloadBlockedByTest
                || error == AT9620.AT9620.ErrorDownloadBlockedByDownload
                || error == AT9620.AT9620.ErrorStartBlockedByDownload
                || error == AT9620.AT9620.ErrorStartBlockedByTest;
        }

        /// <summary>
        /// 统一处理耐压仪忙碌保护结果。重复触发只写现场日志并退出当前入口，
        /// 正在执行的原会话继续负责后续 PLC、MES 和 SQLite 结果流转。
        /// </summary>
        /// <param name="stationLabel">工位日志前缀，例如“耐压1”。</param>
        /// <param name="testType">测试类型标识，例如 ACW 或 DCW。</param>
        /// <param name="error">AT9620 Download/Start 返回的错误说明。</param>
        /// <returns>已按忙碌保护处理时返回 true；其它失败原因返回 false。</returns>
        private bool SkipTvProcessWhenMeterBusy(string stationLabel, string testType, string error)
        {
            if (!IsTvMeterSessionBusyError(error))
            {
                return false;
            }

            writeLog($"[{stationLabel}-{testType}] {error}，忽略本次重复触发");
            return true;
        }

        /// <summary>
        /// 单次耐压触发内允许的参数下发及回读校验总次数。
        /// 五次总尝试分别建立独立 TCP 连接，只覆盖仪器通信和参数校验，不重复启动电测，
        /// 也不改变最终 PLC、SQLite、MES 和产品判定口径。
        /// </summary>
        private const int TvParameterDownloadMaxAttempts = 5;

        /// <summary>
        /// 参数下发首次重试间隔，单位毫秒。
        /// 后续等待按尝试序号递增并限制在 2400ms 内，用于等待仪器释放上一轮短连接及残余应答，
        /// 不影响工艺参数中的上升、保持和下降时间。
        /// </summary>
        private const int TvParameterDownloadRetryDelayMs = 300;

        /// <summary>
        /// 标准产线单次耐压触发允许用于参数下发和回读校验的总等待上限，单位毫秒。
        /// 五轮完整尝试共用该时间窗；到达20秒后由工位入口记录通信异常，并以D=2通知PLC本次动作失败完成，
        /// PLC可据此跳过双电测第二项。该上限不计入耐压仪启动后的上升、保持和下降时间。
        /// </summary>
        private const int TvParameterDownloadTimeoutMs = 20000;

        /// <summary>
        /// 在同一次耐压工位触发中执行参数下发和回读校验，最多完成五轮完整尝试，每轮只回读一次参数。
        /// 五轮共用20秒总等待上限；每轮由 AT9620 建立独立连接，避免外层五轮与内层五次回读组合成25次查询。
        /// 仪器会话忙碌时立即返回，由现有重复触发保护继续处理。最终失败由各工位入口记录通信异常并回写D=2，
        /// 本方法不启动电测、不落SQLite、不上传MES。
        /// </summary>
        /// <param name="meter">当前耐压工位对应的 AT9620 实例；每次尝试使用实例中已经设置好的 ACW 或 DCW 参数。</param>
        /// <param name="stationLabel">现场日志中的耐压工位名称，例如“耐压1”或“耐压2”。</param>
        /// <param name="testType">本轮参数模式，值为 ACW 或 DCW，用于区分操作日志和故障追溯。</param>
        /// <returns>任一轮参数下发及单次回读校验成功时返回成功；五轮或20秒预算用尽时返回汇总失败原因。</returns>
        private global::AT9620.Result DownloadTvParametersWithRetry(AT9620.AT9620 meter, string stationLabel, string testType)
        {
            if (meter == null)
            {
                return new global::AT9620.Result { Error = "仪器实例为空" };
            }

            global::AT9620.Result lastResult = null;
            int completedAttempts = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int attempt = 1; attempt <= TvParameterDownloadMaxAttempts; attempt++)
            {
                int remainingMs = TvParameterDownloadTimeoutMs - (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
                if (remainingMs <= 0)
                {
                    break;
                }

                int receiveTimeoutMs = Math.Max(1, Math.Min(meter.OtherCommandReceiveTimeoutMs, remainingMs));
                lastResult = meter.Download(
                    parameterVerifyMaxAttempts: 1,
                    parameterVerifyReceiveTimeoutMs: receiveTimeoutMs);
                completedAttempts = attempt;
                if (lastResult.Success)
                {
                    if (attempt > 1)
                    {
                        writeLog($"[{stationLabel}-{testType}] 参数下发第{attempt}/{TvParameterDownloadMaxAttempts}轮校验成功，总耗时={stopwatch.ElapsedMilliseconds}ms");
                    }
                    return lastResult;
                }

                if (IsTvMeterSessionBusyError(lastResult.Error))
                {
                    return lastResult;
                }

                if (attempt < TvParameterDownloadMaxAttempts)
                {
                    int retryDelayMs = Math.Min(
                        TvParameterDownloadRetryDelayMs * (1 << (attempt - 1)),
                        2400);
                    int remainingAfterAttemptMs = TvParameterDownloadTimeoutMs
                        - (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
                    if (remainingAfterAttemptMs <= 0)
                    {
                        break;
                    }

                    retryDelayMs = Math.Min(retryDelayMs, remainingAfterAttemptMs);
                    writeLog($"[{stationLabel}-{testType}] 参数下发第{attempt}/{TvParameterDownloadMaxAttempts}轮失败，{retryDelayMs}ms后自动重试: {lastResult.Error}", true);
                    Thread.Sleep(retryDelayMs);
                }
            }

            string lastError = lastResult?.Error ?? "参数下发未返回结果";
            bool timeoutReached = stopwatch.ElapsedMilliseconds >= TvParameterDownloadTimeoutMs;
            return new global::AT9620.Result
            {
                Error = timeoutReached
                    ? $"参数下发达到{TvParameterDownloadTimeoutMs / 1000}秒总等待上限，已完成{completedAttempts}/{TvParameterDownloadMaxAttempts}轮；末次原因={lastError}"
                    : $"参数下发已完成{completedAttempts}/{TvParameterDownloadMaxAttempts}轮仍失败；末次原因={lastError}"
            };
        }

        /// <summary>
        /// 将标准产线参数下发失败记录为本轮电测通信异常，供 CHECK1、MES过程数据和最终报工识别。
        /// 本方法只补齐当前产品的失败追溯记录，不启动耐压测试、不改变PLC工位握手和值守分流。
        /// 双Y运行态继续由工位会话和测试记录ID管理，不进入标准产线占位行更新路径。
        /// </summary>
        /// <param name="stationIndex">耐压工位序号，1、2、3分别对应本站产品码和阻值PLC地址。</param>
        /// <param name="stationLabel">操作日志中的耐压工位名称，例如“耐压1”。</param>
        /// <param name="testType">参数下发模式，值为ACW或DCW，用于SQLite模式前缀和界面行定位。</param>
        /// <param name="meterId">当前工位耐压仪编号，随通信异常过程记录上传MES。</param>
        /// <param name="downloadError">五次参数下发最终失败说明，写入设备异常追踪日志。</param>
        private void PersistTvParameterDownloadCommunicationFailure(
            int stationIndex,
            string stationLabel,
            string testType,
            string meterId,
            string downloadError)
        {
            if (IsDualYElectricalTestModeActive())
            {
                writeLog($"[{stationLabel}-{testType}] 双Y参数下发失败沿用工位会话异常流程，原因={downloadError}", true);
                return;
            }

            string sn;
            string wocode;
            string rawCode;
            int productCodeAddress = DataModel.Settingmodel.AddressSN + 25 * stationIndex;
            if (!TryReadProductCodeFromPlc(productCodeAddress, $"{stationLabel}-{testType}-参数失败追溯", out sn, out wocode, out rawCode))
            {
                writeLog($"[{stationLabel}-{testType}] 参数下发失败记录未绑定产品，PLC地址=D{productCodeAddress}，原因={downloadError}", true);
                return;
            }

            string partnoid = DataModel.Processmodel.PartNOID;
            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + (stationIndex - 1) * 2);
            bool isAcw = string.Equals(testType, "ACW", StringComparison.OrdinalIgnoreCase);
            string tvInfo = TvStatusTranslator.AddTestModePrefix(
                $"{TvStatusTranslator.CommunicationFailureStatus}：参数下发失败",
                isAcw);

            bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isAcw, testType);
            bool saved = isSecondTest
                ? sqlite.InsertTV_SecondTest(
                    wocode, partnoid, sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res, 0, false, 0, tvInfo, meterId)
                : sqlite.UpdateTV(wocode, partnoid, sn, res, 0, false, 0, tvInfo, meterId);

            if (saved)
            {
                updatetv(sn, res, 0, false, 0, tvInfo, meterId, testType, wocode, partnoid);
                writeLog($"[{stationLabel}-{testType}] 参数下发失败已记录为电测通讯异常，SN={sn}，CHECK1按NG3待复测处理", true);
            }
            else
            {
                writeLog($"[{stationLabel}-{testType}] 参数下发失败记录写入SQLite失败，SN={sn}，原因={downloadError}", true);
            }

            sqlite.WriteErrorLog(
                "[耐压通讯异常]参数下发失败",
                $"工位={stationLabel}, 模式={testType}, 仪器={meterId}, SQLite写入={(saved ? "成功" : "失败")}, 原因={downloadError}",
                sn,
                wocode);
        }

        /// <summary>
        /// 识别标准产线双电测本轮首项是否已经形成失败结论。
        /// 当前测试模式决定首项顺序；首项记录必须同时是当前 SN 最新非 IR 记录，避免历史复测结果影响新一轮产品。
        /// 本方法只读取 SQLite 结果，不改变仪器、PLC、MES、阻值和压力状态；数据库未形成首项记录时继续执行既有第二项流程。
        /// </summary>
        /// <param name="wocode">当前工位产品码中的工单号，用于定位本地 SQLite 工单库。</param>
        /// <param name="partnoid">当前产品规格编码，用于保持现有数据库定位口径。</param>
        /// <param name="sn">当前工位产品码中的序列号，用于隔离并行工位和相邻产品。</param>
        /// <param name="firstTestRow">识别成功时返回本轮首项实际测试记录；其它情况返回 null。</param>
        /// <param name="communicationFailure">首项失败属于通信或流程异常时返回 true；仪器正常结束并判定 NG 时返回 false。</param>
        /// <returns>双电测首项已经形成 NG 或通信异常记录时返回 true。</returns>
        private bool TryGetStandardDualTestFirstFailure(
            string wocode,
            string partnoid,
            string sn,
            out sqlite.ElectricalTestProcessRow firstTestRow,
            out bool communicationFailure)
        {
            firstTestRow = null;
            communicationFailure = false;

            if (IsDualYElectricalTestModeActive() || IsAoiOnlyMode)
            {
                return false;
            }

            string firstTestType;
            switch (DataModel.Settingmodel.CurrentTestMode)
            {
                case AT9620.ElectricalTestMode.ACWThenDCW:
                    firstTestType = "ACW";
                    break;
                case AT9620.ElectricalTestMode.DCWThenACW:
                    firstTestType = "DCW";
                    break;
                default:
                    return false;
            }

            List<sqlite.ElectricalTestProcessRow> rows = sqlite.GetStandardElectricalTestProcessRows(wocode, partnoid, sn);
            sqlite.ElectricalTestProcessRow candidate = rows
                .Where(row => string.Equals(row.TestMode, firstTestType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.Id)
                .FirstOrDefault();
            long latestAttemptRecordId = sqlite.GetLatestStandardAttemptRecordId(wocode, partnoid, sn);
            if (candidate == null
                || candidate.Id <= 0
                || candidate.Id != latestAttemptRecordId
                || candidate.TVResult
                || string.IsNullOrWhiteSpace(candidate.TVInfo))
            {
                return false;
            }

            firstTestRow = candidate;
            communicationFailure = TvStatusTranslator.IsCommunicationFailure(candidate.TVInfo);
            return true;
        }

        /// <summary>
        /// 在标准产线双电测第二项触发入口执行首项失败短路。
        /// 首项耐压 NG 或通信异常已经足以确定本轮产品结论时，不再向仪器下发第二项参数，也不生成第二项测试记录；
        /// 对应 D1007、D1009、D1011 只表达“本次触发处理完成”，实际耐压结果继续由 M3041/M3042、SQLite 和 CHECK 流程表达。
        /// 首项参数或流程失败已通过 D=2 通知 PLC 跳过第二项；PLC 时序中已经到达上位机的第二项触发由本方法兜底完成，
        /// 并以 D=1 表示该次跳过动作处理完成。
        /// 单测、首项 PASS、点检、双Y以及无法确认本轮首项记录的场景继续执行既有测试流程。
        /// </summary>
        /// <param name="stationIndex">耐压工位序号；1、2、3分别映射完成信号 AddressStart+7、+9、+11。</param>
        /// <param name="stationLabel">现场日志使用的工位名称，例如“耐压1”。</param>
        /// <param name="testType">PLC 本次触发的测试模式，值为 ACW 或 DCW。</param>
        /// <returns>第二项已按首项失败完成短路并回写 PLC 完成信号时返回 true。</returns>
        private bool TrySkipStandardDualTestSecondStep(int stationIndex, string stationLabel, string testType)
        {
            string secondTestType;
            switch (DataModel.Settingmodel.CurrentTestMode)
            {
                case AT9620.ElectricalTestMode.ACWThenDCW:
                    secondTestType = "DCW";
                    break;
                case AT9620.ElectricalTestMode.DCWThenACW:
                    secondTestType = "ACW";
                    break;
                default:
                    return false;
            }

            if (IsDualYElectricalTestModeActive()
                || !string.Equals(testType, secondTestType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string sn;
            string wocode;
            string rawCode;
            int productCodeAddress = DataModel.Settingmodel.AddressSN + 25 * stationIndex;
            if (!TryReadProductCodeFromPlc(
                productCodeAddress,
                $"{stationLabel}-{testType}-双测短路判断",
                out sn,
                out wocode,
                out rawCode))
            {
                return false;
            }

            sqlite.ElectricalTestProcessRow firstTestRow;
            bool communicationFailure;
            if (!TryGetStandardDualTestFirstFailure(
                wocode,
                DataModel.Processmodel.PartNOID,
                sn,
                out firstTestRow,
                out communicationFailure))
            {
                return false;
            }

            int completionAddressOffset = 5 + stationIndex * 2;
            int completionAddress = DataModel.Settingmodel.AddressStart + completionAddressOffset;
            bool completionWritten = PLC_write(completionAddress.ToString(), (UInt16)1);
            string firstFailureType = communicationFailure ? "通信异常" : "耐压NG";
            writeLog(
                $"[{stationLabel}-{testType}] 双电测第二项因首项{firstFailureType}跳过，SN={sn}，首项={firstTestRow.TestMode}，"
                + $"未下发参数、未启动仪器、未生成第二项记录，D{completionAddress}=1完成信号写入{(completionWritten ? "成功" : "失败")}",
                !completionWritten);
            sqlite.WriteErrorLog(
                "[追踪]双测第二项跳过",
                $"工位={stationLabel}, SN={sn}, 首项={firstTestRow.TestMode}, 首项结果={firstFailureType}, 第二项={testType}, 完成地址=D{completionAddress}, 写入={(completionWritten ? "成功" : "失败")}",
                sn,
                wocode);
            return true;
        }

        /// <summary>
        /// 执行ACW交流耐压测试（TV1工位）
        /// </summary>
        /// <remarks>
        /// 当TV1Trig=1时调用此方法。
        /// 触发前统一下发ACW参数到AT9620设备。
        /// </remarks>
        public void TV1Process_ACW()
        {
            if (TrySkipStandardDualTestSecondStep(1, "耐压1", "ACW"))
            {
                return;
            }

            if (!TryBeginTvProcessIfIdle(DataModel.Settingmodel.AT9620_1, "耐压1", "ACW"))
            {
                return;
            }

            try
            {
                writeLog($"[耐压1-ACW] 开始ACW交流耐压测试");

                // 触发前统一下发参数，避免设备参数未同步
                writeLog($"[耐压1-ACW] 开始下发ACW参数");
                DataModel.Settingmodel.AT9620_1.TVParameter = DataModel.Processmodel.ACWParameter;
                var downloadResult = DownloadTvParametersWithRetry(DataModel.Settingmodel.AT9620_1, "耐压1", "ACW");
                if (!downloadResult.Success)
                {
                    if (SkipTvProcessWhenMeterBusy("耐压1", "ACW", downloadResult.Error))
                    {
                        return;
                    }

                    writeLog($"[耐压1-ACW] ❌ ACW参数下发失败: {downloadResult.Error}", true);
                    PersistTvParameterDownloadCommunicationFailure(
                        1, "耐压1", "ACW",
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                        downloadResult.Error);
                    // 参数流程失败：M3041写0禁止IR，D1007写2通知PLC失败完成并允许跳过双电测第二项。
                    WriteTvOkSignalForIrEnable(1, false);
                    PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                    return;
                }
                DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.ACW;
                writeLog($"[耐压1-ACW] ACW参数下发成功");
                Thread.Sleep(500);

                // 执行测试（复用现有逻辑）
                TV1Process_Core("ACW");
            }
            finally
            {
                EndTvProcessSession(DataModel.Settingmodel.AT9620_1);
            }
        }

        /// <summary>
        /// 执行DCW直流耐压测试（TV1工位）
        /// </summary>
        /// <remarks>
        /// 当TV1Trig=2时调用此方法。
        /// 触发前统一下发DCW参数到AT9620设备。
        /// </remarks>
        public void TV1Process_DCW()
        {
            if (TrySkipStandardDualTestSecondStep(1, "耐压1", "DCW"))
            {
                return;
            }

            if (!TryBeginTvProcessIfIdle(DataModel.Settingmodel.AT9620_1, "耐压1", "DCW"))
            {
                return;
            }

            try
            {
                writeLog($"[耐压1-DCW] 开始DCW直流耐压测试");

                // 触发前统一下发参数，避免设备参数未同步
                writeLog($"[耐压1-DCW] 开始下发DCW参数");
                DataModel.Settingmodel.AT9620_1.TVParameter = DataModel.Processmodel.DCWParameter;
                var downloadResult = DownloadTvParametersWithRetry(DataModel.Settingmodel.AT9620_1, "耐压1", "DCW");
                if (!downloadResult.Success)
                {
                    if (SkipTvProcessWhenMeterBusy("耐压1", "DCW", downloadResult.Error))
                    {
                        return;
                    }

                    writeLog($"[耐压1-DCW] ❌ DCW参数下发失败: {downloadResult.Error}", true);
                    PersistTvParameterDownloadCommunicationFailure(
                        1, "耐压1", "DCW",
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                        downloadResult.Error);
                    // 参数流程失败：M3041写0禁止IR，D1007写2通知PLC失败完成并允许跳过双电测第二项。
                    WriteTvOkSignalForIrEnable(1, false);
                    PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                    return;
                }
                DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.DCW;
                writeLog($"[耐压1-DCW] DCW参数下发成功");
                Thread.Sleep(500);

                // 执行测试（复用现有逻辑）
                TV1Process_Core("DCW");
            }
            finally
            {
                EndTvProcessSession(DataModel.Settingmodel.AT9620_1);
            }
        }

        /// <summary>
        /// TV1耐压测试核心逻辑（ACW/DCW共用）。
        /// 业务说明：只有读到本次完整的 SN;WOCODE 后才允许继续测试和落记录；
        /// 若读码失败，则视为该工位本次流程无法绑定产品，回写耐压NG并中止，避免沿用旧SN。
        /// </summary>
        /// <param name="testType">测试类型标识，用于日志区分（"ACW"或"DCW"）</param>
        private void TV1Process_Core(string testType)
        {
            string snCode;
            string woCode;
            string rawCode;
            if (TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 25, $"耐压1-{testType}", out snCode, out woCode, out rawCode))
            {
                DataModel.Processmodel.TVTestTestModel1.Productinfo = new Productinfo() { SN = snCode, WOCODE = woCode, PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压1-{testType}] 产品编码读取成功: SN={snCode}, WOCODE={woCode}");
            }
            else
            {
                // 读不到本次产品编码时不能继续电测，否则有串SN风险；回写本站NG让PLC走异常/重试分支。
                WriteTvOkSignalForIrEnable(1, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                return;
            }

            if (IsFirstElectricalEntryTest(testType)
                && !RecordRetestEntry(ElectricalRetestScope, testType, snCode, woCode, DataModel.Processmodel.PartNOID))
            {
                WriteTvOkSignalForIrEnable(1, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                return;
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
            DataModel.Processmodel.TVTestTestModel1.Res = res;

            DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;

            // 清空上一次测试残留，避免本次启动失败时沿用旧信息导致“结果/说明”不一致
            DataModel.Processmodel.TVTestTestModel1.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel1.Current = 0;
            DataModel.Processmodel.TVTestTestModel1.Time = 0;

            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_1, DataModel.Processmodel.TVTestTestModel1.Productinfo?.SN, testType);
            if (!r.Success && SkipTvProcessWhenMeterBusy("耐压1", testType, r.Error))
            {
                return;
            }

            // 记录耐压失败原因到界面日志，便于首件异常定位
            if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
            {
                // 将失败原因写入状态/信息，避免首件启动失败时界面仍显示上一件PASS
                //DataModel.Processmodel.TVTestTestModel1.Status = r.Error;
                //DataModel.Processmodel.TVTestTestModel1.TVInfo = r.Error;
                writeLog($"[耐压1-{testType}] 失败原因: {r.Error}", true);
            }

            // 获取翻译后的状态并添加测试模式前缀
            bool isACW = testType == "ACW";
            var localizedTvInfo1 = BuildPersistedTvStatus(
                r,
                DataModel.Processmodel.TVTestTestModel1.TVInfo,
                isACW);
            DataModel.Processmodel.TVTestTestModel1.TVInfo = localizedTvInfo1;

            string wocode = DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE;
            string partnoid = DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID;
            string sn = DataModel.Processmodel.TVTestTestModel1.Productinfo.SN;

            bool updateTvResult;
            if (IsDualYElectricalTestModeActive())
            {
                updateTvResult = PersistDualYTestAttempt(
                    1, sn, wocode, partnoid, res,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                    localizedTvInfo1,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                    testType);
            }
            else
            {
                // 标准产线保持占位行加双模式行的历史存储口径。
                bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isACW, testType);
                if (isSecondTest)
                {
                    updateTvResult = sqlite.InsertTV_SecondTest(wocode, partnoid, sn,
                        DataModel.Settingmodel.SETTING_DATA.StationCode,
                        DataModel.Settingmodel.SETTING_DATA.MachineID,
                        res,
                        DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                        r.Success,
                        DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                        localizedTvInfo1,
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID1);
                    writeLog($"[耐压1-{testType}] 双测模式第二次测试，插入新记录");
                }
                else
                {
                    updateTvResult = sqlite.UpdateTV(wocode, partnoid, sn,
                        res,
                        DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                        r.Success,
                        DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                        localizedTvInfo1,
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID1);
                }

                updatetv(sn, res,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                    localizedTvInfo1,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                    testType);
            }
            
            if (!updateTvResult)
            {
                writeLog($"[耐压1-{testType}] ⚠ 数据库更新失败! SN={sn}, RES={res}", true);
            }

            if (!r.Success)
            {
                var failureStatus1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus1,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                    r.Recordstr
                    );
            }

            writeLog($"[耐压1-{testType}] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}");
            // 将工位1耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(1, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), 1);
        }

        /// <summary>
        /// 检查当前测试是否为双测模式的第二次测试
        ///
        /// 业务逻辑说明：
        /// 在双测模式下，同一个产品需要进行交流(ACW)和直流(DCW)两种耐压测试。
        /// 系统会在TVInfo字段中添加测试模式前缀([ACW]或[DCW])来区分测试类型。
        /// 当检测到已有记录使用另一种测试模式时，说明当前是第二次测试。
        ///
        /// 处理逻辑：
        /// 1. 检查数据库中该SN最新的 ACW/DCW 记录，忽略独立 IR 行
        /// 2. 如果当前是ACW测试但数据库中有DCW记录，则为第二次测试
        /// 3. 如果当前是DCW测试但数据库中有ACW记录，则为第二次测试
        /// 4. 第二次测试时会插入新记录，而不是更新现有记录
        ///
        /// 设计考虑：
        /// - 通过TVInfo前缀标识测试模式，避免修改数据库结构
        /// - 确保双测模式下每种测试都有独立记录，便于数据追溯
        /// - 异常处理确保方法失败时不影响主测试流程
        /// </summary>
        /// <param name="wocode">工单号</param>
        /// <param name="partnoid">规格ID</param>
        /// <param name="sn">产品序列号</param>
        /// <param name="isACW">当前是否为交流测试</param>
        /// <param name="testType">测试类型标识("ACW"或"DCW")</param>
        /// <returns>true=第二次测试，false=第一次测试</returns>
        private bool CheckIsSecondTest(string wocode, string partnoid, string sn, bool isACW, string testType)
        {
            try
            {
                // 获取数据库连接字符串（按工单、规格、SN生成独立的数据库文件）
                string connstr = sqlite.CheckDataBase(wocode, partnoid, sn);
                if (!string.IsNullOrEmpty(connstr))
                {
                    // IR 也会新增独立电测行，但它不能参与 ACW/DCW 双测顺序判断；
                    // 这里只看最新一条耐压模式记录，避免 ACW -> IR -> DCW 时把 DCW 误判为第一次测试。
                    string checkSql = $"SELECT TVInfo FROM BusbarCompressionData WHERE sn='{sn}' AND (TVInfo LIKE '[ACW]%' OR TVInfo LIKE '[DCW]%') ORDER BY id DESC LIMIT 1";
                    var dt = sqlite.Read(checkSql, connstr);
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        // 获取最新记录的TVInfo信息，包含测试模式前缀
                        string existingTvInfo = dt.Rows[0]["TVInfo"]?.ToString() ?? "";

                        // 双测模式判断逻辑：
                        // 如果当前是ACW测试但数据库中已有DCW记录 → 第二次测试
                        // 如果当前是DCW测试但数据库中已有ACW记录 → 第二次测试
                        // 这种设计确保每种测试模式都有独立的记录
                        if ((isACW && existingTvInfo.StartsWith("[DCW]")) ||
                            (!isACW && existingTvInfo.StartsWith("[ACW]")))
                        {
                            writeLog($"[耐压-{testType}] 检测到双测模式，当前为第二次测试，已有记录TVInfo={existingTvInfo}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录异常但不影响主流程，双测模式检查失败时按第一次测试处理
                writeLog($"[耐压-{testType}] 检查双测模式时异常: {ex.Message}", true);
            }
            return false;
        }

        /// <summary>
        /// TV1 历史兼容入口。
        /// 当前 PLC 主生产路径按 TV1Trig 分流到 <see cref="TV1Process_ACW"/> / <see cref="TV1Process_DCW"/>；
        /// 本入口保留旧单模式流程，不负责 ACW/DCW 双测模式分流，调试或维护时不应作为当前生产主路径使用。
        /// </summary>
        public void TV1Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel1.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压1] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                writeLog($"[耐压1] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                // 【日志归置】PLC 数据异常属于设备侧错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                writePlcError($"[PLC数据异常]TV1-产品编码格式错误 | 原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
            DataModel.Processmodel.TVTestTestModel1.Res = res;

            DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;
            DataModel.Processmodel.TVTestTestModel1.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel1.Current = 0;
            DataModel.Processmodel.TVTestTestModel1.Time = 0;


            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_1, DataModel.Processmodel.TVTestTestModel1.Productinfo?.SN, DataModel.Processmodel.CurrentTV1TestModeDisplay);
            if (!r.Success && SkipTvProcessWhenMeterBusy("耐压1", DataModel.Processmodel.CurrentTV1TestModeDisplay, r.Error))
            {
                return;
            }

            var localizedTvInfo1 = BuildPersistedTvStatus(
                r,
                DataModel.Processmodel.TVTestTestModel1.TVInfo,
                DataModel.Processmodel.CurrentTV1TestModeDisplay == "ACW");
            DataModel.Processmodel.TVTestTestModel1.TVInfo = localizedTvInfo1;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1
                );
            if (!updateTvResult)
            {
                writeLog($"[耐压1] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel1.Productinfo.SN}, RES={res}", true);
            }

            updatetv(DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                DataModel.Processmodel.CurrentTV1TestModeDisplay
                );

            //if (r.Success)
            if (!r.Success)
            {
                var failureStatus1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus1,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                    r.Recordstr
                    );
            }

            // 兼容旧流程：将工位1耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(1, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), 1);
        }

        /// <summary>
        /// 执行ACW交流耐压测试（TV2工位）
        /// </summary>
        /// <remarks>
        /// 当TV2Trig=1时调用此方法。
        /// 触发前统一下发ACW参数到AT9620设备。
        /// </remarks>
        public void TV2Process_ACW()
        {
            if (TrySkipStandardDualTestSecondStep(2, "耐压2", "ACW"))
            {
                return;
            }

            if (!TryBeginTvProcessIfIdle(DataModel.Settingmodel.AT9620_2, "耐压2", "ACW"))
            {
                return;
            }

            try
            {
                writeLog($"[耐压2-ACW] 开始ACW交流耐压测试");

                // 触发前统一下发参数，避免设备参数未同步
                writeLog($"[耐压2-ACW] 开始下发ACW参数");
                DataModel.Settingmodel.AT9620_2.TVParameter = DataModel.Processmodel.ACWParameter;
                var downloadResult = DownloadTvParametersWithRetry(DataModel.Settingmodel.AT9620_2, "耐压2", "ACW");
                if (!downloadResult.Success)
                {
                    if (SkipTvProcessWhenMeterBusy("耐压2", "ACW", downloadResult.Error))
                    {
                        return;
                    }

                    writeLog($"[耐压2-ACW] ❌ ACW参数下发失败: {downloadResult.Error}", true);
                    PersistTvParameterDownloadCommunicationFailure(
                        2, "耐压2", "ACW",
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                        downloadResult.Error);
                    // 参数流程失败：M3042写0禁止IR，D1009写2通知PLC失败完成并允许跳过双电测第二项。
                    WriteTvOkSignalForIrEnable(2, false);
                    PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                    return;
                }
                DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.ACW;
                writeLog($"[耐压2-ACW] ACW参数下发成功");
                Thread.Sleep(500);

                // 执行测试（复用现有逻辑）
                TV2Process_Core("ACW");
            }
            finally
            {
                EndTvProcessSession(DataModel.Settingmodel.AT9620_2);
            }
        }

        /// <summary>
        /// 执行DCW直流耐压测试（TV2工位）
        /// </summary>
        /// <remarks>
        /// 当TV2Trig=2时调用此方法。
        /// 触发前统一下发DCW参数到AT9620设备。
        /// </remarks>
        public void TV2Process_DCW()
        {
            if (TrySkipStandardDualTestSecondStep(2, "耐压2", "DCW"))
            {
                return;
            }

            if (!TryBeginTvProcessIfIdle(DataModel.Settingmodel.AT9620_2, "耐压2", "DCW"))
            {
                return;
            }

            try
            {
                writeLog($"[耐压2-DCW] 开始DCW直流耐压测试");

                // 触发前统一下发参数，避免设备参数未同步
                writeLog($"[耐压2-DCW] 开始下发DCW参数");
                DataModel.Settingmodel.AT9620_2.TVParameter = DataModel.Processmodel.DCWParameter;
                var downloadResult = DownloadTvParametersWithRetry(DataModel.Settingmodel.AT9620_2, "耐压2", "DCW");
                if (!downloadResult.Success)
                {
                    if (SkipTvProcessWhenMeterBusy("耐压2", "DCW", downloadResult.Error))
                    {
                        return;
                    }

                    writeLog($"[耐压2-DCW] ❌ DCW参数下发失败: {downloadResult.Error}", true);
                    PersistTvParameterDownloadCommunicationFailure(
                        2, "耐压2", "DCW",
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                        downloadResult.Error);
                    // 参数流程失败：M3042写0禁止IR，D1009写2通知PLC失败完成并允许跳过双电测第二项。
                    WriteTvOkSignalForIrEnable(2, false);
                    PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                    return;
                }
                DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.DCW;
                writeLog($"[耐压2-DCW] DCW参数下发成功");
                Thread.Sleep(500);

                // 执行测试（复用现有逻辑）
                TV2Process_Core("DCW");
            }
            finally
            {
                EndTvProcessSession(DataModel.Settingmodel.AT9620_2);
            }
        }

        /// <summary>
        /// TV2耐压测试核心逻辑（ACW/DCW共用）。
        /// 业务说明：只有读到本次完整的 SN;WOCODE 后才允许继续测试和落记录；
        /// 若读码失败，则视为该工位本次流程无法绑定产品，回写耐压NG并中止，避免沿用旧SN。
        /// </summary>
        /// <param name="testType">测试类型标识，用于日志区分（"ACW"或"DCW"）</param>
        private void TV2Process_Core(string testType)
        {
            string snCode;
            string woCode;
            string rawCode;
            if (TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 25 * 2, $"耐压2-{testType}", out snCode, out woCode, out rawCode))
            {
                DataModel.Processmodel.TVTestTestModel2.Productinfo = new Productinfo() { SN = snCode, WOCODE = woCode, PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压2-{testType}] 产品编码读取成功: SN={snCode}, WOCODE={woCode}");
            }
            else
            {
                // 读不到本次产品编码时不能继续电测，否则有串SN风险；回写本站NG让PLC走异常/重试分支。
                WriteTvOkSignalForIrEnable(2, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                return;
            }

            if (IsFirstElectricalEntryTest(testType)
                && !RecordRetestEntry(ElectricalRetestScope, testType, snCode, woCode, DataModel.Processmodel.PartNOID))
            {
                WriteTvOkSignalForIrEnable(2, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                return;
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
            DataModel.Processmodel.TVTestTestModel2.Res = res;

            DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;

            // 清空上一次测试残留，避免本次启动失败时沿用旧信息导致“结果/说明”不一致
            DataModel.Processmodel.TVTestTestModel2.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel2.Current = 0;
            DataModel.Processmodel.TVTestTestModel2.Time = 0;

            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_2, DataModel.Processmodel.TVTestTestModel2.Productinfo?.SN, testType);
            if (!r.Success && SkipTvProcessWhenMeterBusy("耐压2", testType, r.Error))
            {
                return;
            }

            // 记录耐压失败原因到界面日志，便于首件异常定位
            if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
            {
                // 将失败原因写入状态/信息，避免首件启动失败时界面仍显示上一件PASS
                //DataModel.Processmodel.TVTestTestModel2.Status = r.Error;
                //DataModel.Processmodel.TVTestTestModel2.TVInfo = r.Error;
                writeLog($"[耐压2-{testType}] 失败原因: {r.Error}", true);
            }

            // 获取翻译后的状态并添加测试模式前缀
            bool isACW = testType == "ACW";
            var localizedTvInfo2 = BuildPersistedTvStatus(
                r,
                DataModel.Processmodel.TVTestTestModel2.TVInfo,
                isACW);
            DataModel.Processmodel.TVTestTestModel2.TVInfo = localizedTvInfo2;

            string wocode = DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE;
            string partnoid = DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID;
            string sn = DataModel.Processmodel.TVTestTestModel2.Productinfo.SN;

            bool updateTvResult;
            if (IsDualYElectricalTestModeActive())
            {
                updateTvResult = PersistDualYTestAttempt(
                    2, sn, wocode, partnoid, res,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                    localizedTvInfo2,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                    testType);
            }
            else
            {
                // 标准产线保持占位行加双模式行的历史存储口径。
                bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isACW, testType);
                if (isSecondTest)
                {
                    updateTvResult = sqlite.InsertTV_SecondTest(wocode, partnoid, sn,
                        DataModel.Settingmodel.SETTING_DATA.StationCode,
                        DataModel.Settingmodel.SETTING_DATA.MachineID,
                        res,
                        DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                        r.Success,
                        DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                        localizedTvInfo2,
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID2);
                    writeLog($"[耐压2-{testType}] 双测模式第二次测试，插入新记录");
                }
                else
                {
                    updateTvResult = sqlite.UpdateTV(wocode, partnoid, sn,
                        res,
                        DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                        r.Success,
                        DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                        localizedTvInfo2,
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID2);
                }

                updatetv(sn, res,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                    localizedTvInfo2,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                    testType);
            }
            
            if (!updateTvResult)
            {
                writeLog($"[耐压2-{testType}] ⚠ 数据库更新失败! SN={sn}, RES={res}", true);
            }

            if (!r.Success)
            {
                var failureStatus2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus2,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                    r.Recordstr
                    );
            }

            writeLog($"[耐压2-{testType}] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}");
            // 将工位2耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(2, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), 1);
        }

        /// <summary>
        /// TV2 历史兼容入口。
        /// 当前 PLC 主生产路径按 TV2Trig 分流到 <see cref="TV2Process_ACW"/> / <see cref="TV2Process_DCW"/>；
        /// 本入口保留旧单模式流程，不负责 ACW/DCW 双测模式分流，调试或维护时不应作为当前生产主路径使用。
        /// </summary>
        public void TV2Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel2.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压2] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                //writeLog($"耐压2产品编号读取错误:{s}");
                writeLog($"[耐压2] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                writeLog($"[耐压2] PLC地址: D{DataModel.Settingmodel.AddressSN + 25 * 2}, 请检查PLC寄存器值", true);
                // 【日志归置】PLC 数据异常属于设备侧错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                writePlcError($"[PLC数据异常]TV2-产品编码格式错误 | 原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25 * 2}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
            DataModel.Processmodel.TVTestTestModel2.Res = res;

            DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;
            DataModel.Processmodel.TVTestTestModel2.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel2.Current = 0;
            DataModel.Processmodel.TVTestTestModel2.Time = 0;
            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_2, DataModel.Processmodel.TVTestTestModel2.Productinfo?.SN, DataModel.Processmodel.CurrentTV2TestModeDisplay);
            if (!r.Success && SkipTvProcessWhenMeterBusy("耐压2", DataModel.Processmodel.CurrentTV2TestModeDisplay, r.Error))
            {
                return;
            }

            var localizedTvInfo2 = BuildPersistedTvStatus(
                r,
                DataModel.Processmodel.TVTestTestModel2.TVInfo,
                DataModel.Processmodel.CurrentTV2TestModeDisplay == "ACW");
            DataModel.Processmodel.TVTestTestModel2.TVInfo = localizedTvInfo2;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
               DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
               localizedTvInfo2,
               DataModel.Settingmodel.SETTING_DATA.TVMeterID2
               );
            if (!updateTvResult)
            {
                writeLog($"[耐压2] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel2.Productinfo.SN}, RES={res}", true);
            }

            updatetv(DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                res, DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                localizedTvInfo2,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                DataModel.Processmodel.CurrentTV2TestModeDisplay

                );

            if (!r.Success)
            {
                var failureStatus2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus2,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                    r.Recordstr
                    );
            }

            // 兼容旧流程：将工位2耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(2, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), 1);

        }

        /// <summary>
        /// 执行ACW交流耐压测试（TV3工位）。
        /// D1010=1时调用本入口；D1011仅作为本站流程握手，产品质量结论由SQLite记录和CHECK综合判定。
        /// </summary>
        public void TV3Process_ACW()
        {
            if (TrySkipStandardDualTestSecondStep(3, "耐压3", "ACW"))
            {
                return;
            }

            if (!TryBeginTvProcessIfIdle(DataModel.Settingmodel.AT9620_3, "耐压3", "ACW"))
            {
                return;
            }

            try
            {
                writeLog($"[耐压3-ACW] 开始ACW交流耐压测试");

                writeLog($"[耐压3-ACW] 开始下发ACW参数");
                DataModel.Settingmodel.AT9620_3.TVParameter = DataModel.Processmodel.ACWParameter;
                var downloadResult = DownloadTvParametersWithRetry(DataModel.Settingmodel.AT9620_3, "耐压3", "ACW");
                if (!downloadResult.Success)
                {
                    if (SkipTvProcessWhenMeterBusy("耐压3", "ACW", downloadResult.Error))
                    {
                        return;
                    }

                    writeLog($"[耐压3-ACW] ❌ ACW参数下发失败: {downloadResult.Error}", true);
                    PersistTvParameterDownloadCommunicationFailure(
                        3, "耐压3", "ACW",
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID3,
                        downloadResult.Error);
                    // 参数流程失败：D1011写2通知PLC失败完成并允许跳过双电测第二项；产品追溯由SQLite通信异常记录承接。
                    PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), (UInt16)2);
                    return;
                }
                DataModel.Processmodel.LastTV3TestMode = AT9620.TestMode.ACW;
                writeLog($"[耐压3-ACW] ACW参数下发成功");
                Thread.Sleep(500);

                TV3Process_Core("ACW");
            }
            finally
            {
                EndTvProcessSession(DataModel.Settingmodel.AT9620_3);
            }
        }

        /// <summary>
        /// 执行DCW直流耐压测试（TV3工位）。
        /// D1010=2时调用本入口；D1011仅作为本站流程握手，产品质量结论由SQLite记录和CHECK综合判定。
        /// </summary>
        public void TV3Process_DCW()
        {
            if (TrySkipStandardDualTestSecondStep(3, "耐压3", "DCW"))
            {
                return;
            }

            if (!TryBeginTvProcessIfIdle(DataModel.Settingmodel.AT9620_3, "耐压3", "DCW"))
            {
                return;
            }

            try
            {
                writeLog($"[耐压3-DCW] 开始DCW直流耐压测试");

                writeLog($"[耐压3-DCW] 开始下发DCW参数");
                DataModel.Settingmodel.AT9620_3.TVParameter = DataModel.Processmodel.DCWParameter;
                var downloadResult = DownloadTvParametersWithRetry(DataModel.Settingmodel.AT9620_3, "耐压3", "DCW");
                if (!downloadResult.Success)
                {
                    if (SkipTvProcessWhenMeterBusy("耐压3", "DCW", downloadResult.Error))
                    {
                        return;
                    }

                    writeLog($"[耐压3-DCW] ❌ DCW参数下发失败: {downloadResult.Error}", true);
                    PersistTvParameterDownloadCommunicationFailure(
                        3, "耐压3", "DCW",
                        DataModel.Settingmodel.SETTING_DATA.TVMeterID3,
                        downloadResult.Error);
                    // 参数流程失败：D1011写2通知PLC失败完成并允许跳过双电测第二项；产品追溯由SQLite通信异常记录承接。
                    PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), (UInt16)2);
                    return;
                }
                DataModel.Processmodel.LastTV3TestMode = AT9620.TestMode.DCW;
                writeLog($"[耐压3-DCW] DCW参数下发成功");
                Thread.Sleep(500);

                TV3Process_Core("DCW");
            }
            finally
            {
                EndTvProcessSession(DataModel.Settingmodel.AT9620_3);
            }
        }

        /// <summary>
        /// TV3耐压测试核心逻辑（ACW/DCW共用）。
        /// 业务说明：本站与IR工位共用第三电测位置，由M3032/M3033选择路径；本方法只处理AT9620耐压路径。
        /// 只有读到本次完整的 SN;WOCODE 后才允许继续启表和落记录，读码失败时回写D1011=2交由PLC异常分支处理。
        /// </summary>
        /// <param name="testType">测试类型标识，用于区分D1010触发的ACW或DCW路径。</param>
        private void TV3Process_Core(string testType)
        {
            string snCode;
            string woCode;
            string rawCode;
            if (TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 25 * 3, $"耐压3-{testType}", out snCode, out woCode, out rawCode))
            {
                DataModel.Processmodel.TVTestTestModel3.Productinfo = new Productinfo() { SN = snCode, WOCODE = woCode, PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压3-{testType}] 产品编码读取成功: SN={snCode}, WOCODE={woCode}");
            }
            else
            {
                PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), (UInt16)2);
                return;
            }

            if (IsFirstElectricalEntryTest(testType)
                && !RecordRetestEntry(ElectricalRetestScope, testType, snCode, woCode, DataModel.Processmodel.PartNOID))
            {
                PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), (UInt16)2);
                return;
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
            DataModel.Processmodel.TVTestTestModel3.Res = res;

            DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = 0;

            // 清空上一次测试残留，避免本次启动失败时沿用旧信息导致“结果/说明”不一致
            DataModel.Processmodel.TVTestTestModel3.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel3.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel3.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel3.Current = 0;
            DataModel.Processmodel.TVTestTestModel3.Time = 0;

            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_3, DataModel.Processmodel.TVTestTestModel3.Productinfo?.SN, testType);
            if (!r.Success && SkipTvProcessWhenMeterBusy("耐压3", testType, r.Error))
            {
                return;
            }

            if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
            {
                writeLog($"[耐压3-{testType}] 失败原因: {r.Error}", true);
            }

            bool isACW = testType == "ACW";
            var localizedTvInfo3 = BuildPersistedTvStatus(
                r,
                DataModel.Processmodel.TVTestTestModel3.TVInfo,
                isACW);
            DataModel.Processmodel.TVTestTestModel3.TVInfo = localizedTvInfo3;

            string wocode = DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE;
            string partnoid = DataModel.Processmodel.TVTestTestModel3.Productinfo.PartNOID;
            string sn = DataModel.Processmodel.TVTestTestModel3.Productinfo.SN;

            bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isACW, testType);

            bool updateTvResult;
            if (isSecondTest)
            {
                updateTvResult = sqlite.InsertTV_SecondTest(wocode, partnoid, sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res,
                    DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
                    localizedTvInfo3,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID3);
                writeLog($"[耐压3-{testType}] 双测模式第二次测试，插入新记录");
            }
            else
            {
                updateTvResult = sqlite.UpdateTV(wocode, partnoid, sn,
                    res,
                    DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
                    localizedTvInfo3,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID3);
            }

            if (!updateTvResult)
            {
                writeLog($"[耐压3-{testType}] ⚠ 数据库更新失败! SN={sn}, RES={res}", true);
            }

            updatetv(sn,
                res,
                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
                localizedTvInfo3,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID3,
                testType);

            if (!r.Success)
            {
                var failureStatus3 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel3.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus3,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID3,
                    r.Recordstr
                    );
            }

            writeLog($"[耐压3-{testType}] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}");
            PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), (UInt16)1);
        }

        /// <summary>
        /// TV3 历史兼容入口。
        /// 当前生产路径按D1010分流到 <see cref="TV3Process_ACW"/> / <see cref="TV3Process_DCW"/>；
        /// 本入口面向旧调用方，默认执行ACW路径并复用完整的参数下发、读码、写库和D1011握手流程。
        /// </summary>
        public void TV3Process()
        {
            TV3Process_ACW();
        }

        /// <summary>
        /// 生成 IR 绝缘电阻测试写入 TVInfo 的结果标识。
        /// AT6835FL 本轮稳定结果只给出 GD/NG 判定；SQLite、UI 和 F7 追溯统一使用 [IR] GD / [IR] NG，
        /// 详细通信异常由设备日志和流程异常日志承担，避免 TVInfo 出现空白或长异常文本影响现场筛选。
        /// </summary>
        /// <param name="result">AT6835FL Start() 返回的本轮测试结果，Judgment 来自仪器结果行第三段判定字段。</param>
        /// <returns>用于本地库、界面行和失败追溯入口的 IR 结果文本。</returns>
        private static string BuildIrTvInfo(global::AT6835FL.Result result)
        {
            if (result != null && result.Success)
            {
                return "[IR] GD";
            }

            return "[IR] NG";
        }

        /// <summary>
        /// IR绝缘电阻测试流程（独立电测工位）。
        /// 业务链路：
        /// 1. PLC触发后读取当前产品编码与接触电阻；
        /// 2. AT6835FL执行绝缘电阻测试；
        /// 3. SQLite写入一条 TestMode=IR 的独立电测行；
        /// 4. 失败时复用耐压失败追溯入口，便于F7按仪表号/TVInfo追查；
        /// 5. UI复用 updatetv(...) 的 ACW/DCW/IR 通用显示入口；
        /// 6. finally 必须回写PLC OK/NG，避免现场流程卡在IR工位。
        /// </summary>
        public void IRProcess()
        {
            string sn = string.Empty;
            string wocode = string.Empty;
            string partnoid = DataModel.Processmodel.PartNOID;
            bool irSuccess = false;

            try
            {
                writeLog("[IR测试] 收到PLC触发信号，开始绝缘电阻测试");

                // 1) 从PLC读取产品SN（复用工位3 SN地址）
                string rawCode;
                if (!TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 25 * 3, "IR测试", out sn, out wocode, out rawCode))
                {
                    return;
                }

                int inspectionMatchCount = GetInspectionSnMatchCount(sn);
                if (inspectionMatchCount > 1)
                {
                    writeLog($"[IR测试] 点检SN配置重复，SN={sn}, 命中配置数={inspectionMatchCount}，本轮按NG结束。", true);
                    return;
                }

                partnoid = ResolveInspectionPartNo(sn, wocode, partnoid, "IR测试");
                if (IsIrInspectionSn(sn) && string.IsNullOrWhiteSpace(partnoid))
                {
                    writeLog($"[IR测试] 点检料号为空，SN={sn}, WO={wocode}，本轮按NG结束。", true);
                    return;
                }

                if (IsFirstElectricalEntryTest("IR")
                    && !RecordRetestEntry(ElectricalRetestScope, "IR", sn, wocode, partnoid))
                {
                    return;
                }

                // 2) 读取接触电阻/阻值（写入 RES 列供 CHECK 使用）
                // 注意：这不是 IR 仪器返回的绝缘电阻（绝缘电阻在 r.Resistance 中）。
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
                if (float.IsNaN(res) || res <= 0)
                {
                    // 兜底：若 PLC 未写入或通讯异常，尝试从同一 SN 的最近一条数据库记录复用 RES（例如 ACW/DCW 行）。
                    try
                    {
                        float dbRes;
                        if (SQLITEDATABASE.sqlite.TryGetLatestRes(wocode, partnoid, sn, out dbRes))
                        {
                            res = dbRes;
                            writeLog($"[IR测试] ⚠ PLC阻值无效，已从数据库兜底复用RES={res}（SN={sn}）", true);
                        }
                        else
                        {
                            res = -1;
                            writeLog($"[IR测试] ⚠ PLC阻值无效且数据库兜底失败，RES置为-1（SN={sn}）", true);
                        }
                    }
                    catch
                    {
                        res = -1;
                    }
                }

                // 3) 执行IR测试（AT6835FL Start：自检→下发→STAT:CHAR 自动流程→按 TIME 窗口取稳定结果）
                SyncIrAt6835RuntimeFlagsFromSettings("[IR自动测试]");
                var r = DataModel.Settingmodel.AT6835FL_1.Start();
                irSuccess = r.Success;

                // 4) 构造区分信息：写入 TVInfo 用于多测判定和现场筛选
                string irInfo = BuildIrTvInfo(r);

                // 5) SQLite：插入IR独立电测行，区别靠 TVInfo 前缀
                bool dbOk = sqlite.InsertIR_Test(
                    wocode,
                    partnoid,
                    sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res,
                    (float)r.Resistance,
                    r.Success,
                    (float)r.LeakCurrent,
                    irInfo,
                    DataModel.Settingmodel.SETTING_DATA.IRMeterID);

                if (!dbOk)
                {
                    writeLog($"[IR测试] ⚠ SQLite InsertIR_Test失败! SN={sn}", true);
                }

                RememberIrInspectionResult(
                    sn,
                    wocode,
                    partnoid,
                    res,
                    (float)r.Resistance,
                    (float)r.LeakCurrent,
                    irInfo,
                    DataModel.Settingmodel.SETTING_DATA.IRMeterID,
                    irSuccess,
                    dbOk);

                // 6) F7失败追溯（仅失败时）
                if (!r.Success)
                {
                    // 与耐压失败追溯保持同一落库入口：dr_TVProcess
                    // （通过 TVMETERID + FTVResult（RESULT字段）区分IR）
                    DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                        wocode,
                        sn,
                        DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                        irInfo,
                        DataModel.Settingmodel.SETTING_DATA.WorkerID,
                        DateTime.Now,
                        DataModel.Settingmodel.SETTING_DATA.IRMeterID,
                        r.Recordstr);
                }

                // 7) 更新界面：复用ACW/DCW同一套电测UI记录逻辑，完成后新增/更新 TestMode=IR 独立行
                updatetv(sn, res, (float)r.Resistance, r.Success, (float)r.LeakCurrent, irInfo,
                    DataModel.Settingmodel.SETTING_DATA.IRMeterID, "IR", wocode, partnoid);

                writeLog($"[IR测试] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}, SN={sn}");
            }
            catch (Exception ex)
            {
                irSuccess = false;
                writeLog($"[IR测试] ❌ 流程异常: {ex.Message}, SN={sn}", true);
                sqlite.WriteErrorLog("[IR测试异常]IRProcess失败", $"异常: {ex.Message}, 堆栈: {ex.StackTrace}", sn, wocode);
            }
            finally
            {
                // 8) PLC回写 D1015: 1=OK, 2=NG。无论中途异常与否都回写，避免PLC卡流程。
                bool plcWriteOk = PLC_write(DataModel.Settingmodel.IRResultAddress.ToString(), (UInt16)(irSuccess ? 1 : 2));
                if (!plcWriteOk)
                {
                    writeLog($"[IR测试] ⚠ PLC结果回写失败 D{DataModel.Settingmodel.IRResultAddress}={(irSuccess ? 1 : 2)}, SN={sn}", true);
                }
            }
        }

        /// <summary>
        /// 阻值1读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3035，读取地址：D1200
        /// </summary>
        public void Res1Process()
        {
            try
            {
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
                DataModel.Processmodel.TVTestTestModel1.Res = res;
                writeLog($"阻值1读取完成 D{DataModel.Settingmodel.AddressRes}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25); // TV1工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        string wocode = ss[1];
                        UpdateResValue(sn, res);

                        // 同步将阻值写入SQLite，避免“阻值NG跳过耐压”导致数据库缺失RES
                        bool updateResDbOk = sqlite.UpdateResOnly(wocode, DataModel.Processmodel.PartNOID, sn, res);
                        if (!updateResDbOk)
                        {
                            writeLog($"[阻值1] ⚠ UpdateResOnly写库失败：WOCODE={wocode}, SN={sn}, Res={res}", true);
                        }

                        writeLog($"阻值1已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值1] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值1] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值1读取异常 M{DataModel.Settingmodel.Res1TrigAddress}/D{DataModel.Settingmodel.AddressRes}: {ex.Message}");
            }
        }

        /// <summary>
        /// 阻值2读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3036，读取地址：D1202
        /// </summary>
        public void Res2Process()
        {
            try
            {
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
                DataModel.Processmodel.TVTestTestModel2.Res = res;
                writeLog($"阻值2读取完成 D{DataModel.Settingmodel.AddressRes + 2}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2); // TV2工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        string wocode = ss[1];
                        UpdateResValue(sn, res);

                        // 同步将阻值写入SQLite，避免“阻值NG跳过耐压”导致数据库缺失RES
                        bool updateResDbOk = sqlite.UpdateResOnly(wocode, DataModel.Processmodel.PartNOID, sn, res);
                        if (!updateResDbOk)
                        {
                            writeLog($"[阻值2] ⚠ UpdateResOnly写库失败：WOCODE={wocode}, SN={sn}, Res={res}", true);
                        }

                        writeLog($"阻值2已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值2] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值2] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值2读取异常 M{DataModel.Settingmodel.Res2TrigAddress}/D{DataModel.Settingmodel.AddressRes + 2}: {ex.Message}");
            }
        }

        /// <summary>
        /// 阻值3读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3037，读取地址：D1204
        /// </summary>
        public void Res3Process()
        {
            try
            {
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
                DataModel.Processmodel.TVTestTestModel3.Res = res;
                writeLog($"阻值3读取完成 D{DataModel.Settingmodel.AddressRes + 4}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 3); // TV3工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        string wocode = ss[1];
                        UpdateResValue(sn, res);

                        // 同步将阻值写入SQLite，避免“阻值NG跳过耐压”导致数据库缺失RES
                        bool updateResDbOk = sqlite.UpdateResOnly(wocode, DataModel.Processmodel.PartNOID, sn, res);
                        if (!updateResDbOk)
                        {
                            writeLog($"[阻值3] ⚠ UpdateResOnly写库失败：WOCODE={wocode}, SN={sn}, Res={res}", true);
                        }

                        writeLog($"阻值3已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值3] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值3] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值3读取异常 M{DataModel.Settingmodel.Res3TrigAddress}/D{DataModel.Settingmodel.AddressRes + 4}: {ex.Message}");
            }
        }

        /// <summary>
        /// 电测结果的通用 UI 更新入口。
        ///
        /// 业务含义：
        /// - ACW/DCW/IR 都以 ProductInfoRecord 显示，依靠 TestMode 区分同一 SN 的多条电测记录；
        /// - 已有同 SN + TestMode 行时直接更新，保持界面行稳定；
        /// - 只有同 SN 但无当前模式行时复制基础记录，保留拍照/压力等前序信息；
        /// - IR 这类独立电测工位可能没有前序拍照占位行，因此允许调用方传入工单/料号创建最小显示行。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录。</param>
        private void updatetv(string SN, float res, float maxvoltage, bool result, float maxcurrent, string tvinfo, string tvmeterid, string testMode,
                              string fallbackWocode = null, string fallbackPartnoid = null)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 双测模式下，同一SN会产生两条电测记录（[ACW]/[DCW]）。
                    // 界面行按「SN + TestMode」定位，避免第二次电测结果覆盖第一次记录。
                    // 若当前模式记录不存在：
                    // 1) 优先复用“尚未写入TV结果”的占位记录（拍照后创建的记录）作为第一次电测；
                    // 2) 若已存在另一模式记录，则复制一条新记录用于第二次电测，确保界面可追溯两次电测。

                    ProductInfoRecord firstSnRecord = null;
                    ProductInfoRecord target = null;
                    int targetIndex = -1;
                    bool refreshExistingTarget = false;

                    // 1) 优先找同SN且模式匹配的记录（存在则直接更新）
                    for (int idx = 0; idx < DataModel.Recordmodel.ProductInfoRecords.Count; idx++)
                    {
                        var p = DataModel.Recordmodel.ProductInfoRecords[idx];
                        if (p?.Productinfo?.SN != SN) continue;

                        if (firstSnRecord == null) firstSnRecord = p;

                        if (!string.IsNullOrWhiteSpace(testMode) &&
                            string.Equals(p.TestMode, testMode, StringComparison.OrdinalIgnoreCase))
                        {
                            target = p;
                            targetIndex = idx;
                            refreshExistingTarget = true;
                            break;
                        }
                    }

                    // 2) 若没有同模式记录，尝试复用“占位记录”（第一次电测：避免创建空白的另一模式行）
                    if (target == null)
                    {
                        for (int idx = 0; idx < DataModel.Recordmodel.ProductInfoRecords.Count; idx++)
                        {
                            var p = DataModel.Recordmodel.ProductInfoRecords[idx];
                            if (p?.Productinfo?.SN != SN) continue;

                            bool hasTvData = !string.IsNullOrWhiteSpace(p.TVInfo) ||
                                             !string.IsNullOrWhiteSpace(p.TVMeterID) ||
                                             p.TVMaxVoltage != 0 ||
                                             p.TVMaxCurrent != 0;
                            if (!hasTvData)
                            {
                                target = p;
                                targetIndex = idx;
                                refreshExistingTarget = true;
                                break;
                            }
                        }
                    }

                    // 3) 仍未找到目标：认为是双测第二次测试，复制一条新记录显示第二次电测结果
                    if (target == null && firstSnRecord != null)
                    {
                        var newRecord = new ProductInfoRecord
                        {
                            StationCode = firstSnRecord.StationCode,
                            EQUIPMENTID = firstSnRecord.EQUIPMENTID,
                            DualYStationIndex = firstSnRecord.DualYStationIndex,
                            Productinfo = firstSnRecord.Productinfo,
                            TakePhoto1 = firstSnRecord.TakePhoto1,
                            AppearanceInspection = firstSnRecord.AppearanceInspection,
                            Pressure_Max = firstSnRecord.Pressure_Max,
                            Pressure_Average = firstSnRecord.Pressure_Average,
                            Pressure_Min = firstSnRecord.Pressure_Min,
                            Pressure_Result = firstSnRecord.Pressure_Result,
                            Report = firstSnRecord.Report,
                            DateTime = DateTime.Now,
                            TestMode = testMode
                        };

                        DataModel.Recordmodel.ProductInfoRecords.Insert(0, newRecord);
                        target = newRecord;
                    }

                    // 4) 仍未找到目标但调用方提供了产品信息：创建最小电测行（IR独立工位可走到这里）
                    if (target == null && !string.IsNullOrWhiteSpace(SN) && !string.IsNullOrWhiteSpace(fallbackWocode))
                    {
                        var newRecord = new ProductInfoRecord
                        {
                            StationCode = DataModel.Settingmodel.SETTING_DATA.StationCode,
                            EQUIPMENTID = DataModel.Settingmodel.SETTING_DATA.MachineID,
                            Productinfo = new Productinfo
                            {
                                SN = SN,
                                WOCODE = fallbackWocode ?? string.Empty,
                                PartNOID = string.IsNullOrWhiteSpace(fallbackPartnoid) ? DataModel.Processmodel.PartNOID : fallbackPartnoid
                            },
                            DateTime = DateTime.Now,
                            TestMode = testMode
                        };

                        DataModel.Recordmodel.ProductInfoRecords.Insert(0, newRecord);
                        target = newRecord;
                        writeLog($"[电测] 内存无同SN记录，已新建界面行 SN={SN}, 模式={testMode}");
                    }

                    if (target != null)
                    {
                        target.TestMode = testMode;
                        target.Res = res;
                        target.TVMaxVoltage = maxvoltage;
                        target.TVMaxCurrent = maxcurrent;
                        target.TVResult = result;
                        target.TVInfo = tvinfo;
                        target.TVMeterID = tvmeterid;
                        target.DateTime = DateTime.Now;

                        if (refreshExistingTarget &&
                            targetIndex >= 0 &&
                            targetIndex < DataModel.Recordmodel.ProductInfoRecords.Count)
                        {
                            // ProductInfoRecord uses plain auto-properties, so replace the item
                            // to force DataGrid refresh/re-sort without changing source order.
                            DataModel.Recordmodel.ProductInfoRecords[targetIndex] = target;
                        }
                    }
                    else
                    {
                        // 极端情况：未找到任何记录（例如拍照留底记录未创建/PLC SN异常），记录日志便于现场定位
                        writeLog($"[耐压] ⚠ 未找到内存记录，无法更新界面电测结果：SN={SN}, 模式={testMode}", true);
                        sqlite.WriteErrorLog("UPDATETV_RECORD_NOT_FOUND", $"未找到ProductInfoRecord，无法更新电测结果，模式={testMode}", SN);
                    }
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATETV_EXCEPTION", $"更新耐压/阻值数据失败: {ex.Message}", SN);
                }
            }));
        }

        /// <summary>
        /// 根据产品 SN 仅更新阻值字段到 ProductInfoRecord
        /// 业务含义：
        /// - 在阻值测试完成后立即调用，确保阻值数据及时更新到内存记录；
        /// - 即使后续阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值进行判断。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        /// <param name="res">阻值测量值</param>
        private void UpdateResValue(string SN, float res)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    for (int index = 0; index < DataModel.Recordmodel.ProductInfoRecords.Count; index++)
                    {
                        var p = DataModel.Recordmodel.ProductInfoRecords[index];
                        if (p.Productinfo.SN == SN)
                        {
                            p.Res = res;
                            DataModel.Recordmodel.ProductInfoRecords[index] = p;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATERES_EXCEPTION", $"更新阻值数据失败: {ex.Message}", SN);
                }
            }));
        }

        /// <summary>
        /// 根据产品 SN 更新 DataModel.Recordmodel.ProductInfoRecords 中对应记录的压力测试数据。
        /// 业务含义：
        /// - 在电测压力监控完成后调用，将平均值、最大值、最小值及判定结果写入内存记录；
        /// - 这些压力数据会一并在 SaveBusBarData 报存到 MES，方便后续追溯。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        private void updatepressure(string SN, UInt16 Pressure_Average, UInt16 Pressure_Max, UInt16 Pressure_Min, bool Pressure_Result)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    for (int index = 0; index < DataModel.Recordmodel.ProductInfoRecords.Count; index++)
                    {
                        var p = DataModel.Recordmodel.ProductInfoRecords[index];
                        if (p.Productinfo.SN == SN)
                        {
                            p.Pressure_Average = Pressure_Average;
                            p.Pressure_Max = Pressure_Max;
                            p.Pressure_Min = Pressure_Min;
                            p.Pressure_Result = Pressure_Result;
                            DataModel.Recordmodel.ProductInfoRecords[index] = p;

                            // 不对同SN历史记录做“全量同步刷新”，仅更新第一条匹配记录（通常是列表中最新的一条）。
                            // 双测的另一条记录允许不包含压力信息，以避免点检SN/重复SN场景下污染历史显示。
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATEPRESSURE_EXCEPTION", $"更新压力数据失败: {ex.Message}", SN);
                }
            }));
        }


        public bool Download_PressureParameter()
        {
            try
            {
                var r1 = PLC_write(DataModel.Settingmodel.MaxPressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Max_Pressure);
                var r2 = PLC_write(DataModel.Settingmodel.MinPressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Min_Pressure);
                var r3 = PLC_write(DataModel.Settingmodel.Pressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Pressure);
                return r1 & r2 & r3;
            }
            catch {; }
            return false;
        }

        /// <summary>
        /// 将测试模式写入PLC地址D1012
        /// </summary>
        /// <remarks>
        /// 测试模式值：
        /// - 0: 只测交流(ACW Only)
        /// - 1: 只测直流(DCW Only)
        /// - 2: 先交后直(ACW then DCW)
        /// - 3: 先直后交(DCW then ACW)
        /// </remarks>
        /// <returns>true: 写入成功; false: 写入失败</returns>
        public bool WriteTestModeToPLC()
        {
            try
            {
                ushort modeValue = (ushort)DataModel.Settingmodel.CurrentTestMode;
                string address = DataModel.Settingmodel.TestModeAddress.ToString();
                
                writeLog($"[测试模式] 写入PLC地址D{address}, 值={modeValue} ({DataModel.Settingmodel.CurrentTestMode})");
                
                bool result = PLC_write(address, modeValue);
                
                if (!result)
                {
                    writeLog($"[测试模式] ❌ 写入PLC失败! 地址=D{address}, 值={modeValue}", true);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                writeLog($"[测试模式] ❌ 写入PLC异常: {ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// 将指定测试模式写入PLC
        /// </summary>
        /// <param name="testMode">要写入的测试模式</param>
        /// <returns>true: 写入成功; false: 写入失败</returns>
        public bool WriteTestModeToPLC(AT9620.ElectricalTestMode testMode)
        {
            DataModel.Settingmodel.CurrentTestMode = testMode;
            return WriteTestModeToPLC();
        }



        /// <summary>
        /// 根据产品 SN 更新 DataModel.Recordmodel.ProductInfoRecords 中对应记录的 AOI 整轮外观检测结果。
        /// 业务含义：
        /// - 单条机器人 AOI 指令完成判定后调用，将该指令结果折算到当前 SN 的整轮外观结果；
        /// - 同一轮内任一参与判定的 AOI 指令 NG 后，AppearanceInspection 保持 NG，供 CHECK2、点检 OK 和 MES 追溯使用。
        /// </summary>
        /// <param name="SN">产品序列号，用于定位内存记录和当前 AOI 轮次。</param>
        /// <param name="result">当前机器人 AOI 指令的汇总结果；true 表示该指令下参与判定工具全部 OK。</param>
        /// <param name="dt">当前指令完成时间，用于界面记录与过程追溯。</param>
        /// <returns>当前 SN 在本轮 AOI 中的累计外观结果；true 表示已完成的 AOI 指令全部 OK。</returns>
        private bool updatetakephoto2(string SN, bool result, DateTime dt)
        {
            bool overallResult = UpdateAoiInspectionOverallResult(SN, result);
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.AppearanceInspection = overallResult;
                            p.DateTime = dt;

                            // 不对同SN历史记录做“全量同步刷新”，仅更新第一条匹配记录（通常是列表中最新的一条）。
                            // 双测的另一条记录允许不包含AOI信息，以避免点检SN/重复SN场景下污染历史显示。
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 同时写入UI日志与数据库错误日志，便于现场快速定位“内存更新失败/对象不存在/线程异常”等问题
                    writeLog($"[AOI] 更新内存外观结果失败：SN={SN}, 结果={(overallResult ? "OK" : "NG")}, 异常={ex.Message}", true);
                    sqlite.WriteErrorLog("UPDATETAKEPHOTO2_EXCEPTION", $"更新AOI外观数据失败: {ex.Message}", SN);
                }
            }));

            return overallResult;
        }

        /// <summary>
        /// 清除指定 SN 的 AOI 整轮结果缓存。
        /// 拍照留底建档代表该 SN 开始新的检测轮次，重复点检 SN 也从本轮第一条 AOI 指令重新累计。
        /// </summary>
        /// <param name="SN">当前建档的产品或点检 SN；空值直接忽略。</param>
        private void ResetAoiInspectionOverallResult(string SN)
        {
            if (string.IsNullOrWhiteSpace(SN))
            {
                return;
            }

            lock (_aoiInspectionResultLock)
            {
                _aoiInspectionOverallResultBySn.Remove(SN);
            }
        }

        /// <summary>
        /// 折算当前 SN 的 AOI 整轮外观结果。
        /// 首条 AOI 指令采用自身结果，后续指令按“已完成指令全部 OK”累计；任一 NG 会保留到本轮 CHECK2。
        /// </summary>
        /// <param name="SN">当前检测轮次对应的产品或点检 SN。</param>
        /// <param name="commandResult">当前机器人 AOI 指令的汇总结果。</param>
        /// <returns>当前轮次已完成 AOI 指令的累计外观结果。</returns>
        private bool UpdateAoiInspectionOverallResult(string SN, bool commandResult)
        {
            if (string.IsNullOrWhiteSpace(SN))
            {
                return commandResult;
            }

            lock (_aoiInspectionResultLock)
            {
                bool previousResult;
                if (_aoiInspectionOverallResultBySn.TryGetValue(SN, out previousResult))
                {
                    bool overallResult = previousResult && commandResult;
                    _aoiInspectionOverallResultBySn[SN] = overallResult;
                    return overallResult;
                }

                _aoiInspectionOverallResultBySn[SN] = commandResult;
                return commandResult;
            }
        }

        /// <summary>
        /// 检查参与产品判定的 AOI 工具是否全部为 OK 状态。
        /// 用于 AOI OK 点检；模板定位等辅助工具保持定位职责，不参与产品外观 OK/NG 口径。
        /// </summary>
        /// <returns>true 表示所有参与判定的 AOI 工具均为 OK；false 表示无可判定工具、存在未完成工具或存在非 OK 状态。</returns>
        private bool CheckAllAOIToolsOK()
        {
            return CheckAllAOIJudgingToolsStatus(ToolStatus.OK, "AOI_OK点检");
        }

        /// <summary>
        /// 检查参与产品判定的 AOI 工具是否均为可认定的检出不良状态（NG 或 NG2）。
        /// 用于 AOI NG 点检：全部判定工具达到 NG/NG2 时向 PLC 写入点检通过信号。
        /// NG 表示检出不合格；NG2 表示检不出或无法完成判定，在生产流程中与 NG 同属外观不合格。
        /// 不改变生产 CHECK2 的 AppearanceInspection 布尔口径与量产放行逻辑。
        /// </summary>
        /// <returns>true 表示所有参与判定的 AOI 工具均为 NG 或 NG2；false 表示无可判定工具、存在 OK/等待中/识别中等非检出态。</returns>
        private bool CheckAllAOIToolsNG()
        {
            const string logTag = "AOI_NG点检";
            try
            {
                var judgingTools = DataModel.FaraVisionDataModel.Processmodel.Tools
                    .Where(IsJudgingTool)
                    .ToList();

                if (judgingTools.Count == 0)
                {
                    writeLog($"{logTag}->无参与判定的工具配置，返回false");
                    return false;
                }

                foreach (var tool in judgingTools)
                {
                    if (!IsAoiNgInspectionDetectStatus(tool.ToolStatus))
                    {
                        writeLog($"{logTag}->工具[{tool.Name}]状态为{tool.ToolStatus}，期望=NG或NG2");
                        return false;
                    }
                }

                writeLog($"{logTag}->所有{judgingTools.Count}个参与判定工具均为NG/NG2");
                return true;
            }
            catch (Exception ex)
            {
                writeLog($"{logTag}->检查工具状态异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 判断工具状态是否属于 AOI NG 点检认可的检出不良态。
        /// </summary>
        /// <param name="status">工具当前 ToolStatus。</param>
        /// <returns>true 表示 NG 或 NG2。</returns>
        private static bool IsAoiNgInspectionDetectStatus(ToolStatus status)
        {
            return status == ToolStatus.NG || status == ToolStatus.NG2;
        }

        /// <summary>
        /// 按指定状态检查参与产品判定的 AOI 工具。
        /// 供 AOI OK 点检使用；工具范围与生产外观判定一致，模板定位等辅助工具不参与。
        /// </summary>
        /// <param name="expectedStatus">点检要求的目标状态；OK 点检要求 OK。</param>
        /// <param name="logTag">日志阶段标识，用于现场按点检类型检索异常工具。</param>
        /// <returns>true 表示所有参与判定工具均达到目标状态。</returns>
        private bool CheckAllAOIJudgingToolsStatus(ToolStatus expectedStatus, string logTag)
        {
            try
            {
                var judgingTools = DataModel.FaraVisionDataModel.Processmodel.Tools
                    .Where(IsJudgingTool)
                    .ToList();

                if (judgingTools.Count == 0)
                {
                    writeLog($"{logTag}->无参与判定的工具配置，返回false");
                    return false;
                }

                foreach (var tool in judgingTools)
                {
                    if (tool.ToolStatus != expectedStatus)
                    {
                        writeLog($"{logTag}->工具[{tool.Name}]状态为{tool.ToolStatus}，期望={expectedStatus}");
                        return false;
                    }
                }

                writeLog($"{logTag}->所有{judgingTools.Count}个参与判定工具均为{expectedStatus}");
                return true;
            }
            catch (Exception ex)
            {
                writeLog($"{logTag}->检查工具状态异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 向PLC写入AOI NG点检信号
        /// </summary>
        /// <param name="value">1: 点检通过; 0: 点检失败</param>
        private void WriteAOI_NG_InspectionSignal(UInt16 value)
        {
                try
                {
                    ModbusTcpNet modbusTcp = new ModbusTcpNet();
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    
                    var connectresult = modbusTcp.ConnectServer();
                    if (connectresult.IsSuccess)
                    {
                        // M寄存器是线圈（Coil），使用WriteCoil方法写入Bool值
                        bool boolValue = (value == 1);
                        var writeResult = modbusTcp.WriteCoil(DataModel.Settingmodel.AOI_NG_InspectionAddress.ToString(), boolValue);
                        modbusTcp.ConnectClose();
                        
                        if (writeResult.IsSuccess)
                        {
                            writeLog($"AOI_NG点检->向PLC地址M{DataModel.Settingmodel.AOI_NG_InspectionAddress}写入{value}成功");
                        }
                        else
                        {
                                writeLog($"AOI_NG点检->向PLC地址M{DataModel.Settingmodel.AOI_NG_InspectionAddress}写入{value}失败: {writeResult.Message}");
                            }
                        }
                    else
                    {
                            writeLog($"AOI_NG点检->PLC连接失败: {connectresult.Message}");
                        }
                    }
                catch (Exception ex)
                {
                        writeLog($"AOI_NG点检->写入PLC信号异常: {ex.Message}");
                    }
                }


        #endregion
        #region 照片存储

        /// <summary>
        /// 清理图片目录或文件名中的业务字段。规格、SN、工具名和结果字段统一使用该口径，
        /// 避免 MES 或工程配置中的特殊字符导致现场追溯图片无法落盘。
        /// </summary>
        /// <param name="value">MES、工程配置或视觉工具提供的原始字段。</param>
        /// <param name="fallback">原始字段为空或清理后为空时使用的稳定追溯标识。</param>
        /// <returns>可作为 Windows 单级目录名或文件名片段的文本。</returns>
        private static string SanitizeImagePathPart(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
            {
                builder.Append(invalidChars.Contains(character) ? '_' : character);
            }

            string sanitized = builder.ToString().Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }

        /// <summary>
        /// 解析图片归档规格。当前产品或点检扫码上下文中的 MES 规格优先，
        /// 运行中的产线规格和 AOI 工程名称依次兼容；全部为空时使用 UNKNOWN_SPEC，
        /// 使异常回图仍有明确归档位置。
        /// </summary>
        /// <param name="productPartNoId">本轮扫码建账时冻结的 MES 规格编码。</param>
        /// <returns>完成路径字符清理的规格目录名和文件名前缀。</returns>
        private string ResolveImageSpecificationName(string productPartNoId)
        {
            string specification = productPartNoId;
            if (string.IsNullOrWhiteSpace(specification))
            {
                specification = DataModel?.Processmodel?.PartNOID;
            }
            if (string.IsNullOrWhiteSpace(specification))
            {
                specification = DataModel?.FaraVisionDataModel?.Settingmodel?.Name;
            }

            return SanitizeImagePathPart(specification, "UNKNOWN_SPEC");
        }

        /// <summary>
        /// 解析点检图片的归档身份。
        /// 目录中的 OK/NG 表示标准件期望类型，耐压、IR、AOI 的实际测试结果继续由图片标注、
        /// 文件名、PLC/CHECK 和 MES 记录表达，使误判图片仍归属于被验证的标准件类别。
        /// </summary>
        /// <param name="sn">本轮扫码识别的产品或点检 SN。</param>
        /// <param name="workOrderCode">本轮 PLC 产品码携带的工单号，用于匹配当前点检扫码上下文。</param>
        /// <param name="fallbackPartNoId">普通产品上下文或调用方提供的规格编码。</param>
        /// <param name="archivePartNoId">返回图片使用的规格；点检优先采用当前扫码时 MES 冻结的规格。</param>
        /// <param name="expectedResult">返回点检标准件期望类别，取值为 OK 或 NG。</param>
        /// <param name="inspectionType">返回耐压、IR 或 AOI 点检类型，用于文件名追溯。</param>
        /// <returns>SN 唯一命中一个已配置点检码时返回 true；普通产品或重复点检配置返回 false。</returns>
        private bool TryResolveInspectionImageArchive(
            string sn,
            string workOrderCode,
            string fallbackPartNoId,
            out string archivePartNoId,
            out string expectedResult,
            out string inspectionType)
        {
            archivePartNoId = (fallbackPartNoId ?? string.Empty).Trim();
            expectedResult = string.Empty;
            inspectionType = string.Empty;

            if (GetInspectionSnMatchCount(sn) != 1)
            {
                return false;
            }

            if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN))
            {
                expectedResult = "OK";
                inspectionType = "耐压";
            }
            else if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN))
            {
                expectedResult = "NG";
                inspectionType = "耐压";
            }
            else if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIROKSN))
            {
                expectedResult = "OK";
                inspectionType = "IR";
            }
            else if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionIRNGSN))
            {
                expectedResult = "NG";
                inspectionType = "IR";
            }
            else if (IsConfiguredInspectionSn(sn, DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN))
            {
                expectedResult = "OK";
                inspectionType = "AOI";
            }
            else
            {
                expectedResult = "NG";
                inspectionType = "AOI";
            }

            string normalizedSn = (sn ?? string.Empty).Trim();
            string normalizedWorkOrderCode = (workOrderCode ?? string.Empty).Trim();
            lock (_inspectionRunContextSync)
            {
                InspectionRunContext context;
                if (_inspectionRunContexts.TryGetValue(normalizedSn, out context)
                    && string.Equals(context.WorkOrderCode, normalizedWorkOrderCode, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(context.PartNoId))
                {
                    archivePartNoId = context.PartNoId.Trim();
                }
            }

            return true;
        }

        /// <summary>
        /// 生成图片归档目录。普通拍照留底、AOI 外观检测和点检图片共享“类型/规格/日期/结果”层级，
        /// 点检使用独立“点检”类型目录；路径分类只影响图片追溯，不改变检测判定、PLC、MES 和 SQLite 流程。
        /// </summary>
        /// <param name="imageSaveDir">图片保存配置中的根目录；为空时使用程序运行目录。</param>
        /// <param name="imageType">拍照留底或外观检测等业务分类。</param>
        /// <param name="productPartNoId">当前产品上下文中的 MES 规格编码。</param>
        /// <param name="captureTime">本张图片的采集时间，用于生成日期目录。</param>
        /// <param name="result">OK、NG 等图片结果分类。</param>
        /// <returns>包含业务类型、规格、日期和结果的图片目录。</returns>
        private string BuildImageArchiveDirectory(
            string imageSaveDir,
            string imageType,
            string productPartNoId,
            DateTime captureTime,
            string result)
        {
            string rootDirectory = string.IsNullOrWhiteSpace(imageSaveDir)
                ? Environment.CurrentDirectory
                : imageSaveDir;
            return Path.Combine(
                rootDirectory,
                SanitizeImagePathPart(imageType, "图片"),
                ResolveImageSpecificationName(productPartNoId),
                captureTime.ToString("yyyyMMdd"),
                SanitizeImagePathPart(result, "UNKNOWN"));
        }

        /// <summary>
        /// 保存拍照留底工位的原始图片。普通产品沿用调用方提供的类型和结果；
        /// 点检标准件强制写入“点检/规格/日期/期望OK或NG”，并在文件名保留点检类型。
        /// 文件名同时携带规格、SN、相机序号和采集时间，单张图片离开归档目录后仍可追溯本轮身份。
        /// </summary>
        /// <param name="image">相机回调提供的 HALCON 图像。</param>
        /// <param name="sn">本轮拍照留底产品的 SN。</param>
        /// <param name="workOrderCode">本轮 PLC 产品码携带的工单号，用于点检扫码上下文匹配。</param>
        /// <param name="partNoId">普通产品上下文中的规格编码；点检优先使用扫码时 MES 冻结的规格。</param>
        /// <param name="type">图片业务分类，拍照留底工位传入“拍照留底”。</param>
        /// <param name="index">拍照相机序号，用于区分同一产品的多张留底图片。</param>
        /// <param name="result">普通产品图片的归档结果；点检图片由标准件配置确定期望 OK/NG。</param>
        public void SaveImage(HObject image, string sn, string workOrderCode, string partNoId, string type, int index, string result)
        {
            DateTime captureTime = DateTime.Now;
            string archivePartNoId;
            string expectedResult;
            string inspectionType;
            bool isInspectionImage = TryResolveInspectionImageArchive(
                sn,
                workOrderCode,
                partNoId,
                out archivePartNoId,
                out expectedResult,
                out inspectionType);
            string archiveType = isInspectionImage ? "点检" : type;
            string archiveResult = isInspectionImage ? expectedResult : result;
            string specification = ResolveImageSpecificationName(archivePartNoId);
            string directory = BuildImageArchiveDirectory(
                DataModel.Settingmodel.ImageSaveSetting.ImageSaveDir,
                archiveType,
                archivePartNoId,
                captureTime,
                archiveResult);
            string inspectionIdentity = isInspectionImage
                ? $"-{SanitizeImagePathPart(inspectionType, "点检")}-{expectedResult}标准件"
                : string.Empty;
            string fileName = $"{specification}-{SanitizeImagePathPart(sn, "NOSN")}{inspectionIdentity}"
                + $"-{index:00}-{captureTime:yyyyMMddHHmmssFFF}.jpg";
            string savefilename = Path.Combine(directory, fileName);
            string dir = Path.GetDirectoryName(savefilename);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            HOperatorSet.WriteImage(image, "jpg", 0, savefilename);
        }
        #endregion

        #region 性能日志
        /// <summary>
        /// 统一的性能诊断日志方法
        /// 写入性能诊断日志到独立文件,不影响现有业务日志
        /// 超过1秒的耗时会用特殊格式突出显示
        /// </summary>
        /// <param name="component">组件标识，如BUSINESS、UI等</param>
        /// <param name="tag">日志标签,如 SCAN_SUCCESS、PROCESS_COMPLETE等</param>
        /// <param name="message">日志消息</param>
        /// <param name="elapsedMs">耗时(毫秒),可选</param>
        /// <param name="extraInfo">额外信息,可选</param>
        private void writePerfLog(string component, string tag, string message, long? elapsedMs = null, string extraInfo = null)
        {
            try
            {
                // 获取线程ID
                int threadId = Thread.CurrentThread.ManagedThreadId;
                string threadName = Thread.CurrentThread.Name ?? (threadId == 1 ? "UI-Thread" : $"Thread-{threadId}");

                // 获取设备ID(从配置中读取)
                string deviceId = DataModel.Settingmodel.SETTING_DATA?.MachineID ?? "Unknown";

                // 检测是否为异常耗时（超过1秒）
                bool isAbnormalTime = elapsedMs.HasValue && elapsedMs.Value > 1000;
                string perfLevel = isAbnormalTime ? "PERF-异常" : "PERF";

                // 构建日志内容
                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}]");
                logBuilder.Append($"[{threadName}]");
                logBuilder.Append($"[{component}_{tag}]");
                logBuilder.Append($"[{perfLevel}]");
                logBuilder.Append($"[Device-{deviceId}] {message}");

                if (elapsedMs.HasValue)
                {
                    string timeDisplay = isAbnormalTime ?
                        $"耗时={elapsedMs.Value}ms[异常!!!]" :
                        $"耗时={elapsedMs.Value}ms";
                    logBuilder.Append($" | {timeDisplay}");
                }

                if (!string.IsNullOrEmpty(extraInfo))
                {
                    logBuilder.Append($" | {extraInfo}");
                }

                string logContent = logBuilder.ToString();

                // 写入独立的性能日志文件
                string filename = $"{Environment.CurrentDirectory}\\日志\\性能诊断\\{DateTime.Now.ToString("yyyyMMdd")}_performance.log";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 使用文件锁确保多线程安全
                lock (writeLog_Locker)
                {
                    using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                    {
                        sw.WriteLine(logContent);
                        sw.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                // 性能日志失败不应影响业务,仅记录到错误日志
                try
                {
                    writeError($"性能日志写入失败: {ex.Message}");
                }
                catch { }
            }
        }
        #endregion
    }
}
