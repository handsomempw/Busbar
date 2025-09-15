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
        private TcpClient tcp = new TcpClient();
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


                if ((tcp.Client == null) || (!tcp.Connected))
                {
                    tcp = new TcpClient();
                    tcp.ReceiveTimeout = 10000;
                    tcp.ConnectAsync(IP_address, port).Wait(3000);
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
                    t?.Abort();
                    tcp?.Close();
                    isconnected = false;
                }
            }
            catch
            {

            }

            return !isconnected;
        }

        public Result Get_String()
        {
            Result r = new Result();
            try
            {
                resultarray.Clear();
                if (isconnected)
                {

                    double[] result = new double[15];
                    Status[] statuses = new Status[15];

                    #region 读取数值
                    string trig = "TRIGGER";
                    tcp.Client.Send(Encoding.ASCII.GetBytes($"{trig}"));

                    Thread.Sleep(100);
                    byte[] receive = new byte[1024];
                    tcp.Client.Receive(receive);

                    string result_str = System.Text.Encoding.ASCII.GetString(receive).Replace("\0", "").Replace("\r", "").Replace("\n", "");
                    if (string.IsNullOrEmpty(result_str))
                    {
                        r.Error = "接收数据为空";
                    }
                    else
                    {
                        r.value = result_str;
                        r.Status = Status.OK;
                    }
                    #endregion


                }
            }
            catch (Exception ex)
            {
                r.Error = ex.ToString();
                ;
            }
            return r;


        }


        public Result Scanner()
        {
            var c = connect(IP_address, port);
            if (!c)
            {
                return new Result() { Error = "连接失败" };
            }
            var r = Get_String();
            disconnect();
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
