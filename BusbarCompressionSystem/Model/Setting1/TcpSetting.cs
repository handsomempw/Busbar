using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting
{


    public class TcpSetting : ObservableObject
    {
        [XmlElement("连接名称")]
        public string ConnectName { set; get; } = "通讯口";
        [XmlElement("本地IP")]
        public string LocalIP { set; get; } = "127.0.0.1";
        [XmlElement("本地端口")]
        public int LocalPort { set; get; } = 3000;
        [XmlElement("远程IP")]
        public string RemoteIP { set; get; } = "127.0.0.1";
        [XmlElement("远程端口")]
        public int RemotePort { set; get; } = 1000;
        [XmlElement("连接状态")]
        [XmlIgnore]
        public bool IsConnected { set; get; } = false;
    }
}

