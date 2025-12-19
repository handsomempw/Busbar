using GalaSoft.MvvmLight;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace Honeywell
{
    public class HF800:ObservableObject
    {
        private TcpClient tcp = null; //初始化为null，避免未使用状态下占用资源
        [XmlElement("扫码器IP地址")]
        public string IP_address { set; get; } = "10.8.23.87";
        [XmlElement("扫码器端口号")]
        public int port { set; get; } = 55256;

        [XmlElement("数据接收超时ms")]
        public int receivetimeout { set; get; } = 5000;
        [XmlIgnore]
        public bool isconnected = false;
        [XmlIgnore]
        public ObservableCollection<Result> resultarray = new ObservableCollection<Result>();

        [XmlIgnore]
        public int toolnum = 0;
        Thread t = null;

        bool testresult = false;

        public bool connect(string _IPaddress, int _Port, int _receivetimeout = 5000)
        {
            try
            {
                IP_address = _IPaddress;
                port = _Port;
                receivetimeout = _receivetimeout;

                // 如果已有连接，先清理旧连接（防止状态残留）
                if (tcp != null)
                {
                    try
                    {
                        if (tcp.Connected)
                        {
                            tcp.Close();
                        }
                    }
                    catch { }
                    finally
                    {
                        tcp = null;
                    }
                }

                // 创建新连接
                tcp = new TcpClient();
                tcp.ReceiveTimeout = receivetimeout;
                tcp.SendTimeout = 5000;
                
                // 异步连接，设置超时
                var connectTask = tcp.ConnectAsync(IP_address, port);
                if (connectTask.Wait(3000))
                {
                    isconnected = tcp.Connected;
                }
                else
                {
                    // 连接超时，清理资源
                    isconnected = false;
                    try { tcp.Close(); } catch { }
                    tcp = null;
                }
            }
            catch (Exception ex)
            {
                // 连接失败，清理资源（防止状态残留）
                isconnected = false;
                try
                {
                    if (tcp != null)
                    {
                        tcp.Close();
                    }
                }
                catch { }
                finally
                {
                    tcp = null; // 重置tcp对象，防止下次连接时状态错误
                }
            }

            return isconnected;
        }

        public bool disconnect()
        {
            try
            {
                // 安全关闭TCP连接（移除Thread.Abort()，避免资源泄漏）
                if (tcp != null)
                {
                    try
                    {
                        if (tcp.Connected)
                        {
                            // 先关闭发送和接收，再关闭连接
                            tcp.Client.Shutdown(SocketShutdown.Both);
                        }
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            tcp.Close();
                        }
                        catch { }
                        // 关键修复：重置tcp对象，防止状态残留
                        tcp = null;
                    }
                }
                isconnected = false;
            }
            catch
            {
                // 确保即使异常也能重置状态
                isconnected = false;
                tcp = null;
            }

            return !isconnected;
        }

        public Result Get_String()
        {
            Result r = new Result();
            try
            {
                resultarray.Clear();
                
                // 检查连接状态
                if (!isconnected || tcp == null || !tcp.Connected)
                {
                    r.Error = "扫码器未连接";
                    r.Status = Status.NG;
                    return r;
                }

                #region 读取数值
                string trig = "TRIGGER";
                byte[] sendData = Encoding.ASCII.GetBytes(trig);
                tcp.Client.Send(sendData);

                Thread.Sleep(100);
                
                // 检查是否有数据可读
                if (tcp.Client.Available > 0)
                {
                    byte[] receive = new byte[1024];
                    int bytesReceived = tcp.Client.Receive(receive);
                    
                    if (bytesReceived > 0)
                    {
                        string result_str = Encoding.ASCII.GetString(receive, 0, bytesReceived)
                            .Replace("\0", "")
                            .Replace("\r", "")
                            .Replace("\n", "")
                            .Trim();
                            
                        if (string.IsNullOrEmpty(result_str))
                        {
                            r.Error = "接收数据为空";
                            r.Status = Status.NG;
                        }
                        else
                        {
                            r.value = result_str;
                            r.Status = Status.OK;
                        }
                    }
                    else
                    {
                        r.Error = "未接收到数据";
                        r.Status = Status.NG;
                    }
                }
                else
                {
                    r.Error = "无数据可读";
                    r.Status = Status.NG;
                }
                #endregion
            }
            catch (SocketException sex)
            {
                // 网络异常，连接可能已断开
                r.Error = $"网络错误: {sex.Message}";
                r.Status = Status.NG;
                // 更新连接状态，防止后续操作使用失效连接
                isconnected = false;
                try
                {
                    if (tcp != null)
                    {
                        tcp.Close();
                    }
                }
                catch { }
                finally
                {
                    tcp = null;
                }
            }
            catch (Exception ex)
            {
                r.Error = $"读取数据异常: {ex.Message}";
                r.Status = Status.NG;
                // 异常时更新连接状态
                isconnected = false;
            }
            return r;
        }


        public Result Scanner()
        {
            Result r = new Result();
            try
            {
                // 连接扫码器
                bool c = connect(IP_address, port, receivetimeout);
                if (!c)
                {
                    return new Result() { Error = "扫码器连接失败，请检查设备连接和网络设置", Status = Status.NG };
                }
                
                // 读取数据
                r = Get_String();
            }
            catch (Exception ex)
            {
                r.Error = $"扫码操作异常: {ex.Message}";
                r.Status = Status.NG;
            }
            finally
            {
                // 确保连接总是被断开，防止资源泄漏
                disconnect();
            }
            return r;
        }



    }

    public class Result
    {
        public string value { set; get; } = string.Empty;
        public Status Status { set; get; } = Status.NG;
        public string Error { set; get; } = string.Empty;
    }
    public enum Status
    {
        OK,
        NG,
        NULL,
    }
}
