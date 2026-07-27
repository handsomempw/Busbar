
using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Record
{
    public class ProductInfoRecord : ObservableObject
    {
        private int _dualYStationIndex;

        public Productinfo Productinfo { set; get; } = new Productinfo();

        public string StationCode { set; get; }
        public string EQUIPMENTID { set; get; }

        /// <summary>
        /// 双Y物理工位号。1 和 2 分别表示 Y1/Y2；0 表示标准产线记录或升级前的历史记录。
        /// 该字段随记录 XML 保存，用于小屏界面区分并行产品；MES 工站代码和 SQLite 主记录继续沿用现有口径。
        /// </summary>
        public int DualYStationIndex
        {
            get { return _dualYStationIndex; }
            set
            {
                if (_dualYStationIndex == value) return;
                _dualYStationIndex = value;
                RaisePropertyChanged(() => DualYStationIndex);
                RaisePropertyChanged(() => StationDisplay);
            }
        }

        /// <summary>
        /// 生产界面的工位显示文本。双Y记录显示 Y1/Y2，标准产线及历史记录沿用工站代码。
        /// </summary>
        [XmlIgnore]
        public string StationDisplay
        {
            get { return DualYStationIndex > 0 ? "Y" + DualYStationIndex : StationCode; }
        }

        public bool TakePhoto1 { set; get; } = false;

        public float Res { set; get; } = 0;
        public float TVVoltage { set; get; } = 0;

        public float TVMaxVoltage { set; get; } = 0;
        public float TVMaxCurrent { set; get; } = 0;


        public bool TVResult { set; get; } = false;

        public bool AppearanceInspection { set; get; } = false;
        public DateTime DateTime { set; get; } = DateTime.Now;

        public UInt16 Pressure_Max { set; get; }
        public UInt16 Pressure_Average { set; get; }
        public UInt16 Pressure_Min { set; get; }

        public bool Pressure_Result { set; get; }
        public string TVMeterID { set; get; }
        public string TVInfo { set; get; }

        /// <summary>
        /// 测试模式标识（ACW/DCW）
        /// 用于区分交流耐压测试和直流耐压测试
        /// </summary>
        public string TestMode { set; get; } = "ACW";


        public bool Report { set; get; } = false;


    }
}
