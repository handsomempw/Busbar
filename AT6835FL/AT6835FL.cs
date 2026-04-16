using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using GalaSoft.MvvmLight;

namespace AT6835FL
{
    /// <summary>
    /// AT6835FL 绝缘电阻测试仪串口驱动
    /// </summary>
    public class AT6835FL : ObservableObject
    {
        // === 串口配置（XML 序列化） ===
        [XmlElement("串口号")]
        public string PortName { get; set; } = "COM1";

        [XmlElement("波特率")]
        public int BaudRate { get; set; } = 9600;

        [XmlElement("数据位")]
        public int DataBits { get; set; } = 8;

        [XmlElement("停止位")]
        public StopBits StopBits { get; set; } = StopBits.One;

        [XmlElement("校验位")]
        public Parity Parity { get; set; } = Parity.None;

        // === 测试参数（XML 序列化，默认值可被 MES 下发覆盖） ===
        public IRParameter IRParameter { get; set; } = new IRParameter();

        // === 控制与状态 ===
        /// <summary>
        /// 停止标志，上位机置位后将在充电/测试等待循环中尽快结束并放电
        /// </summary>
        [XmlIgnore]
        public bool stop { get; set; } = false;

        /// <summary>
        /// 串口连接状态，仅作为运行时状态标记
        /// </summary>
        [XmlIgnore]
        public bool isconnected = false;

        /// <summary>
        /// 原始通信记录字符串（供上位机保存到 F7 追溯）
        /// </summary>
        [XmlIgnore]
        public string strrecord { get; set; } = string.Empty;

        /// <summary>
        /// 串口详细调试：打开后会生成独立调试日志文件，并记录轮询快照（不影响 XML 配置持久化）。
        /// </summary>
        [XmlIgnore]
        public bool VerboseSerialDebug { get; set; } = false;

        /// <summary>
        /// 写命令/查询前是否清空输入缓冲（生产默认 true；调试可关闭以观察残留回包）。
        /// </summary>
        [XmlIgnore]
        public bool DiscardInBufferBeforeWrite { get; set; } = true;

        /// <summary>
        /// 参数下发前是否执行自检（默认 true）。
        /// </summary>
        [XmlIgnore]
        public bool DownloadSelfCheck { get; set; } = true;

        [XmlIgnore]
        private string _serialDebugSessionFile;

        [XmlIgnore]
        private readonly object _serialDebugLock = new object();

        // === 事件 ===
        public event EventHandler<LogEventArgs> LogMessage;

        // === 串口对象（不参与序列化） ===
        [XmlIgnore]
        private SerialPort _serialPort;

        /// <summary>
        /// 单条命令发送后等待回复的超时时间（毫秒）
        /// </summary>
        [XmlElement("命令接收超时")]
        public int ReceiveTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 轮询 STAT? 等待状态变化时的间隔（毫秒）
        /// </summary>
        [XmlElement("状态轮询间隔")]
        public int PollIntervalMs { get; set; } = 200;

        /// <summary>
        /// 充电阶段最长等待时间（秒）
        /// </summary>
        [XmlElement("最大充电时间")]
        public int MaxChargeSeconds { get; set; } = 30;

        /// <summary>
        /// 测试结果最长等待时间（秒）
        /// </summary>
        [XmlElement("最大测试时间")]
        public int MaxTestSeconds { get; set; } = 30;

        private void WriteLog(string message)
        {
            LogMessage?.Invoke(this, new LogEventArgs { Message = message });
        }

        private void EnsureSerialDebugSessionFile()
        {
            if (!VerboseSerialDebug)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_serialDebugSessionFile))
            {
                return;
            }

            string dir = Path.Combine(Environment.CurrentDirectory, "日志", "IR串口调试", DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);
            _serialDebugSessionFile = Path.Combine(dir, $"IR_Serial_{DateTime.Now:HHmmss_fff}.txt");
            SerialDebugTrace($"会话开始 Port={PortName} Baud={BaudRate} DiscardIn={DiscardInBufferBeforeWrite} DownloadSelfCheck={DownloadSelfCheck}");
        }

        private void SerialDebugTrace(string message)
        {
            if (!VerboseSerialDebug)
            {
                return;
            }

            try
            {
                EnsureSerialDebugSessionFile();
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][T{Thread.CurrentThread.ManagedThreadId}] {message}";
                lock (_serialDebugLock)
                {
                    File.AppendAllText(_serialDebugSessionFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 调试日志失败不影响业务
            }
        }

        private static string ToHexPreview(string s, int maxChars = 256)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "<empty>";
            }

            int take = Math.Min(s.Length, maxChars);
            var sb = new StringBuilder();
            for (int i = 0; i < take; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(((int)s[i]).ToString("X2"));
            }
            if (s.Length > take)
            {
                sb.Append(" ...");
            }
            return sb.ToString();
        }

        private Result ReadLineVerbose(string phase)
        {
            var r = new Result();
            if (!Connect())
            {
                r.Error = "串口未连接";
                return r;
            }

            if (!VerboseSerialDebug)
            {
                _serialPort.ReadTimeout = ReceiveTimeoutMs;
                try
                {
                    string line = _serialPort.ReadLine();
                    strrecord += $"< {line}\n";
                    WriteLog($"接收: {line}");
                    r.Success = true;
                    r.Value = line;
                    return r;
                }
                catch (TimeoutException)
                {
                    r.Error = $"接收超时(>{ReceiveTimeoutMs}ms)";
                    WriteLog(r.Error);
                    return r;
                }
            }

            // Verbose：轮询 ReadExisting，直到遇到 LF 或超时
            var sb = new StringBuilder();
            int start = Environment.TickCount;
            int lastLog = -1000000;
            int lastPending = int.MinValue;

            while (Environment.TickCount - start < ReceiveTimeoutMs)
            {
                int pending = 0;
                try { pending = _serialPort.BytesToRead; } catch { pending = 0; }

                int elapsed = Environment.TickCount - start;
                if (elapsed - lastLog >= 200 || pending != lastPending)
                {
                    lastLog = elapsed;
                    lastPending = pending;
                    SerialDebugTrace($"轮询 phase={phase} elapsedMs={elapsed} bytesToRead={pending} collectedLen={sb.Length}");
                }

                if (pending > 0)
                {
                    string chunk = _serialPort.ReadExisting();
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        SerialDebugTrace($"数据块 phase={phase} len={chunk.Length} hex={ToHexPreview(chunk)} ascii={chunk.Replace("\r", "\\r").Replace("\n", "\\n")}");
                        sb.Append(chunk);
                        if (sb.ToString().IndexOf('\n') >= 0)
                        {
                            break;
                        }
                    }
                }

                Thread.Sleep(10);
            }

            string raw = sb.ToString();
            if (string.IsNullOrEmpty(raw))
            {
                int pendingEnd = 0;
                try { pendingEnd = _serialPort.BytesToRead; } catch { pendingEnd = -1; }
                SerialDebugTrace($"超时 phase={phase} no_data bytesToRead={pendingEnd}");
                r.Error = $"接收超时(>{ReceiveTimeoutMs}ms)";
                WriteLog(r.Error);
                return r;
            }

            int lf = raw.IndexOf('\n');
            string line2 = lf >= 0 ? raw.Substring(0, lf).TrimEnd('\r') : raw.TrimEnd('\r');
            strrecord += $"< {line2}\n";
            WriteLog($"接收: {line2}");
            SerialDebugTrace($"整行 phase={phase} value={line2} raw_len={raw.Length}");
            r.Success = true;
            r.Value = line2;
            return r;
        }

        /// <summary>
        /// </summary>
        private static string NormalizeQueryCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return cmd;
            }

            string trimmed = cmd.Trim();
            if (trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                return trimmed;
            }

            if (trimmed.IndexOf('?') < 0)
            {
                return trimmed;
            }

            if (string.Equals(trimmed, "VOLT?", StringComparison.OrdinalIgnoreCase)) return "volt?";
            if (string.Equals(trimmed, "TIME?", StringComparison.OrdinalIgnoreCase)) return "time?";
            if (string.Equals(trimmed, "COMP:RES?", StringComparison.OrdinalIgnoreCase)) return "comp:res?";

            return trimmed;
        }


        #region 串口基础

        /// <summary>
        /// 建立串口连接
        /// </summary>
        public bool Connect()
        {
            try
            {
                if (_serialPort == null)
                {
                    _serialPort = new SerialPort
                    {
                        PortName = PortName,
                        BaudRate = BaudRate,
                        DataBits = DataBits,
                        StopBits = StopBits,
                        Parity = Parity,
                        Encoding = Encoding.ASCII,
                        NewLine = "\n",
                        ReadTimeout = ReceiveTimeoutMs,
                        WriteTimeout = ReceiveTimeoutMs
                    };
                }
                else
                {
                    if (!_serialPort.IsOpen)
                    {
                        _serialPort.PortName = PortName;
                        _serialPort.BaudRate = BaudRate;
                        _serialPort.DataBits = DataBits;
                        _serialPort.StopBits = StopBits;
                        _serialPort.Parity = Parity;
                        _serialPort.NewLine = "\n";
                    }
                    _serialPort.ReadTimeout = ReceiveTimeoutMs;
                    _serialPort.WriteTimeout = ReceiveTimeoutMs;
                }

                if (!_serialPort.IsOpen)
                {
                    // 每次打开串口时重置调试会话文件（避免把多次操作混在一个文件里难以阅读）
                    if (VerboseSerialDebug)
                    {
                        _serialDebugSessionFile = null;
                    }
                    _serialPort.Open();
                    // 现场验证：安柏调试助手常用 DTR=False/RTS=False；.NET 打开串口后可能使用系统默认握手线状态，
                    // 对部分 RS232/转换器电路会造成“能打开但设备不回包/回包异常”。这里强制为 false 以对齐调试助手行为。
                    try
                    {
                        _serialPort.DtrEnable = false;
                        _serialPort.RtsEnable = false;
                    }
                    catch
                    {
                        // 个别驱动不支持设置握手线：忽略
                    }
                    isconnected = true;
                    WriteLog($"串口已打开: {PortName}, {BaudRate},{DataBits},{Parity},{StopBits}");
                    SerialDebugTrace($"打开串口 Port={PortName} Baud={BaudRate} DataBits={DataBits} Parity={Parity} StopBits={StopBits} Handshake={_serialPort.Handshake} DTR={_serialPort.DtrEnable} RTS={_serialPort.RtsEnable} ReceiveTimeoutMs={ReceiveTimeoutMs} Verbose={VerboseSerialDebug} DiscardIn={DiscardInBufferBeforeWrite}");
                }

                return true;
            }
            catch (Exception ex)
            {
                isconnected = false;
                WriteLog($"串口连接失败: {ex.Message}");
                SerialDebugTrace($"打开串口失败 {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭串口连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    SerialDebugTrace("会话结束");
                    _serialPort.Close();
                    WriteLog("串口已关闭");
                    SerialDebugTrace("关闭串口");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"关闭串口异常: {ex.Message}");
                SerialDebugTrace($"关闭串口失败 {ex.Message}");
            }
            finally
            {
                isconnected = false;
                _serialDebugSessionFile = null;
            }
        }

        /// <summary>
        /// 仅发送命令，不关心返回
        /// </summary>
        private Result Send(string cmd)
        {
            var r = new Result();
            try
            {
                if (!Connect())
                {
                    r.Error = "串口未连接";
                    return r;
                }

                string toSend = cmd.EndsWith("\n") ? cmd : cmd + "\n";
                if (DiscardInBufferBeforeWrite)
                {
                    // 避免历史残留回包影响后续查询
                    try { _serialPort.DiscardInBuffer(); } catch { }
                    SerialDebugTrace("清空输入缓冲 before=SEND");
                }
                else
                {
                    int pending = 0;
                    try { pending = _serialPort.BytesToRead; } catch { pending = -1; }
                    SerialDebugTrace($"跳过清空输入缓冲 before=SEND bytesToRead={pending}");
                }
                _serialPort.Write(toSend);
                strrecord += $"> {toSend}";
                WriteLog($"发送: {toSend.Replace("\n", "\\n")}");
                SerialDebugTrace($"> {toSend.Replace("\n", "\\n")}");
                r.Success = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = $"发送命令失败: {ex.Message}";
                WriteLog(r.Error);
                return r;
            }
        }

        /// <summary>
        /// 发送命令并读取一行回复
        /// </summary>
        private Result Get_String(string cmd)
        {
            var r = new Result();
            try
            {
                if (!Connect())
                {
                    r.Error = "串口未连接";
                    return r;
                }

                string normalizedCmd = NormalizeQueryCommand(cmd);
                string toSend = normalizedCmd.EndsWith("\n", StringComparison.Ordinal) ? normalizedCmd : normalizedCmd + "\n";
                if (DiscardInBufferBeforeWrite)
                {
                    // 查询类指令前先清空接收缓冲，避免读到上一条命令的残留回包
                    try { _serialPort.DiscardInBuffer(); } catch { }
                    SerialDebugTrace("清空输入缓冲 before=GET");
                }
                else
                {
                    int pending = 0;
                    try { pending = _serialPort.BytesToRead; } catch { pending = -1; }
                    SerialDebugTrace($"跳过清空输入缓冲 before=GET bytesToRead={pending}");
                }
                _serialPort.Write(toSend);
                strrecord += $"> {toSend}";
                WriteLog($"发送: {toSend.Replace("\n", "\\n")}");
                SerialDebugTrace($"> {toSend.Replace("\n", "\\n")}");

                var read = ReadLineVerbose($"GET {normalizedCmd}");
                if (!read.Success)
                {
                    r.Error = read.Error;
                    return r;
                }

                r.Success = true;
                r.Value = read.Value;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = $"接收失败: {ex.Message}";
                WriteLog(r.Error);
                SerialDebugTrace($"查询失败 {r.Error}");
                return r;
            }
        }

        /// <summary>
        /// 仅读取一行回复（不发送任何命令）。
        /// 说明：用于某些设备在 *TRG 后“主动回传一行结果”的场景，避免发送多余的换行符干扰仪器状态机。
        /// </summary>
        private Result ReadLineOnly()
        {
            var r = new Result();
            try
            {
                if (!Connect())
                {
                    r.Error = "串口未连接";
                    return r;
                }

                var rr = ReadLineVerbose("READLINE_ONLY");
                return rr;
            }
            catch (Exception ex)
            {
                r.Error = $"接收失败: {ex.Message}";
                WriteLog(r.Error);
                SerialDebugTrace($"仅接收一行失败 {r.Error}");
                return r;
            }
        }

        #endregion

        #region 自检 / 参数下发 / 测试流程

        /// <summary>
        /// 自检流程：确认链路和基本状态正常
        /// </summary>
        public Result SelfCheck()
        {
            var r = new Result();
            strrecord = string.Empty;

            try
            {
                // IDN 确认设备身份：部分设备不支持 *IDN?，因此兼容两种写法
                var idn = Get_String("*IDN?");
                if (!idn.Success)
                {
                    WriteLog($"*IDN? 不支持或超时，改用 IDN? 重试: {idn.Error}");
                    idn = Get_String("IDN?");
                }

                if (!idn.Success)
                {
                    r.Error = $"自检失败: {idn.Error}";
                    return r;
                }

                // 兼容不同固件回包格式：只要包含 6835 或 6835FL 即认为是目标设备
                string idnVal = idn.Value ?? string.Empty;
                if (!(idnVal.IndexOf("6835FL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      idnVal.IndexOf("6835", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    r.Error = $"自检失败: 识别到的设备为 \"{idn.Value}\"";
                    return r;
                }

                // STAT? 检查放电状态；如非 discharge，则强制放电
                var s1 = Get_String("STAT?");
                if (!s1.Success)
                {
                    r.Error = $"自检失败: 读取状态失败({s1.Error})";
                    return r;
                }

                if (!string.Equals(s1.Value.Trim(), "discharge", StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog($"当前状态为 {s1.Value}，发送 STAT:DIS 切换到放电");
                    var dis = Send("STAT:DIS");
                    if (!dis.Success)
                    {
                        r.Error = $"自检失败: 放电命令失败({dis.Error})";
                        return r;
                    }

                    Thread.Sleep(200);
                    var s2 = Get_String("STAT?");
                    if (!s2.Success || !string.Equals(s2.Value.Trim(), "discharge", StringComparison.OrdinalIgnoreCase))
                    {
                        r.Error = $"自检失败: 放电后状态异常({s2.Value})";
                        return r;
                    }
                }

                // 触发源设为 hold
                var src = Send("TRIG:SOUR hold");
                if (!src.Success)
                {
                    r.Error = $"自检失败: 设置触发源失败({src.Error})";
                    return r;
                }

                Thread.Sleep(100);
                var srcq = Get_String("TRIG:SOUR?");
                if (!srcq.Success || !string.Equals(srcq.Value.Trim(), "hold", StringComparison.OrdinalIgnoreCase))
                {
                    r.Error = $"自检失败: 触发源回读不为 hold({srcq.Value})";
                    return r;
                }

                // 关闭字符回送
                var echo = Send("ERR:SHAK OFF");
                if (!echo.Success)
                {
                    r.Error = $"自检失败: 关闭回送失败({echo.Error})";
                    return r;
                }

                r.Success = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = $"自检异常: {ex.Message}";
                WriteLog(r.Error);
                return r;
            }
        }

        /// <summary>
        /// 参数下发：VOLT / TIME / COMP:RES 并回读验证
        /// </summary>
        public Result Download()
        {
            // public Download：按“单次操作”模式管理连接生命周期（连接→下发→断开）
            if (!Connect())
            {
                return new Result { Success = false, Error = "串口连接失败" };
            }

            try
            {
                return DownloadCore();
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>
        /// 参数下发核心实现（假设已连接）。
        /// </summary>
        private Result DownloadCore()
        {
            var r = new Result();
            try
            {
                var p = IRParameter ?? new IRParameter();

                if (DownloadSelfCheck)
                {
                    var sc0 = SelfCheck();
                    if (!sc0.Success)
                    {
                        r.Error = $"参数下发前自检失败: {sc0.Error}";
                        SerialDebugTrace($"下发前自检失败 {sc0.Error}");
                        return r;
                    }
                    SerialDebugTrace("下发前自检通过");
                }
                else
                {
                    // 现场日志显示：若完全跳过自检，设备可能对后续 query（如 volt?）不回包（bytesToRead 持续为0）。
                    // 因此在“关闭自检”模式下，仍执行最小化的初始化命令（不做身份/状态回读），以尽量对齐自检后的设备状态。
                    SerialDebugTrace("下发前自检已跳过（执行最小初始化）");
                    try
                    {
                        // 触发源设为 hold（与 SelfCheck 保持一致）
                        Send("TRIG:SOUR hold");
                        Thread.Sleep(50);
                        // 关闭字符回送（与 SelfCheck 保持一致）
                        Send("ERR:SHAK OFF");
                        Thread.Sleep(50);
                    }
                    catch
                    {
                        // 最小初始化失败不阻塞：后续会从 query 超时中体现
                    }
                }

                // VOLT 设置与回读
                var sv = Send($"VOLT {p.Voltage}");
                if (!sv.Success)
                {
                    r.Error = $"VOLT 设置失败: {sv.Error}";
                    return r;
                }
                Thread.Sleep(100);
                var rv = Get_String("VOLT?");
                if (!rv.Success)
                {
                    r.Error = $"VOLT? 回读失败: {rv.Error}";
                    return r;
                }

                // TIME 设置与回读
                var st = Send($"TIME {p.TestTime}");
                if (!st.Success)
                {
                    r.Error = $"TIME 设置失败: {st.Error}";
                    return r;
                }
                Thread.Sleep(100);
                var rt = Get_String("TIME?");

                if (!rt.Success)
                {
                    r.Error = $"TIME? 回读失败: {rt.Error}";
                    return r;
                }

                // COMP:RES 设置与回读
                var sc = Send($"COMP:RES {p.CompRes}");
                if (!sc.Success)
                {
                    r.Error = $"COMP:RES 设置失败: {sc.Error}";
                    return r;
                }
                Thread.Sleep(100);
                var rc = Get_String("COMP:RES?");
                if (!rc.Success)
                {
                    r.Error = $"COMP:RES? 回读失败: {rc.Error}";
                    return r;
                }

                // 使用 IRParameter.Compare 做一致性验证
                var cmp = p.Compare(rv.Value, rt.Value, rc.Value);
                if (!cmp.Success)
                {
                    r.Error = $"参数验证失败: {cmp.Error}";
                    return r;
                }

                r.Success = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = $"参数下发异常: {ex.Message}";
                WriteLog(r.Error);
                return r;
            }
        }

        /// <summary>
        /// 外部调用入口：一次完整的自检 + 下发 + 测试流程
        /// </summary>
        public Result Start()
        {
            var result = new Result();

            if (!Connect())
            {
                result.Error = "串口连接失败";
                return result;
            }

            try
            {
                result = _Start();
            }
            finally
            {
                Disconnect();
            }

            return result;
        }

        /// <summary>
        /// 内部核心测试流程
        /// </summary>
        public Result _Start()
        {
            var r = new Result();
            stop = false;
            strrecord = string.Empty;

            try
            {
                // 1. 自检
                var sc = SelfCheck();
                if (!sc.Success)
                {
                    r.Error = sc.Error;
                    return r;
                }

                // 2. 下发参数
                var dl = DownloadCore();
                if (!dl.Success)
                {
                    r.Error = dl.Error;
                    return r;
                }

                // 3. 切换到充电/测试状态
                var ch = Send("STAT:CHAR");
                if (!ch.Success)
                {
                    r.Error = $"STAT:CHAR 发送失败: {ch.Error}";
                    return r;
                }

                // 4. 轮询 STAT? 等待进入 test，期间可响应 stop
                int chargeElapsed = 0;
                while (true)
                {
                    if (stop)
                    {
                        WriteLog("收到停止信号，执行放电退出");
                        Send("STAT:DIS");
                        r.Error = "测试被外部停止";
                        return r;
                    }

                    var st = Get_String("STAT?");
                    if (!st.Success)
                    {
                        r.Error = $"STAT? 读取失败: {st.Error}";
                        return r;
                    }

                    var state = st.Value.Trim().ToLowerInvariant();
                    if (state == "test")
                    {
                        break;
                    }

                    Thread.Sleep(PollIntervalMs);
                    chargeElapsed += PollIntervalMs;
                    if (chargeElapsed > MaxChargeSeconds * 1000)
                    {
                        Send("STAT:DIS");
                        r.Error = "充电超时";
                        return r;
                    }
                }

                // 5. *TRG 触发测试并等待结果
                var trgSend = Send("*TRG");
                if (!trgSend.Success)
                {
                    r.Error = $"*TRG 发送失败: {trgSend.Error}";
                    Send("STAT:DIS");
                    return r;
                }

                string resultLine = null;
                int testElapsed = 0;
                while (true)
                {
                    if (stop)
                    {
                        WriteLog("收到停止信号，测试阶段执行放电退出");
                        Send("STAT:DIS");
                        r.Error = "测试被外部停止";
                        return r;
                    }

                    try
                    {
                        var rr = ReadLineOnly(); // 直接 ReadLine，一些设备 *TRG 后直接返回
                        if (rr.Success && !string.IsNullOrWhiteSpace(rr.Value))
                        {
                            resultLine = rr.Value;
                            break;
                        }
                    }
                    catch
                    {
                        // 忽略单次异常，尝试 FETCH?
                    }

                    // 尝试 FETCH? 获取结果
                    var fr = Get_String("FETCH?");
                    if (fr.Success && !string.IsNullOrWhiteSpace(fr.Value))
                    {
                        resultLine = fr.Value;
                        break;
                    }

                    Thread.Sleep(PollIntervalMs);
                    testElapsed += PollIntervalMs;
                    if (testElapsed > MaxTestSeconds * 1000)
                    {
                        Send("STAT:DIS");
                        r.Error = "测试结果获取超时";
                        return r;
                    }
                }

                // 6. 解析结果：电阻,电流,判定
                if (string.IsNullOrWhiteSpace(resultLine))
                {
                    r.Error = "结果字符串为空";
                    Send("STAT:DIS");
                    return r;
                }

                var parts = resultLine.Split(',');
                if (parts.Length < 3)
                {
                    r.Error = $"结果格式错误: {resultLine}";
                    Send("STAT:DIS");
                    return r;
                }

                if (double.TryParse(parts[0], out var res))
                {
                    r.Resistance = res;
                }
                if (double.TryParse(parts[1], out var cur))
                {
                    r.LeakCurrent = cur;
                }

                r.Judgment = parts[2].Trim();
                r.Success = string.Equals(r.Judgment, "GD", StringComparison.OrdinalIgnoreCase);
                r.Recordstr = strrecord;

                // 7. 放电收尾
                Send("STAT:DIS");
                var sFinal = Get_String("STAT?");
                if (!sFinal.Success || !string.Equals(sFinal.Value.Trim(), "discharge", StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog($"放电后状态异常: {sFinal.Value}");
                }

                return r;
            }
            catch (Exception ex)
            {
                r.Error = $"测试异常: {ex.Message}";
                WriteLog(r.Error);
                try
                {
                    Send("STAT:DIS");
                }
                catch { }
                return r;
            }
        }

        #endregion
    }

    /// <summary>
    /// IR 测试参数
    /// </summary>
    public class IRParameter : ObservableObject
    {
        /// <summary>测试电压 V</summary>
        public int Voltage { get; set; } = 1000;

        /// <summary>测试电压下限 V（MES下发，当前仅记录/日志使用）</summary>
        public int VoltageLow { get; set; } = 0;

        /// <summary>测试电压上限 V（MES下发，当前仅记录/日志使用）</summary>
        public int VoltageHigh { get; set; } = 0;

        /// <summary>测试时间 s</summary>
        public int TestTime { get; set; } = 10;

        /// <summary>电阻合格阈值字符串（如 \"1g\"）</summary>
        public string CompRes { get; set; } = "1g";

        /// <summary>绝缘电阻下限（MES下发，字符串原样保存，通常用于生成 CompRes）</summary>
        public string ResLow { get; set; } = string.Empty;

        /// <summary>绝缘电阻上限（MES下发，字符串原样保存，当前仅记录/日志使用）</summary>
        public string ResHigh { get; set; } = string.Empty;

        /// <summary>
        /// 与仪器回读字符串进行对比校验
        /// </summary>
        public Result Compare(string voltageReadback, string timeReadback, string compResReadback)
        {
            var r = new Result();
            try
            {
                // 若 MES 下发了下限且调用方未显式设置 CompRes，则默认用下限作为阈值下发（保持向前兼容）。
                if (string.IsNullOrWhiteSpace(CompRes) && !string.IsNullOrWhiteSpace(ResLow))
                {
                    CompRes = ResLow.Trim();
                }

                if (!string.IsNullOrWhiteSpace(voltageReadback))
                {
                    // 仪器回读可能是 "1000.0" 这类带小数格式
                    if (TryParseNumberInvariant(voltageReadback.Trim(), out var v))
                    {
                        if (Math.Abs(v - Voltage) > 0.01)
                        {
                            r.Error += $"电压不一致: 期望 {Voltage}, 实际 {v.ToString(CultureInfo.InvariantCulture)}; ";
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(timeReadback))
                {
                    // 仪器回读可能是 "10.0" 这类带小数格式
                    if (TryParseNumberInvariant(timeReadback.Trim(), out var t))
                    {
                        if (Math.Abs(t - TestTime) > 0.01)
                        {
                            r.Error += $"时间不一致: 期望 {TestTime}, 实际 {t.ToString(CultureInfo.InvariantCulture)}; ";
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(compResReadback))
                {
                    // 兼容科学计数法回读：例如 1000 vs 1.000000e+03（数值等价即通过）
                    string actual = compResReadback.Trim();
                    string expected = (CompRes ?? string.Empty).Trim();

                    if (TryParseNumberInvariant(actual, out var a) && TryParseNumberInvariant(expected, out var e))
                    {
                        // 允许小误差（浮点格式化差异）
                        if (Math.Abs(a - e) > Math.Max(1e-9, Math.Abs(e) * 1e-9))
                        {
                            r.Error += $"合格阈值不一致: 期望 {expected}, 实际 {actual}; ";
                        }
                    }
                    else if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        r.Error += $"合格阈值不一致: 期望 {expected}, 实际 {actual}; ";
                    }
                }

                if (string.IsNullOrEmpty(r.Error))
                {
                    r.Success = true;
                }
            }
            catch (Exception ex)
            {
                r.Error += $"Compare 异常: {ex.Message}";
            }

            return r;
        }

        private static bool TryParseNumberInvariant(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }

            // 兼容：整数、小数、科学计数法（如 1.000000e+03）
            return double.TryParse(
                s,
                NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
        }
    }

    /// <summary>
    /// IR 测试结果
    /// </summary>
    public class Result : ObservableObject
    {
        public bool Success { get; set; } = false;
        public string Value { get; set; } = string.Empty;
        public string Error { get; set; }
        public string Recordstr { get; set; } = string.Empty;

        /// <summary>绝缘电阻值 (Ohm)</summary>
        public double Resistance { get; set; }

        /// <summary>漏电流 (A)</summary>
        public double LeakCurrent { get; set; }

        /// <summary>分选结果（GD/NG）</summary>
        public string Judgment { get; set; }
    }

    /// <summary>
    /// 日志事件参数
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        public string Message { get; set; }
    }
}

