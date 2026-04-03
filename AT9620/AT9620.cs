using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Serialization;
using GalaSoft.MvvmLight;

namespace AT9620
{
    public class AT9620 : ObservableObject
    {
        public event EventHandler DataReceived;
        public event EventHandler<LogEventArgs> LogMessage; // 新增日志事件
        
        public TVParameter TVParameter { get; set; } = new TVParameter();
        public int delaytime { set; get; } = 1000;
        public int intervaltime { set; get; } = 30;

        public bool stop { set; get; } = false;

        [XmlIgnore]
        public string strrecord { set; get; } = string.Empty;
        public bool TestResult { set; get; } = false;

        private TcpClient tcp = new TcpClient();

        public string IP { set; get; } = "192.168.0.211";
        public int Port { set; get; } = 2000;

        [XmlIgnore]
        public bool isconnected = false;

        /// <summary>
        /// 耐压测试诊断日志（由上位机在每次测试前注入，测试结束置空）。行内已为「时间戳\t正文」。
        /// </summary>
        [XmlIgnore]
        public Action<string> DiagnosticLog { get; set; }

        /// <summary>
        /// 通信级日志（由上位机在每次测试前注入），用于记录发送的指令和原始接收字符串。行内已为「时间戳\t正文」。
        /// </summary>
        [XmlIgnore]
        public Action<string> CommunicationLog { get; set; }

        /// <summary>Fetch? 单次 Socket 接收超时（毫秒）。</summary>
        public int FetchReceiveTimeoutMs { get; set; } = 200;

        /// <summary>
        /// Fetch? 最大尝试次数（含首次）：共 N 次 = 1 次首发 + (N - 1) 次重试。
        /// </summary>
        public int FetchMaxAttempts { get; set; } = 3;

        /// <summary>Fetch? 失败后到下一次重新发送前的间隔（毫秒）。</summary>
        public int FetchRetryDelayMs { get; set; } = 100;

        /// <summary>rp?、FUNC:SOUR:STEP? 等单次接收超时（毫秒）。</summary>
        public int OtherCommandReceiveTimeoutMs { get; set; } = 3000;

        /// <summary>发送后、Receive 前的等待（毫秒）。</summary>
        public int PostSendDelayMs { get; set; } = 100;

        /// <summary>最近一次 Fetch? 返回的原始字符串（供诊断；失败时可能为空）。</summary>
        [XmlIgnore]
        public string LastFetchRaw { get; private set; } = string.Empty;

        private void Diag(string message)
        {
            DiagnosticLog?.Invoke($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{message}");
        }

        private void Comm(string message)
        {
            CommunicationLog?.Invoke($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{message}");
        }

        // 日志方法
        private void WriteLog(string message)
        {
            LogMessage?.Invoke(this, new LogEventArgs { Message = message });
        }

        /// <summary>
        /// 建立到耐压仪的 TCP 连接并更新 <see cref="isconnected"/>。
        /// 用 <see cref="OtherCommandReceiveTimeoutMs"/> 与
        /// <see cref="FetchReceiveTimeoutMs"/> 的较大值（异常为非正时回退 5s）作为 <see cref="TcpClient.ReceiveTimeout"/> 初值，
        /// 避免连接建立后底层仍处于过长阻塞；实际每次读仍在 <see cref="SendAndReceiveOnce"/> 里按指令类型单独设置超时，
        /// 以满足总测试时间较短时 Fetch 轮询不能单次卡死过久的需求。
        /// </summary>
        public bool connect(string _IPaddress, int _Port, int _receivetimeout = 5000)
        {
            try
            {
                IP = _IPaddress;
                Port = _Port;

                if ((tcp.Client == null) || (!tcp.Connected))
                {
                    tcp = new TcpClient();
                    int rx = Math.Max(OtherCommandReceiveTimeoutMs, FetchReceiveTimeoutMs);
                    if (rx <= 0)
                    {
                        rx = 5000;
                    }
                    tcp.ReceiveTimeout = rx;
                    tcp.ConnectAsync(IP, Port).Wait(200);
                }
                isconnected = tcp.Connected;
            }
            catch (Exception ex)
            {
                isconnected = false;
            }

            return isconnected;
        }

        public bool disconnect()
        {
            try
            {
                if (isconnected)
                {
                    tcp?.Close();
                    isconnected = false;
                }
            }
            catch
            {

            }

            return !isconnected;
        }

        public Result Start()
        {
            var c = connect(IP, Port);
            if (!c)
            {
                return new Result() { Error = "连接失败" };
            }
            var r = _Start();
            disconnect();
            return r;
        }

        public Result Download()
        {
            var c = connect(IP, Port);
            if (!c)
            {
                return new Result() { Error = "连接失败" };
            }
            var r = _Download();
            disconnect();
            return r;
        }
        public Result _Download()
        {

            Result r = new Result();

            var r1 = Send("FUNC:SOUR:STEP:NEW\n");
            if (!r1.Success)
            {
                WriteLog($"[参数下发] ❌ 创建新步骤失败: {r1.Error}");
                r.Error = r1.Error;
                return r;
            }
            WriteLog($"[参数下发] ✓ 成功创建新步骤");
            Thread.Sleep(intervaltime);

            // 根据测试模式使用不同的命令格式
            string wp;

            if (TVParameter.TestMode == TestMode.ACW)
            {
                // ACW命令格式: WP <STEP,ACW,VOLT,TIME,RISETIME,FALLTIME,HIGH,LOW,ARC,FREQ>
                // FREQ: 0=50Hz, 1=60Hz
                wp = $"WP 1,{TVParameter.TestMode},{TVParameter.Voltage},{TVParameter.TestTime},{TVParameter.RiseTime},{TVParameter.FallTime},{TVParameter.High},{TVParameter.Low},{TVParameter.Arc},{(TVParameter.Freq == 50 ? 0 : 1)}\n";
                WriteLog($"[参数下发] ACW模式参数:");
                WriteLog($"  - 电压: {TVParameter.Voltage}V");
                WriteLog($"  - 测试时间: {TVParameter.TestTime}s");
                WriteLog($"  - 上升时间: {TVParameter.RiseTime}s");
                WriteLog($"  - 下降时间: {TVParameter.FallTime}s");
                WriteLog($"  - 电流上限: {TVParameter.High}mA");
                WriteLog($"  - 电流下限: {TVParameter.Low}mA");
                WriteLog($"  - 电弧侦测: {TVParameter.Arc}");
                WriteLog($"  - 频率: {TVParameter.Freq}Hz ({(TVParameter.Freq == 50 ? 0 : 1)})");
                WriteLog($"[参数下发] 发送命令: {wp.Replace("\n", "\\n")}");
            }
            else if (TVParameter.TestMode == TestMode.DCW)
            {
                // DCW命令格式: WP <STEP,DCW,VOLT,TIME,RISETIME,FALLTIME,HIGH,LOW,ARC,CHG,RUPPER>
                // CHG: 充电参数（通常为0）
                // RUPPER: 缓升上限
                wp = $"WP 1,{TVParameter.TestMode},{TVParameter.Voltage},{TVParameter.TestTime},{TVParameter.RiseTime},{TVParameter.FallTime},{TVParameter.High},{TVParameter.Low},{TVParameter.Arc},0,0\n";
                WriteLog($"[参数下发] DCW模式参数:");
                WriteLog($"  - 电压: {TVParameter.Voltage}V");
                WriteLog($"  - 测试时间: {TVParameter.TestTime}s");
                WriteLog($"  - 上升时间: {TVParameter.RiseTime}s");
                WriteLog($"  - 下降时间: {TVParameter.FallTime}s");
                WriteLog($"  - 电流上限: {TVParameter.High}mA");
                WriteLog($"  - 电流下限: {TVParameter.Low}mA");
                WriteLog($"  - 电弧侦测: {TVParameter.Arc}");
                WriteLog($"  - 充电参数: 0");
                WriteLog($"  - 上限电阻: 0");
                WriteLog($"[参数下发] 发送命令: {wp.Replace("\n", "\\n")}");
            }
            else
            {
                // IR或其他模式（暂不支持）
                WriteLog($"[参数下发] ❌ 不支持的测试模式: {TVParameter.TestMode}");
                r.Error = $"不支持的测试模式: {TVParameter.TestMode}";
                return r;
            }
            
            var r2 = Send(wp);
            if (!r2.Success)
            {
                WriteLog($"[参数下发] ❌ 参数下发失败: {r2.Error}");
                r.Error = r2.Error;
                return r;
            }
            WriteLog($"[参数下发] ✓ 参数下发成功");
            Thread.Sleep(intervaltime);

            WriteLog($"[参数下发] 开始回读参数验证...");
            var r3 = Get_String("rp? 1\n");
            if (!r3.Success)
            {
                WriteLog($"[参数下发] ❌ 回读参数失败: {r3.Error}");
                r.Error = r3.Error;
                return r;
            }
            WriteLog($"[参数下发] ✓ 回读参数成功: {r3.Value}");
            
            WriteLog($"[参数下发] 开始参数对比验证...");
            var r4 = TVParameter.compare(r3.Value);
            if (!r4.Success)
            {
                WriteLog($"[参数下发] ❌ 参数验证失败:");
                WriteLog($"{r4.Error}");
                r.Error = $"测试工艺参数不一致，请重新下发工艺参数\r\n{r4.Error}";
                return r;
            }
            WriteLog($"[参数下发] ✓ 参数验证成功，所有参数一致");
            WriteLog($"[参数下发] ========== 参数下发流程完成 ==========");

            r.Success = true;


            return r;


        }

        /// <summary>
        /// 执行耐压测试的核心方法
        /// 这是电测系统的核心执行逻辑，负责启动测试、实时监控测试过程、判断测试结果
        /// </summary>
        /// <returns>Result对象，包含测试结果和详细的错误信息或过程数据</returns>
        public Result _Start()
        {
            // 初始化返回结果对象
            Result r = new Result();

            // 步骤1：验证测试步骤数量
            // AT9620设备支持多个测试步骤，这里确保只有一个步骤（简化模式）
            var r1 = Get_StepNum();
            if (!r1.Success)
            {
                Diag($"【启动中止】步骤数量读取失败：{r1.Error}");
                r.Error = r1.Error;
                return r;
            }
            if (r1.Value != 1)
            {
                Diag($"【启动中止】测试工艺步骤数量{r1.Value}，不等于1");
                r.Error = $"测试工艺步骤数量{r1.Value}，不等于1";
                return r;
            }

            // 步骤2：参数一致性验证
            // 从设备回读当前设置的参数，与本地TVParameter对象进行对比
            // 确保设备实际运行的参数与期望参数一致
            var r2 = Get_String("rp? 1\n");
            if (!r2.Success)
            {
                Diag($"【启动中止】回读工艺参数失败：{r2.Error}");
                r.Error = r2.Error;
                return r;
            }
            var r3 = TVParameter.compare(r2.Value);
            if (!r3.Success)
            {
                Diag($"【启动中止】本机工艺参数与仪器回读不一致");
                r.Error = $"测试工艺参数不一致，请重新下发工艺参数\r\n{r3.Error}";
                return r;
            }

            // 步骤3：启动耐压测试
            // 向AT9620设备发送启动测试命令
            var r4 = Send("FUNCtion:STARt\n");
            Thread.Sleep(delaytime); // 等待设备响应

            // 初始化测试监控变量
            ResultTVProcess resultTVProcess = new ResultTVProcess(); // 测试过程数据接收对象
            DateTime dt = DateTime.Now; // 记录测试开始时间
            // 计算理论测试总时间 = 上升时间 + 保持时间 + 下降时间
            float totaltime = TVParameter.TestTime + TVParameter.RiseTime + TVParameter.FallTime;
            Diag($"【进入监控】已发 FUNCtion:STARt，理论总时长={totaltime}s（上升{TVParameter.RiseTime}+保持{TVParameter.TestTime}+下降{TVParameter.FallTime}），上位机超时判定={totaltime + 2}s（+2s缓冲）");

            #region 清空数据
            // 清空历史测试记录，为新的测试过程做准备
            strrecord = string.Empty;
            #endregion

            // 初始化停止标志
            stop = false;

            // 用于「阶段变化时记录仪器原文」：与上一次成功解析的阶段比较
            string lastStatusForRawCapture = null;
            // 最近一次成功解析的阶段（用于超时/异常时的说明）
            string lastSuccessParsedStatus = null;
            int successPollCount = 0;
            float lastSampleV = 0, lastSampleI = 0, lastSampleT = 0;
            string lastSampleStatus = null;
            double lastSampleElapsedPc = 0;

            void TryDiagLastSample()
            {
                if (successPollCount > 1)
                {
                    Diag($"【采样-末条】阶段={lastSampleStatus}，电压={lastSampleV}，电流={lastSampleI}，仪器时间={lastSampleT}s，PC已耗时={lastSampleElapsedPc:F2}s");
                }
            }

            // 步骤4：进入测试监控主循环
            // 实时监控测试过程，直到测试完成或超时
            while (true)
            {
                try
                {
                    // 检查是否有外部停止信号
                    if (stop)
                    {
                        Diag("【外部停止】收到 stop 标志，已发送 FUNCtion:STOP");
                        var stopr = Send("FUNCtion:STOP\n"); // 发送停止测试命令
                    }

                    // 获取实时测试过程数据
                    resultTVProcess = GetProcess();
                    if (resultTVProcess.Success)
                    {
                        var st = resultTVProcess.Value.status;
                        lastSuccessParsedStatus = st;
                        double elapsedPc = (DateTime.Now - dt).TotalSeconds;
                        var v = resultTVProcess.Value.Voltage;
                        var cur = resultTVProcess.Value.Current;
                        var instrT = resultTVProcess.Value.Time;

                        successPollCount++;
                        if (successPollCount == 1)
                        {
                            Diag($"【采样-首条】阶段={st}，电压={v}，电流={cur}，仪器时间={instrT}s，PC已耗时={elapsedPc:F2}s");
                        }

                        if (st != lastStatusForRawCapture)
                        {
                            if (lastStatusForRawCapture != null)
                            {
                                Diag($"【阶段变化】{lastStatusForRawCapture}→{st}，电压={v}，电流={cur}，仪器时间={instrT}s，PC已耗时={elapsedPc:F2}s");
                            }
                            if (!string.IsNullOrEmpty(LastFetchRaw))
                            {
                                Diag($"【仪器原文（阶段变化时记录）】{LastFetchRaw}");
                            }
                            lastStatusForRawCapture = st;
                        }

                        lastSampleV = v;
                        lastSampleI = cur;
                        lastSampleT = instrT;
                        lastSampleStatus = st;
                        lastSampleElapsedPc = elapsedPc;

                        // 触发数据接收事件，通知上层应用更新界面显示
                        DataReceived.Invoke(this, new AT9620EventArgs()
                        {
                            ResultTVProcess = resultTVProcess
                        });

                        // 核心状态判断逻辑
                        // AT9620设备在测试的不同阶段会返回不同的状态字符串：
                        // "Ramp Up" - 电压上升阶段
                        // "Dwell" - 电压保持阶段
                        // "Ramp Down" - 电压下降阶段
                        // "PASS" - 测试通过
                        // 其他状态 - 测试失败（如"上升不良"、"电流超限"等）
                        if (resultTVProcess.Value.status != "Dwell" &&
                            resultTVProcess.Value.status != "Ramp Up" &&
                            resultTVProcess.Value.status != "Ramp Down")
                        {
                            // 收到非过程状态，表示测试已结束
                            if (resultTVProcess.Value.status == "PASS")
                            {
                                r.Success = true; // 测试通过
                                Diag("【结束】仪器返回结束状态 PASS");
                            }
                            else
                            {
                                // 测试失败，记录具体的失败原因
                                r.Error = resultTVProcess.Value.status;
                                Diag($"【结束】仪器返回结束状态：{resultTVProcess.Value.status}");
                            }
                            TryDiagLastSample();
                            break; // 退出监控循环
                        }
                    }
                    else
                    {
                        double failElapsedPc = (DateTime.Now - dt).TotalSeconds;
                        string rawPart = string.IsNullOrEmpty(LastFetchRaw) ? "(空)" : LastFetchRaw;
                        string phaseRef = lastSuccessParsedStatus ?? "尚无成功解析";
                        if (successPollCount > 0)
                        {
                            Diag($"【采样失败】原因={resultTVProcess.Error ?? "未知"}，仪器原文={rawPart}，同一测试内参考阶段={phaseRef}，最近一次成功值 电压={lastSampleV} 电流={lastSampleI} 仪器时间={lastSampleT}s，PC已耗时={failElapsedPc:F2}s（若阶段未变而出现多条本行，多为间歇通信/缓冲脏数据）");
                        }
                        else
                        {
                            Diag($"【采样失败】原因={resultTVProcess.Error ?? "未知"}，仪器原文={rawPart}，尚未有成功Fetch，PC已耗时={failElapsedPc:F2}s");
                        }
                    }

                    // 超时检测机制
                    // 如果测试时间超过理论总时间+2秒（缓冲时间），认为测试异常
                    if ((DateTime.Now - dt).TotalSeconds > totaltime + 2)
                    {
                        r.Error = "测试超时未完成";
                        Diag($"【结束】上位机判定超时（PC已耗时 > {totaltime + 2}s），最后成功解析阶段={lastSuccessParsedStatus ?? "无"}");
                        TryDiagLastSample();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Diag($"【监控异常】已忽略并继续轮询：{ex.Message}");
                    // 捕获异常但不中断测试，继续监控
                    // 这是为了防止网络波动等临时问题导致测试中断
                    continue;
                }
            }

            // 保存完整的测试过程原始数据记录
            // 无论测试成功还是失败，都保存过程数据用于后续分析
            //if(!r.Success)
            //{
            r.Recordstr = strrecord;
            //}

            // 返回测试结果
            return r;
        }

        /// <summary>
        /// 向已连接仪器发送一条指令并同步读取应答（ASCII，去 \r\n）。
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// 轮询用 <c>Fetch?</c>：单次 <see cref="Socket.Receive"/> 使用较短超时（<see cref="FetchReceiveTimeoutMs"/>），
        /// 避免在总测试仅约十多秒的场景下单次等待 10s 占满时间窗；失败则按 <see cref="FetchRetryDelayMs"/> 间隔再发起整轮「发送→等待→接收」，
        /// 最多 <see cref="FetchMaxAttempts"/> 次（含首次）；用尽仍失败时记一条「Fetch? 重试用尽」通信日志便于检索。
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <c>rp?</c>、<c>FUNC:SOUR:STEP?</c> 等其它命令：只执行单轮收发，使用 <see cref="OtherCommandReceiveTimeoutMs"/>，不自动重试。
        /// </description>
        /// </item>
        /// </list>
        /// </summary>
        public Result Get_String(string cmd)
        {
            if (IsFetchQueryCommand(cmd))
            {
                Result last = new Result();
                int attempts = Math.Max(1, FetchMaxAttempts);
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    last = SendAndReceiveOnce(cmd, FetchReceiveTimeoutMs);
                    if (last.Success)
                    {
                        return last;
                    }
                    if (attempt < attempts)
                    {
                        Thread.Sleep(Math.Max(0, FetchRetryDelayMs));
                    }
                }

                Comm($"Fetch? 重试用尽: {last.Error ?? "失败"}");
                return last;
            }

            return SendAndReceiveOnce(cmd, OtherCommandReceiveTimeoutMs);
        }

        /// <summary>
        /// 是否为 <c>Fetch?</c> 查询：去掉尾部空白后以 OrdinalIgnoreCase 比较，供 <see cref="Get_String"/> 选择短超时加重试分支。
        /// </summary>
        private static bool IsFetchQueryCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd))
            {
                return false;
            }

            string t = cmd.TrimEnd('\r', '\n', ' ', '\t');
            return string.Equals(t, "Fetch?", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 单轮通信：打发送日志 → 设置本次 <see cref="Socket.ReceiveTimeout"/> → 发送 → <see cref="PostSendDelayMs"/> 等待 → 接收 → 成功/空包/异常日志。
        /// 每次 Receive 前单独设置超时，使同一 TCP 连接上对 Fetch 与其它 SCPI 可使用不同时长上限，而不必在 <see cref="connect"/> 里写死一种值。
        /// </summary>
        private Result SendAndReceiveOnce(string cmd, int receiveTimeoutMs)
        {
            Result r = new Result();
            try
            {
                if (!isconnected)
                {
                    return r;
                }

                Comm($"发送: {cmd.Replace("\n", "\\n")}");
                tcp.Client.ReceiveTimeout = receiveTimeoutMs;
                tcp.Client.Send(Encoding.ASCII.GetBytes($"{cmd}"));

                Thread.Sleep(Math.Max(0, PostSendDelayMs));
                byte[] receive = new byte[1024];
                int bytesRead = tcp.Client.Receive(receive);

                string result_str = Encoding.ASCII.GetString(receive, 0, bytesRead).Replace("\0", "").Replace("\r", "").Replace("\n", "");
                if (string.IsNullOrEmpty(result_str))
                {
                    r.Error = "接收数据为空";
                    Comm("接收失败: 接收数据为空");
                }
                else
                {
                    r.Value = result_str;
                    r.Success = true;
                    Comm($"接收成功({result_str.Length}字符): {result_str}");
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.ToString();
                Comm($"接收异常: {ex.Message}");
            }

            return r;
        }

        public Resultint Get_StepNum()
        {
            Resultint r = new Resultint();

            var re = Get_String("FUNC:SOUR:STEP?\n");
            if (re.Success)
            {
                try
                {
                    r.Value = Convert.ToInt32(re.Value.Split('-')[1].Replace("TOTAL", "").Trim());
                    r.Success = true;
                }
                catch (Exception EX)
                {

                    r.Error = $"接收内容:{re.Value}\r\n {EX.ToString()}";
                }

            }
            else
            {
                r.Error = re.Error;
                r.Success = re.Success;

            }



            return r;


        }
        public Result Send(string cmd)
        {
            Result r = new Result();
            try
            {
                if (isconnected)
                {
                    #region 读取数值
                    //string steprquestcmd = "FUNC:SOUR:STEP?\n";
                    //tcp.Client.Send(Encoding.ASCII.GetBytes($"{steprquestcmd}"));
                    tcp.Client.Send(Encoding.ASCII.GetBytes($"{cmd}"));

                    //Thread.Sleep(100);
                    //byte[] receive = new byte[1024];
                    //tcp.Client.Receive(receive);

                    //string result_str = System.Text.Encoding.ASCII.GetString(receive).Replace("\0", "").Replace("\r", "").Replace("\n", "");
                    //if (string.IsNullOrEmpty(result_str))
                    //{
                    //    r.Error = "接收数据为空";
                    //}
                    //else
                    //{
                    //    r.Value = result_str;
                    //    r.Success = true;
                    //}
                    r.Success = true;
                    #endregion


                }
            }
            catch (Exception ex)
            {
                r.Error = ex.ToString();
            }
            return r;
        }

        public ResultTVProcess GetProcess()
        {
            ResultTVProcess ResultTVProcess = new ResultTVProcess();
            var re = Get_String("Fetch?\n");
            if (re.Success)
            {
                LastFetchRaw = re.Value ?? string.Empty;
                strrecord += re.Value + "/";
                string[] ss = re.Value.Split(',');
                if (ss.Length == 6)
                {
                    float f1, f2, f3;
                    bool r1 = float.TryParse(ss[3], out f1);
                    bool r2 = float.TryParse(ss[4], out f2);
                    bool r3 = float.TryParse(ss[5], out f3);
                    if (r1 & r2 & r3)
                    {
                        ResultTVProcess.Value.status = ss[2];
                        ResultTVProcess.Value.Voltage = f1;
                        ResultTVProcess.Value.Current = f2;
                        ResultTVProcess.Value.Time = f3;
                        ResultTVProcess.Success = true;
                    }
                    else
                    {
                        ResultTVProcess.Error = "字符串格式错误";
                    }
                }
                else
                {
                    ResultTVProcess.Error = $"字段数量异常({ss.Length})，预期6段";
                }

            }
            else
            {
                LastFetchRaw = string.Empty;
                ResultTVProcess.Error = re.Error;

            }
            return ResultTVProcess;
        }


    }



    public class Resultint : ObservableObject
    {
        public bool Success { get; set; } = false;
        public int Value { get; set; } = -1;
        public string Error { get; set; }

    }


    public class Result : ObservableObject
    {
        public bool Success { get; set; } = false;
        public string Value { set; get; } = string.Empty;
        public string Error { get; set; }
        public string Recordstr { set; get; }

    }

    public class ResultTVProcess : ObservableObject
    {
        public bool Success { get; set; } = false;
        public TVProcess Value { set; get; } = new TVProcess();
        public string Error { get; set; }
    }


    public class TVParameter : ObservableObject
    {

        //public string Parastring
        //{
        //    get
        //    {
        //        return $"{TestMode},{Voltage},{TestTime},{RiseTime},{FallTime},{High},{Low},{Arc},{(Freq == 50 ? 0 : 1)}";

        //    }
        //}

        public Result compare(string cmd)
        {
            Result result = new Result();
            try
            {
                string[] ss = cmd.Split(',');

                // 验证测试模式
                if (TestMode.ToString() != ss[0])
                {
                    result.Error += $"\r\n测试模式不匹配: 期望{TestMode}，实际{ss[0]}";
                }

                // 解析通用参数（ACW和DCW都有的参数）
                float _voltage, _testtime, _risetime, _falltime, _high, _low, _arc;
                var r1 = float.TryParse(ss[1], out _voltage);
                var r2 = float.TryParse(ss[2], out _testtime);
                var r3 = float.TryParse(ss[3], out _risetime);
                var r4 = float.TryParse(ss[4], out _falltime);
                var r5 = float.TryParse(ss[5], out _high);
                var r6 = float.TryParse(ss[6], out _low);
                var r7 = float.TryParse(ss[7], out _arc);

                // DCW模式下，仪器回读的电流单位是微安(μA)，需要转换为毫安(mA)
                if (TestMode == TestMode.DCW)
                {
                    _high = _high / 1000.0f;  // μA → mA
                    _low = _low / 1000.0f;    // μA → mA
                }

                if (!r1)
                {
                    result.Error += $"\r\n回读电压数据格式错误";
                }
                if (!r2)
                {
                    result.Error += $"\r\n回读测试时间数据格式错误";
                }
                if (!r3)
                {
                    result.Error += $"\r\n回读上升时间数据格式错误";
                }
                if (!r4)
                {
                    result.Error += $"\r\n回读下降时间数据格式错误";
                }
                if (!r5)
                {
                    result.Error += $"\r\n回读电流上限数据格式错误";
                }
                if (!r6)
                {
                    result.Error += $"\r\n回读电流下限数据格式错误";
                }
                if (!r7)
                {
                    result.Error += $"\r\n回读电弧侦测数据格式错误";
                }

                // 验证通用参数值
                if (Voltage != _voltage)
                {
                    result.Error += $"\r\n回读电压与测试工艺不符合: 期望{Voltage}V，实际{_voltage}V";
                }
                if (TestTime != _testtime)
                {
                    result.Error += $"\r\n回读测试时间与测试工艺不符合: 期望{TestTime}s，实际{_testtime}s";
                }
                if (RiseTime != _risetime)
                {
                    result.Error += $"\r\n回读上升时间与测试工艺不符合: 期望{RiseTime}s，实际{_risetime}s";
                }
                if (FallTime != _falltime)
                {
                    result.Error += $"\r\n回读下降时间与测试工艺不符合: 期望{FallTime}s，实际{_falltime}s";
                }
                if (High != _high)
                {
                    result.Error += $"\r\n回读电流上限与测试工艺不符合: 期望{High}mA，实际{_high}mA";
                }
                if (Low != _low)
                {
                    result.Error += $"\r\n回读电流下限与测试工艺不符合: 期望{Low}mA，实际{_low}mA";
                }
                if (Arc != _arc)
                {
                    result.Error += $"\r\n回读电弧与测试工艺不符合: 期望{Arc}，实际{_arc}";
                }

                // 根据测试模式验证特定参数
                if (TestMode == TestMode.ACW)
                {
                    // ACW模式：验证频率参数（第9个参数，索引8）
                    if (ss.Length > 8)
                    {
                        float _freq;
                        var r8 = float.TryParse(ss[8], out _freq);
                        if (!r8)
                        {
                            result.Error += $"\r\n回读频率数据格式错误";
                        }
                        else
                        {
                            _freq = (_freq == 0 ? 50 : 60);
                            if (Freq != _freq)
                            {
                                result.Error += $"\r\n回读频率与测试工艺不符合: 期望{Freq}Hz，实际{_freq}Hz";
                            }
                        }
                    }
                }
                else if (TestMode == TestMode.DCW)
                {
                    // DCW模式：验证CHG和RUPPER参数（第9、10个参数，索引8、9）
                    // 注意：DCW模式不验证这两个参数，因为它们通常为0且不影响测试
                    // 如果需要验证，可以在这里添加逻辑
                }

                if (string.IsNullOrEmpty(result.Error))
                {
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Error += "\r\n参数对比异常: " + ex.Message;
            }
            return result;
        }


        public TestMode TestMode { get; set; } = TestMode.ACW;

        public float Voltage { set; get; } = -1;

        public float TestTime { set; get; } = -1;
        public float RiseTime { set; get; } = -1;
        public float FallTime { set; get; } = -1;
        public float High { set; get; } = -1;
        public float Low { set; get; } = 0;
        public float Arc { set; get; } = 0;
        public float Freq { set; get; } = 50;


    }
    public class TVProcess
    {

        public string status { set; get; } = string.Empty;

        public float Voltage { set; get; } = 0;
        public float Current { set; get; } = 0;
        public float Time { set; get; } = 0;
    }

    public enum TestMode
    {
        ACW,
        DCW,
        IR
    }

    /// <summary>
    /// 电测测试模式枚举（用于PLC信号D1012）
    /// </summary>
    /// <remarks>
    /// 定义上位机向PLC写入的测试模式值，PLC根据此值控制测试顺序。
    /// 值类型为ushort，对应PLC D寄存器的uint16格式。
    /// </remarks>
    public enum ElectricalTestMode : ushort
    {
        /// <summary>只测交流 (ACW Only) - 仅执行ACW交流耐压测试</summary>
        ACWOnly = 0,
        /// <summary>只测直流 (DCW Only) - 仅执行DCW直流耐压测试</summary>
        DCWOnly = 1,
        /// <summary>先交后直 (ACW then DCW) - 先执行ACW，再执行DCW</summary>
        ACWThenDCW = 2,
        /// <summary>先直后交 (DCW then ACW) - 先执行DCW，再执行ACW</summary>
        DCWThenACW = 3
    }

    public class AT9620EventArgs : EventArgs
    {
        public ResultTVProcess ResultTVProcess { set; get; } = new ResultTVProcess();
    }

    /// <summary>
    /// 日志事件参数
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        public string Message { get; set; }
    }
}
