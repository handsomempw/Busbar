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

        public AT9620.TVParameter TVParameter { set; get; } = new AT9620.TVParameter();

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
