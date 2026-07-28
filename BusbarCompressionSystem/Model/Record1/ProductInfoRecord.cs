
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
        private long _databaseRowId;
        private float _res;
        private float _tvMaxVoltage;
        private float _tvMaxCurrent;
        private bool _tvResult;
        private DateTime _dateTime = DateTime.Now;
        private UInt32 _pressureMax;
        private UInt32 _pressureAverage;
        private UInt32 _pressureMin;
        private bool _pressureResult;
        private bool _pressureRecorded;
        private string _tvMeterId;
        private string _tvInfo;
        private string _testMode = "ACW";

        public Productinfo Productinfo { set; get; } = new Productinfo();

        public string StationCode { set; get; }
        public string EQUIPMENTID { set; get; }

        /// <summary>
        /// 本条过程记录在工单 SQLite 数据库中的主键。
        /// 双Y流程使用该值把本次测试行与压力、归档和 MES 过程数据精确关联；0 表示标准产线记录或升级前的历史界面记录。
        /// </summary>
        public long DatabaseRowId
        {
            get { return _databaseRowId; }
            set
            {
                if (Set(ref _databaseRowId, value))
                {
                    RaiseElectricalDisplayPropertiesChanged();
                }
            }
        }

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

        public float Res
        {
            get { return _res; }
            set
            {
                if (Set(ref _res, value))
                {
                    RaisePropertyChanged(() => ResDisplay);
                }
            }
        }
        public float TVVoltage { set; get; } = 0;

        public float TVMaxVoltage
        {
            get { return _tvMaxVoltage; }
            set
            {
                if (Set(ref _tvMaxVoltage, value))
                {
                    RaiseElectricalDisplayPropertiesChanged();
                }
            }
        }

        public float TVMaxCurrent
        {
            get { return _tvMaxCurrent; }
            set
            {
                if (Set(ref _tvMaxCurrent, value))
                {
                    RaiseElectricalDisplayPropertiesChanged();
                }
            }
        }


        public bool TVResult
        {
            get { return _tvResult; }
            set
            {
                if (Set(ref _tvResult, value))
                {
                    RaisePropertyChanged(() => TVResultDisplay);
                }
            }
        }

        public bool AppearanceInspection { set; get; } = false;
        public DateTime DateTime
        {
            get { return _dateTime; }
            set { Set(ref _dateTime, value); }
        }

        /// <summary>
        /// 压力最大值。PLC 以 uint32 占用两个连续 D 寄存器，界面、SQLite 与 MES 保留完整 32 位数值。
        /// </summary>
        public UInt32 Pressure_Max
        {
            get { return _pressureMax; }
            set
            {
                if (Set(ref _pressureMax, value))
                {
                    RaisePressureDisplayPropertiesChanged();
                }
            }
        }

        /// <summary>
        /// 压力平均值。Y1 与 Y2 分别读取各自 PLC 快照地址，单位沿用现场 PLC 标定。
        /// </summary>
        public UInt32 Pressure_Average
        {
            get { return _pressureAverage; }
            set
            {
                if (Set(ref _pressureAverage, value))
                {
                    RaisePressureDisplayPropertiesChanged();
                }
            }
        }

        /// <summary>
        /// 压力最小值。该值参与流程结束压力下限判定，并随本次测试行归档。
        /// </summary>
        public UInt32 Pressure_Min
        {
            get { return _pressureMin; }
            set
            {
                if (Set(ref _pressureMin, value))
                {
                    RaisePressureDisplayPropertiesChanged();
                }
            }
        }

        public bool Pressure_Result
        {
            get { return _pressureResult; }
            set
            {
                if (Set(ref _pressureResult, value))
                {
                    RaisePropertyChanged(() => PressureResultDisplay);
                }
            }
        }

        /// <summary>
        /// 本条测试行是否已经取得流程结束压力快照。
        /// false 在双Y界面显示“待归档”，true 时按 <see cref="Pressure_Result"/> 显示压力判定。
        /// </summary>
        public bool PressureRecorded
        {
            get { return _pressureRecorded; }
            set
            {
                if (Set(ref _pressureRecorded, value))
                {
                    RaisePressureDisplayPropertiesChanged();
                }
            }
        }

        public string TVMeterID
        {
            get { return _tvMeterId; }
            set
            {
                if (Set(ref _tvMeterId, value))
                {
                    RaiseElectricalDisplayPropertiesChanged();
                }
            }
        }

        public string TVInfo
        {
            get { return _tvInfo; }
            set
            {
                if (Set(ref _tvInfo, value))
                {
                    RaiseElectricalDisplayPropertiesChanged();
                }
            }
        }

        /// <summary>
        /// 测试模式标识（ACW/DCW）
        /// 用于区分交流耐压测试和直流耐压测试
        /// </summary>
        public string TestMode
        {
            get { return _testMode; }
            set
            {
                if (Set(ref _testMode, value))
                {
                    RaiseElectricalDisplayPropertiesChanged();
                }
            }
        }

        /// <summary>
        /// 本行是否包含一次已经执行的电测。扫码占位行显示“待测”，实际测试行显示合格或不合格。
        /// </summary>
        [XmlIgnore]
        public bool HasElectricalResult
        {
            get
            {
                return DatabaseRowId > 0
                    || !string.IsNullOrWhiteSpace(TVInfo)
                    || !string.IsNullOrWhiteSpace(TVMeterID)
                    || TVMaxVoltage != 0
                    || TVMaxCurrent != 0;
            }
        }

        [XmlIgnore]
        public string TVResultDisplay => HasElectricalResult ? (TVResult ? "合格" : "不合格") : "待测";

        [XmlIgnore]
        public string TVMaxVoltageDisplay => HasElectricalResult ? TVMaxVoltage.ToString("0.###") : "--";

        [XmlIgnore]
        public string TVMaxCurrentDisplay => HasElectricalResult ? TVMaxCurrent.ToString("0.###") : "--";

        [XmlIgnore]
        public string ResDisplay => HasElectricalResult ? Res.ToString("0.###") : "--";

        [XmlIgnore]
        public bool HasPressureData => PressureRecorded || Pressure_Average != 0 || Pressure_Max != 0 || Pressure_Min != 0;

        [XmlIgnore]
        public string PressureAverageDisplay => HasPressureData ? Pressure_Average.ToString() : "--";

        [XmlIgnore]
        public string PressureResultDisplay => HasPressureData ? (Pressure_Result ? "合格" : "不合格") : "待归档";


        public bool Report { set; get; } = false;

        private void RaiseElectricalDisplayPropertiesChanged()
        {
            RaisePropertyChanged(() => HasElectricalResult);
            RaisePropertyChanged(() => TVResultDisplay);
            RaisePropertyChanged(() => TVMaxVoltageDisplay);
            RaisePropertyChanged(() => TVMaxCurrentDisplay);
            RaisePropertyChanged(() => ResDisplay);
        }

        private void RaisePressureDisplayPropertiesChanged()
        {
            RaisePropertyChanged(() => HasPressureData);
            RaisePropertyChanged(() => PressureAverageDisplay);
            RaisePropertyChanged(() => PressureResultDisplay);
        }

    }
}
