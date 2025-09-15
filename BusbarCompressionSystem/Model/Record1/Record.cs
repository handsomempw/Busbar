using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting
{
    public class ConnectRecord : ObservableObject
    {
        [XmlElement("通讯时间")]
        public DateTime dt { set; get; } = DateTime.MinValue;
        [XmlElement("备注")]
        public string name { set; get; } = string.Empty;
        [XmlElement("通讯内容")]
        public string Msg { set; get; } = string.Empty;
        [XmlElement("通讯结果")]
        public bool IsSuccess { set; get; } = false;
    }

    public class Logmodel : ObservableObject
    {
        [XmlElement("通讯时间")]
        public DateTime dt { set; get; } = DateTime.MinValue;
        [XmlElement("通讯内容")]
        public string Msg { set; get; } = string.Empty;
    }
}

