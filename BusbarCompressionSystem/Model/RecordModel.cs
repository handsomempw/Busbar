using BusbarCompressionSystem.Model.Record;
using BusbarCompressionSystem.Model.Setting;
using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model
{
    public class RecordModel : ObservableObject
    {
        [XmlElement("通讯过程数据")]
        public ObservableCollection<ConnectRecord> ComuncationLog { get; set; } = new ObservableCollection<ConnectRecord>();

        [XmlIgnore]
        [XmlElement("日志")]
        public ObservableCollection<logitem> workLog { set; get; } = new ObservableCollection<logitem>();

        [XmlElement("错误")]
        public ObservableCollection<string> ErrorLog { set; get; } = new ObservableCollection<string>();


        public ObservableCollection<ProductInfoRecord> ProductInfoRecords { get; set; } = new ObservableCollection<ProductInfoRecord>();



    }
    public class logitem
    {
        public DateTime DateTime { get; set; } = DateTime.Now;
        public string log { set; get; } = string.Empty;
    }
}
