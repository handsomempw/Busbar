using GalaSoft.MvvmLight;
using HslCommunication.ModBus;
using System.Collections.Generic;
using System.Threading;
using System;
using System.Runtime.CompilerServices;


namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        // 用于跟踪PLC连接状态，避免重复记录日志
        private bool _lastPLCConnectedStatus = false;

        // IR可用状态：仅在变化时记录日志，避免刷屏
        private bool? _lastIrAvailable = null;
        private bool? _lastIrRawCoil = null;

        // IR触发诊断：仅在变化时记录，避免刷屏
        private int? _lastIrTrigLogged = null;
        private int? _lastIrStartSkipReasonLoggedForTrig = null;
        private bool? _lastTv3IrBothAvailable = null;

        /// <summary>
        /// 标准耐压握手最近一次已落盘快照。
        /// 仅在 D1006-D1011 任一触发或结果值变化时记录，用于还原 PLC 触发、上位机沿判定和结果清零的先后顺序。
        /// </summary>
        private int? _lastTv1TriggerTrace;
        private int? _lastTv1ResultTrace;
        private int? _lastTv2TriggerTrace;
        private int? _lastTv2ResultTrace;
        private int? _lastTv3TriggerTrace;
        private int? _lastTv3ResultTrace;

        /// <summary>
        /// 双Y部署强制忽略工位3后的 TV3Available 快照；仅在状态变化时记日志，避免周期刷屏。
        /// </summary>
        private bool? _lastDualYForcedTv3Unavailable = null;

        // 联动扫码线程保护：同一 M3046 高电平周期只允许一个业务处理线程运行
        private int _linkedScanProcessing = 0;

        /// <summary>
        /// 应用退出停止信号。PLC 轮询与可用状态刷新共用该信号，窗口关闭后停止接受新的设备触发。
        /// </summary>
        private readonly ManualResetEventSlim _runtimeStopSignal = new ManualResetEventSlim(false);
        private readonly object _runtimeWorkerSync = new object();
        private readonly List<Thread> _runtimeBusinessWorkers = new List<Thread>();
        private volatile bool _runtimeStopRequested;
        private Thread _plcProcessThread;
        private Thread _plcHandshakeThread;

        /// <summary>
        /// AOI-only模式判定：当TV1、TV2、TV3与IR电测路径均不可用时，视为仅走AOI流程。
        /// </summary>
        /// <remarks>
        /// TV3与IR共用第三电测位置，现场通过M3032/M3033选择耐压或绝缘电阻路径；
        /// 任一电测路径可用时，阻值、压力与CHECK仍按电测流程参与判定。
        /// </remarks>
        private bool IsAoiOnlyMode
        {
            get
            {
                return DataModel != null
                    && DataModel.Processmodel != null
                    && DataModel.Processmodel.TVAvailable != null
                    && !DataModel.Processmodel.TVAvailable.TV1Available
                    && !DataModel.Processmodel.TVAvailable.TV2Available
                    && !DataModel.Processmodel.TVAvailable.TV3Available
                    && !DataModel.Processmodel.TVAvailable.IRAvailable;
            }
        }

        /// <summary>
        /// 启动 PLC 生产信号轮询线程。该线程使用后台线程并支持统一停止，重复调用时沿用已经运行的实例。
        /// </summary>
        public void PLC_Start()
        {
            lock (_runtimeWorkerSync)
            {
                if (_plcProcessThread != null && _plcProcessThread.IsAlive)
                {
                    return;
                }

                _runtimeStopRequested = false;
                _runtimeStopSignal.Reset();
                _plcProcessThread = new Thread(PLC_Process)
                {
                    IsBackground = true,
                    Name = "PLC生产信号轮询"
                };
                _plcProcessThread.Start();
            }
        }
        private void PLC_Process()
        {

            while (!_runtimeStopRequested)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;

                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        // 如果上次连接失败，现在连接成功，记录一次恢复日志
                        if (!_lastPLCConnectedStatus)
                        {
                            writeLog("PLC通讯已恢复，重新连接成功", true);
                        }
                        // 更新连接状态为已连接
                        _lastPLCConnectedStatus = true;
                        
                        #region 读取数据
                        var readresult = modbusTcp.ReadUInt16(DataModel.Settingmodel.AddressStart.ToString(), 20);
                        var secondScanResult = modbusTcp.ReadUInt16(DataModel.Settingmodel.SecondScanTrigAddress.ToString(), 1);
                        var linkedScanResult = modbusTcp.ReadCoil(DataModel.Settingmodel.LinkedScanTrigAddress.ToString(), 1);
                        var dualYModeResult = modbusTcp.ReadCoil(DataModel.DualYConfiguration.ElectricalTestModeCoilAddress.ToString(), 1);
                        var dualYStation1ScanResult = modbusTcp.ReadUInt16(DataModel.DualYConfiguration.Station1ScanTriggerAddress.ToString(), 1);
                        var dualYStation2ScanResult = modbusTcp.ReadUInt16(DataModel.DualYConfiguration.Station2ScanTriggerAddress.ToString(), 1);
                        var dualYStation1FlowEndResult = modbusTcp.ReadUInt16(DataModel.DualYConfiguration.Station1FlowEndAddress.ToString(), 1);
                        var dualYStation2FlowEndResult = modbusTcp.ReadUInt16(DataModel.DualYConfiguration.Station2FlowEndAddress.ToString(), 1);

                        if (readresult.IsSuccess)
                        {
                            int ScanTrig = readresult.Content[0];
                            int TakePhoto1Trig = readresult.Content[2];
                            int TV1Trig = readresult.Content[6];
                            int TV1Result = readresult.Content[7];
                            int TV2Trig = readresult.Content[8];
                            int TV2Result = readresult.Content[9];
                            int TV3Trig = readresult.Content[10];
                            int TV3Result = readresult.Content[11];
                            // IR触发（D1014）：1=启动 2=停止
                            int IRTrig = readresult.Content[14];

                            int SecondScanTrig = secondScanResult.IsSuccess ? secondScanResult.Content[0] : 0;
                            int LinkedScanTrig = linkedScanResult.IsSuccess && linkedScanResult.Content[0] ? 1 : 0;
                            bool DualYModeKnown = dualYModeResult.IsSuccess;
                            bool DualYModeCoil = DualYModeKnown && dualYModeResult.Content[0];
                            int DualYStation1ScanTrig = dualYStation1ScanResult.IsSuccess ? dualYStation1ScanResult.Content[0] : 0;
                            int DualYStation2ScanTrig = dualYStation2ScanResult.IsSuccess ? dualYStation2ScanResult.Content[0] : 0;
                            int DualYStation1FlowEndTrig = dualYStation1FlowEndResult.IsSuccess ? dualYStation1FlowEndResult.Content[0] : 0;
                            int DualYStation2FlowEndTrig = dualYStation2FlowEndResult.IsSuccess ? dualYStation2FlowEndResult.Content[0] : 0;

                            RefreshDualYModeFromPlc(DualYModeKnown, DualYModeCoil);

                            // 部署约束：双Y专用窗口没有相机/机器人资源，即使线圈为标准模式也不进入标准流程。
                            // 模式未知时双侧模式相关触发全部暂停，避免读失败被当成标准流程。
                            bool dualYDeployment = IsDualYElectricalTestDeployment();
                            bool DualYModeActive = DualYModeKnown && DualYModeCoil;
                            bool StandardFlowActive = DualYModeKnown && !DualYModeCoil && !dualYDeployment;
                            bool ModeTriggersPaused = !DualYModeKnown || (dualYDeployment && !DualYModeCoil);

                            TraceTvHandshakeSnapshot(
                                1, TV1Trig, TV1Result,
                                DataModel.Processmodel.TV1_Trig_IO.IOstatus,
                                StandardFlowActive,
                                ref _lastTv1TriggerTrace,
                                ref _lastTv1ResultTrace);
                            TraceTvHandshakeSnapshot(
                                2, TV2Trig, TV2Result,
                                DataModel.Processmodel.TV2_Trig_IO.IOstatus,
                                StandardFlowActive,
                                ref _lastTv2TriggerTrace,
                                ref _lastTv2ResultTrace);
                            TraceTvHandshakeSnapshot(
                                3, TV3Trig, TV3Result,
                                DataModel.Processmodel.TV3_Trig_IO.IOstatus,
                                StandardFlowActive,
                                ref _lastTv3TriggerTrace,
                                ref _lastTv3ResultTrace);

                            if (DualYModeActive)
                            {
                                ScanTrig = DualYStation1ScanTrig;
                            }
                            else if (ModeTriggersPaused)
                            {
                                // 保持扫码沿判定不前进，恢复模式后再识别 0→1。
                                ScanTrig = DataModel.Processmodel.Scan_Trig_IO.IOstatus;
                            }

                            #region 扫码触发
                            try
                            {
                                if (DualYModeActive)
                                {
                                    // 双Y：启动/断线后须先见扫码触发回 0，再认 0→1，避免残留高电平误触发。
                                    if (ObserveDualYRisingEdge(
                                        ref _dualYY1ScanEdgeArmed,
                                        ScanTrig,
                                        DataModel.Processmodel.Scan_Trig_IO.IOstatus,
                                        dualYStation1ScanResult.IsSuccess))
                                    {
                                        StartRuntimeWorker(ScannerProcess, "扫码处理");
                                    }
                                }
                                else if (ScanTrig == 1 & DataModel.Processmodel.Scan_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(ScannerProcess, "扫码处理");
                                }
                            }
                            catch {; }
                            #endregion

                            #region 第二扫码触发
                            try
                            {
                                if (DualYModeActive && SecondScanTrig == 1 && DataModel.Processmodel.SecondScan_Trig_IO.IOstatus == 0)
                                {
                                    writeLog($"[双Y电测] 忽略下料扫码触发 D{DataModel.Settingmodel.SecondScanTrigAddress}=1");
                                }
                                else if (StandardFlowActive && SecondScanTrig == 1 & DataModel.Processmodel.SecondScan_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(SecondScannerProcess, "下料扫码处理");
                                }
                            }
                            catch {; }
                            #endregion

                            #region 双Y 2工位扫码触发
                            try
                            {
                                if (DualYModeActive &&
                                    ObserveDualYRisingEdge(
                                        ref _dualYY2ScanEdgeArmed,
                                        DualYStation2ScanTrig,
                                        DataModel.Processmodel.DualYStation2Scan_Trig_IO.IOstatus,
                                        dualYStation2ScanResult.IsSuccess))
                                {
                                    StartRuntimeWorker(DualYStation2ScannerProcess, "双Y-Y2扫码处理");
                                }
                            }
                            catch (Exception ex)
                            {
                                writeLog($"[双Y-2工位扫码] D{DataModel.DualYConfiguration.Station2ScanTriggerAddress}触发处理异常: {ex.Message}", true);
                            }
                            #endregion

                            #region 双Y流程结束触发
                            try
                            {
                                if (DualYModeActive &&
                                    ObserveDualYRisingEdge(
                                        ref _dualYY1FlowEndEdgeArmed,
                                        DualYStation1FlowEndTrig,
                                        DataModel.Processmodel.DualYStation1FlowEnd_Trig_IO.IOstatus,
                                        dualYStation1FlowEndResult.IsSuccess))
                                {
                                    StartRuntimeWorker(() => DualYFlowEndProcess(1), "双Y-Y1结算");
                                }

                                if (DualYModeActive &&
                                    ObserveDualYRisingEdge(
                                        ref _dualYY2FlowEndEdgeArmed,
                                        DualYStation2FlowEndTrig,
                                        DataModel.Processmodel.DualYStation2FlowEnd_Trig_IO.IOstatus,
                                        dualYStation2FlowEndResult.IsSuccess))
                                {
                                    StartRuntimeWorker(() => DualYFlowEndProcess(2), "双Y-Y2结算");
                                }
                            }
                            catch (Exception ex)
                            {
                                writeLog($"[双Y归档] 流程结束触发处理异常: {ex.Message}", true);
                            }
                            #endregion

                            #region 联动扫码触发
                            try
                            {
                                if (DualYModeActive && LinkedScanTrig == 1 && DataModel.Processmodel.LinkedScan_Trig_IO.IOstatus == 0)
                                {
                                    writeLog($"[双Y电测] 忽略联动扫码触发 M{DataModel.Settingmodel.LinkedScanTrigAddress}=1");
                                }
                                else if (StandardFlowActive && LinkedScanTrig == 1 & DataModel.Processmodel.LinkedScan_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(LinkedScannerProcess, "联动扫码处理");
                                }
                            }
                            catch (Exception ex)
                            {
                                writeLog($"[联动扫码] M{DataModel.Settingmodel.LinkedScanTrigAddress}触发处理异常: {ex.Message}", true);
                            }
                            #endregion

                            #region 拍照触发
                            try
                            {
                                if (DualYModeActive && TakePhoto1Trig == 1 && DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus == 0)
                                {
                                    writeLog($"[双Y电测] 忽略拍照留底触发 D{DataModel.Settingmodel.AddressStart + 2}=1");
                                }
                                else if (StandardFlowActive && TakePhoto1Trig == 1 & DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(TakePhoto1Process, "拍照留底处理");
                                }
                            }
                            catch {; }
                            #endregion

                            #region 耐压1触发
                            try
                            {
                                // TV1Trig=1: ACW交流耐压测试
                                // TV1Trig=2: DCW直流耐压测试
                                // TV1Trig=3: 停止测试
                                if (TV1Trig == 1 & DataModel.Processmodel.TV1_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(TV1Process_ACW, "耐压1-ACW");
                                }
                                else if (TV1Trig == 2 & DataModel.Processmodel.TV1_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(TV1Process_DCW, "耐压1-DCW");
                                }
                                if ((TV1Trig == 3) & DataModel.Processmodel.TV1_Trig_IO.IOstatus != TV1Trig)
                                {
                                    DataModel.Settingmodel.AT9620_1.stop = true;
                                }
                            }
                            catch {; }
                            #endregion

                            #region 耐压2触发
                            try
                            {
                                // TV2Trig=1: ACW交流耐压测试
                                // TV2Trig=2: DCW直流耐压测试
                                // TV2Trig=3: 停止测试
                                if (TV2Trig == 1 & DataModel.Processmodel.TV2_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(TV2Process_ACW, "耐压2-ACW");
                                }
                                else if (TV2Trig == 2 & DataModel.Processmodel.TV2_Trig_IO.IOstatus == 0)
                                {
                                    StartRuntimeWorker(TV2Process_DCW, "耐压2-DCW");
                                }
                                if ((TV2Trig == 3) & DataModel.Processmodel.TV2_Trig_IO.IOstatus != TV2Trig)
                                {
                                    DataModel.Settingmodel.AT9620_2.stop = true;
                                }
                            }
                            catch {; }
                            #endregion

                            #region IR绝缘电阻测试触发（需 M3033 有效）
                            try
                            {
                                if (StandardFlowActive)
                                {
                                    // 仅在触发值变化时记录一次诊断日志，帮助定位“PLC已写1但上位机没动作”
                                    if (_lastIrTrigLogged == null || _lastIrTrigLogged.Value != IRTrig)
                                    {
                                        _lastIrTrigLogged = IRTrig;
                                        _lastIrStartSkipReasonLoggedForTrig = null; // 新的触发值到来，允许记录一次“未触发原因”
                                        writeLog($"[IR触发] D{DataModel.Settingmodel.IRTrigAddress}={IRTrig}, 上次IOstatus={DataModel.Processmodel.IR_Trig_IO.IOstatus}, IRAvailable(M{DataModel.Settingmodel.IRMeterAvailableAddress})={DataModel.Processmodel.TVAvailable.IRAvailable} Raw={DataModel.Processmodel.TVAvailable.IRAvailableRawCoil}");
                                    }

                                    if (DataModel.Processmodel.TVAvailable.IRAvailable)
                                    {
                                        // 1=启动：仅当上一次状态为0（低电平）时触发，避免重复启动
                                        if (IRTrig == 1 & DataModel.Processmodel.IR_Trig_IO.IOstatus == 0)
                                        {
                                            writeLog("[IR触发] ✅ 条件满足，启动IRProcess线程");
                                            StartRuntimeWorker(IRProcess, "绝缘电阻测试");
                                        }
                                        else if (IRTrig == 1)
                                        {
                                            // 避免刷屏：仅在“本次触发值=1”的周期内记录一次原因
                                            if (_lastIrStartSkipReasonLoggedForTrig == null)
                                            {
                                                _lastIrStartSkipReasonLoggedForTrig = 1;
                                                writeLog($"[IR触发] ⏭ 已收到启动(=1)但未触发线程：原因=IOstatus非0（当前IOstatus={DataModel.Processmodel.IR_Trig_IO.IOstatus}，需要PLC产生0->1沿）");
                                            }
                                        }

                                        // 2=停止：置位 stop，设备内部 STAT:DISC 放电退出
                                        if (IRTrig == 2 & DataModel.Processmodel.IR_Trig_IO.IOstatus != IRTrig)
                                        {
                                            writeLog("[IR触发] 收到停止(=2)，置位AT6835FL.stop=true");
                                            DataModel.Settingmodel.AT6835FL_1.stop = true;
                                        }
                                    }
                                    else if (IRTrig == 1)
                                    {
                                        // 避免刷屏：仅在“本次触发值=1”的周期内记录一次原因
                                        if (_lastIrStartSkipReasonLoggedForTrig == null)
                                        {
                                            _lastIrStartSkipReasonLoggedForTrig = 1;
                                            writeLog($"[IR触发] ⛔ PLC请求启动(=1)但IRAvailable=false，已忽略启动。请检查M{DataModel.Settingmodel.IRMeterAvailableAddress}信号约定（1=可用）及PLC状态。");
                                        }
                                    }
                                }
                                else if (IRTrig == 1 && DataModel.Processmodel.IR_Trig_IO.IOstatus == 0)
                                {
                                    writeLog($"[双Y电测] 忽略IR启动触发 D{DataModel.Settingmodel.IRTrigAddress}=1");
                                }
                                else if (IRTrig == 2 && DataModel.Processmodel.IR_Trig_IO.IOstatus != IRTrig)
                                {
                                    DataModel.Settingmodel.AT6835FL_1.stop = true;
                                }
                            }
                            catch { ; }
                            #endregion

                            #region 耐压3触发
                            try
                            {
                                // TV3Trig=1: ACW交流耐压测试
                                // TV3Trig=2: DCW直流耐压测试
                                // TV3Trig=3: 停止测试
                                if (DualYModeActive && (TV3Trig == 1 || TV3Trig == 2) && DataModel.Processmodel.TV3_Trig_IO.IOstatus != TV3Trig)
                                {
                                    writeLog($"[双Y电测] 忽略耐压3触发 D{DataModel.Settingmodel.AddressStart + 10}={TV3Trig}");
                                }
                                else if (StandardFlowActive && DataModel.Processmodel.TVAvailable.TV3Available)
                                {
                                    if (TV3Trig == 1 & DataModel.Processmodel.TV3_Trig_IO.IOstatus == 0)
                                    {
                                        StartRuntimeWorker(TV3Process_ACW, "耐压3-ACW");
                                    }
                                    else if (TV3Trig == 2 & DataModel.Processmodel.TV3_Trig_IO.IOstatus == 0)
                                    {
                                        StartRuntimeWorker(TV3Process_DCW, "耐压3-DCW");
                                    }
                                }
                                else if (StandardFlowActive && (TV3Trig == 1 || TV3Trig == 2) & DataModel.Processmodel.TV3_Trig_IO.IOstatus != TV3Trig)
                                {
                                    writeLog($"[耐压3触发] D{DataModel.Settingmodel.AddressStart + 10}={TV3Trig}，但TV3Available=false，忽略本次启动。请检查M{DataModel.Settingmodel.Meter3AvailableAddress}可用状态。");
                                }

                                if ((TV3Trig == 3) & DataModel.Processmodel.TV3_Trig_IO.IOstatus != TV3Trig)
                                {
                                    DataModel.Settingmodel.AT9620_3.stop = true;
                                }
                            }
                            catch {; }
                            #endregion

                            #region 阻值触发
                            int Res1Trig = 0;
                            int Res2Trig = 0;
                            int Res3Trig = 0;

                            // AOI-only模式：跳过全部电测（TV/RES/压力），阻值触发与读取流程直接忽略
                            if (!IsAoiOnlyMode)
                            {
                                // 读取阻值触发信号（M地址，Bool类型）
                                var res1TrigResult = modbusTcp.ReadCoil(DataModel.Settingmodel.Res1TrigAddress.ToString(), 1);
                                if (!res1TrigResult.IsSuccess)
                                {
                                    writeLog($"阻值1触发信号读取失败 M{DataModel.Settingmodel.Res1TrigAddress}, Err:{res1TrigResult.Message}");
                                }
                                var res2TrigResult = modbusTcp.ReadCoil(DataModel.Settingmodel.Res2TrigAddress.ToString(), 1);
                                if (!res2TrigResult.IsSuccess)
                                {
                                    writeLog($"阻值2触发信号读取失败 M{DataModel.Settingmodel.Res2TrigAddress}, Err:{res2TrigResult.Message}");
                                }
                                var res3TrigResult = modbusTcp.ReadCoil(DataModel.Settingmodel.Res3TrigAddress.ToString(), 1);
                                if (!res3TrigResult.IsSuccess)
                                {
                                    writeLog($"阻值3触发信号读取失败 M{DataModel.Settingmodel.Res3TrigAddress}, Err:{res3TrigResult.Message}");
                                }

                                // 判断读取阻值触发信号（M地址，Bool类型）的结果，若通信成功且内容为true，则ResTrig为1，否则为0
                                Res1Trig = res1TrigResult.IsSuccess && res1TrigResult.Content[0] ? 1 : 0;
                                Res2Trig = res2TrigResult.IsSuccess && res2TrigResult.Content[0] ? 1 : 0;
                                Res3Trig = res3TrigResult.IsSuccess && res3TrigResult.Content[0] ? 1 : 0;

                                // 触发状态变更日志（避免刷屏，仅在状态变化时记录）
                                if (Res1Trig != DataModel.Processmodel.Res1_Trig_IO.IOstatus)
                                {
                                    writeLog($"阻值1触发状态 M{DataModel.Settingmodel.Res1TrigAddress} 变更为 {Res1Trig}");
                                }
                                if (Res2Trig != DataModel.Processmodel.Res2_Trig_IO.IOstatus)
                                {
                                    writeLog($"阻值2触发状态 M{DataModel.Settingmodel.Res2TrigAddress} 变更为 {Res2Trig}");
                                }
                                if (Res3Trig != DataModel.Processmodel.Res3_Trig_IO.IOstatus)
                                {
                                    writeLog($"阻值3触发状态 M{DataModel.Settingmodel.Res3TrigAddress} 变更为 {Res3Trig}");
                                }

                                // 阻值1触发
                                try
                                {
                                    if (Res1Trig == 1 & DataModel.Processmodel.Res1_Trig_IO.IOstatus == 0)
                                    {
                                        writeLog($"阻值1触发=1(M{DataModel.Settingmodel.Res1TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes}");
                                        StartRuntimeWorker(Res1Process, "阻值1处理");
                                    }
                                }
                                catch {; }

                                // 阻值2触发
                                try
                                {
                                    if (Res2Trig == 1 & DataModel.Processmodel.Res2_Trig_IO.IOstatus == 0)
                                    {
                                        writeLog($"阻值2触发=1(M{DataModel.Settingmodel.Res2TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes + 2}");
                                        StartRuntimeWorker(Res2Process, "阻值2处理");
                                    }
                                }
                                catch {; }

                                // 阻值3触发
                                try
                                {
                                    if (Res3Trig == 1 & DataModel.Processmodel.Res3_Trig_IO.IOstatus == 0)
                                    {
                                        writeLog($"阻值3触发=1(M{DataModel.Settingmodel.Res3TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes + 4}");
                                        StartRuntimeWorker(Res3Process, "阻值3处理");
                                    }
                                }
                                catch {; }
                            }
                            #endregion

                            #region 数据复制刷新
                            if (ModeTriggersPaused)
                            {
                                // 模式未知或双Y部署拒绝标准模式时，模式相关沿判定回退为 -1，恢复后重新武装并识别 0→1。
                                DisarmDualYScanAndFlowEndEdges();
                                DataModel.Processmodel.Scan_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.SecondScan_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.LinkedScan_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.DualYStation2Scan_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.DualYStation1FlowEnd_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.DualYStation2FlowEnd_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.IR_Trig_IO.IOstatus = -1;
                                DataModel.Processmodel.TV3_Trig_IO.IOstatus = -1;
                            }
                            else
                            {
                                DataModel.Processmodel.Scan_Trig_IO.IOstatus = DualYModeActive && !dualYStation1ScanResult.IsSuccess
                                    ? -1
                                    : ScanTrig;
                                DataModel.Processmodel.SecondScan_Trig_IO.IOstatus = SecondScanTrig;
                                DataModel.Processmodel.LinkedScan_Trig_IO.IOstatus = linkedScanResult.IsSuccess ? LinkedScanTrig : -1;
                                DataModel.Processmodel.DualYStation2Scan_Trig_IO.IOstatus = dualYStation2ScanResult.IsSuccess ? DualYStation2ScanTrig : -1;
                                DataModel.Processmodel.DualYStation1FlowEnd_Trig_IO.IOstatus = dualYStation1FlowEndResult.IsSuccess ? DualYStation1FlowEndTrig : -1;
                                DataModel.Processmodel.DualYStation2FlowEnd_Trig_IO.IOstatus = dualYStation2FlowEndResult.IsSuccess ? DualYStation2FlowEndTrig : -1;
                                DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = TakePhoto1Trig;
                                DataModel.Processmodel.IR_Trig_IO.IOstatus = IRTrig;
                                DataModel.Processmodel.TV3_Trig_IO.IOstatus = TV3Trig;
                            }

                            DataModel.Processmodel.TV1_Trig_IO.IOstatus = TV1Trig;
                            DataModel.Processmodel.TV2_Trig_IO.IOstatus = TV2Trig;
                            DataModel.Processmodel.Res1_Trig_IO.IOstatus = Res1Trig;
                            DataModel.Processmodel.Res2_Trig_IO.IOstatus = Res2Trig;
                            DataModel.Processmodel.Res3_Trig_IO.IOstatus = Res3Trig;
                            #endregion
                        }
                        #endregion
                        modbusTcp.ConnectClose();

                    }
                    else
                    {
                        #region 通讯失败数据置为-1
                        DisarmDualYScanAndFlowEndEdges();
                        DataModel.Processmodel.Scan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.SecondScan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.LinkedScan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.DualYStation2Scan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.DualYStation1FlowEnd_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.DualYStation2FlowEnd_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.IR_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV3_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res3_Trig_IO.IOstatus = -1;
                        _lastTv1TriggerTrace = null;
                        _lastTv1ResultTrace = null;
                        _lastTv2TriggerTrace = null;
                        _lastTv2ResultTrace = null;
                        _lastTv3TriggerTrace = null;
                        _lastTv3ResultTrace = null;
                        
                        // 只在状态从连接成功变为失败时记录一次日志，避免重复刷屏
                        if (_lastPLCConnectedStatus)
                        {
                            writeLog("PLC通讯失败，所有触发信号置为-1", true);
                        }
                        // 更新连接状态为未连接
                        _lastPLCConnectedStatus = false;
                        #endregion
                    }
                    //modbusTcp?.ConnectClose();

                }
                catch {; }
                finally
                {
                    try { modbusTcp.ConnectClose(); } catch { }
                }

                if (_runtimeStopSignal.Wait(100))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 记录标准耐压工位的 PLC 握手变化。
        /// 快照同时保留触发寄存器、结果寄存器和上位机上一轮沿状态，可区分 PLC 未发起触发、上位机未识别启动沿、
        /// 以及结果写入后 PLC 已清零等现场时序；该诊断只记录状态，不改变耐压启动条件和 PLC 数据。
        /// </summary>
        /// <param name="stationIndex">耐压工位序号；1、2、3分别对应 D1006/D1007、D1008/D1009、D1010/D1011。</param>
        /// <param name="triggerValue">本轮批量读取取得的 PLC 耐压触发值。</param>
        /// <param name="resultValue">本轮批量读取取得的 PLC 耐压完成结果值。</param>
        /// <param name="edgeStatus">进入本轮判断前，上位机缓存的触发状态。</param>
        /// <param name="standardFlowActive">当前是否允许执行标准产线耐压入口。</param>
        /// <param name="lastTriggerValue">该工位最近一次已记录的触发值。</param>
        /// <param name="lastResultValue">该工位最近一次已记录的结果值。</param>
        private void TraceTvHandshakeSnapshot(
            int stationIndex,
            int triggerValue,
            int resultValue,
            int edgeStatus,
            bool standardFlowActive,
            ref int? lastTriggerValue,
            ref int? lastResultValue)
        {
            if (lastTriggerValue == triggerValue && lastResultValue == resultValue)
            {
                return;
            }

            int triggerAddress = DataModel.Settingmodel.AddressStart + 4 + stationIndex * 2;
            int resultAddress = triggerAddress + 1;
            bool startEdgeAccepted = standardFlowActive
                && (triggerValue == 1 || triggerValue == 2)
                && edgeStatus == 0;
            string previousTrigger = lastTriggerValue.HasValue ? lastTriggerValue.Value.ToString() : "首次";
            string previousResult = lastResultValue.HasValue ? lastResultValue.Value.ToString() : "首次";

            writeLog(
                $"[耐压握手-PLC快照] 工位={stationIndex}, D{triggerAddress}触发={previousTrigger}->{triggerValue}, "
                + $"D{resultAddress}结果={previousResult}->{resultValue}, 上位机沿状态={edgeStatus}, "
                + $"标准流程={standardFlowActive}, 启动沿判定={(startEdgeAccepted ? "成立" : "保持")}"
            );

            lastTriggerValue = triggerValue;
            lastResultValue = resultValue;
        }

        /// <summary>
        /// 在 PLC 发出阻值读取信号时记录判定输入快照。
        /// 原始 Word、CDAB 浮点解码、D2020/D2022 阈值和同一时刻压力寄存器用于对照 PLC 梯形图，
        /// 可定位工位地址、数据类型、字序或压力联锁差异；快照读取失败只写诊断日志，既有阻值落库流程继续执行。
        /// </summary>
        /// <param name="stationIndex">阻值所属耐压工位序号。</param>
        /// <param name="triggerAddress">PLC 发出的阻值读取 M 地址。</param>
        /// <param name="resistanceAddress">阻值 Float 起始 D 地址，占两个连续 Word。</param>
        /// <param name="decodedResistance">既有业务读取方法得到的阻值，单位沿用现场阻值标定。</param>
        /// <param name="sn">当前阻值工位读取到的产品 SN；产品码异常时为空。</param>
        private void TraceResistanceJudgementInputs(
            int stationIndex,
            int triggerAddress,
            int resistanceAddress,
            float decodedResistance,
            string sn)
        {
            ModbusTcpNet modbusTcp = null;

            try
            {
                modbusTcp = new ModbusTcpNet
                {
                    ConnectTimeOut = 1000,
                    ReceiveTimeOut = 1000,
                    IpAddress = DataModel.Settingmodel.PLC_IP,
                    Port = DataModel.Settingmodel.PLC_Port,
                    DataFormat = HslCommunication.Core.DataFormat.CDAB
                };
                var connectResult = modbusTcp.ConnectServer();
                if (!connectResult.IsSuccess)
                {
                    writeLog(
                        $"[阻值判定输入] 工位={stationIndex}, M{triggerAddress}=1, SN={sn}, PLC连接失败: {connectResult.Message}",
                        true);
                    return;
                }

                var resistanceWords = modbusTcp.ReadUInt16(resistanceAddress.ToString(), 2);
                var maxWords = modbusTcp.ReadUInt16(DataModel.Settingmodel.Res_Max_Address.ToString(), 2);
                var minWords = modbusTcp.ReadUInt16(DataModel.Settingmodel.Res_Min_Address.ToString(), 2);
                var maxFloat = modbusTcp.ReadFloat(DataModel.Settingmodel.Res_Max_Address.ToString(), 1);
                var minFloat = modbusTcp.ReadFloat(DataModel.Settingmodel.Res_Min_Address.ToString(), 1);
                var pressureWords = modbusTcp.ReadUInt16(DataModel.Settingmodel.AddressPressure.ToString(), 5);

                string resistanceRaw = resistanceWords.IsSuccess && resistanceWords.Content != null && resistanceWords.Content.Length >= 2
                    ? $"0x{resistanceWords.Content[0]:X4},0x{resistanceWords.Content[1]:X4}"
                    : $"读取失败:{resistanceWords.Message}";
                string maxRaw = maxWords.IsSuccess && maxWords.Content != null && maxWords.Content.Length >= 2
                    ? $"0x{maxWords.Content[0]:X4},0x{maxWords.Content[1]:X4}"
                    : $"读取失败:{maxWords.Message}";
                string minRaw = minWords.IsSuccess && minWords.Content != null && minWords.Content.Length >= 2
                    ? $"0x{minWords.Content[0]:X4},0x{minWords.Content[1]:X4}"
                    : $"读取失败:{minWords.Message}";
                string maxDecoded = maxFloat.IsSuccess && maxFloat.Content != null && maxFloat.Content.Length > 0
                    ? maxFloat.Content[0].ToString()
                    : $"读取失败:{maxFloat.Message}";
                string minDecoded = minFloat.IsSuccess && minFloat.Content != null && minFloat.Content.Length > 0
                    ? minFloat.Content[0].ToString()
                    : $"读取失败:{minFloat.Message}";
                string pressureSnapshot = pressureWords.IsSuccess && pressureWords.Content != null && pressureWords.Content.Length >= 5
                    ? $"Avg={pressureWords.Content[0]},Max={pressureWords.Content[2]},Min={pressureWords.Content[4]}"
                    : $"读取失败:{pressureWords.Message}";

                writeLog(
                    $"[阻值判定输入] 工位={stationIndex}, M{triggerAddress}=1, SN={sn}, "
                    + $"D{resistanceAddress}-D{resistanceAddress + 1}原始=[{resistanceRaw}], 上位机解码={decodedResistance}, "
                    + $"D{DataModel.Settingmodel.Res_Max_Address}-D{DataModel.Settingmodel.Res_Max_Address + 1}原始=[{maxRaw}], "
                    + $"Float={maxDecoded}, 当前上位机上限={DataModel.Processmodel.ResParameter.Max_Res}, "
                    + $"D{DataModel.Settingmodel.Res_Min_Address}-D{DataModel.Settingmodel.Res_Min_Address + 1}原始=[{minRaw}], "
                    + $"Float={minDecoded}, 当前上位机下限={DataModel.Processmodel.ResParameter.Min_Res}, "
                    + $"压力D{DataModel.Settingmodel.AddressPressure}/D{DataModel.Settingmodel.AddressPressure + 2}/D{DataModel.Settingmodel.AddressPressure + 4}=[{pressureSnapshot}]"
                );
            }
            catch (Exception ex)
            {
                writeLog(
                    $"[阻值判定输入] 工位={stationIndex}, M{triggerAddress}=1, SN={sn}, 快照读取异常: {ex.Message}",
                    true);
            }
            finally
            {
                if (modbusTcp != null)
                {
                    try { modbusTcp.ConnectClose(); } catch { }
                }
            }
        }


        /// <summary>
        /// 启动 PLC 电测设备可用状态刷新线程。该线程只更新设备入口状态，退出时与生产信号轮询共用停止信号。
        /// </summary>
        public void PLC_shankhand()
        {
            lock (_runtimeWorkerSync)
            {
                if (_plcHandshakeThread != null && _plcHandshakeThread.IsAlive)
                {
                    return;
                }

                _runtimeStopRequested = false;
                _runtimeStopSignal.Reset();
                _plcHandshakeThread = new Thread(() =>
                {
                    while (!_runtimeStopRequested)
                    {
                        if (_runtimeStopSignal.Wait(1500))
                        {
                            break;
                        }

                        try
                        {
                            PLC_ReadTVAvailable();
                        }
                        catch
                        {
                            if (_runtimeStopRequested)
                            {
                                break;
                            }
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "PLC电测可用状态刷新"
                };
                _plcHandshakeThread.Start();
            }
        }

        /// <summary>
        /// 停止 PLC 常驻轮询并等待线程退出。该入口先关闭新的扫码、电测和归档触发，再由窗口释放仪器与网络连接。
        /// </summary>
        /// <param name="joinTimeoutMs">等待每条常驻线程退出的最长时间，单位 ms。</param>
        public void StopRuntimeWorkers(int joinTimeoutMs = 3000)
        {
            _runtimeStopRequested = true;
            _runtimeStopSignal.Set();

            JoinRuntimeWorker(_plcProcessThread, joinTimeoutMs);
            JoinRuntimeWorker(_plcHandshakeThread, joinTimeoutMs);
        }

        /// <summary>
        /// 等待扫码、电测、归档等一次性业务线程退出。
        /// 关闭流程在置位仪器 stop 后调用，使 FUNCtion:STOP / STAT:DISC 仍可在连接存活时发出，再断开通信。
        /// </summary>
        /// <param name="joinTimeoutMs">等待每条业务线程退出的最长时间，单位 ms。</param>
        public void JoinRuntimeBusinessWorkers(int joinTimeoutMs = 5000)
        {
            Thread[] workers;
            lock (_runtimeWorkerSync)
            {
                workers = _runtimeBusinessWorkers.ToArray();
            }

            foreach (Thread worker in workers)
            {
                JoinRuntimeWorker(worker, joinTimeoutMs);
            }
        }

        private static void JoinRuntimeWorker(Thread worker, int joinTimeoutMs)
        {
            if (worker == null || !worker.IsAlive || worker == Thread.CurrentThread)
            {
                return;
            }

            try
            {
                worker.Join(Math.Max(0, joinTimeoutMs));
            }
            catch
            {
            }
        }

        /// <summary>
        /// 启动由 PLC 沿触发的一次性业务线程。后台线程不会阻止应用退出；停止信号置位后拒绝创建新的扫码、电测或归档任务。
        /// </summary>
        /// <param name="action">本次 PLC 触发对应的业务入口。</param>
        /// <param name="name">线程诊断名称，用于现场进程转储识别业务阶段。</param>
        private void StartRuntimeWorker(ThreadStart action, string name)
        {
            if (_runtimeStopRequested || action == null)
            {
                return;
            }

            var worker = new Thread(() =>
            {
                try
                {
                    action();
                }
                finally
                {
                    lock (_runtimeWorkerSync)
                    {
                        _runtimeBusinessWorkers.Remove(Thread.CurrentThread);
                    }
                }
            })
            {
                IsBackground = true,
                Name = name
            };

            lock (_runtimeWorkerSync)
            {
                if (_runtimeStopRequested)
                {
                    return;
                }

                _runtimeBusinessWorkers.Add(worker);
            }

            worker.Start();
        }



        

        /// <summary>
        /// 向标准扫码结果寄存器写入 Float，并记录调用入口、线程和实际尝试次数。
        /// 该日志用于确认上位机完成反馈的真实写入来源，不改变既有地址及重试策略。
        /// </summary>
        /// <param name="result">写入 PLC 的 Float 值。</param>
        /// <param name="caller">发起写入的业务方法，由编译器自动填充。</param>
        /// <returns>true 表示 PLC 在既有重试范围内确认写入。</returns>
        private bool PLC_write(float result, [CallerMemberName] string caller = "")
        {
            int maxRetry = 6;
            string address = (DataModel.Settingmodel.AddressStart + 1).ToString();
            string lastError = string.Empty;
            for (int i = 0; i < maxRetry; i++)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.Write(address, result);
                        if (r.IsSuccess)
                        {
                            TracePlcWriteResult(address, result.ToString(), result == 1, caller, i + 1, maxRetry, true, string.Empty);
                            return true;
                        }

                        lastError = r.Message;
                    }
                    else
                    {
                        lastError = connectresult.Message;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                finally
                {
                    try { modbusTcp.ConnectClose(); } catch { }
                }
                Thread.Sleep(100);
            }

            TracePlcWriteResult(address, result.ToString(), result == 1, caller, maxRetry, maxRetry, false, lastError);
            return false;
        }

        /// <summary>
        /// 向 PLC D 寄存器写入 Word，并记录完成反馈的业务来源和重试结果。
        /// D1007、D1009、D1011类耐压完成地址会附加工位、触发沿缓存和当前 SN，便于排查提前完成反馈。
        /// </summary>
        /// <param name="address">目标 PLC D 地址。</param>
        /// <param name="result">写入 PLC 的 Word 值。</param>
        /// <param name="caller">发起写入的业务方法，由编译器自动填充。</param>
        /// <returns>true 表示 PLC 在既有重试范围内确认写入。</returns>
        private bool PLC_write(string address, UInt16 result, [CallerMemberName] string caller = "")
        {
            int maxRetry = 6;
            string lastError = string.Empty;
            for (int i = 0; i < maxRetry; i++)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.Write((address).ToString(), (UInt16)result);
                        if (r.IsSuccess)
                        {
                            TracePlcWriteResult(address, result.ToString(), result == 1, caller, i + 1, maxRetry, true, string.Empty);
                            return true;
                        }

                        lastError = r.Message;
                    }
                    else
                    {
                        lastError = connectresult.Message;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                finally
                {
                    try { modbusTcp.ConnectClose(); } catch { }
                }
                Thread.Sleep(100);
            }

            TracePlcWriteResult(address, result.ToString(), result == 1, caller, maxRetry, maxRetry, false, lastError);
            return false;
        }

        /// <summary>
        /// 向 PLC D 寄存器写入 Float 参数，并记录参数下发的目标地址、调用入口和重试结果。
        /// </summary>
        /// <param name="address">目标 PLC D 地址，占用空间由 Modbus Float 协议决定。</param>
        /// <param name="result">写入 PLC 的 Float 参数值。</param>
        /// <param name="caller">发起写入的业务方法，由编译器自动填充。</param>
        /// <returns>true 表示 PLC 在既有重试范围内确认写入。</returns>
        private bool PLC_write(string address, float result, [CallerMemberName] string caller = "")
        {
            int maxRetry = 6;
            string lastError = string.Empty;
            for (int i = 0; i < maxRetry; i++)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.Write((address).ToString(), result);
                        if (r.IsSuccess)
                        {
                            TracePlcWriteResult(address, result.ToString(), result == 1, caller, i + 1, maxRetry, true, string.Empty);
                            return true;
                        }

                        lastError = r.Message;
                    }
                    else
                    {
                        lastError = connectresult.Message;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                finally
                {
                    try { modbusTcp.ConnectClose(); } catch { }
                }
                Thread.Sleep(100);
            }

            TracePlcWriteResult(address, result.ToString(), result == 1, caller, maxRetry, maxRetry, false, lastError);
            return false;
        }

        /// <summary>
        /// 汇总一次 PLC 写入的最终结果。耐压完成寄存器会补充工位运行上下文，其他参数写入沿用同一日志格式。
        /// </summary>
        /// <param name="address">本次写入的 PLC D 地址。</param>
        /// <param name="value">用于日志显示的写入值。</param>
        /// <param name="isOkValue">写入值是否等于协议中的 OK 值 1。</param>
        /// <param name="caller">发起写入的业务方法。</param>
        /// <param name="attemptCount">本次调用实际执行的连接及写入尝试次数。</param>
        /// <param name="maxRetry">既有写入策略允许的最大尝试次数。</param>
        /// <param name="success">PLC 是否确认本次写入。</param>
        /// <param name="lastError">最终一次失败返回的通信错误；成功时为空。</param>
        private void TracePlcWriteResult(
            string address,
            string value,
            bool isOkValue,
            string caller,
            int attemptCount,
            int maxRetry,
            bool success,
            string lastError)
        {
            try
            {
                string completionContext = BuildTvCompletionWriteContext(address);
                string errorContext = string.IsNullOrWhiteSpace(lastError) ? string.Empty : $", 错误={lastError}";
                writeLog(
                    $"视觉->PLC:{value}、{(isOkValue ? "OK" : "NG")}, 地址=D{address}, 调用={caller}, "
                    + $"线程={Thread.CurrentThread.ManagedThreadId}, 尝试={attemptCount}/{maxRetry}, "
                    + $"写入={(success ? "成功" : "失败")}{completionContext}{errorContext}",
                    !success);
            }
            catch
            {
                // 诊断失败保持 PLC 写入返回值不变，避免日志异常触发完成信号重复回写。
            }
        }

        /// <summary>
        /// 识别标准耐压完成寄存器所属工位，地址按 AddressStart+7/+9/+11 协议解析。
        /// </summary>
        /// <param name="address">待识别的 PLC D 地址。</param>
        /// <param name="stationIndex">识别成功时返回耐压工位 1、2 或 3。</param>
        /// <returns>true 表示该地址属于标准耐压完成反馈寄存器。</returns>
        private bool TryGetTvCompletionStation(string address, out int stationIndex)
        {
            stationIndex = 0;
            int numericAddress;
            if (!int.TryParse(address, out numericAddress))
            {
                return false;
            }

            int addressOffset = numericAddress - DataModel.Settingmodel.AddressStart;
            if (addressOffset != 7 && addressOffset != 9 && addressOffset != 11)
            {
                return false;
            }

            stationIndex = (addressOffset - 5) / 2;
            return true;
        }

        /// <summary>
        /// 生成耐压完成写入时的工位上下文。触发沿缓存与当前 SN 用于确认结果是否由本工位有效启动流程产生。
        /// </summary>
        /// <param name="address">本次写入的 PLC D 地址。</param>
        /// <returns>耐压完成地址返回工位诊断文本；其他地址返回空文本。</returns>
        private string BuildTvCompletionWriteContext(string address)
        {
            int stationIndex;
            if (!TryGetTvCompletionStation(address, out stationIndex))
            {
                return string.Empty;
            }

            int triggerEdgeState;
            string sn;
            if (stationIndex == 1)
            {
                triggerEdgeState = DataModel.Processmodel.TV1_Trig_IO.IOstatus;
                sn = DataModel.Processmodel.TVTestTestModel1.Productinfo?.SN;
            }
            else if (stationIndex == 2)
            {
                triggerEdgeState = DataModel.Processmodel.TV2_Trig_IO.IOstatus;
                sn = DataModel.Processmodel.TVTestTestModel2.Productinfo?.SN;
            }
            else
            {
                triggerEdgeState = DataModel.Processmodel.TV3_Trig_IO.IOstatus;
                sn = DataModel.Processmodel.TVTestTestModel3.Productinfo?.SN;
            }

            return $", 耐压工位={stationIndex}, 触发沿缓存={triggerEdgeState}, 当前SN={(string.IsNullOrWhiteSpace(sn) ? "空" : sn)}";
        }

        /// <summary>
        /// 向 PLC 写入 M 线圈信号。
        /// 用于业务完成、联锁和互锁类 Bool 反馈，区别于 D 寄存器结果写入，调用方需按现场协议决定置 1 或清 0 的时机。
        /// </summary>
        /// <param name="address">PLC M 线圈地址，如 3047。</param>
        /// <param name="value">写入线圈的目标状态；true 表示置位，false 表示复位。</param>
        /// <param name="context">业务上下文名称，用于日志定位设备交互位置。</param>
        /// <returns>true 表示 PLC 已确认写入；false 表示连接、写入或程序异常。</returns>
        private bool PLC_WriteCoil(int address, bool value, string context)
        {
            int maxRetry = 3;
            string lastError = string.Empty;
            for (int i = 0; i < maxRetry; i++)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1000;
                    modbusTcp.ReceiveTimeOut = 1000;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;

                    var connectresult = modbusTcp.ConnectServer();
                    if (connectresult.IsSuccess)
                    {
                        var writeResult = modbusTcp.WriteCoil(address.ToString(), value);
                        modbusTcp.ConnectClose();
                        if (writeResult.IsSuccess)
                        {
                            return true;
                        }

                        lastError = writeResult.Message ?? "WriteCoil返回失败";
                    }
                    else
                    {
                        lastError = connectresult.Message ?? "PLC连接失败";
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }

                Thread.Sleep(40);
            }

            writePlcError($"[PLC通讯]{context}-写入M{address}={(value ? 1 : 0)}失败 | 重试={maxRetry}, 错误={lastError}");
            return false;
        }

        /// <summary>
        /// 向PLC写入“耐压结果→是否允许IR”的线圈信号。
        /// 业务说明：每次工位耐压流程结束都主动刷新一次线圈，不用上次结果做跳过判断；
        /// 这样即使PLC侧清线圈、漏读或重试同一工位，也能收到本次明确的OK/NG状态。
        /// M3041: TV1耐压结果（0=NG,1=OK）
        /// M3042: TV2耐压结果（0=NG,1=OK）
        /// </summary>
        private void WriteTvOkSignalForIrEnable(int stationIndex, bool isOk)
        {
            try
            {
                int addr = 0;
                if (stationIndex == 1)
                {
                    addr = DataModel.Settingmodel.IrEnableByTv1ResultAddress;
                }
                else if (stationIndex == 2)
                {
                    addr = DataModel.Settingmodel.IrEnableByTv2ResultAddress;
                }
                else
                {
                    return;
                }

                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                modbusTcp.ConnectTimeOut = 1;
                modbusTcp.ReceiveTimeOut = 1;
                modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;

                var connectresult = modbusTcp.ConnectServer();
                if (!connectresult.IsSuccess)
                {
                    writeLog($"[IR启用判定] PLC连接失败，无法写入M{addr}={ (isOk ? 1 : 0) }（工位{stationIndex}耐压结果）: {connectresult.Message}", true);
                    return;
                }

                var writeResult = modbusTcp.WriteCoil(addr.ToString(), isOk);
                modbusTcp.ConnectClose();
                if (writeResult.IsSuccess)
                {
                    writeLog($"[IR启用判定] 已写入M{addr}={(isOk ? 1 : 0)}（工位{stationIndex}耐压结果：{(isOk ? "OK" : "NG")}）");
                }
                else
                {
                    writeLog($"[IR启用判定] 写入M{addr}失败（工位{stationIndex}耐压结果：{(isOk ? "OK" : "NG")}）: {writeResult.Message}", true);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[IR启用判定] 写入PLC异常（工位{stationIndex}）: {ex.Message}", true);
            }
        }
        public UInt16 PLC_ReadUint16(int address)
        {
            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            int maxRetry = 6;

            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    modbusTcp.ConnectTimeOut = 1000; // 优化超时设置
                    modbusTcp.ReceiveTimeOut = 1000;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;

                    var connectresult = modbusTcp.ConnectServer();
                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.ReadUInt16(address.ToString(), 1);
                        modbusTcp.ConnectClose();

                        if (r.IsSuccess)
                        {
                            return r.Content[0];
                        }
                        else
                        {
                            // 若最后一次也失败，记录日志
                            if (i == maxRetry - 1)
                                writeLog($"[PLC通讯] 读取UInt16失败(D{address}): {r.Message}", false);
                        }
                    }
                    else
                    {
                        if (i == maxRetry - 1)
                            writeLog($"[PLC通讯] 连接失败(D{address}): {connectresult.Message}", false);
                    }
                }
                catch (Exception ex)
                {
                    if (i == maxRetry - 1)
                        writeLog($"[PLC通讯] 读取UInt16异常(D{address}): {ex.Message}", false);
                }

                // 简单延时重试
                Thread.Sleep(50);
            }

            return 0;
        }

        /// <summary>
        /// 读取占用两个连续 D 寄存器的 PLC uint32 数据。
        /// 双Y压力平均值、最大值和最小值使用该宽度；字序沿用设备现有 CDAB 配置，与浮点读取保持一致。
        /// </summary>
        /// <param name="address">uint32 高低字起始 D 寄存器编号，例如 Y2 平均压力使用 1630。</param>
        /// <returns>读取成功时返回完整 32 位无符号值；重试完成后仍失败时返回 0，并写入 PLC 通讯日志。</returns>
        public UInt32 PLC_ReadUint32(int address)
        {
            int maxRetry = 6;
            for (int i = 0; i < maxRetry; i++)
            {
                var modbusTcp = new ModbusTcpNet
                {
                    ConnectTimeOut = 1000,
                    ReceiveTimeOut = 1000,
                    IpAddress = DataModel.Settingmodel.PLC_IP,
                    Port = DataModel.Settingmodel.PLC_Port,
                    DataFormat = HslCommunication.Core.DataFormat.CDAB
                };

                try
                {
                    var connectResult = modbusTcp.ConnectServer();
                    if (connectResult.IsSuccess)
                    {
                        var readResult = modbusTcp.ReadUInt32(address.ToString(), 1);
                        if (readResult.IsSuccess)
                        {
                            return readResult.Content[0];
                        }

                        if (i == maxRetry - 1)
                        {
                            writeLog($"[PLC通讯] 读取UInt32失败(D{address}-D{address + 1}): {readResult.Message}", false);
                        }
                    }
                    else if (i == maxRetry - 1)
                    {
                        writeLog($"[PLC通讯] 连接失败(D{address}-D{address + 1}): {connectResult.Message}", false);
                    }
                }
                catch (Exception ex)
                {
                    if (i == maxRetry - 1)
                    {
                        writeLog($"[PLC通讯] 读取UInt32异常(D{address}-D{address + 1}): {ex.Message}", false);
                    }
                }
                finally
                {
                    try { modbusTcp.ConnectClose(); } catch { }
                }

                Thread.Sleep(50);
            }

            return 0;
        }

        public float PLC_ReadFloat(int address)
        {
            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            int maxRetry = 6;

            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    modbusTcp.ConnectTimeOut = 1000; // 优化超时
                    modbusTcp.ReceiveTimeOut = 1000;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;

                    var connectresult = modbusTcp.ConnectServer();
                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.ReadFloat(address.ToString(), 1);
                        modbusTcp.ConnectClose();

                        if (r.IsSuccess)
                        {
                            return r.Content[0];
                        }
                        else
                        {
                            if (i == maxRetry - 1)
                                writeLog($"[PLC通讯] 读取Float失败(D{address}): {r.Message}", false);
                        }
                    }
                    else
                    {
                        if (i == maxRetry - 1)
                            writeLog($"[PLC通讯] 连接失败(D{address}): {connectresult.Message}", false);
                    }
                }
                catch (Exception ex)
                {
                    if (i == maxRetry - 1)
                        writeLog($"[PLC通讯] 读取Float异常(D{address}): {ex.Message}", false);
                }
                Thread.Sleep(50);
            }

            return float.NaN;
        }
        /// <summary>
        /// 从PLC读取字符串数据
        /// </summary>
        /// <param name="address">PLC寄存器地址（整数类型，例如：D100表示地址100）</param>
        /// <returns>成功时返回读取到的字符串（已去除空字符），失败时返回空字符串</returns>
        /// <remarks>
        /// 1. 支持自动重试机制，最多重试6次，提高通信可靠性
        /// 2. 每次读取25个字符长度的字符串数据
        /// 3. 自动去除字符串中的空字符（\0）
        /// 4. 连接失败或读取失败时会记录日志（仅在最后一次重试时记录，避免日志过多）
        /// 5. 使用CDAB数据格式进行通信
        /// </remarks>
        public string PLC_Readstring(int address)
        {
            // 创建Modbus TCP通信对象
            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            // 设置最大重试次数为6次
            int maxRetry = 6;

            // 循环重试机制：如果第一次读取失败，会自动重试，最多重试6次
            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    modbusTcp.ConnectTimeOut = 1000;
                    modbusTcp.ReceiveTimeOut = 1000;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    // 从配置模型中获取PLC的端口号，通常是502（Modbus TCP标准端口）
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    // 设置数据格式为CDAB（字节序），确保数据解析正确
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;

                    // 尝试连接到PLC服务器
                    var connectresult = modbusTcp.ConnectServer();
                    // 判断连接是否成功
                    if (connectresult.IsSuccess)
                    {
                        // 连接成功，开始读取字符串数据
                        // address.ToString()：将整数地址转换为字符串格式（如：100 -> "100"）
                        // 25：读取25个字符长度的字符串（PLC中字符串通常占用多个寄存器，这里读取25个字符）
                        var r = modbusTcp.ReadString(address.ToString(), 25);
                        // 读取完成后立即关闭连接，释放网络资源
                        modbusTcp.ConnectClose();
                        // 判断读取操作是否成功
                        if (r.IsSuccess)
                        {
                            // 读取成功，返回读取到的字符串内容
                            // Replace("\0", "")：去除字符串中的空字符（\0），因为PLC返回的字符串可能包含填充的空字符
                            return r.Content.Replace("\0", "");
                        }
                        else
                        {
                            // 读取失败，如果这是最后一次重试（i == maxRetry - 1），则记录错误日志
                            // 只在最后一次重试时记录日志，避免重复记录相同的错误信息
                            if (i == maxRetry - 1)
                                writeLog($"[PLC通讯] 读取String失败(D{address}): {r.Message}", false);
                        }
                    }
                    else
                    {
                        // 连接失败，如果这是最后一次重试，则记录连接失败的日志
                        if (i == maxRetry - 1)
                            writeLog($"[PLC通讯] 连接失败(D{address}): {connectresult.Message}", false);
                    }
                }
                catch (Exception ex)
                {
                    // 捕获异常（如网络异常、超时异常等），如果这是最后一次重试，则记录异常日志
                    if (i == maxRetry - 1)
                        writeLog($"[PLC通讯] 读取String异常(D{address}): {ex.Message}", false);
                }
                // 每次重试前等待50毫秒，避免频繁重试对PLC造成压力，也给网络一些恢复时间
                Thread.Sleep(50);
            }

            // 如果所有重试都失败了，返回空字符串，表示读取失败
            return string.Empty;
        }

        /// <summary>
        /// 读取PLC产品编码并做业务级重试。
        /// 业务说明：PLC_Readstring 只保证通讯层重试；这里额外等待 PLC 把完整的 "SN;WOCODE" 写入完成。
        /// 失败时不返回旧SN/旧工单，调用方必须按本次流程失败处理，避免电测或CHECK结果串到上一件产品。
        /// </summary>
        private bool TryReadProductCodeFromPlc(int address, string context, out string sn, out string wocode, out string raw,
            int maxAttempts = 3, int productCodeRetryDelayMs = 300)
        {
            sn = string.Empty;
            wocode = string.Empty;
            raw = string.Empty;
            string lastNonEmpty = string.Empty;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string current = (PLC_Readstring(address) ?? string.Empty).Trim();
                raw = current;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    lastNonEmpty = current;
                }

                string[] parts = current.Split(';');
                if (parts.Length == 2)
                {
                    string candidateSn = (parts[0] ?? string.Empty).Trim();
                    string candidateWo = (parts[1] ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(candidateSn) && !string.IsNullOrWhiteSpace(candidateWo))
                    {
                        sn = candidateSn;
                        wocode = candidateWo;
                        if (attempt > 1)
                        {
                            writeLog($"[{context}] 产品编码第{attempt}次读取成功: SN={sn}, WOCODE={wocode}, PLC地址=D{address}");
                        }
                        return true;
                    }
                }

                if (attempt < maxAttempts)
                {
                    Thread.Sleep(productCodeRetryDelayMs);
                }
            }

            string shownRaw = string.IsNullOrWhiteSpace(lastNonEmpty) ? raw : lastNonEmpty;
            string failureType = string.IsNullOrWhiteSpace(shownRaw) ? "通讯/未写入" : "值/格式";
            int segmentCount = (shownRaw ?? string.Empty).Split(';').Length;
            writeLog($"[{context}] ❌ 产品编码读取错误({failureType})! 原始值=[{shownRaw}], 期望格式=[SN;WOCODE], 分段数={segmentCount}, 重试次数={maxAttempts}, PLC地址=D{address}", true);
            writePlcError($"[PLC数据异常]{context}-产品编码读取失败 | 失败类型={failureType}, 原始值=[{shownRaw}], 分段数={segmentCount}, 重试次数={maxAttempts}, PLC地址=D{address}");
            return false;
        }

        public bool PLC_Writestring(string address, string data)
        {
            int maxRetry = 3;
            string lastError = string.Empty;
            for (int i = 0; i < maxRetry; i++)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.WriteUnicodeString(address.ToString(), data);
                        modbusTcp.ConnectClose();
                        if (r.IsSuccess)
                        {
                            return true;
                        }
                        lastError = r.Message ?? "WriteUnicodeString返回失败";
                    }
                    else
                    {
                        lastError = connectresult.Message ?? "PLC连接失败";
                    }
                }
                catch (Exception ex)
                {
                    // 重试阶段不刷屏，保留最后一次错误信息即可
                    lastError = ex.Message;
                }
                Thread.Sleep(40);
            }

            // 只在最终失败时记录一次日志，用于定位“写入失败 -> 后续读到空/格式错误”的链路问题
            writeLog($"[PLC通讯] 写入String失败({maxRetry}次重试后仍失败): 地址={address}, 长度={(data?.Length ?? 0)}, 错误={lastError}", true);
            return false;

        }

        /// <summary>
        /// 从PLC读取耐压仪器（TV）可用状态，并同步更新到 <see cref="DataModel.Processmodel"/>。
        /// 双Y专用部署机台无第三台耐压仪，读完 M3032 后强制 TV3Available=false，避免参数下发与触发误走工位3。
        /// </summary>
        /// <returns>连接成功且三台耐压仪线圈读取成功时返回 true，否则返回 false。</returns>
        /// <remarks>
        /// 1. 由 PLC_shankhand() 后台线程周期性调用（约每1500ms一次），用于UI/流程层判断工位是否可用。
        /// 2. 读取线圈：Meter1AvailableAddress、Meter2AvailableAddress、Meter3AvailableAddress，并将读取到的bit取反后写入 TVAvailable（现场信号为“不可用=1”）。
        ///    IR(M3033) 逻辑相反：现场确认 1 表示“开启/可用”，不取反。
        /// 3. 同步写入：ShankHandAddress=1（握手）、DeviceAvailableAddress=allow_start（设备可运行标志）。
        /// 4. TV3(M3032)与IR(M3033)共用第三电测位置；同时可用时记录诊断日志，流程仍按D1010/D1014实际触发执行。
        /// 5. 双Y专用部署（DeploymentEnabled）强制忽略工位3；标准产线仍完全按 M3032 镜像。
        /// 6. 失败时最多重试3次；仅在最后一次失败时记录异常日志，避免日志刷屏。
        /// </remarks>
        public bool PLC_ReadTVAvailable()
        {
            int maxRetry = 3;
            for (int i = 0; i < maxRetry; i++)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r1 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter1AvailableAddress.ToString(), 1);
                        var r2 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter2AvailableAddress.ToString(), 1);
                        var r3 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter3AvailableAddress.ToString(), 1);
                        // IR绝缘电阻仪可用状态（M3033）
                        var rIR = modbusTcp.ReadCoil(DataModel.Settingmodel.IRMeterAvailableAddress.ToString(), 1);
                        modbusTcp.Write(DataModel.Settingmodel.ShankHandAddress.ToString(), (UInt16)1);
                        modbusTcp.Write(DataModel.Settingmodel.DeviceAvailableAddress.ToString(), DataModel.Processmodel.allow_start);
                        modbusTcp.ConnectClose();
                        if (r1.IsSuccess)
                        {
                            DataModel.Processmodel.TVAvailable.TV1Available = !r1.Content[0];
                        }
                        else
                        {
                            DataModel.Processmodel.TVAvailable.TV1Available = false;
                            if (i == maxRetry - 1)
                            {
                                writeLog($"[耐压1可用状态] 读取M{DataModel.Settingmodel.Meter1AvailableAddress}失败，TV1Available=false");
                            }
                        }

                        if (r2.IsSuccess)
                        {
                            DataModel.Processmodel.TVAvailable.TV2Available = !r2.Content[0];
                        }
                        else
                        {
                            DataModel.Processmodel.TVAvailable.TV2Available = false;
                            if (i == maxRetry - 1)
                            {
                                writeLog($"[耐压2可用状态] 读取M{DataModel.Settingmodel.Meter2AvailableAddress}失败，TV2Available=false");
                            }
                        }

                        if (r3.IsSuccess)
                        {
                            DataModel.Processmodel.TVAvailable.TV3Available = !r3.Content[0];
                        }
                        else
                        {
                            DataModel.Processmodel.TVAvailable.TV3Available = false;
                            if (i == maxRetry - 1)
                            {
                                writeLog($"[耐压3可用状态] 读取M{DataModel.Settingmodel.Meter3AvailableAddress}失败，TV3Available=false");
                            }
                        }

                        // 双Y专用部署仅有 Y1/Y2 耐压仪，忽略 M3032 可用镜像，避免无仪器下发失败阻断规格写入。
                        if (IsDualYElectricalTestDeployment())
                        {
                            bool wasAvailable = DataModel.Processmodel.TVAvailable.TV3Available;
                            DataModel.Processmodel.TVAvailable.TV3Available = false;
                            if (_lastDualYForcedTv3Unavailable == null || _lastDualYForcedTv3Unavailable.Value != true)
                            {
                                _lastDualYForcedTv3Unavailable = true;
                                writeLog($"[双Y部署] 强制忽略耐压工位3：TV3Available=false（PLC M{DataModel.Settingmodel.Meter3AvailableAddress}镜像={(wasAvailable ? "可用" : "不可用")}，本机无AT9620_3）", true);
                            }
                        }
                        else if (_lastDualYForcedTv3Unavailable != null)
                        {
                            _lastDualYForcedTv3Unavailable = null;
                        }

                        // 同步更新IR可用状态（M3033）
                        if (rIR.IsSuccess)
                        {
                            // 记录原始线圈值（用于诊断）
                            bool raw = rIR.Content[0];
                            DataModel.Processmodel.TVAvailable.IRAvailableRawCoil = raw;

                            // 现场确认：IR(M3033) 与耐压可用信号逻辑相反——1 表示“开启/可用”
                            bool available = raw;
                            DataModel.Processmodel.TVAvailable.IRAvailable = available;

                            // 仅在状态变化时打印一次，避免刷屏
                            if (_lastIrAvailable == null || _lastIrAvailable.Value != available ||
                                _lastIrRawCoil == null || _lastIrRawCoil.Value != raw)
                            {
                                _lastIrAvailable = available;
                                _lastIrRawCoil = raw;
                                writeLog($"[IR可用状态] 读取M{DataModel.Settingmodel.IRMeterAvailableAddress}={raw}（原始线圈） -> IRAvailable={available}");
                            }
                        }
                        else
                        {
                            DataModel.Processmodel.TVAvailable.IRAvailable = false;
                            DataModel.Processmodel.TVAvailable.IRAvailableRawCoil = false;

                            if (_lastIrAvailable == null || _lastIrAvailable.Value != false)
                            {
                                _lastIrAvailable = false;
                                _lastIrRawCoil = null;
                                writeLog($"[IR可用状态] 读取M{DataModel.Settingmodel.IRMeterAvailableAddress}失败，IRAvailable=false");
                            }
                        }

                        bool tv3IrBothAvailable = DataModel.Processmodel.TVAvailable.TV3Available && DataModel.Processmodel.TVAvailable.IRAvailable;
                        if (_lastTv3IrBothAvailable == null || _lastTv3IrBothAvailable.Value != tv3IrBothAvailable)
                        {
                            _lastTv3IrBothAvailable = tv3IrBothAvailable;
                            if (tv3IrBothAvailable)
                            {
                                writeLog($"[工位3电测互斥] M{DataModel.Settingmodel.Meter3AvailableAddress}=可用且M{DataModel.Settingmodel.IRMeterAvailableAddress}=可用，软件按D{DataModel.Settingmodel.AddressStart + 10}/D{DataModel.Settingmodel.IRTrigAddress}实际触发执行对应电测路径。", true);
                            }
                        }

                        if (r1.IsSuccess & r2.IsSuccess & r3.IsSuccess)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (i == maxRetry - 1)
                    {
                        writeLog($"PLC读取仪器可用状态异常: {ex.Message}", true);
                    }
                }
                Thread.Sleep(50);
            }

            return false;
        }

    }
}
