/*
 * 生产记录和日志模型
 *
 * MVVM架构中的Model层，管理所有历史记录和日志数据：
 * - 产品记录：每个产品的完整测试和检测结果
 * - 通信日志：与硬件设备的通信记录
 * - 系统日志：程序运行过程中的重要事件记录
 * - 统计数据：生产数据统计和分析结果
 *
 * 作用：持久化保存生产数据，支持追溯查询和数据分析
 */

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
