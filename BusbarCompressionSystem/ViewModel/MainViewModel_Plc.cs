using GalaSoft.MvvmLight;
using HslCommunication.ModBus;
using System.Threading;
using System;


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

        public void PLC_Start()
        {
            Thread t = new Thread(PLC_Process);
            t.Start();

            //Thread t2 = new Thread(shakehand2);
            //t2.Start();
        }
        private void PLC_Process()
        {

            while (true)
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

                        if (readresult.IsSuccess)
                        {
                            int ScanTrig = readresult.Content[0];
                            int TakePhoto1Trig = readresult.Content[2];
                            int TV1Trig = readresult.Content[6];
                            int TV2Trig = readresult.Content[8];
                            int TV3Trig = readresult.Content[10];
                            // IR触发（D1014）：1=启动 2=停止
                            int IRTrig = readresult.Content[14];

                            int SecondScanTrig = secondScanResult.IsSuccess ? secondScanResult.Content[0] : 0;

                            #region 扫码触发
                            try
                            {
                                if (ScanTrig == 1 & DataModel.Processmodel.Scan_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        ScannerProcess();
                                    }).Start();
                                }
                            }
                            catch {; }
                            #endregion

                            #region 第二扫码触发
                            try
                            {
                                if (SecondScanTrig == 1 & DataModel.Processmodel.SecondScan_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        SecondScannerProcess();
                                    }).Start();
                                }
                            }
                            catch {; }
                            #endregion

                            #region 拍照触发
                            try
                            {
                                if (TakePhoto1Trig == 1 & DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TakePhoto1Process();
                                    }).Start();
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
                                    new Thread(() =>
                                    {
                                        TV1Process_ACW();
                                    }).Start();
                                }
                                else if (TV1Trig == 2 & DataModel.Processmodel.TV1_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV1Process_DCW();
                                    }).Start();
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
                                    new Thread(() =>
                                    {
                                        TV2Process_ACW();
                                    }).Start();
                                }
                                else if (TV2Trig == 2 & DataModel.Processmodel.TV2_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV2Process_DCW();
                                    }).Start();
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
                                        new Thread(() => { IRProcess(); }).Start();
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
                            catch { ; }
                            #endregion

                            #region 耐压3触发
                            try
                            {
                                // TV3Trig=1: ACW交流耐压测试
                                // TV3Trig=2: DCW直流耐压测试
                                // TV3Trig=3: 停止测试
                                if (DataModel.Processmodel.TVAvailable.TV3Available)
                                {
                                    if (TV3Trig == 1 & DataModel.Processmodel.TV3_Trig_IO.IOstatus == 0)
                                    {
                                        new Thread(() =>
                                        {
                                            TV3Process_ACW();
                                        }).Start();
                                    }
                                    else if (TV3Trig == 2 & DataModel.Processmodel.TV3_Trig_IO.IOstatus == 0)
                                    {
                                        new Thread(() =>
                                        {
                                            TV3Process_DCW();
                                        }).Start();
                                    }
                                }
                                else if ((TV3Trig == 1 || TV3Trig == 2) & DataModel.Processmodel.TV3_Trig_IO.IOstatus != TV3Trig)
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
                                        new Thread(() => { Res1Process(); }).Start();
                                    }
                                }
                                catch {; }

                                // 阻值2触发
                                try
                                {
                                    if (Res2Trig == 1 & DataModel.Processmodel.Res2_Trig_IO.IOstatus == 0)
                                    {
                                        writeLog($"阻值2触发=1(M{DataModel.Settingmodel.Res2TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes + 2}");
                                        new Thread(() => { Res2Process(); }).Start();
                                    }
                                }
                                catch {; }

                                // 阻值3触发
                                try
                                {
                                    if (Res3Trig == 1 & DataModel.Processmodel.Res3_Trig_IO.IOstatus == 0)
                                    {
                                        writeLog($"阻值3触发=1(M{DataModel.Settingmodel.Res3TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes + 4}");
                                        new Thread(() => { Res3Process(); }).Start();
                                    }
                                }
                                catch {; }
                            }
                            #endregion

                            #region 数据复制刷新
                            DataModel.Processmodel.Scan_Trig_IO.IOstatus = ScanTrig;
                            DataModel.Processmodel.SecondScan_Trig_IO.IOstatus = SecondScanTrig;
                            DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = TakePhoto1Trig;
                            DataModel.Processmodel.TV1_Trig_IO.IOstatus = TV1Trig;
                            DataModel.Processmodel.TV2_Trig_IO.IOstatus = TV2Trig;
                            // IR 触发状态复制刷新（D1014）
                            DataModel.Processmodel.IR_Trig_IO.IOstatus = IRTrig;
                            DataModel.Processmodel.TV3_Trig_IO.IOstatus = TV3Trig;
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
                        DataModel.Processmodel.Scan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.SecondScan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.IR_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV3_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res3_Trig_IO.IOstatus = -1;
                        
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
                Thread.Sleep(100);
            }
        }


        public void PLC_shankhand()
        {

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(1500);
                    try
                    {
                        PLC_ReadTVAvailable();
                    }
                    catch (Exception e)
                    { continue; }

                }
            }).Start();

        }



        

        private bool PLC_write(float result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int maxRetry = 6;
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
                        var r = modbusTcp.Write((DataModel.Settingmodel.AddressStart + 1).ToString(), result);
                        modbusTcp.ConnectClose();
                        if (r.IsSuccess)
                        { return true; }
                    }
                }
                catch
                {
                    writeLog($"视觉写入PLC信号异常1");
                }
                Thread.Sleep(100);
            }

            return false;
        }

        private bool PLC_write(string address, UInt16 result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int maxRetry = 6;
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
                        modbusTcp.ConnectClose();
                        if (r.IsSuccess)
                        { return true; }
                    }
                }
                catch
                {
                    writeLog($"视觉写入PLC信号异常2") ;
                }
                Thread.Sleep(100);
            }

            return false;
        }

        private bool PLC_write(string address, float result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int maxRetry = 6;
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
                        modbusTcp.ConnectClose();
                        if (r.IsSuccess)
                        { return true; }
                    }
                }
                catch
                {
                    writeLog($"视觉写入PLC信号异常3");
                }
                Thread.Sleep(100);
            }

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
        /// </summary>
        /// <returns>连接成功且三台耐压仪线圈读取成功时返回 true，否则返回 false。</returns>
        /// <remarks>
        /// 1. 由 PLC_shankhand() 后台线程周期性调用（约每1500ms一次），用于UI/流程层判断工位是否可用。
        /// 2. 读取线圈：Meter1AvailableAddress、Meter2AvailableAddress、Meter3AvailableAddress，并将读取到的bit取反后写入 TVAvailable（现场信号为“不可用=1”）。
        ///    IR(M3033) 逻辑相反：现场确认 1 表示“开启/可用”，不取反。
        /// 3. 同步写入：ShankHandAddress=1（握手）、DeviceAvailableAddress=allow_start（设备可运行标志）。
        /// 4. TV3(M3032)与IR(M3033)共用第三电测位置；同时可用时记录诊断日志，流程仍按D1010/D1014实际触发执行。
        /// 5. 失败时最多重试3次；仅在最后一次失败时记录异常日志，避免日志刷屏。
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
