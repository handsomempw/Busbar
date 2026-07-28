using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.Record;
using SQLITEDATABASE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BusbarCompressionSystem.ViewModel
{
    /// <summary>
    /// 双Y单工位扫码处理结果。界面据此区分业务是否已经建账，以及 PLC 扫码结果反馈是否完成，
    /// 避免在业务已经成功、仅反馈失败时引导操作员重复扫码。
    /// </summary>
    internal sealed class DualYStationScanResult
    {
        /// <summary>
        /// 创建一次双Y工位扫码结果。
        /// </summary>
        /// <param name="businessCompleted">MES 校验、SQLite 建账和 PLC 产品码写入是否完成。</param>
        /// <param name="plcFeedbackCompleted">对应工位的 PLC 扫码 OK/NG 结果是否写回成功。</param>
        /// <param name="message">提供给操作员和运行日志的失败原因；完整成功时为空。</param>
        internal DualYStationScanResult(bool businessCompleted, bool plcFeedbackCompleted, string message)
        {
            BusinessCompleted = businessCompleted;
            PlcFeedbackCompleted = plcFeedbackCompleted;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// MES 校验、SQLite 建账和 PLC 产品码写入已经完成。
        /// 该值为 true 时，同一产品不应因反馈故障再次提交。
        /// </summary>
        public bool BusinessCompleted { get; private set; }

        /// <summary>
        /// PLC 扫码结果反馈已经写入对应工位地址，Y1 使用标准扫码返回地址，Y2 使用双Y 2工位返回地址。
        /// </summary>
        public bool PlcFeedbackCompleted { get; private set; }

        /// <summary>
        /// 业务处理与 PLC 结果反馈均完成时为 true。
        /// </summary>
        public bool IsSuccess
        {
            get { return BusinessCompleted && PlcFeedbackCompleted; }
        }

        /// <summary>
        /// 操作员可执行的失败原因；完整成功时为空字符串。
        /// </summary>
        public string Message { get; private set; }
    }

    /// <summary>
    /// 双Y并行仅电测模式：2工位扫码、D1020/D1021 流程结束归档（替代 CHECK/机器人）。
    /// </summary>
    public partial class MainViewModel
    {
        /// <summary>
        /// 双Y单工位当前扫码会话。每次实际电测完成后追加 SQLite 行 ID，流程结束据此限定压力、判定和 MES 上传范围。
        /// </summary>
        private sealed class DualYTestRunState
        {
            internal string Sn { get; set; } = string.Empty;
            internal string WoCode { get; set; } = string.Empty;
            internal string PartNoId { get; set; } = string.Empty;
            internal List<long> TestRecordIds { get; } = new List<long>();

            internal void Begin(string sn, string woCode, string partNoId)
            {
                Sn = sn ?? string.Empty;
                WoCode = woCode ?? string.Empty;
                PartNoId = partNoId ?? string.Empty;
                TestRecordIds.Clear();
            }
        }

        private readonly object _dualYMesLock = new object();
        private readonly object _dualYTestRunSync = new object();
        private readonly DualYTestRunState _dualYStation1TestRun = new DualYTestRunState();
        private readonly DualYTestRunState _dualYStation2TestRun = new DualYTestRunState();
        private readonly object _dualYStation1WorkflowSync = new object();
        private readonly object _dualYStation2WorkflowSync = new object();
        private int _dualYStation1ScanProcessing = 0;
        private int _dualYStation2ScanProcessing = 0;
        private int _dualYStation1FlowEndProcessing = 0;
        private int _dualYStation2FlowEndProcessing = 0;
        /// <summary>
        /// 最近一次已记录的双Y模式日志状态：null=尚未记录或未知，true/false=已记录的线圈值。
        /// </summary>
        private bool? _lastDualYModeLogged = null;
        private bool _lastDualYModeUnknownLogged;

        /// <summary>
        /// 运行时双Y模式判定：以双Y配置指定的 PLC 模式线圈为准；部署标志仅影响启动期窗口和资源初始化。
        /// </summary>
        public bool IsDualYElectricalTestModeActive()
        {
            return DataModel != null
                && DataModel.Processmodel != null
                && DataModel.Processmodel.DualYElectricalTestModeKnown
                && DataModel.Processmodel.DualYElectricalTestModeActive;
        }

        /// <summary>
        /// 双Y专用部署判定。
        /// 该配置用于启动阶段资源边界，双Y专用机台跳过机器人 TCP 与相机硬件初始化；运行中的产品流向仍以 PLC M3050 为准。
        /// 同时作为本机无耐压工位3的判定：参数下发、可用状态镜像和仪器配置界面强制忽略 AT9620_3。
        /// </summary>
        public bool IsDualYElectricalTestDeployment()
        {
            return DataModel != null
                && DataModel.DualYConfiguration != null
                && DataModel.DualYConfiguration.DeploymentEnabled;
        }

        /// <summary>
        /// 从 PLC 刷新双Y模式状态。
        /// 读取成功时按线圈值切换“仅电测/标准产线”；读取失败时标记模式未知并暂停模式相关触发，避免失败被当成标准流程。
        /// </summary>
        /// <param name="modeKnown">M3050 线圈本周期是否读取成功。</param>
        /// <param name="coilValue">读取成功时的线圈值；读取失败时可传 false，不会作为有效模式。</param>
        internal void RefreshDualYModeFromPlc(bool modeKnown, bool coilValue)
        {
            if (DataModel?.Processmodel == null)
            {
                return;
            }

            DataModel.Processmodel.DualYElectricalTestModeKnown = modeKnown;
            if (!modeKnown)
            {
                if (!_lastDualYModeUnknownLogged)
                {
                    _lastDualYModeUnknownLogged = true;
                    _lastDualYModeLogged = null;
                    writeLog($"[双Y电测] M{DataModel.DualYConfiguration.ElectricalTestModeCoilAddress}读取失败，模式未知，已暂停模式相关触发", true);
                }

                return;
            }

            _lastDualYModeUnknownLogged = false;
            DataModel.Processmodel.DualYElectricalTestModeActive = coilValue;
            if (_lastDualYModeLogged == null || _lastDualYModeLogged.Value != coilValue)
            {
                _lastDualYModeLogged = coilValue;
                writeLog($"[双Y电测] M{DataModel.DualYConfiguration.ElectricalTestModeCoilAddress}={(coilValue ? 1 : 0)}，模式={(coilValue ? "仅电测" : "标准产线")}", true);
            }
        }

        /// <summary>
        /// 尝试占用指定工位的流程结束归档入口。扫码和归档共享同一工位同步边界，
        /// 防止产品码尚在校验或写入时并发读取并归档上一笔数据。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号，取值 1 或 2。</param>
        /// <param name="busyReason">占用失败时返回当前工位正在处理的业务阶段。</param>
        /// <returns>成功占用归档入口时返回 true；工位正在扫码或归档时返回 false。</returns>
        private bool TryBeginDualYFlowEndProcessing(int stationIndex, out string busyReason)
        {
            object workflowSync = stationIndex == 1 ? _dualYStation1WorkflowSync : _dualYStation2WorkflowSync;
            lock (workflowSync)
            {
                int scanProcessing = stationIndex == 1 ? _dualYStation1ScanProcessing : _dualYStation2ScanProcessing;
                int flowEndProcessing = stationIndex == 1 ? _dualYStation1FlowEndProcessing : _dualYStation2FlowEndProcessing;
                if (scanProcessing != 0)
                {
                    busyReason = "扫码仍在处理";
                    return false;
                }

                if (flowEndProcessing != 0)
                {
                    busyReason = "上一笔归档仍在处理";
                    return false;
                }

                if (stationIndex == 1)
                {
                    _dualYStation1FlowEndProcessing = 1;
                }
                else
                {
                    _dualYStation2FlowEndProcessing = 1;
                }

                busyReason = string.Empty;
                return true;
            }
        }

        /// <summary>
        /// 释放指定工位的流程结束占用，使下一次扫码或归档触发可以进入业务链。
        /// </summary>
        /// <param name="stationIndex">本次归档所属的双Y物理工位号。</param>
        private void EndDualYFlowEndProcessing(int stationIndex)
        {
            object workflowSync = stationIndex == 1 ? _dualYStation1WorkflowSync : _dualYStation2WorkflowSync;
            lock (workflowSync)
            {
                if (stationIndex == 1)
                {
                    _dualYStation1FlowEndProcessing = 0;
                }
                else
                {
                    _dualYStation2FlowEndProcessing = 0;
                }
            }
        }

        /// <summary>
        /// 尝试占用指定工位的扫码入口。自动扫码与操作员手动扫码共用该状态，
        /// 同一工位一次只允许一笔 MES 校验、SQLite 建账和 PLC 写码业务执行。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号，取值 1 或 2。</param>
        /// <param name="busyReason">占用失败时返回扫码或归档中的具体阶段。</param>
        /// <returns>成功占用扫码入口时返回 true；工位正在扫码或归档时返回 false。</returns>
        private bool TryBeginDualYScanProcessing(int stationIndex, out string busyReason)
        {
            object workflowSync = stationIndex == 1 ? _dualYStation1WorkflowSync : _dualYStation2WorkflowSync;
            lock (workflowSync)
            {
                int scanProcessing = stationIndex == 1 ? _dualYStation1ScanProcessing : _dualYStation2ScanProcessing;
                int flowEndProcessing = stationIndex == 1 ? _dualYStation1FlowEndProcessing : _dualYStation2FlowEndProcessing;
                if (scanProcessing != 0)
                {
                    busyReason = "上一笔扫码仍在处理";
                    return false;
                }

                if (flowEndProcessing != 0)
                {
                    busyReason = "工位正在归档";
                    return false;
                }

                if (stationIndex == 1)
                {
                    _dualYStation1ScanProcessing = 1;
                }
                else
                {
                    _dualYStation2ScanProcessing = 1;
                }

                busyReason = string.Empty;
                return true;
            }
        }

        /// <summary>
        /// 释放指定工位的扫码占用，使自动扫码、手动扫码或流程结束可以处理下一笔请求。
        /// </summary>
        /// <param name="stationIndex">本次扫码所属的双Y物理工位号。</param>
        private void EndDualYScanProcessing(int stationIndex)
        {
            object workflowSync = stationIndex == 1 ? _dualYStation1WorkflowSync : _dualYStation2WorkflowSync;
            lock (workflowSync)
            {
                if (stationIndex == 1)
                {
                    _dualYStation1ScanProcessing = 0;
                }
                else
                {
                    _dualYStation2ScanProcessing = 0;
                }
            }
        }

        /// <summary>
        /// 根据当前电测模式得到双Y流程结束阶段必须存在的耐压过程数据。
        /// 单测模式只要求对应 ACW 或 DCW 行；双测模式要求 ACW 与 DCW 均完成，顺序由 PLC 测试流程控制。
        /// </summary>
        /// <param name="requireAcw">返回 true 表示本轮归档必须存在有效 ACW 过程行。</param>
        /// <param name="requireDcw">返回 true 表示本轮归档必须存在有效 DCW 过程行。</param>
        /// <returns>用于日志展示的当前测试模式名称。</returns>
        private string GetDualYRequiredElectricalModes(out bool requireAcw, out bool requireDcw)
        {
            requireAcw = false;
            requireDcw = false;

            switch (DataModel.Settingmodel.CurrentTestMode)
            {
                case AT9620.ElectricalTestMode.ACWOnly:
                    requireAcw = true;
                    break;
                case AT9620.ElectricalTestMode.DCWOnly:
                    requireDcw = true;
                    break;
                case AT9620.ElectricalTestMode.ACWThenDCW:
                case AT9620.ElectricalTestMode.DCWThenACW:
                    requireAcw = true;
                    requireDcw = true;
                    break;
                default:
                    requireAcw = true;
                    break;
            }

            return DataModel.Settingmodel.CurrentTestModeDisplay;
        }

        /// <summary>
        /// 双Y 2工位扫码：D1120 触发，写入 D950，逻辑对标工位1 <see cref="ScannerProcess"/>。
        /// 自动扫码与手动扫码共用工位级互斥、MES/SQLite 业务链和 D1121 结果反馈。
        /// </summary>
        public void DualYStation2ScannerProcess()
        {
            string busyReason;
            if (!TryBeginDualYScanProcessing(2, out busyReason))
            {
                writeLog($"[双Y-工位2自动扫码] {busyReason}，忽略重复触发。", true);
                return;
            }

            try
            {
                string scanRaw = string.Empty;
                bool hardwareOk = false;

                if (DataModel.DualYConfiguration.Station2ScannerMode == "HF800")
                {
                    var r = DataModel.DualYConfiguration.Station2HF800.Scanner();
                    if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(r.value))
                    {
                        scanRaw = r.value.Replace("\r", "").Replace("\n", "").Trim();
                        hardwareOk = true;
                    }
                }
                else
                {
                    var r = DataModel.DualYConfiguration.Station2ScannerModel.Scanner();
                    scanRaw = r.receivestring?.Replace("\r", "").Replace("\n", "").Trim() ?? string.Empty;
                    hardwareOk = r.IsSuccess && !string.IsNullOrEmpty(scanRaw);
                }

                if (!hardwareOk)
                {
                    CompleteDualYScanReadFailure(2, "自动", "扫码器未读取到有效产品编号");
                    return;
                }

                ExecuteDualYStationScan(scanRaw, 2, "自动");
            }
            catch (Exception ex)
            {
                CompleteDualYScanReadFailure(2, "自动", $"扫码处理异常：{ex.Message}");
            }
            finally
            {
                EndDualYScanProcessing(2);
            }
        }

        /// <summary>
        /// 处理双Y小屏提交的手动扫码。手动入口在双Y电测模式下直接提交，PLC 扫码触发位只驱动自动扫码入口，
        /// 两类入口与流程结束共享工位级互斥；成功后执行 MES 校验、SQLite 建账、PLC 产品码写入和该工位扫码结果反馈。
        /// </summary>
        /// <param name="scanRaw">操作员输入或键盘式扫码枪录入的原始产品编号。</param>
        /// <param name="stationIndex">界面入口固定的双Y物理工位号，取值 1 或 2。</param>
        /// <returns>扫码业务和 PLC 反馈的结构化结果，供界面决定清空输入或保留失败内容。</returns>
        internal DualYStationScanResult ProcessDualYManualScan(string scanRaw, int stationIndex)
        {
            string normalizedScan = (scanRaw ?? string.Empty).Trim();
            if (stationIndex != 1 && stationIndex != 2)
            {
                return new DualYStationScanResult(false, false, "手动扫码工位无效");
            }

            if (string.IsNullOrWhiteSpace(normalizedScan))
            {
                return new DualYStationScanResult(false, false, "请输入产品编号");
            }

            if (!IsDualYElectricalTestModeActive())
            {
                const string inactiveReason = "PLC尚未进入双Y电测模式";
                writeLog($"[双Y-工位{stationIndex}手动扫码] {inactiveReason}", true);
                return new DualYStationScanResult(false, false, inactiveReason);
            }

            DualYStationDisplayState stationDisplay = stationIndex == 1
                ? DataModel.Processmodel.DualYStation1Display
                : DataModel.Processmodel.DualYStation2Display;
            if (stationDisplay.WorkflowState == "等待电测"
                || stationDisplay.WorkflowState == "归档处理中"
                || stationDisplay.WorkflowState == "归档失败"
                || stationDisplay.WorkflowState == "归档异常")
            {
                string activeProductReason = $"Y{stationIndex}当前产品 {stationDisplay.CurrentSn} 流程尚未结束，已阻止覆盖扫码";
                writeLog($"[双Y-工位{stationIndex}手动扫码] {activeProductReason}", true);
                return new DualYStationScanResult(false, false, activeProductReason);
            }

            string busyReason;
            if (!TryBeginDualYScanProcessing(stationIndex, out busyReason))
            {
                writeLog($"[双Y-工位{stationIndex}手动扫码] {busyReason}，已忽略本次提交。", true);
                return new DualYStationScanResult(false, false, busyReason);
            }

            writeLog($"[双Y-工位{stationIndex}手动扫码] 操作员提交 SN={normalizedScan}");
            try
            {
                return ExecuteDualYStationScan(normalizedScan, stationIndex, "手动");
            }
            finally
            {
                EndDualYScanProcessing(stationIndex);
            }
        }

        /// <summary>
        /// 执行双Y单工位共用扫码业务并写回该工位 PLC 扫码结果。
        /// 自动与手动入口均在取得工位占用后调用，业务完成口径保持 MES 校验、SQLite 建账和 PLC 产品码写入一致。
        /// </summary>
        /// <param name="scanRaw">扫码器或手动入口取得的产品编号。</param>
        /// <param name="stationIndex">双Y物理工位号，决定产品码和扫码结果的 PLC 地址。</param>
        /// <param name="source">用于现场日志区分“自动”或“手动”扫码来源。</param>
        /// <returns>业务完成状态、PLC 反馈状态和操作员可执行的失败原因。</returns>
        private DualYStationScanResult ExecuteDualYStationScan(string scanRaw, int stationIndex, string source)
        {
            string scanError;
            DualYStationDisplayState stationDisplay = stationIndex == 1
                ? DataModel.Processmodel.DualYStation1Display
                : DataModel.Processmodel.DualYStation2Display;
            bool activeProductConflict = stationDisplay.WorkflowState == "等待电测"
                || stationDisplay.WorkflowState == "归档处理中"
                || stationDisplay.WorkflowState == "归档失败"
                || stationDisplay.WorkflowState == "归档异常";
            if (activeProductConflict)
            {
                scanError = $"Y{stationIndex}当前产品 {stationDisplay.CurrentSn} 流程尚未结束，已阻止覆盖扫码";
            }
            else
            {
                try
                {
                    scanError = ScanDualYStationSn(scanRaw, stationIndex);
                }
                catch (Exception ex)
                {
                    scanError = $"扫码业务异常：{ex.Message}";
                }
            }

            bool businessCompleted = string.IsNullOrEmpty(scanError);
            bool plcFeedbackCompleted = WriteDualYScanResultFeedback(stationIndex, businessCompleted, source);
            string message = scanError;

            if (businessCompleted && !plcFeedbackCompleted)
            {
                message = "扫码业务已完成，PLC结果反馈失败，请检查通信，避免重复扫码";
                UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("等待电测", "PLC扫码反馈失败"));
            }
            else if (!businessCompleted)
            {
                if (!plcFeedbackCompleted)
                {
                    message = $"{message}；PLC扫码NG反馈失败";
                }

                if (!activeProductConflict)
                {
                    UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("扫码失败", message));
                }
                writeLog($"[双Y-工位{stationIndex}{source}扫码] {message}", true);
            }

            return new DualYStationScanResult(businessCompleted, plcFeedbackCompleted, message);
        }

        /// <summary>
        /// 处理扫码器未取得有效条码或硬件调用异常，并向对应工位写回扫码 NG。
        /// 该入口只结束当前扫码请求，不创建 SQLite 记录，也不写入 PLC 产品码。
        /// </summary>
        /// <param name="stationIndex">发生读码失败的双Y物理工位号。</param>
        /// <param name="source">用于日志区分自动或手动扫码来源。</param>
        /// <param name="reason">扫码器返回的可诊断失败原因。</param>
        private void CompleteDualYScanReadFailure(int stationIndex, string source, string reason)
        {
            bool feedbackCompleted = WriteDualYScanResultFeedback(stationIndex, false, source);
            string message = feedbackCompleted ? reason : $"{reason}；PLC扫码NG反馈失败";
            DualYStationDisplayState stationDisplay = stationIndex == 1
                ? DataModel.Processmodel.DualYStation1Display
                : DataModel.Processmodel.DualYStation2Display;
            bool preserveActiveProduct = stationDisplay.WorkflowState == "等待电测"
                || stationDisplay.WorkflowState == "归档处理中"
                || stationDisplay.WorkflowState == "归档失败"
                || stationDisplay.WorkflowState == "归档异常";
            if (!preserveActiveProduct)
            {
                UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("扫码失败", message));
            }
            writeLog($"[双Y-工位{stationIndex}{source}扫码] {message}", true);
        }

        /// <summary>
        /// 将双Y工位扫码结果写回 PLC。Y1 沿用标准扫码返回地址，Y2 使用双Y专用返回地址；
        /// 1 表示业务成功，2 表示读码或业务失败。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号，取值 1 或 2。</param>
        /// <param name="success">扫码业务是否完整完成。</param>
        /// <param name="source">日志中的扫码来源，用于区分自动和手动操作。</param>
        /// <returns>PLC 在重试范围内成功接收结果时返回 true。</returns>
        private bool WriteDualYScanResultFeedback(int stationIndex, bool success, string source)
        {
            int resultAddress = stationIndex == 1
                ? DataModel.DualYConfiguration.Station1ScanResultAddress
                : DataModel.DualYConfiguration.Station2ScanResultAddress;
            bool feedbackCompleted = PLC_write(resultAddress.ToString(), (UInt16)(success ? 1 : 2));
            if (!feedbackCompleted)
            {
                writeLog($"[双Y-工位{stationIndex}{source}扫码] PLC结果反馈失败 D{resultAddress}={(success ? 1 : 2)}", true);
            }

            return feedbackCompleted;
        }

        /// <summary>
        /// 在 WPF 线程刷新指定双Y工位的操作员展示状态。该入口负责界面摘要，
        /// 原业务流程继续负责 PLC 反馈、SQLite 数据和 MES 报工。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号，取值 1 或 2。</param>
        /// <param name="update">针对工位展示状态执行的刷新动作。</param>
        private void UpdateDualYStationDisplay(int stationIndex, Action<DualYStationDisplayState> update)
        {
            if (update == null || DataModel?.Processmodel == null)
            {
                return;
            }

            DualYStationDisplayState display = stationIndex == 1
                ? DataModel.Processmodel.DualYStation1Display
                : DataModel.Processmodel.DualYStation2Display;

            Action apply = () => update(display);
            if (App.Current?.Dispatcher != null && !App.Current.Dispatcher.CheckAccess())
            {
                App.Current.Dispatcher.BeginInvoke(apply);
                return;
            }

            apply();
        }

        /// <summary>
        /// 开始双Y单工位扫码会话并清空该工位上一轮测试行身份。
        /// Y1/Y2 各自维护状态，随后每次 ACW/DCW 复测都会向当前会话追加一条 SQLite 行 ID。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号，取值 1 或 2。</param>
        /// <param name="sn">本轮扫码经 MES 校验后的产品 SN。</param>
        /// <param name="woCode">本轮产品工单号。</param>
        /// <param name="partNoId">本轮产品规格编码。</param>
        private void BeginDualYTestRun(int stationIndex, string sn, string woCode, string partNoId)
        {
            lock (_dualYTestRunSync)
            {
                DualYTestRunState state = stationIndex == 1 ? _dualYStation1TestRun : _dualYStation2TestRun;
                state.Begin(sn, woCode, partNoId);
            }
        }

        /// <summary>
        /// 将一次实际电测的 SQLite 行 ID 登记到对应双Y工位会话。
        /// 应用从测试中途恢复时允许依据 PLC 产品码重建空会话；已有其他 SN 的会话会拦截登记并记录串站风险。
        /// </summary>
        /// <param name="stationIndex">执行本次测试的双Y物理工位号。</param>
        /// <param name="sn">耐压触发时从该工位 PLC 产品码区读取的 SN。</param>
        /// <param name="woCode">与 SN 同时读取的工单号。</param>
        /// <param name="partNoId">当前生产规格编码。</param>
        /// <param name="recordId">本次测试插入 SQLite 后返回的主键。</param>
        /// <returns>行 ID 已归入当前工位会话时返回 true。</returns>
        private bool RegisterDualYTestAttempt(int stationIndex, string sn, string woCode, string partNoId, long recordId)
        {
            if (recordId <= 0)
            {
                return false;
            }

            lock (_dualYTestRunSync)
            {
                DualYTestRunState state = stationIndex == 1 ? _dualYStation1TestRun : _dualYStation2TestRun;
                if (string.IsNullOrWhiteSpace(state.Sn))
                {
                    state.Begin(sn, woCode, partNoId);
                    writeLog($"[双Y-工位{stationIndex}] 已根据电测产品码恢复当前会话，SN={sn}, WO={woCode}", true);
                }

                if (!string.Equals(state.Sn, sn, StringComparison.Ordinal)
                    || !string.Equals(state.WoCode, woCode, StringComparison.Ordinal))
                {
                    writeLog($"[双Y-工位{stationIndex}] 测试行归属冲突，当前会话={state.Sn}/{state.WoCode}，电测读取={sn}/{woCode}，SQLite ID={recordId}", true);
                    return false;
                }

                state.TestRecordIds.Add(recordId);
                return true;
            }
        }

        /// <summary>
        /// 获取指定双Y工位本轮测试行 ID 快照，并校验流程结束读取的产品码与扫码会话一致。
        /// </summary>
        /// <param name="stationIndex">流程结束信号所属双Y物理工位号。</param>
        /// <param name="sn">流程结束时从 PLC 读取的产品 SN。</param>
        /// <param name="woCode">流程结束时从 PLC 读取的工单号。</param>
        /// <param name="recordIds">返回本轮每次实际测试对应的 SQLite 行 ID。</param>
        /// <param name="reason">校验失败时返回可写入操作员日志的原因。</param>
        /// <returns>产品会话一致且至少存在一条测试行时返回 true。</returns>
        private bool TryGetDualYTestRunRecordIds(int stationIndex, string sn, string woCode, out List<long> recordIds, out string reason)
        {
            lock (_dualYTestRunSync)
            {
                DualYTestRunState state = stationIndex == 1 ? _dualYStation1TestRun : _dualYStation2TestRun;
                recordIds = state.TestRecordIds.Where(id => id > 0).Distinct().ToList();

                if (!string.Equals(state.Sn, sn, StringComparison.Ordinal)
                    || !string.Equals(state.WoCode, woCode, StringComparison.Ordinal))
                {
                    reason = $"扫码会话={state.Sn}/{state.WoCode}，流程结束读取={sn}/{woCode}";
                    return false;
                }

                if (recordIds.Count == 0)
                {
                    reason = "当前扫码会话尚无电测记录";
                    return false;
                }

                reason = string.Empty;
                return true;
            }
        }

        /// <summary>
        /// 将一次双Y实际电测作为新行追加到过程表。
        /// 行身份、工位、模式和完成时间在创建后保持稳定，流程结束阶段只补充对应压力字段。
        /// </summary>
        /// <param name="stationIndex">执行本次测试的 Y1/Y2 工位号。</param>
        /// <param name="databaseRowId">本次测试对应的 SQLite 主键；数据库写入失败时为 0。</param>
        /// <param name="sn">本次测试产品 SN。</param>
        /// <param name="woCode">本次测试产品工单号。</param>
        /// <param name="partNoId">本次测试产品规格编码。</param>
        /// <param name="res">本次接触电阻值，单位沿用 PLC 标定。</param>
        /// <param name="maxVoltage">本次耐压最大电压，单位 V。</param>
        /// <param name="result">本次耐压判定。</param>
        /// <param name="maxCurrent">本次耐压最大电流，单位 mA。</param>
        /// <param name="tvInfo">带模式前缀的仪器状态文本。</param>
        /// <param name="meterId">执行本次测试的耐压仪编号。</param>
        /// <param name="testMode">本次测试模式 ACW 或 DCW。</param>
        /// <param name="testedAt">本次仪器测试完成时间。</param>
        private void AppendDualYTestRecord(int stationIndex, long databaseRowId, string sn, string woCode, string partNoId,
            float res, float maxVoltage, bool result, float maxCurrent, string tvInfo, string meterId, string testMode, DateTime testedAt)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var record = new ProductInfoRecord
                {
                    DatabaseRowId = databaseRowId,
                    StationCode = DataModel.Settingmodel.SETTING_DATA.StationCode,
                    EQUIPMENTID = DataModel.Settingmodel.SETTING_DATA.MachineID,
                    DualYStationIndex = stationIndex,
                    Productinfo = new Productinfo
                    {
                        SN = sn,
                        WOCODE = woCode,
                        PartNOID = partNoId
                    },
                    Res = res,
                    TVMaxVoltage = maxVoltage,
                    TVMaxCurrent = maxCurrent,
                    TVResult = result,
                    TVInfo = tvInfo,
                    TVMeterID = meterId,
                    TestMode = testMode,
                    DateTime = testedAt,
                    PressureRecorded = false
                };

                DataModel.Recordmodel.ProductInfoRecords.Insert(0, record);
                UpdateDualYStationDisplay(stationIndex,
                    display => display.UpdateWorkflow("等待电测", $"{testMode} {(result ? "合格" : "不合格")} / {testedAt:HH:mm:ss}"));
            }));
        }

        /// <summary>
        /// 持久化并展示一次双Y实际耐压测试。
        /// 本入口始终向 SQLite 追加新行，随后把行 ID 登记到对应工位会话并在过程表追加一行。
        /// </summary>
        /// <param name="stationIndex">执行本次测试的 Y1/Y2 工位号。</param>
        /// <param name="sn">本次测试产品 SN。</param>
        /// <param name="woCode">本次测试产品工单号。</param>
        /// <param name="partNoId">本次测试产品规格编码。</param>
        /// <param name="res">本次接触电阻值，单位沿用 PLC 标定。</param>
        /// <param name="maxVoltage">本次耐压最大电压，单位 V。</param>
        /// <param name="result">本次耐压判定。</param>
        /// <param name="maxCurrent">本次耐压最大电流，单位 mA。</param>
        /// <param name="tvInfo">带 ACW/DCW 前缀的仪器状态文本。</param>
        /// <param name="meterId">执行本次测试的耐压仪编号。</param>
        /// <param name="testMode">本次测试模式 ACW 或 DCW。</param>
        /// <returns>SQLite 写入和工位会话登记均完成时返回 true。</returns>
        private bool PersistDualYTestAttempt(int stationIndex, string sn, string woCode, string partNoId,
            float res, float maxVoltage, bool result, float maxCurrent, string tvInfo, string meterId, string testMode)
        {
            DateTime testedAt = DateTime.Now;
            long recordId = sqlite.InsertDualYTestAttempt(
                woCode,
                partNoId,
                sn,
                DataModel.Settingmodel.SETTING_DATA.StationCode,
                DataModel.Settingmodel.SETTING_DATA.MachineID,
                res,
                maxVoltage,
                result,
                maxCurrent,
                tvInfo,
                meterId,
                testedAt);

            bool registered = RegisterDualYTestAttempt(stationIndex, sn, woCode, partNoId, recordId);
            AppendDualYTestRecord(stationIndex, recordId, sn, woCode, partNoId,
                res, maxVoltage, result, maxCurrent, tvInfo, meterId, testMode, testedAt);

            if (recordId <= 0)
            {
                writeLog($"[双Y-工位{stationIndex}-{testMode}] SQLite测试行新增失败，SN={sn}, WO={woCode}", true);
            }
            else if (!registered)
            {
                writeLog($"[双Y-工位{stationIndex}-{testMode}] SQLite测试行已保存，当前扫码会话登记失败，ID={recordId}, SN={sn}", true);
            }
            else
            {
                writeLog($"[双Y-工位{stationIndex}-{testMode}] 已追加第{GetDualYTestAttemptCount(stationIndex)}次测试记录，ID={recordId}, SN={sn}");
            }

            return recordId > 0 && registered;
        }

        /// <summary>
        /// 获取双Y指定工位当前扫码会话已经登记的测试次数，仅用于操作日志。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号。</param>
        /// <returns>本轮 ACW/DCW 实际测试行数量。</returns>
        private int GetDualYTestAttemptCount(int stationIndex)
        {
            lock (_dualYTestRunSync)
            {
                DualYTestRunState state = stationIndex == 1 ? _dualYStation1TestRun : _dualYStation2TestRun;
                return state.TestRecordIds.Count;
            }
        }

        /// <summary>
        /// 从本轮会话全部电测行中选出应写入压力的目标行 ID。
        /// 业务口径：压力属于各模式最终一次测试；同模式更早的复测行不写压力，便于区分复测尝试与最终归档行。
        /// ACW/DCW 双测时各自保留最新一条，避免只写全局最后一行导致另一模式无法参与压力判定。
        /// </summary>
        /// <param name="woCode">当前工位产品工单号，用于读取本地测试行。</param>
        /// <param name="partNoId">当前工位产品规格编码。</param>
        /// <param name="sn">当前工位产品 SN。</param>
        /// <param name="sessionRecordIds">当前扫码会话内全部 ACW/DCW 测试行 ID。</param>
        /// <returns>每个测试模式最新一条对应的 SQLite 行 ID；会话为空时返回空列表。</returns>
        private List<long> SelectDualYPressureTargetRecordIds(string woCode, string partNoId, string sn, IEnumerable<long> sessionRecordIds)
        {
            List<sqlite.ElectricalTestProcessRow> rows = sqlite.GetElectricalTestProcessRowsByIds(woCode, partNoId, sn, sessionRecordIds);
            return rows
                .Where(row => row.Id > 0)
                .GroupBy(row => row.TestMode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(row => row.Id).First().Id)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// 按 SQLite 行 ID 给双Y过程表中的压力目标行补充压力快照。
        /// 仅刷新各模式最新测试行；同模式更早复测行保持“待归档”，记录完成时间和列表顺序保持稳定。
        /// </summary>
        /// <param name="stationIndex">流程结束所属 Y1/Y2 工位号。</param>
        /// <param name="sn">当前工位产品 SN。</param>
        /// <param name="woCode">当前工位产品工单号；SQLite ID 在各工单数据库内独立编号，因此与 ID 共同定位界面行。</param>
        /// <param name="recordIds">应写入压力的 SQLite 行 ID，通常为各模式最新一条。</param>
        /// <param name="averagePressure">PLC uint32 压力平均值，单位沿用现场标定。</param>
        /// <param name="maxPressure">PLC uint32 压力最大值，单位沿用现场标定。</param>
        /// <param name="minPressure">PLC uint32 压力最小值，单位沿用现场标定。</param>
        /// <param name="pressureResult">按当前工艺上下限计算的压力判定。</param>
        private void UpdateDualYPressureRecords(int stationIndex, string sn, string woCode, IEnumerable<long> recordIds,
            UInt32 averagePressure, UInt32 maxPressure, UInt32 minPressure, bool pressureResult)
        {
            var idSet = new HashSet<long>((recordIds ?? Enumerable.Empty<long>()).Where(id => id > 0));
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (ProductInfoRecord record in DataModel.Recordmodel.ProductInfoRecords)
                {
                    if (record.DualYStationIndex != stationIndex
                        || !string.Equals(record.Productinfo?.SN, sn, StringComparison.Ordinal)
                        || !string.Equals(record.Productinfo?.WOCODE, woCode, StringComparison.Ordinal)
                        || !idSet.Contains(record.DatabaseRowId))
                    {
                        continue;
                    }

                    record.Pressure_Average = averagePressure;
                    record.Pressure_Max = maxPressure;
                    record.Pressure_Min = minPressure;
                    record.Pressure_Result = pressureResult;
                    record.PressureRecorded = true;
                }
            }));
        }

        /// <summary>
        /// D1020/D1021 流程结束触发后的归档：按双Y独立配置的产品码地址读取 SN/工单，再读压力、校验电测、写 MES 并报工。
        /// 产品码地址与流程结束触发地址分离；Y1/Y2 分别使用 Station1ScanSnAddress / Station2ScanSnAddress。
        /// </summary>
        /// <param name="stationIndex">1 或 2。</param>
        public void DualYFlowEndProcess(int stationIndex)
        {
            if (stationIndex != 1 && stationIndex != 2)
            {
                return;
            }

            string busyReason;
            if (!TryBeginDualYFlowEndProcessing(stationIndex, out busyReason))
            {
                writeLog($"[双Y-工位{stationIndex}流程结束] {busyReason}，忽略重复触发。", true);
                return;
            }

            UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档处理中"));

            try
            {
                // 产品码读取地址与 D1020/D1021 流程结束触发地址分离：
                // 触发由 Station1/2FlowEndAddress 负责；此处只读取扫码写入的 SN/工单区。
                int snAddress = stationIndex == 1
                    ? DataModel.DualYConfiguration.Station1ScanSnAddress
                    : DataModel.DualYConfiguration.Station2ScanSnAddress;

                string snCode;
                string woCode;
                string rawCode;
                if (!TryReadProductCodeFromPlc(snAddress, $"双Y-工位{stationIndex}流程结束", out snCode, out woCode, out rawCode, 3, 500))
                {
                    writeLog($"[双Y-工位{stationIndex}流程结束] 产品编码读取失败，已中止归档。原始值=[{rawCode}]", true);
                    UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档失败", "产品码读取失败"));
                    return;
                }

                UpdateDualYStationDisplay(stationIndex, display =>
                {
                    display.BeginProduct(snCode, woCode);
                    display.UpdateWorkflow("归档处理中");
                });

                string partnoid = DataModel.Processmodel.PartNOID;
                List<long> testRecordIds;
                string testRunError;
                if (!TryGetDualYTestRunRecordIds(stationIndex, snCode, woCode, out testRecordIds, out testRunError))
                {
                    writeLog($"[双Y归档] 当前工位测试记录校验失败，工位={stationIndex}, SN={snCode}, 原因={testRunError}", true);
                    UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档失败", testRunError));
                    return;
                }

                bool requireAcw;
                bool requireDcw;
                string requiredModeText = GetDualYRequiredElectricalModes(out requireAcw, out requireDcw);
                writeLog($"[双Y归档] 开始，工位={stationIndex}, SN={snCode}, WO={woCode}, 模式={requiredModeText}, 测试次数={testRecordIds.Count}");

                int pressureAddress = stationIndex == 1
                    ? DataModel.Settingmodel.AddressPressure
                    : DataModel.DualYConfiguration.Station2PressureAddress;
                UInt32 averagePressure = PLC_ReadUint32(pressureAddress);
                UInt32 maxPressure = PLC_ReadUint32(pressureAddress + 2);
                UInt32 minPressure = PLC_ReadUint32(pressureAddress + 4);
                bool pressureResult = maxPressure <= DataModel.Processmodel.PressureParamter.Max_Pressure
                    && minPressure >= DataModel.Processmodel.PressureParamter.Min_Pressure;
                writeLog($"[双Y归档] 压力读取完成，工位={stationIndex}, 地址=D{pressureAddress}/D{pressureAddress + 2}/D{pressureAddress + 4}, 平均={averagePressure}, 最大={maxPressure}, 最小={minPressure}");

                // 口径B：压力只归属各模式最新一次测试；同模式更早复测行保持无压力，便于区分尝试与最终行。
                List<long> pressureTargetIds = SelectDualYPressureTargetRecordIds(woCode, partnoid, snCode, testRecordIds);
                if (pressureTargetIds.Count == 0)
                {
                    writeLog($"[双Y归档] 压力目标行为空，工位={stationIndex}, SN={snCode}, WO={woCode}", true);
                    UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档失败", "本轮无可写入压力的电测行"));
                    return;
                }

                writeLog($"[双Y归档] 压力写入目标，工位={stationIndex}, 会话行数={testRecordIds.Count}, 目标行数={pressureTargetIds.Count}, IDs={string.Join(",", pressureTargetIds)}");
                bool pressureDbOk = sqlite.UpdatePressureForElectricalRowsByIds(
                    woCode, partnoid, snCode, pressureTargetIds, averagePressure, maxPressure, minPressure, pressureResult);
                if (!pressureDbOk)
                {
                    writeLog($"[双Y归档] SQLite压力写入失败，工位={stationIndex}, SN={snCode}, WO={woCode}", true);
                    UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档失败", "本轮压力数据写入失败"));
                    return;
                }
                UpdateDualYPressureRecords(
                    stationIndex, snCode, woCode, pressureTargetIds, averagePressure, maxPressure, minPressure, pressureResult);
                TracePressureNg3IfFailed($"双Y-工位{stationIndex}流程结束",
                    maxPressure, minPressure, averagePressure, pressureResult, snCode, woCode);

                int checkCode = sqlite.CheckElectricalOnlyDualTest(
                    woCode, partnoid, snCode, DataModel.Processmodel.ResParameter.Max_Res,
                    requireAcw, requireDcw, testRecordIds);
                if (!pressureResult && checkCode == 0)
                {
                    checkCode = 3;
                }
                string resultstr = MapElectricalCheckCodeToResultString(checkCode);
                writeLog($"[双Y归档] 综合判定完成，工位={stationIndex}, SN={snCode}, 结果={resultstr}");

                bool mesOk;
                bool reportOk;
                lock (_dualYMesLock)
                {
                    mesOk = SaveDualYElectricalProcessDataToMes(
                        snCode, woCode, partnoid, resultstr, requireAcw, requireDcw, testRecordIds);
                    reportOk = report(woCode, snCode, resultstr);
                }

                if (!reportOk)
                {
                    writeLog($"[双Y归档] 报工失败，工位={stationIndex}, SN={snCode}, WO={woCode}, 结果={resultstr}", true);
                }

                string archiveState = mesOk && reportOk ? "归档完成" : "归档异常";
                string archiveResult = $"{resultstr} / MES:{(mesOk ? "成功" : "失败")} / 报工:{(reportOk ? "成功" : "失败")}";
                UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow(archiveState, archiveResult));
                writeLog($"[双Y归档] 完成，工位={stationIndex}, SN={snCode}, 结果={resultstr}, MES过程数据={(mesOk ? "成功" : "失败")}, 报工={(reportOk ? "成功" : "失败")}");
            }
            catch (Exception ex)
            {
                writeLog($"[双Y归档] 异常，工位={stationIndex}, {ex.Message}", true);
                sqlite.WriteErrorLog("[双Y流程结束异常]归档失败", ex.Message, string.Empty, string.Empty);
                UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档异常", "请查看运行日志"));
            }
            finally
            {
                EndDualYFlowEndProcessing(stationIndex);
            }
        }

        /// <summary>
        /// 双Y扫码成功后开始对应工位的测试会话并刷新工位卡片。
        /// 过程表只接收仪器已经完成的实际测试行，扫码阶段的产品状态由 Y1/Y2 工位卡片承载。
        /// </summary>
        /// <param name="sn">MES 解码和工单校验通过的产品 SN。</param>
        /// <param name="wocode">与当前产品对应的工单号，用于工位卡片和过程记录显示。</param>
        /// <param name="partnoid">当前产品规格；为空时沿用运行中的产品规格。</param>
        /// <param name="dualYStationIndex">扫码来源的双Y物理工位号，取值 1 或 2。</param>
        internal void BeginDualYProductSession(string sn, string wocode, string partnoid, int dualYStationIndex)
        {
            if (string.IsNullOrWhiteSpace(sn))
            {
                return;
            }

            string effectivePartNoId = string.IsNullOrWhiteSpace(partnoid) ? DataModel.Processmodel.PartNOID : partnoid;
            BeginDualYTestRun(dualYStationIndex, sn, wocode, effectivePartNoId);
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    UpdateDualYStationDisplay(dualYStationIndex, display => display.BeginProduct(sn, wocode));
                    writeLog($"[双Y-工位{dualYStationIndex}扫码] 已开始测试会话，SN={sn}, WO={wocode}");
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("[双Y]BeginDualYProductSession失败", ex.Message, sn, wocode);
                }
            }));
        }

        private static string MapElectricalCheckCodeToResultString(int checkCode)
        {
            switch (checkCode)
            {
                case 0: return "合格";
                case 2: return "耐压测试不合格";
                case 3: return "阻值或压力测试不合格";
                default: return "耐压测试不合格";
            }
        }

        /// <summary>
        /// 上传双Y当前工位会话的全部 MES 电测过程数据。
        /// 每次 ACW/DCW 复测均对应一条过程数据；产品报工仍由流程结束阶段统一执行一次。
        /// </summary>
        /// <param name="sn">当前流程结束工位从 PLC 产品码区读取到的 SN。</param>
        /// <param name="wocode">当前 SN 对应工单号。</param>
        /// <param name="partnoid">当前生产规格编码。</param>
        /// <param name="resultstr">本轮流程结束综合结果，用于 MES 过程数据最终结果字段。</param>
        /// <param name="requireAcw">true 表示本轮应上传 ACW 过程数据。</param>
        /// <param name="requireDcw">true 表示本轮应上传 DCW 过程数据。</param>
        /// <param name="recordIds">当前工位扫码会话内每次实际测试对应的 SQLite 行 ID。</param>
        /// <returns>所有期望过程数据均存在且上传成功时返回 true。</returns>
        private bool SaveDualYElectricalProcessDataToMes(string sn, string wocode, string partnoid, string resultstr,
            bool requireAcw, bool requireDcw, IEnumerable<long> recordIds)
        {
            string stationCode = DataModel.Settingmodel.SETTING_DATA.StationCode;
            string machineId = DataModel.Settingmodel.SETTING_DATA.MachineID;

            List<sqlite.ElectricalTestProcessRow> rows = sqlite.GetElectricalTestProcessRowsByIds(wocode, partnoid, sn, recordIds);
            rows = rows
                .Where(row => (requireAcw && string.Equals(row.TestMode, "ACW", StringComparison.OrdinalIgnoreCase))
                    || (requireDcw && string.Equals(row.TestMode, "DCW", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (rows.Count == 0)
            {
                writeLog($"[双Y归档] MES过程数据上传失败，SN={sn}, WO={wocode}, 原因=本地期望电测记录为空", true);
                return false;
            }

            bool allOk = true;
            bool hasAcw = rows.Any(row => string.Equals(row.TestMode, "ACW", StringComparison.OrdinalIgnoreCase));
            bool hasDcw = rows.Any(row => string.Equals(row.TestMode, "DCW", StringComparison.OrdinalIgnoreCase));
            if ((requireAcw && !hasAcw) || (requireDcw && !hasDcw))
            {
                writeLog($"[双Y归档] MES过程数据不完整，SN={sn}, WO={wocode}, 期望ACW={requireAcw}, 实际ACW={hasAcw}, 期望DCW={requireDcw}, 实际DCW={hasDcw}", true);
                allOk = false;
            }

            foreach (var row in rows)
            {
                var persistedTvInfo = GetLocalizedTvStatus(row.TVInfo);
                bool saveOk = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                    stationCode, machineId, partnoid, wocode, sn,
                    row.TakePhoto1, row.Res, row.TVMaxVoltage, row.TVMaxCurrent, row.TVMeterID, persistedTvInfo, row.TVResult,
                    row.PressureMax, row.PressureAverage, row.PressureMin, row.PressureResult, row.TakePhoto2, resultstr);

                writeLog($"[双Y归档] MES过程数据上传{(saveOk ? "成功" : "失败")}，SN={sn}, WO={wocode}, 模式={row.TestMode}, 结果={resultstr}", !saveOk);
                allOk = allOk && saveOk;
            }

            return allOk;
        }
    }
}
