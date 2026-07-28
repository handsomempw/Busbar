using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpServerHelper
{
    public class TCPServerH
    {
        private const int ReceiveBufferSize = 1024;
        private const int PendingMessageLimit = 64;

        public bool listiening { set; get; } = false;

        public delegate void EventHandling(TCPServerH sender, object e);
        public event EventHandling MessageReceived;
        public event EventHandling ClientDisconnected;
        public event EventHandling ClientConnected;

        private readonly object clientLock = new object();
        private readonly object logLock = new object();
        private TcpListener _tcpServer = null;
        private NetworkStream _stream = null;
        private TcpClient _tcpClient = null;
        private int receiveLoopRunning = 0;
        private int reconnectCount = 0;
        private string pendingCheckMessage = string.Empty;
        private volatile bool stopRequested = false;

        public string ReceiveMSG { set; get; } = string.Empty;

        public string GetMsg()
        {
            string _s = ReceiveMSG;
            return _s;
        }

        private volatile bool isConnected = false;

        /// <summary>
        /// 启动机器人 TCP 服务端监听，等待机器人控制器作为客户端接入。
        /// 该入口建立设备联动通道，连接、收包、发包和线程状态会写入独立机器人通信日志，便于现场追溯 CHECK 指令是否到达上位机底层。
        /// </summary>
        /// <param name="IPAddress_str">上位机本地监听 IP，来自设备通信配置。</param>
        /// <param name="Port">上位机本地监听端口，来自设备通信配置。</param>
        /// <returns>监听启动成功或已在监听时返回 true。</returns>
        public bool StartListener(string IPAddress_str, int Port)
        {
            if (listiening)
            {
                WriteRobotTcpLog("[连接] TCP服务端已在监听");
                EnsureReceiveLoop();
                return true;
            }

            stopRequested = false;
            IPAddress ipAddress = IPAddress.Parse(IPAddress_str);
            _tcpServer = new TcpListener(ipAddress, Port);
            _tcpServer.Start();
            listiening = true;
            WriteRobotTcpLog($"[连接] 服务启动 Local={IPAddress_str}:{Port}");

            AcceptClient("客户端连接");
            EnsureReceiveLoop();
            Task.Run(CheckIsConntect);
            return true;
        }

        /// <summary>
        /// 兼容外部调用的接收入口。
        /// 接收循环在服务端内部按单实例运行，防止同一连接被多个线程同时读取造成 CHECK 指令丢失或日志错乱。
        /// </summary>
        public void ReceiveMsg()
        {
            EnsureReceiveLoop();
        }

        /// <summary>
        /// 发送上位机判定结果到机器人控制器，并在独立通信日志记录发送成败。
        /// 该方法服务 CHECK1/CHECK2 的 OK、NG1、NG2、NG3、NG4 回包，发送失败时会触发掉线事件供上层业务告警。
        /// </summary>
        /// <param name="msg">发送给机器人控制器的业务回包。</param>
        public void SendMessage(string msg)
        {
            NetworkStream stream;
            string remote;

            lock (clientLock)
            {
                stream = _stream;
                remote = GetRemoteDescription();
            }

            if (!isConnected || stream == null)
            {
                WriteRobotTcpLog($"[发送] Msg={EscapeVisible(msg)} Result=失败 Reason=客户端掉线 Remote={remote}");
                RaiseClientDisconnected("发送失败，客户端掉线");
                return;
            }

            try
            {
                byte[] reply = Encoding.ASCII.GetBytes(msg);
                stream.Write(reply, 0, reply.Length);
                WriteRobotTcpLog($"[发送] Msg={EscapeVisible(msg)} Bytes={reply.Length} Result=成功 Remote={remote}");
            }
            catch (Exception ex)
            {
                isConnected = false;
                WriteRobotTcpLog($"[发送] Msg={EscapeVisible(msg)} Result=失败 Remote={remote} Reason={ex.Message}");
                RaiseClientDisconnected("发送失败，客户端掉线");
                throw;
            }
        }

        /// <summary>
        /// 等待机器人断线后的重新接入，并在重连成功时按需恢复接收循环。
        /// 该线程只负责连接恢复，业务 CHECK 指令仍由 MessageReceived 事件交给上位机原有流程处理。
        /// </summary>
        public void CheckIsConntect()
        {
            while (!stopRequested)
            {
                if (!isConnected && _tcpServer != null)
                {
                    try
                    {
                        CloseCurrentClient();
                        AcceptClient("客户端重新连接");
                        EnsureReceiveLoop();
                    }
                    catch (Exception ex)
                    {
                        WriteRobotTcpLog($"[连接] 等待机器人重连异常 Reason={ex.Message}");
                        Thread.Sleep(1000);
                    }
                }

                Thread.Sleep(1000);
            }
        }

        private void EnsureReceiveLoop()
        {
            if (Interlocked.CompareExchange(ref receiveLoopRunning, 1, 0) != 0)
            {
                WriteRobotTcpLog("[线程] 接收线程已在运行");
                return;
            }

            Task.Run(ReceiveMsgCore);
        }

        private void ReceiveMsgCore()
        {
            WriteRobotTcpLog("[线程] 接收线程启动");
            try
            {
                while (!stopRequested)
                {
                    if (!isConnected)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    NetworkStream stream;
                    TcpClient client;
                    string remote;

                    lock (clientLock)
                    {
                        client = _tcpClient;
                        stream = _stream;
                        remote = GetRemoteDescription();
                    }

                    if (client == null || stream == null)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    try
                    {
                        byte[] data = new byte[ReceiveBufferSize];
                        int bytesRead = stream.Read(data, 0, data.Length);
                        if (bytesRead <= 0)
                        {
                            HandleClientDisconnected("客户端掉线", remote, "远端关闭连接");
                            break;
                        }

                        string message = Encoding.ASCII.GetString(data, 0, bytesRead);
                        Debug.WriteLine(client.Client?.Connected);
                        TraceReceivedMessage(data, bytesRead, message, remote);
                        DispatchReceivedMessage(message);
                    }
                    catch (Exception ex)
                    {
                        HandleClientDisconnected("客户端掉线", remote, ex.Message);
                        break;
                    }

                    Thread.Sleep(10);
                }
            }
            finally
            {
                Interlocked.Exchange(ref receiveLoopRunning, 0);
                WriteRobotTcpLog("[线程] 接收线程退出");
                if (isConnected && !stopRequested)
                {
                    WriteRobotTcpLog("[线程] 接收线程退出时已有机器人连接，自动恢复接收线程");
                    EnsureReceiveLoop();
                }
            }
        }

        private void AcceptClient(string eventMessage)
        {
            TcpClient client = _tcpServer.AcceptTcpClient();
            string remote;

            lock (clientLock)
            {
                _tcpClient = client;
                _stream = client.GetStream();
                isConnected = true;
                remote = GetRemoteDescription();
            }

            if (eventMessage == "客户端重新连接")
            {
                reconnectCount++;
                WriteRobotTcpLog($"[连接] 机器人重连 Remote={remote} ReconnectCount={reconnectCount}");
            }
            else
            {
                WriteRobotTcpLog($"[连接] 机器人连接 Remote={remote}");
            }

            RaiseClientConnected(eventMessage);
        }

        private void HandleClientDisconnected(string eventMessage, string remote, string reason)
        {
            isConnected = false;
            WriteRobotTcpLog($"[连接] 机器人断开 Remote={remote} Reason={reason}");
            RaiseClientDisconnected(eventMessage);
        }

        private void DispatchReceivedMessage(string rawMessage)
        {
            if (string.IsNullOrEmpty(rawMessage))
            {
                return;
            }

            if (ContainsFrameTerminator(rawMessage))
            {
                string[] frames = rawMessage.Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string frame in frames)
                {
                    DispatchNormalizedMessage(frame.Trim());
                }
                return;
            }

            DispatchPossiblyFragmentedMessage(rawMessage.Trim());
        }

        private void DispatchPossiblyFragmentedMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (string.IsNullOrEmpty(pendingCheckMessage))
            {
                if (IsCheckFragment(message))
                {
                    pendingCheckMessage = message;
                    WriteRobotTcpLog($"[接收] Result=疑似半包 Pending='{EscapeVisible(pendingCheckMessage)}'");
                    return;
                }

                DispatchNormalizedMessage(message);
                return;
            }

            string combined = pendingCheckMessage + message;
            if (IsExactCheckCommand(combined))
            {
                WriteRobotTcpLog($"[接收] Result=半包合并完成 Pending='{EscapeVisible(pendingCheckMessage)}' Incoming='{EscapeVisible(message)}' Command={combined}");
                pendingCheckMessage = string.Empty;
                DispatchNormalizedMessage(combined);
                return;
            }

            if (IsCheckFragment(combined) && combined.Length <= PendingMessageLimit)
            {
                pendingCheckMessage = combined;
                WriteRobotTcpLog($"[接收] Result=疑似半包 Pending='{EscapeVisible(pendingCheckMessage)}'");
                return;
            }

            WriteRobotTcpLog($"[接收] Result=半包合并失败 Pending='{EscapeVisible(pendingCheckMessage)}' Incoming='{EscapeVisible(message)}'");
            pendingCheckMessage = string.Empty;
            DispatchNormalizedMessage(message);
        }

        private void DispatchNormalizedMessage(string message)
        {
            string normalized = NormalizeMessage(message);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            ReceiveMSG = normalized;
            RaiseMessageReceived(normalized);
        }

        private void TraceReceivedMessage(byte[] data, int bytesRead, string rawMessage, string remote)
        {
            string trimmed = NormalizeMessage(rawMessage);
            string hex = BitConverter.ToString(data, 0, bytesRead);
            string result = AnalyzeReceiveResult(rawMessage, trimmed);
            WriteRobotTcpLog($"[接收] Bytes={bytesRead} Raw='{EscapeVisible(rawMessage)}' Hex={hex} Trim='{EscapeVisible(trimmed)}' Result={result} Remote={remote}");
        }

        private static string AnalyzeReceiveResult(string rawMessage, string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed))
            {
                return "空包";
            }

            if (IsExactCheckCommand(trimmed) || trimmed.StartsWith("A"))
            {
                return rawMessage == trimmed ? "完整命令" : "完整命令-带结束符或空白";
            }

            int check1Count = CountToken(trimmed, "CHECK1");
            int check2Count = CountToken(trimmed, "CHECK2");
            if (check1Count + check2Count > 1)
            {
                return "疑似粘包";
            }

            if (check1Count == 1 || check2Count == 1)
            {
                return "疑似复合报文";
            }

            if (IsCheckFragment(trimmed))
            {
                return "疑似半包";
            }

            return "未知命令";
        }

        private static bool ContainsFrameTerminator(string value)
        {
            return value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\0') >= 0;
        }

        private static bool IsExactCheckCommand(string value)
        {
            return value == "CHECK1" || value == "CHECK2";
        }

        private static bool IsCheckFragment(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return ("CHECK1".StartsWith(value) || "CHECK2".StartsWith(value)
                || ("CHECK1".Contains(value) && value.Length >= 2)
                || ("CHECK2".Contains(value) && value.Length >= 2))
                && !IsExactCheckCommand(value);
        }

        private static int CountToken(string value, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        private static string NormalizeMessage(string value)
        {
            return (value ?? string.Empty).Trim(' ', '\t', '\r', '\n', '\0');
        }

        private static string EscapeVisible(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("\0", "\\0");
        }

        private void RaiseMessageReceived(string message)
        {
            MessageReceived?.Invoke(this, CreateTcpEvent(message));
        }

        private void RaiseClientConnected(string message)
        {
            ClientConnected?.Invoke(this, CreateTcpEvent(message));
        }

        private void RaiseClientDisconnected(string message)
        {
            ClientDisconnected?.Invoke(this, CreateTcpEvent(message));
        }

        private TCPevent CreateTcpEvent(string message)
        {
            IPEndPoint endpoint = null;

            try
            {
                endpoint = _tcpClient?.Client?.RemoteEndPoint as IPEndPoint;
            }
            catch
            {
            }

            return new TCPevent()
            {
                IPaddress = endpoint?.Address.ToString() ?? string.Empty,
                Port = endpoint?.Port ?? 0,
                Msg = message
            };
        }

        private string GetRemoteDescription()
        {
            try
            {
                IPEndPoint endpoint = _tcpClient?.Client?.RemoteEndPoint as IPEndPoint;
                if (endpoint != null)
                {
                    return $"{endpoint.Address}:{endpoint.Port}";
                }
            }
            catch
            {
            }

            return "Unknown";
        }

        private void CloseCurrentClient()
        {
            lock (clientLock)
            {
                try
                {
                    _stream?.Close();
                }
                catch
                {
                }

                try
                {
                    _tcpClient?.Close();
                }
                catch
                {
                }

                _stream = null;
                _tcpClient = null;
            }
        }

        /// <summary>
        /// 软件退出时停止机器人 TCP 监听、重连和接收循环，并关闭当前客户端连接。
        /// 该入口只释放通信资源，不发送 CHECK 结果，也不改变已经写入 SQLite 或 MES 的生产判定。
        /// </summary>
        public void StopListener()
        {
            stopRequested = true;
            isConnected = false;
            listiening = false;
            CloseCurrentClient();

            try
            {
                _tcpServer?.Stop();
            }
            catch
            {
            }

            _tcpServer = null;
            WriteRobotTcpLog("[连接] 软件退出，TCP服务端已停止");
        }

        private void WriteRobotTcpLog(string message)
        {
            try
            {
                DateTime now = DateTime.Now;
                string dir = Path.Combine(Environment.CurrentDirectory, "日志", "机器人通信", now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"RobotTcp-{now:yyyyMMddHH}.txt");

                lock (logLock)
                {
                    File.AppendAllText(file, $"[{now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }

    public class TCPevent : EventArgs
    {
        public string IPaddress { set; get; }
        public int Port { set; get; }
        public string Msg { set; get; }
    }
}
