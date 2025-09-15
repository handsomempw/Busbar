using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TcpServerHelper
{
    public class TCPServerH
    {

        public bool listiening { set; get; } = false;

        public delegate void EventHandling(TCPServerH sender, object e);
        public event EventHandling MessageReceived;
        public event EventHandling ClientDisconnected;
        public event EventHandling ClientConnected;

        private TcpListener _tcpServer = null;
        private NetworkStream _stream = null;
        private StreamReader _sr = null;
        private TcpClient _tcpClient = null;
        //public Action<string> ReciviMsgAction { get; set; }

        public string ReceiveMSG { set; get; } = string.Empty;
        public string GetMsg()
        {
            string _s = ReceiveMSG;
            return _s;
        }


        private bool isConnected = false;
        /// <summary>
        /// 开启监听
        /// </summary>
        public bool StartListener(string IPAddress_str, int Port)
        {
            if (listiening)
            {
                return true;
            }
            IPAddress ipAddress = IPAddress.Parse(IPAddress_str);
            _tcpServer = new TcpListener(ipAddress, Port);
            _tcpServer.Start();
            listiening = true;
            _tcpClient = _tcpServer.AcceptTcpClient();        //接收挂起的连接请求

            ClientConnected.Invoke(this, new TCPevent()
            {
                IPaddress = _tcpClient.Client.RemoteEndPoint.AddressFamily.ToString(),
                Port = ((int)_tcpClient.Client.RemoteEndPoint.AddressFamily),
                Msg = "客户端连接"
            });
            isConnected = true;
            Task.Run(ReceiveMsg);
            Task.Run(CheckIsConntect);
            return true;

        }

        /// <summary>
        /// 接收消息
        /// </summary>
        public void ReceiveMsg()
        {
            try
            {
                while (true)
                {
                    if (isConnected)
                    {
                        _stream = _tcpClient.GetStream();
                        _sr = new StreamReader(_stream);
                        byte[] data = new byte[1024];
                        int bytesRead = _stream.Read(data, 0, data.Length);
                        string message = Encoding.ASCII.GetString(data, 0, bytesRead);
                        //ReciviMsgAction.Invoke(message);
                        Debug.WriteLine(_tcpClient.Client?.Connected);

                        if (!string.IsNullOrEmpty(message))
                        {
                            MessageReceived.Invoke(
                                this,
                                new TCPevent()
                                {
                                    IPaddress = _tcpClient.Client.RemoteEndPoint.AddressFamily.ToString(),
                                    Port = ((int)_tcpClient.Client.RemoteEndPoint.AddressFamily),
                                    Msg = message
                                });
                        }
                        else
                        {
                            isConnected = false;
                            ClientDisconnected.Invoke(
                                this,
                                new TCPevent()
                                {
                                    IPaddress = _tcpClient.Client.RemoteEndPoint.AddressFamily.ToString(),
                                    Port = ((int)_tcpClient.Client.RemoteEndPoint.AddressFamily),
                                    Msg = "客户端掉线"
                                });
                        }

                        Thread.Sleep(10);
                    }


                }
            }
            catch (Exception)
            {
                //ReciviMsgAction.Invoke("客户端掉线");
                ClientDisconnected.Invoke(
                    this,
                    new TCPevent()
                    {
                        IPaddress = _tcpClient.Client.RemoteEndPoint.AddressFamily.ToString(),
                        Port = ((int)_tcpClient.Client.RemoteEndPoint.AddressFamily),
                        Msg = "客户端掉线"
                    });
                isConnected = false;
            }




        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="msg"></param>
        public void SendMessage(string msg)
        {
            if (!isConnected)
            {

                try
                {

                    //ReciviMsgAction.Invoke("发送失败，客户端掉线");
                    ClientDisconnected.Invoke(
                       this,
                       new TCPevent()
                       {
                           IPaddress = _tcpClient.Client.RemoteEndPoint.AddressFamily.ToString(),
                           Port = ((int)_tcpClient.Client.RemoteEndPoint.AddressFamily),
                           Msg = "发送失败，客户端掉线"
                       });
                }
                catch
                {
                    ClientDisconnected.Invoke(
                      this,
                      new TCPevent()
                      {
                          Msg = "发送失败，客户端掉线"
                      });
                }
                return;
            }
            byte[] reply = Encoding.ASCII.GetBytes(msg);
            _stream.Write(reply, 0, reply.Length);

        }

        /// <summary>
        /// 检测客户端是都断联
        /// </summary>
        public void CheckIsConntect()
        {
            while (true)
            {
                if (!isConnected)
                {
                    //IPAddress ipAddress = IPAddress.Parse("127.0.0.1");
                    //_tcpServer = new TcpListener(ipAddress, 1233);
                    _tcpServer.Start();
                    _tcpClient = _tcpServer.AcceptTcpClient();        //接收挂起的连接请求
                    isConnected = true;
                    //ReciviMsgAction.Invoke("客户端重新连接");
                    ClientConnected.Invoke(
                   this,
                   new TCPevent()
                   {
                       IPaddress = _tcpClient.Client.RemoteEndPoint.AddressFamily.ToString(),
                       Port = ((int)_tcpClient.Client.RemoteEndPoint.AddressFamily),
                       Msg = "客户端重新连接"
                   });
                    //Task.Run(ReceiveMsg);
                }
                Thread.Sleep(1000);
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
