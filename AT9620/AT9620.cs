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

        /// <summary>
        /// Fetch 指令单次 Socket 接收超时（毫秒）。
        /// 该值来自耐压仪独立配置文件，用于单次接收与轮询等待；默认值 200 保持现有现场节拍。
        /// </summary>
        public int FetchReceiveTimeoutMs { get; set; } = 200;

        /// <summary>
        /// Fetch 指令最大尝试次数（含首次发送）。
        /// 该值来自耐压仪独立配置文件，决定单次读取的完整重试窗口；默认值 5 覆盖现场短时响应波动。
        /// </summary>
        public int FetchMaxAttempts { get; set; } = 5;

        /// <summary>
        /// Fetch 指令失败后到下一次重新发送前的间隔（毫秒）。
        /// 该值来自耐压仪独立配置文件，用于控制同一读数窗口内的重试节拍；默认值 100 保持现有现场节拍。
        /// </summary>
        public int FetchRetryDelayMs { get; set; } = 100;

        /// <summary>
        /// 参数回读与启动前步骤查询的最大尝试次数（含首次发送）。
        /// 该值来自耐压仪独立配置文件；默认值 5，用于手动参数下发回读和测试启动前工艺确认。
        /// 标准产线自动参数下发由工位统一组织五轮，每轮只使用一次回读；启动命令始终只发送一次。
        /// </summary>
        public int SetupQueryMaxAttempts { get; set; } = 5;

        /// <summary>
        /// 参数回读与启动前步骤查询的基础重试间隔，单位毫秒。
        /// 后续等待按尝试序号递增并限制在 2000ms 内，为仪器步骤切换、参数应用和 TCP 重连预留时间。
        /// </summary>
        public int SetupQueryRetryDelayMs { get; set; } = 200;

        /// <summary>
        /// WP 参数发送成功后首次执行 rp? 回读前的等待时间，单位毫秒。
        /// 默认值 300，只影响参数同步节拍，不改变耐压上升、保持和下降时间。
        /// </summary>
        public int ParameterApplyDelayMs { get; set; } = 300;

        /// <summary>
        /// 其它耐压命令单次接收超时（毫秒）。
        /// 适用于 rp?、FUNC:SOUR:STEP? 这类单次等待更长的命令；默认值 3000 保持现有现场节拍。
        /// </summary>
        public int OtherCommandReceiveTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 每次发送后、Receive 前的等待（毫秒）。
        /// 该值用于控制发包后的最小节拍，默认值 100 保持现有现场节拍。
        /// </summary>
        public int PostSendDelayMs { get; set; } = 100;

        /// <summary>最近一次 Fetch? 返回的原始字符串（供诊断；失败时可能为空）。</summary>
        [XmlIgnore]
        public string LastFetchRaw { get; private set; } = string.Empty;

        /// <summary>测试进行中拒绝参数下发时返回给上层的固定说明。</summary>
        public const string ErrorDownloadBlockedByTest = "仪器正在测试，已拒绝参数下发";

        /// <summary>参数下发进行中拒绝启动测试时返回给上层的固定说明。</summary>
        public const string ErrorStartBlockedByDownload = "参数正在下发，已拒绝启动测试";

        /// <summary>测试进行中拒绝重复启动时返回给上层的固定说明。</summary>
        public const string ErrorStartBlockedByTest = "仪器正在测试，已拒绝重复启动";

        /// <summary>参数下发进行中拒绝重复下发时返回给上层的固定说明。</summary>
        public const string ErrorDownloadBlockedByDownload = "参数正在下发，已拒绝重复下发";

        /// <summary>
        /// Fetch 轮询中，状态字段连续无法识别时结束测试所需的次数。
        /// 代码内固定阈值，不写入耐压仪通信参数 XML。
        /// 达到该次数后按通信异常结束：Error 写通信说明，不把乱码原文当作仪器判定结论；
        /// 未达次数前继续轮询，过程态与已知结束态仍立即采信。
        /// </summary>
        public const int UnknownStatusConfirmCount = 3;

        /// <summary>
        /// AT9620 测试过程中的阶段状态白名单。
        /// 仅这三类表示耐压仍在执行；命中后继续轮询，并清零状态可疑连续计数。
        /// </summary>
        private static readonly HashSet<string> KnownProcessStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Ramp Up",
            "Dwell",
            "Ramp Down"
        };

        /// <summary>
        /// AT9620 已知结束状态白名单（与上位机 TvStatusTranslator 映射表口径一致）。
        /// 命中后立即结束测试：PASS 记合格，其余记仪器不合格原因；不参与状态可疑连续确认。
        /// </summary>
        private static readonly HashSet<string> KnownTerminalStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PASS",
            "SHORT",
            "ARC",
            "GFI",
            "BREAKDOWN",
            "ERROR",
            "OV",
            "UPPER",
            "LOWER",
            "RISELOW"
        };

        private enum InstrumentSessionState
        {
            Idle,
            Downloading,
            Testing
        }

        private readonly object _sessionLock = new object();
        private InstrumentSessionState _sessionState = InstrumentSessionState.Idle;

        /// <summary>
        /// 本机仪器是否处于参数下发或测试会话中；供 PLC 入口判断是否忽略重复触发。
        /// </summary>
        [XmlIgnore]
        public bool IsSessionActive
        {
            get
            {
                lock (_sessionLock)
                {
                    return _sessionState != InstrumentSessionState.Idle;
                }
            }
        }

        private static string GetOperationName(InstrumentSessionState state)
        {
            switch (state)
            {
                case InstrumentSessionState.Downloading:
                    return "Download";
                case InstrumentSessionState.Testing:
                    return "Start";
                default:
                    return "Idle";
            }
        }

        /// <summary>
        /// 尝试占用当前耐压仪会话。参数下发和启动测试共用同一个 TCP 通道，
        /// 忙碌请求立即返回固定错误，上层据此按 PLC 重复触发处理，现场通信保持单一指令链。
        /// </summary>
        /// <param name="requestedState">请求进入的会话类型，来自参数下发或启动测试入口。</param>
        /// <param name="requestedOperation">写入诊断日志的操作名称。</param>
        /// <param name="error">会话被占用时返回给上层的固定说明；允许进入时为空。</param>
        /// <returns>成功占用通信会话时返回 true；同台仪器已有会话时返回 false。</returns>
        private bool TryBeginSession(InstrumentSessionState requestedState, string requestedOperation, out string error)
        {
            string activeOperation;
            lock (_sessionLock)
            {
                if (_sessionState == InstrumentSessionState.Idle)
                {
                    _sessionState = requestedState;
                    error = string.Empty;
                    return true;
                }

                error = GetSessionBlockedReason(requestedState, _sessionState);
                activeOperation = GetOperationName(_sessionState);
            }

            LogSessionBlocked(requestedOperation, error, activeOperation);
            return false;
        }

        /// <summary>
        /// 将会话占用状态转换为上层可识别的固定忙碌说明，便于 UI 日志、PLC 入口和诊断文件使用同一口径。
        /// </summary>
        /// <param name="requestedState">本次请求进入的参数下发或测试会话。</param>
        /// <param name="activeState">当前已经占用同台仪器的会话。</param>
        /// <returns>用于 Result.Error 的固定中文说明。</returns>
        private static string GetSessionBlockedReason(InstrumentSessionState requestedState, InstrumentSessionState activeState)
        {
            if (requestedState == InstrumentSessionState.Downloading && activeState == InstrumentSessionState.Testing)
            {
                return ErrorDownloadBlockedByTest;
            }

            if (requestedState == InstrumentSessionState.Downloading && activeState == InstrumentSessionState.Downloading)
            {
                return ErrorDownloadBlockedByDownload;
            }

            if (requestedState == InstrumentSessionState.Testing && activeState == InstrumentSessionState.Downloading)
            {
                return ErrorStartBlockedByDownload;
            }

            if (requestedState == InstrumentSessionState.Testing && activeState == InstrumentSessionState.Testing)
            {
                return ErrorStartBlockedByTest;
            }

            return "仪器通信会话忙碌，已拒绝本次请求";
        }

        /// <summary>
        /// 记录同台耐压仪会话占用拦截信息，供现场日志确认重复触发来源、当前会话和目标仪器地址。
        /// </summary>
        /// <param name="requestedOperation">被拦截的上层请求，例如 Download 或 Start。</param>
        /// <param name="reason">返回给业务流程的固定忙碌说明。</param>
        /// <param name="activeOperation">当前占用该仪器通信链路的操作名称。</param>
        private void LogSessionBlocked(string requestedOperation, string reason, string activeOperation)
        {
            string line =
                $"【会话拦截】请求操作={requestedOperation}，拦截原因={reason}，当前操作={activeOperation}，仪器IP={IP}，端口={Port}";
            WriteLog(line);
            string stamped = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{line}";
            DiagnosticLog?.Invoke(stamped);
            CommunicationLog?.Invoke(stamped);
        }

        /// <summary>
        /// 释放当前耐压仪会话占用。仅释放调用方持有的会话类型，保留异常路径下的状态一致性。
        /// </summary>
        /// <param name="expectedState">调用方进入会话时登记的状态。</param>
        private void EndSession(InstrumentSessionState expectedState)
        {
            lock (_sessionLock)
            {
                if (_sessionState == expectedState)
                {
                    _sessionState = InstrumentSessionState.Idle;
                }
            }
        }

        /// <summary>
        /// 关闭当前 TCP 连接并更新连接标志。该方法供 Start/Download 结束路径复用，保证异常结束后通信通道回到可重连状态。
        /// </summary>
        /// <returns>连接已经处于断开状态时返回 true。</returns>
        private bool DisconnectCore()
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
            catch (Exception)
            {
                isconnected = false;
            }

            return isconnected;
        }

        /// <summary>
        /// 手动断开空闲状态下的耐压仪 TCP 连接。正在参数下发或测试的会话由各自结束路径关闭连接。
        /// </summary>
        /// <returns>空闲连接断开成功时返回 true；会话占用中返回 false。</returns>
        public bool disconnect()
        {
            lock (_sessionLock)
            {
                if (_sessionState != InstrumentSessionState.Idle)
                {
                    return false;
                }
            }

            return DisconnectCore();
        }

        /// <summary>
        /// 软件退出时中止当前耐压会话并关闭 TCP 连接。
        /// 先置位 stop，等待测试线程在连接仍存活时发送 FUNCtion:STOP；超时后若会话仍占用且 TCP 仍在，再补发一次 STOP，最后关闭连接。
        /// 正常测试完成仍沿用会话自身的收尾路径。
        /// </summary>
        public void Shutdown()
        {
            stop = true;

            int deadline = Environment.TickCount + 3000;
            while (IsSessionActive && unchecked(Environment.TickCount - deadline) < 0)
            {
                Thread.Sleep(50);
            }

            if (IsSessionActive && isconnected)
            {
                try
                {
                    Send("FUNCtion:STOP\n");
                }
                catch
                {
                }
            }

            DisconnectCore();
        }

        /// <summary>
        /// 启动当前 AT9620 测试并读取结果。入口负责独占测试会话，参数、Fetch 轮询、结果解析沿用既有测试流程。
        /// </summary>
        /// <returns>测试执行结果；会话忙碌或连接失败时返回失败说明。</returns>
        public Result Start()
        {
            string error;
            if (!TryBeginSession(InstrumentSessionState.Testing, "Start", out error))
            {
                return new Result { Error = error };
            }

            try
            {
                var c = connect(IP, Port);
                if (!c)
                {
                    return new Result { Error = "连接失败" };
                }

                return _Start();
            }
            finally
            {
                DisconnectCore();
                EndSession(InstrumentSessionState.Testing);
            }
        }

        /// <summary>
        /// 下发当前 AT9620 工艺参数并按通信配置回读校验。
        /// 手动下发和兼容调用使用 <see cref="SetupQueryMaxAttempts"/> 次回读机会；生产自动流程可通过带参数重载，
        /// 将回读次数纳入工位统一重试预算。入口负责独占参数下发会话，不启动耐压测试。
        /// </summary>
        /// <returns>参数下发和回读比对结果；会话忙碌或连接失败时返回失败说明。</returns>
        public Result Download()
        {
            return Download(SetupQueryMaxAttempts, OtherCommandReceiveTimeoutMs);
        }

        /// <summary>
        /// 下发当前 AT9620 工艺参数，并按调用方提供的回读预算完成本轮校验。
        /// 标准产线每轮传入一次回读，使五轮完整下发最多产生五次参数校验；手动下发继续使用无参数入口的配置次数。
        /// 该入口只约束本轮参数同步，不改变启动命令、测试时间、PLC完成状态和产品判定。
        /// </summary>
        /// <param name="parameterVerifyMaxAttempts">本轮参数写入后的回读校验次数，含首次查询；标准产线传入1。</param>
        /// <param name="parameterVerifyReceiveTimeoutMs">本轮单次参数回读接收超时，单位毫秒；用于服从工位总等待上限。</param>
        /// <returns>参数下发及任一次回读比对成功时返回成功；会话占用、连接、写入或校验失败时返回原因。</returns>
        public Result Download(int parameterVerifyMaxAttempts, int parameterVerifyReceiveTimeoutMs)
        {
            string error;
            if (!TryBeginSession(InstrumentSessionState.Downloading, "Download", out error))
            {
                return new Result { Error = error };
            }

            try
            {
                var c = connect(IP, Port);
                if (!c)
                {
                    return new Result { Error = "连接失败" };
                }

                return _Download(parameterVerifyMaxAttempts, parameterVerifyReceiveTimeoutMs);
            }
            finally
            {
                DisconnectCore();
                EndSession(InstrumentSessionState.Downloading);
            }
        }

        /// <summary>
        /// 已连接状态下按通信配置执行参数写入和回读校验，供历史调用方保持原有入口。
        /// </summary>
        /// <returns>参数写入及回读一致时返回成功；写入或校验失败时返回原因。</returns>
        public Result _Download()
        {
            return _Download(SetupQueryMaxAttempts, OtherCommandReceiveTimeoutMs);
        }

        /// <summary>
        /// 已连接状态下执行一次参数同步流程。
        /// 调用方决定本轮回读次数和接收超时，使生产工位可以在五轮完整下发与20秒等待上限内共享通信预算。
        /// </summary>
        /// <param name="parameterVerifyMaxAttempts">本轮参数写入后的回读校验次数，含首次查询。</param>
        /// <param name="parameterVerifyReceiveTimeoutMs">本轮单次参数回读接收超时，单位毫秒。</param>
        /// <returns>参数写入及任一次回读一致时返回成功；写入或校验失败时返回原因。</returns>
        private Result _Download(int parameterVerifyMaxAttempts, int parameterVerifyReceiveTimeoutMs)
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
            Thread.Sleep(Math.Max(0, ParameterApplyDelayMs));

            WriteLog("[参数下发] 开始回读参数验证...");
            Result verifyResult = VerifyCurrentParametersWithRetry(
                message => WriteLog($"[参数下发] {message}"),
                retryTransportFailures: false,
                reconnectBetweenAttempts: false,
                maxAttempts: parameterVerifyMaxAttempts,
                receiveTimeoutMs: parameterVerifyReceiveTimeoutMs);
            if (!verifyResult.Success)
            {
                WriteLog($"[参数下发] ❌ 参数验证失败: {verifyResult.Error}");
                r.Error = verifyResult.Error;
                return r;
            }
            WriteLog($"[参数下发] ✓ 参数验证成功，所有参数一致");
            WriteLog($"[参数下发] ========== 参数下发流程完成 ==========");

            r.Success = true;


            return r;


        }

        /// <summary>
        /// 按通信配置复核仪器当前步骤参数，任一次回读与当前工艺一致即完成确认。
        /// 参数下载路径在收到旧值时按递增间隔继续读取，Socket 失败交给外层完整下载建立新连接；
        /// 启动前确认路径在每次失败后重连，清除延迟应答。该方法只执行 rp? 查询和本地参数比较。
        /// </summary>
        /// <param name="trace">接收每次回读、参数不一致和重连结果的诊断输出；允许为空。</param>
        /// <param name="retryTransportFailures">通信读取失败后继续本层查询时为 true；交给外层完整下载重连时为 false。</param>
        /// <param name="reconnectBetweenAttempts">后续查询前重新建立 TCP 连接时为 true；沿用当前连接复核参数生效状态时为 false。</param>
        /// <param name="maxAttempts">本次确认允许的回读总次数，含首次查询。</param>
        /// <param name="receiveTimeoutMs">单次回读接收超时，单位毫秒；生产自动流程可传入剩余时间预算。</param>
        /// <returns>参数一致时返回成功及末次回读原文；尝试用尽时返回末次通信或参数差异说明。</returns>
        private Result VerifyCurrentParametersWithRetry(
            Action<string> trace,
            bool retryTransportFailures,
            bool reconnectBetweenAttempts,
            int maxAttempts,
            int receiveTimeoutMs)
        {
            int attempts = Math.Max(1, maxAttempts);
            int configuredReceiveTimeoutMs = Math.Max(1, OtherCommandReceiveTimeoutMs);
            int effectiveReceiveTimeoutMs = receiveTimeoutMs > 0
                ? Math.Min(configuredReceiveTimeoutMs, receiveTimeoutMs)
                : configuredReceiveTimeoutMs;
            string lastError = "参数回读未返回结果";

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (attempt > 1 && reconnectBetweenAttempts
                    && !ReconnectForSetupQuery("参数回读", attempt, attempts, trace))
                {
                    lastError = $"参数回读第{attempt}/{attempts}次重连失败";
                    continue;
                }
                if (attempt > 1 && !reconnectBetweenAttempts)
                {
                    int retryDelayMs = GetSetupQueryRetryDelayMs(attempt - 1);
                    trace?.Invoke($"第{attempt}/{attempts}次回读将在{retryDelayMs}ms后执行");
                    Thread.Sleep(retryDelayMs);
                }

                Result readResult = SendAndReceiveOnce("rp? 1\n", effectiveReceiveTimeoutMs);
                if (!readResult.Success)
                {
                    lastError = readResult.Error ?? "参数回读失败";
                    trace?.Invoke($"第{attempt}/{attempts}次回读失败: {lastError}");
                    if (!retryTransportFailures)
                    {
                        return new Result { Error = lastError };
                    }
                    continue;
                }

                trace?.Invoke($"第{attempt}/{attempts}次回读成功: {readResult.Value}");
                Result compareResult = TVParameter.compare(readResult.Value);
                if (compareResult.Success)
                {
                    return new Result { Success = true, Value = readResult.Value };
                }

                lastError = compareResult.Error ?? "回读参数与当前工艺不一致";
                trace?.Invoke($"第{attempt}/{attempts}次回读参数暂未生效: {lastError}");
            }

            return new Result
            {
                Error = $"测试工艺参数不一致或回读失败，已完成{attempts}次复核\r\n{lastError}"
            };
        }

        /// <summary>
        /// 为参数回读或启动前步骤查询重新建立 TCP 连接。
        /// 先关闭旧连接，再按当前尝试序号递增等待，可清除超时连接中的延迟应答并给仪器保留恢复时间。
        /// </summary>
        /// <param name="queryName">查询名称，用于通信日志区分参数回读和步骤数量确认。</param>
        /// <param name="attempt">即将执行的尝试序号，从 2 开始。</param>
        /// <param name="attempts">本轮允许的总尝试次数。</param>
        /// <param name="trace">接收重连等待和结果的诊断输出；允许为空。</param>
        /// <returns>新 TCP 连接建立成功时返回 true。</returns>
        private bool ReconnectForSetupQuery(string queryName, int attempt, int attempts, Action<string> trace)
        {
            DisconnectCore();
            int retryDelayMs = GetSetupQueryRetryDelayMs(attempt - 1);
            trace?.Invoke($"{queryName}第{attempt}/{attempts}次将在{retryDelayMs}ms后重连");
            Thread.Sleep(retryDelayMs);

            bool connected = connect(IP, Port);
            if (!connected)
            {
                trace?.Invoke($"{queryName}第{attempt}/{attempts}次重连失败");
            }
            return connected;
        }

        /// <summary>
        /// 计算参数与步骤查询的递增重试间隔。
        /// </summary>
        /// <param name="completedAttempt">刚完成的失败尝试序号，从 1 开始。</param>
        /// <returns>下一次查询前的等待时间，单位毫秒，最大 2000ms。</returns>
        private int GetSetupQueryRetryDelayMs(int completedAttempt)
        {
            int exponent = Math.Max(0, Math.Min(completedAttempt - 1, 10));
            long delayMs = (long)Math.Max(0, SetupQueryRetryDelayMs) * (1L << exponent);
            return (int)Math.Min(delayMs, 2000L);
        }

        /// <summary>
        /// 执行耐压测试：向 AT9620 发启动命令，轮询 Fetch 过程数据，并按状态口径给出本机测试结论。
        ///
        /// 业务场景：
        /// - 上层工位流程在参数下发成功后调用 Start/_Start，等待本方法返回后再做 PLC 回写与数据库落库。
        ///
        /// 核心口径：
        /// - 过程态（Ramp Up / Dwell / Ramp Down）继续轮询。
        /// - 已知结束态（PASS / SHORT / ARC 等）立即结束；PASS 为合格，其余为仪器不合格原因。
        /// - 状态字段可解析但不在上述白名单时，按不可信状态处理：连续达到
        ///   <see cref="UnknownStatusConfirmCount"/> 次后以通信异常结束，避免偶发乱码被写成仪器 NG。
        /// - 超时仍按理论总时长 +2s 兜底。
        /// - 通信异常与超时属上位机单方面收工：退出前先发 FUNCtion:STOP，避免仅断 TCP 后仪器仍加压、
        ///   而 PLC 已按流程完成推进下一件。已知结束态由仪器自行收束，不再重复 STOP。
        /// - 本方法不写 PLC、不落库。
        /// </summary>
        /// <returns>
        /// 本机测试结论：Success 表示仪器判定 PASS；
        /// Error 为仪器不合格原因、通信异常说明或超时说明；Recordstr 为过程原始串。
        /// </returns>
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
            var r2 = VerifyCurrentParametersWithRetry(
                message => Diag($"【启动前参数确认】{message}"),
                retryTransportFailures: true,
                reconnectBetweenAttempts: true,
                maxAttempts: SetupQueryMaxAttempts,
                receiveTimeoutMs: OtherCommandReceiveTimeoutMs);
            if (!r2.Success)
            {
                Diag($"【启动中止】回读工艺参数失败：{r2.Error}");
                r.Error = r2.Error;
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

            // 用于「阶段变化时记录仪器原文」：与上一次成功解析的可信阶段比较
            string lastStatusForRawCapture = null;
            // 最近一次成功解析的可信阶段（过程态或已知结束态；用于超时/异常时的说明）
            string lastSuccessParsedStatus = null;
            int successPollCount = 0;
            // 连续收到“格式可解析但状态字段不可信”的次数；过程态/已知结束态/采样失败会清零
            int unknownStatusStreak = 0;
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

            void RecordTrustedSample(string st, float v, float cur, float instrT, double elapsedPc)
            {
                // 仅记录过程态/已知结束态采样，供阶段变化诊断与超时说明；未知状态不进入此路径
                lastSuccessParsedStatus = st;
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
            }

            // 上位机异常收工前先停仪：仪器可能仍在 Dwell 加高压，仅断 TCP 不会停止输出；
            // PLC 随后会按本方法返回推进下一件，必须先发 STOP 再退出监控。
            void StopInstrumentBeforeAbnormalExit(string exitReason)
            {
                var stopResult = Send("FUNCtion:STOP\n");
                if (stopResult.Success)
                {
                    Diag($"【异常收工停仪】原因={exitReason}，已发送 FUNCtion:STOP");
                }
                else
                {
                    Diag($"【异常收工停仪】原因={exitReason}，FUNCtion:STOP 发送失败：{stopResult.Error ?? "未知"}");
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
                        double elapsedPc = (DateTime.Now - dt).TotalSeconds;
                        var v = resultTVProcess.Value.Voltage;
                        var cur = resultTVProcess.Value.Current;
                        var instrT = resultTVProcess.Value.Time;

                        // 触发数据接收事件，通知上层应用更新界面显示
                        DataReceived.Invoke(this, new AT9620EventArgs()
                        {
                            ResultTVProcess = resultTVProcess
                        });

                        // 状态判定业务口径（影响本方法返回的 Success/Error，不影响 PLC/落库）：
                        // 1) 过程态——耐压仍在执行，清零可疑计数后继续轮询
                        // 2) 已知结束态——立即结束；PASS 合格，其余按仪器原因 NG
                        // 3) 未知状态——状态字段不可信（现场常见于干扰乱码如 Dwgll、?well）：
                        //    不立刻结束；连续 UnknownStatusConfirmCount 次仍不可信时按通信异常结束；
                        //    超时兜底仍由下方 totaltime+2s 负责
                        if (IsProcessStatus(st))
                        {
                            unknownStatusStreak = 0;
                            RecordTrustedSample(st, v, cur, instrT, elapsedPc);
                        }
                        else if (IsKnownTerminalStatus(st))
                        {
                            unknownStatusStreak = 0;
                            RecordTrustedSample(st, v, cur, instrT, elapsedPc);

                            if (string.Equals(st, "PASS", StringComparison.OrdinalIgnoreCase))
                            {
                                r.Success = true;
                                Diag("【结束】仪器返回结束状态 PASS");
                            }
                            else
                            {
                                r.Error = st;
                                Diag($"【结束】仪器返回结束状态：{st}");
                            }
                            TryDiagLastSample();
                            break;
                        }
                        else
                        {
                            unknownStatusStreak++;
                            string rawPart = string.IsNullOrEmpty(LastFetchRaw) ? "(空)" : LastFetchRaw;
                            string phaseRef = lastSuccessParsedStatus ?? "尚无成功解析";
                            Diag($"【状态可疑-忽略】原文状态={st}，连续可疑次数={unknownStatusStreak}/{UnknownStatusConfirmCount}，同一测试内参考阶段={phaseRef}，仪器原文={rawPart}，PC已耗时={elapsedPc:F2}s");

                            if (unknownStatusStreak >= UnknownStatusConfirmCount)
                            {
                                r.Error = $"通信异常：状态字段连续{UnknownStatusConfirmCount}次无法识别（末次={st}）";
                                Diag($"【结束】{r.Error}");
                                TryDiagLastSample();
                                // 通信异常时仪器未必已出结束态，可能仍在加压；先 STOP 再断链，避免 PLC 收工后高压残留
                                StopInstrumentBeforeAbnormalExit(r.Error);
                                break;
                            }
                        }
                    }
                    else
                    {
                        // 采样失败（超时/空包等）与“状态字段可疑”分属不同路径：中断连续可疑计数，避免与空包混算
                        unknownStatusStreak = 0;
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
                        // 与通信异常同属上位机单方面收工：仪器可能仍在输出，先 STOP 再退出
                        StopInstrumentBeforeAbnormalExit(r.Error);
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
        /// <c>rp?</c>、<c>FUNC:SOUR:STEP?</c> 等其它命令：本方法执行单轮收发并使用
        /// <see cref="OtherCommandReceiveTimeoutMs"/>；参数下载和启动前确认入口按
        /// <see cref="SetupQueryMaxAttempts"/> 组织重连与复核，避免测试启动命令被重复发送。
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
            int attempts = Math.Max(1, SetupQueryMaxAttempts);
            string lastError = "步骤数量查询未返回结果";

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (attempt > 1 && !ReconnectForSetupQuery("步骤数量查询", attempt, attempts, message => Diag($"【启动前步骤确认】{message}")))
                {
                    lastError = $"步骤数量查询第{attempt}/{attempts}次重连失败";
                    continue;
                }

                var re = Get_String("FUNC:SOUR:STEP?\n");
                if (!re.Success)
                {
                    lastError = re.Error ?? "步骤数量查询失败";
                    Diag($"【启动前步骤确认】第{attempt}/{attempts}次查询失败：{lastError}");
                    continue;
                }

                try
                {
                    r.Value = Convert.ToInt32(re.Value.Split('-')[1].Replace("TOTAL", "").Trim());
                    r.Success = true;
                    Diag($"【启动前步骤确认】第{attempt}/{attempts}次查询成功：步骤数={r.Value}");
                    return r;
                }
                catch (Exception ex)
                {
                    lastError = $"接收内容:{re.Value}\r\n {ex}";
                    Diag($"【启动前步骤确认】第{attempt}/{attempts}次格式异常：{ex.Message}");
                }
            }

            r.Error = $"步骤数量查询已完成{attempts}次尝试仍失败\r\n{lastError}";
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

        /// <summary>
        /// 判断 Fetch 返回的状态是否为测试过程阶段。
        /// 过程阶段表示耐压仍在执行，监控循环继续轮询，不进入结束判定。
        /// </summary>
        /// <param name="status">
        /// 仪器 Fetch 应答第 3 段状态原文；来自 GetProcess 解析结果，本方法不修改该值。
        /// </param>
        /// <returns>属于 Ramp Up / Dwell / Ramp Down 时返回 true，表示测试未结束。</returns>
        private static bool IsProcessStatus(string status)
        {
            return !string.IsNullOrEmpty(status) && KnownProcessStatuses.Contains(status);
        }

        /// <summary>
        /// 判断 Fetch 返回的状态是否为仪器已知结束结论。
        /// 已知结束结论可立即采信写入本方法返回值；不进入状态可疑连续确认。
        /// </summary>
        /// <param name="status">
        /// 仪器 Fetch 应答第 3 段状态原文；与 TvStatusTranslator 已知码表对齐，比较时忽略大小写。
        /// </param>
        /// <returns>属于 PASS / SHORT / ARC 等已知结束码时返回 true，表示可立即结束测试。</returns>
        private static bool IsKnownTerminalStatus(string status)
        {
            return !string.IsNullOrEmpty(status) && KnownTerminalStatuses.Contains(status);
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
