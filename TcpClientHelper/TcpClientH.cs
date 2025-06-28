using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpClientHelper
{
    public class TcpClientH
    {

        public event EventHandler Received;
        TcpClient tcp = null;
        NetworkStream workStream = null;
        public ManualResetEvent connectDone = new ManualResetEvent(false);

        public bool Connect(string IPAddress, int Port, int WaitTime = 2000)
        {
            try
            {

                //if (tcp == null)
                //{
                    tcp = new TcpClient();
                //}

                //tcp.ReceiveTimeout = WaitTime;
                if (tcp.ConnectAsync(IPAddress, Port).Wait(WaitTime))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
        public bool SendMsg(string Msg, int timeout = 1000)
        {
            try
            {
                tcp.SendTimeout = timeout;
                if (tcp == null || tcp.Client == null || tcp.Connected == false)
                {
                    return false;
                }
                byte[] Msgbytes = Encoding.UTF8.GetBytes(Msg);
                tcp.SendTimeout =
                tcp.Client.Send(Msgbytes);
                return true;

            }
            catch { return false; }
        }

        public string ReceiveMsg(int timeout = 5000)
        {
            try
            {
            
                if (tcp == null || tcp.Client == null || tcp.Connected == false)
                {
                    throw new Exception();
                }
                else
                {
                    tcp.ReceiveTimeout = timeout;
                    byte[] Msgbytes = new byte[1024];
                    tcp.Client.Receive(Msgbytes);
                    return Encoding.UTF8.GetString(Msgbytes).Replace("\0","");
                }

            }
            catch { }
            return string.Empty;
        }

        public void DisConnect()
        {
            try
            {
                if (tcp != null && tcp.Client != null)
                {
                    if (tcp.Connected)
                    {
                        tcp.Close();
                    }
                }
            }
            catch { return; }
        }
    }
}
