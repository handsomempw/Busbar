/*
 * 生产流程参数模型
 *
 * MVVM架构中的Model层，管理生产过程中的动态数据：
 * - 产品信息：SN、工单号、物料编码等基本信息
 * - 测试参数：耐压测试参数、压力测试参数等
 * - 实时数据：当前测试结果、状态信息等
 * - 缓存数据：临时存储的检测和测试结果
 *
 * 作用：存储和管理生产线上实时变化的数据，支持数据绑定到UI显示
 */

using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusbarCompressionSystem.Model.Record;
using BusbarCompressionSystem.Model.Record1;
using System.Xml.Serialization;
using System.Collections.ObjectModel;
using System.Data;

namespace BusbarCompressionSystem.Model
{
    public class Processmodel : ObservableObject
    {
        public string sninputstr { set; get; } = string.Empty;


        public string wocodeinputstr { set; get; } = string.Empty;

        public string PartNOID { set; get; } = string.Empty;



        #region 耐压测试参数

        /// <summary>
        /// 当前耐压测试参数（兼容旧逻辑，保留）
        /// </summary>
        public AT9620.TVParameter TVParameter { set; get; } = new AT9620.TVParameter();

        /// <summary>
        /// ACW交流耐压测试参数
        /// </summary>
        /// <remarks>
        /// 独立的交流耐压参数对象，TestMode固定为ACW。
        /// 当TV1Trig=1时使用此参数。
        /// </remarks>
        public AT9620.TVParameter ACWParameter { set; get; } = new AT9620.TVParameter() { TestMode = AT9620.TestMode.ACW };

        /// <summary>
        /// DCW直流耐压测试参数
        /// </summary>
        /// <remarks>
        /// 独立的直流耐压参数对象，TestMode固定为DCW。
        /// 当TV1Trig=2时使用此参数。
        /// </remarks>
        public AT9620.TVParameter DCWParameter { set; get; } = new AT9620.TVParameter() { TestMode = AT9620.TestMode.DCW };

        /// <summary>
        /// TV1上次执行的测试模式（用于界面显示/记录，保留兼容）
        /// </summary>
        /// <remarks>
        /// 说明：当前逻辑在每次触发测试前都会统一下发对应工艺参数到AT9620设备，已不再依赖该字段来判断“是否需要下发”。
        /// 该字段主要用于界面绑定显示（CurrentTV1TestModeDisplay）以及记录上一次执行的模式。
        /// </remarks>
        [XmlIgnore]
        private AT9620.TestMode _lastTV1TestMode = AT9620.TestMode.ACW;
        
        [XmlIgnore]
        public AT9620.TestMode LastTV1TestMode 
        { 
            get { return _lastTV1TestMode; }
            set 
            { 
                _lastTV1TestMode = value;
                RaisePropertyChanged(() => LastTV1TestMode);
                RaisePropertyChanged(() => CurrentTV1TestModeDisplay);
            }
        }

        /// <summary>
        /// 耐压仪2上次执行的测试模式
        /// </summary>
        [XmlIgnore]
        private AT9620.TestMode _lastTV2TestMode = AT9620.TestMode.ACW;
        
        [XmlIgnore]
        public AT9620.TestMode LastTV2TestMode 
        { 
            get { return _lastTV2TestMode; }
            set 
            { 
                _lastTV2TestMode = value;
                RaisePropertyChanged(() => LastTV2TestMode);
                RaisePropertyChanged(() => CurrentTV2TestModeDisplay);
            }
        }

        /// <summary>
        /// 当前TV1测试模式显示文本（用于界面绑定）
        /// </summary>
        [XmlIgnore]
        public string CurrentTV1TestModeDisplay
        {
            get
            {
                return LastTV1TestMode == AT9620.TestMode.ACW ? "ACW" : "DCW";
            }
        }

        /// <summary>
        /// 当前TV2测试模式显示文本（用于界面绑定）
        /// </summary>
        [XmlIgnore]
        public string CurrentTV2TestModeDisplay
        {
            get
            {
                return LastTV2TestMode == AT9620.TestMode.ACW ? "ACW" : "DCW";
            }
        }

        public PressureParamter PressureParamter { set; get; } = new PressureParamter();

        public ResParameter ResParameter { set; get; } = new ResParameter();

        #endregion

        #region 测试数据
        public TakePhotoTestModel TakePhotoTestModel { set; get; } = new TakePhotoTestModel();
        public TVTestTestModel TVTestTestModel1 { set; get; } = new TVTestTestModel();
        public TVTestTestModel TVTestTestModel2 { set; get; } = new TVTestTestModel();
        public TVTestTestModel TVTestTestModel3 { set; get; } = new TVTestTestModel();
        public TakePhotoTestModel TakePhotoTestMode2 { set; get; } = new TakePhotoTestModel();
        #endregion
        public string CMD { set; get; } = "";

        #region 触发信号
        [XmlIgnore]
        [XmlElement("扫码触发")]
        public IO Scan_Trig_IO { get; set; } = new IO();

        [XmlIgnore]
        [XmlElement("第二扫码触发")]
        public IO SecondScan_Trig_IO { get; set; } = new IO();

        [XmlIgnore]
        [XmlElement("拍照触发")]
        public IO TakePhoto1_Trig_IO { get; set; } = new IO();


        [XmlIgnore]
        [XmlElement("耐压1触发")]
        public IO TV1_Trig_IO { get; set; } = new IO();
        [XmlIgnore]
        [XmlElement("耐压2触发")]
        public IO TV2_Trig_IO { get; set; } = new IO();
        [XmlIgnore]
        [XmlElement("耐压3触发")]
        public IO TV3_Trig_IO { get; set; } = new IO();

        [XmlIgnore]
        [XmlElement("阻值1触发")]
        public IO Res1_Trig_IO { get; set; } = new IO();
        [XmlIgnore]
        [XmlElement("阻值2触发")]
        public IO Res2_Trig_IO { get; set; } = new IO();
        [XmlIgnore]
        [XmlElement("阻值3触发")]
        public IO Res3_Trig_IO { get; set; } = new IO();

        #endregion


        public CheckData CheckData { set; get; } = new CheckData();
        public TVAvailable TVAvailable { set; get; } = new TVAvailable();
        public UInt16 allow_start { set; get; } = 1;
        public CheckList CheckList { set; get; } = new CheckList();


    }


    public class CheckData : ObservableObject
    {
        #region 检查时间
        public DateTime TVMeterCheckTime { set; get; } = DateTime.MinValue;
        public DateTime TV1OKCheckTime { set; get; } = DateTime.MinValue;
        public DateTime TV2OKCheckTime { set; get; } = DateTime.MinValue;
        public DateTime TV3OKCheckTime { set; get; } = DateTime.MinValue;
        public DateTime TV1NGCheckTime { set; get; } = DateTime.MinValue;
        public DateTime TV2NGCheckTime { set; get; } = DateTime.MinValue;
        public DateTime TV3NGCheckTime { set; get; } = DateTime.MinValue;
        public DateTime AOIOKCheckTime { set; get; } = DateTime.MinValue;
        public DateTime AOINGCheckTime { set; get; } = DateTime.MinValue;
        #endregion

        #region 点检参数



        #endregion
    }


    public class CheckList : ObservableObject
    {
        #region 标准件清单
        public ObservableCollection<string> TVOKCheckList { set; get; } = new ObservableCollection<string>();
        public ObservableCollection<string> TVNGCheckList { set; get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AOIOKCheckList { set; get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AOINGCheckList { set; get; } = new ObservableCollection<string>();

        #endregion
    }










    public class TVAvailable : ObservableObject
    {
        public bool TV1Available { set; get; } = true;
        public bool TV2Available { set; get; } = true;
        public bool TV3Available { set; get; } = true;

    }




    public class PressureParamter : ObservableObject
    {
        public UInt16 Pressure { set; get; } = 1000;

        public float Max_Pressure { set; get; } = 1100;
        public float Min_Pressure { set; get; } = 950;
    }
    public class ResParameter : ObservableObject
    {
        public float Max_Res { set; get; } = 20;
        public float Min_Res { set; get; } = 14;
    }

}
