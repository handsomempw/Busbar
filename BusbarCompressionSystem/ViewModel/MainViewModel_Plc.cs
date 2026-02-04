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

        /// <summary>
        /// AOI-only模式判定：当耐压1/耐压2工位均不可用时，视为仅走AOI流程。
        /// </summary>
        /// <remarks>
        /// 口径1：只看TV1/TV2是否可用（当前系统已不再使用TV3）。
        /// 在该模式下：不伪造耐压记录，且耐压相关触发在上位机侧直接忽略。
        /// </remarks>
        private bool IsAoiOnlyMode
        {
            get
            {
                return DataModel != null
                    && DataModel.Processmodel != null
                    && DataModel.Processmodel.TVAvailable != null
                    && !DataModel.Processmodel.TVAvailable.TV1Available
                    && !DataModel.Processmodel.TVAvailable.TV2Available;
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
                            // 【优化】不再读取第三个耐压仪器的触发信号（设备已更新，不再使用第三个仪器）
                            // int TV3Trig = readresult.Content[10];

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

                            #region 耐压3触发【已禁用】
                            // 【优化】第三个耐压仪器已不再使用，注释掉触发逻辑
                            //try
                            //{
                            //    if ((TV3Trig == 1 || TV3Trig == 2) & DataModel.Processmodel.TV3_Trig_IO.IOstatus == 0)
                            //    {
                            //        new Thread(() =>
                            //        {
                            //            TV3Process();
                            //        }).Start();
                            //    }
                            //    if ((TV3Trig == 3) & DataModel.Processmodel.TV3_Trig_IO.IOstatus != TV3Trig)
                            //    {
                            //        DataModel.Settingmodel.AT9620_3.stop = true;
                            //    }
                            //}
                            //catch {; }
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
                            // 【优化】第三个耐压仪器已禁用，设置状态为-1（不可用）
                            DataModel.Processmodel.TV3_Trig_IO.IOstatus = -1;
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
        /// <returns>连接成功且前两台耐压仪线圈读取成功时返回 true，否则返回 false。</returns>
        /// <remarks>
        /// 1. 由 PLC_shankhand() 后台线程周期性调用（约每1500ms一次），用于UI/流程层判断工位是否可用。
        /// 2. 读取线圈：Meter1AvailableAddress、Meter2AvailableAddress，并将读取到的bit取反后写入 TVAvailable（现场信号为“不可用=1”）。
        /// 3. 同步写入：ShankHandAddress=1（握手）、DeviceAvailableAddress=allow_start（设备可运行标志）。
        /// 4. 第三台耐压仪已停用：不再读取/判断第三台的PLC状态，且强制 TV3Available=false。
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
                        // 【优化】不再读取第三个耐压仪器的PLC状态（设备已更新，不再使用第三个仪器）
                        // var r3 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter3AvailableAddress.ToString(), 1);
                        modbusTcp.Write(DataModel.Settingmodel.ShankHandAddress.ToString(), (UInt16)1);
                        modbusTcp.Write(DataModel.Settingmodel.DeviceAvailableAddress.ToString(), DataModel.Processmodel.allow_start);
                        modbusTcp.ConnectClose();
                        if (r1.IsSuccess)
                        {
                            DataModel.Processmodel.TVAvailable.TV1Available = !r1.Content[0];
                            DataModel.Processmodel.TVAvailable.TV2Available = !r2.Content[0];
                            // 【优化】强制设置第三个仪器为不可用状态
                            DataModel.Processmodel.TVAvailable.TV3Available = false;
                        }

                        // 【优化】只返回前两个仪器的状态，不再检查第三个仪器
                        if (r1.IsSuccess & r2.IsSuccess)
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
