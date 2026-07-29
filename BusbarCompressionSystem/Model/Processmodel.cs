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
        /// 当TV1/TV2/TV3触发值为1时使用此参数。
        /// </remarks>
        public AT9620.TVParameter ACWParameter { set; get; } = new AT9620.TVParameter() { TestMode = AT9620.TestMode.ACW };

        /// <summary>
        /// DCW直流耐压测试参数
        /// </summary>
        /// <remarks>
        /// 独立的直流耐压参数对象，TestMode固定为DCW。
        /// 当TV1/TV2/TV3触发值为2时使用此参数。
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
        /// 耐压仪3上次执行的测试模式。
        /// D1010触发工位3 ACW/DCW时刷新该字段，用于主界面显示当前TV3电测口径。
        /// </summary>
        [XmlIgnore]
        private AT9620.TestMode _lastTV3TestMode = AT9620.TestMode.ACW;

        [XmlIgnore]
        public AT9620.TestMode LastTV3TestMode
        {
            get { return _lastTV3TestMode; }
            set
            {
                _lastTV3TestMode = value;
                RaisePropertyChanged(() => LastTV3TestMode);
                RaisePropertyChanged(() => CurrentTV3TestModeDisplay);
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

        /// <summary>
        /// 当前TV3测试模式显示文本（用于界面绑定）。
        /// </summary>
        [XmlIgnore]
        public string CurrentTV3TestModeDisplay
        {
            get
            {
                return LastTV3TestMode == AT9620.TestMode.ACW ? "ACW" : "DCW";
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

        /// <summary>
        /// IR绝缘电阻测试实时数据（用于 UI 展示与点检判定）
        /// </summary>
        public IRTestModel IRTestModel { set; get; } = new IRTestModel();
        #endregion
        public string CMD { set; get; } = "";

        #region 触发信号
        [XmlIgnore]
        [XmlElement("扫码触发")]
        public IO Scan_Trig_IO { get; set; } = new IO();

        [XmlIgnore]
        [XmlElement("第二扫码触发")]
        public IO SecondScan_Trig_IO { get; set; } = new IO();

        /// <summary>
        /// 联动扫码触发状态（M3046，PLC -> 上位机）。
        /// 用于上一设备已提供转换后 SN 的联调场景，状态只影响联动取码入口，手动扫码和扫码器自动扫码保持原有触发路径。
        /// </summary>
        [XmlIgnore]
        [XmlElement("联动扫码触发")]
        public IO LinkedScan_Trig_IO { get; set; } = new IO();

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

        /// <summary>
        /// IR绝缘电阻测试触发信号（PLC→PC，1启动 2停止）
        /// </summary>
        [XmlIgnore]
        [XmlElement("绝缘电阻触发")]
        public IO IR_Trig_IO { get; set; } = new IO();

        [XmlIgnore]
        [XmlElement("阻值1触发")]
        public IO Res1_Trig_IO { get; set; } = new IO();
        [XmlIgnore]
        [XmlElement("阻值2触发")]
        public IO Res2_Trig_IO { get; set; } = new IO();
        [XmlIgnore]
        [XmlElement("阻值3触发")]
        public IO Res3_Trig_IO { get; set; } = new IO();

        /// <summary>
        /// 双Y 2工位进站扫码触发（D1120）。
        /// </summary>
        [XmlIgnore]
        public IO DualYStation2Scan_Trig_IO { get; set; } = new IO();

        /// <summary>
        /// 双Y 1工位流程结束触发（D1020）。
        /// </summary>
        [XmlIgnore]
        public IO DualYStation1FlowEnd_Trig_IO { get; set; } = new IO();

        /// <summary>
        /// 双Y 2工位流程结束触发（D1021）。
        /// </summary>
        [XmlIgnore]
        public IO DualYStation2FlowEnd_Trig_IO { get; set; } = new IO();

        private bool _dualYElectricalTestModeActive;
        private bool _dualYElectricalTestModeKnown;

        /// <summary>
        /// PLC M3050 双Y电测模式。该状态决定运行中的产品流程，并向双Y生产界面提供实时模式提示；
        /// 机台启动时选择标准界面或双Y界面仍由配置 XML 中的部署标志负责。
        /// 仅在 <see cref="DualYElectricalTestModeKnown"/> 为 true 时表示线圈已成功读取；读取失败时不得据此切换标准/双Y流程。
        /// </summary>
        [XmlIgnore]
        public bool DualYElectricalTestModeActive
        {
            get { return _dualYElectricalTestModeActive; }
            set
            {
                if (_dualYElectricalTestModeActive == value)
                {
                    return;
                }

                _dualYElectricalTestModeActive = value;
                RaisePropertyChanged(() => DualYElectricalTestModeActive);
                RaisePropertyChanged(() => DualYElectricalTestModeDisplay);
            }
        }

        /// <summary>
        /// M3050 模式线圈是否已成功读取。false 时上位机暂停模式相关触发，避免读失败被当成标准流程。
        /// </summary>
        [XmlIgnore]
        public bool DualYElectricalTestModeKnown
        {
            get { return _dualYElectricalTestModeKnown; }
            set
            {
                if (_dualYElectricalTestModeKnown == value)
                {
                    return;
                }

                _dualYElectricalTestModeKnown = value;
                RaisePropertyChanged(() => DualYElectricalTestModeKnown);
                RaisePropertyChanged(() => DualYElectricalTestModeDisplay);
            }
        }

        /// <summary>
        /// 双Y生产界面显示的 PLC 流程模式。
        /// 线圈读取失败显示模式未知；接通后显示双Y仅电测；断开且读取成功时显示标准产线。
        /// </summary>
        [XmlIgnore]
        public string DualYElectricalTestModeDisplay
        {
            get
            {
                if (!DualYElectricalTestModeKnown)
                {
                    return "模式未知/暂停触发";
                }

                return DualYElectricalTestModeActive ? "双Y仅电测" : "标准产线";
            }
        }

        /// <summary>
        /// 双Y工位1的当前产品与归档状态，生产界面使用该状态展示当前节拍；
        /// 过程数据 XML、PLC、MES 和 SQLite 继续使用各自正式业务模型。
        /// </summary>
        [XmlIgnore]
        public DualYStationDisplayState DualYStation1Display { get; private set; } = new DualYStationDisplayState(1);

        /// <summary>
        /// 双Y工位2的当前产品与归档状态，生产界面使用该状态展示当前节拍；
        /// 过程数据 XML、PLC、MES 和 SQLite 继续使用各自正式业务模型。
        /// </summary>
        [XmlIgnore]
        public DualYStationDisplayState DualYStation2Display { get; private set; } = new DualYStationDisplayState(2);

        #endregion


        public CheckData CheckData { set; get; } = new CheckData();
        public TVAvailable TVAvailable { set; get; } = new TVAvailable();
        public UInt16 allow_start { set; get; } = 1;
        public CheckList CheckList { set; get; } = new CheckList();

        /// <summary>
        /// IR绝缘电阻测试参数（MES下发或默认值，用于 Download() 前置校验）
        /// </summary>
        public AT6835FL.IRParameter IRParameter { set; get; } = new AT6835FL.IRParameter();

    }

    /// <summary>
    /// 双Y单工位的操作员展示状态。该模型汇总扫码产品和当前实测结算结果，
    /// 帮助小屏界面区分 Y1/Y2 当前节拍；正式测试结果仍以 SQLite、MES 和 PLC 反馈为准。
    /// </summary>
    public class DualYStationDisplayState : ObservableObject
    {
        private string _currentSn = "--";
        private string _currentWoCode = "--";
        private string _workflowState = "等待扫码";
        private string _lastResult = "--";

        /// <summary>
        /// 创建指定双Y工位的界面状态容器。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号，取值 1 或 2，用于界面标识和诊断。</param>
        public DualYStationDisplayState(int stationIndex)
        {
            StationIndex = stationIndex;
        }

        /// <summary>
        /// 双Y物理工位号，界面显示为 Y1 或 Y2。
        /// </summary>
        public int StationIndex { get; private set; }

        /// <summary>
        /// 当前工位最近一次业务扫码成功的产品 SN。
        /// </summary>
        public string CurrentSn
        {
            get { return _currentSn; }
            private set
            {
                if (_currentSn == value) return;
                _currentSn = value;
                RaisePropertyChanged(() => CurrentSn);
            }
        }

        /// <summary>
        /// 当前工位最近一次业务扫码成功的工单号。
        /// </summary>
        public string CurrentWoCode
        {
            get { return _currentWoCode; }
            private set
            {
                if (_currentWoCode == value) return;
                _currentWoCode = value;
                RaisePropertyChanged(() => CurrentWoCode);
            }
        }

        /// <summary>
        /// 当前工位面向操作员的流程状态，例如等待电测、结果结算中或测试完成。
        /// </summary>
        public string WorkflowState
        {
            get { return _workflowState; }
            private set
            {
                if (_workflowState == value) return;
                _workflowState = value;
                RaisePropertyChanged(() => WorkflowState);
            }
        }

        /// <summary>
        /// 当前工位最近一次实测综合判定及点检摘要；后台MES归档状态只写日志，不覆盖当前产品显示。
        /// </summary>
        public string LastResult
        {
            get { return _lastResult; }
            private set
            {
                if (_lastResult == value) return;
                _lastResult = value;
                RaisePropertyChanged(() => LastResult);
            }
        }

        /// <summary>
        /// 扫码业务完成后刷新工位产品，进入等待电测状态并清空上一笔结果显示。
        /// </summary>
        /// <param name="sn">MES 解码和工单校验通过的产品 SN。</param>
        /// <param name="woCode">与当前产品对应的工单号。</param>
        public void BeginProduct(string sn, string woCode)
        {
            CurrentSn = string.IsNullOrWhiteSpace(sn) ? "--" : sn;
            CurrentWoCode = string.IsNullOrWhiteSpace(woCode) ? "--" : woCode;
            WorkflowState = "等待电测";
            LastResult = "--";
        }

        /// <summary>
        /// 刷新工位流程阶段与结果摘要。该状态服务于界面提示；正式报工结果和设备联锁继续以业务流程返回值为准。
        /// </summary>
        /// <param name="workflowState">操作员可识别的当前流程阶段。</param>
        /// <param name="lastResult">实测综合判定和点检摘要；传空值时保留现有结果。</param>
        public void UpdateWorkflow(string workflowState, string lastResult = null)
        {
            WorkflowState = string.IsNullOrWhiteSpace(workflowState) ? "状态未知" : workflowState;
            if (lastResult != null)
            {
                LastResult = lastResult;
            }
        }
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

        /// <summary>
        /// 耐压3可用标志，由M3032周期刷新。
        /// 现场线圈口径为1=不可用、0=可用，上位机取反后用于TV3触发、参数下发和界面启用。
        /// 双Y专用部署强制为 false（本机无 AT9620_3），不受 M3032 镜像影响。
        /// </summary>
        public bool TV3Available { set; get; } = true;

        /// <summary>
        /// IR绝缘电阻仪可用标志（由 M3033 写入）
        /// </summary>
        public bool IRAvailable { set; get; } = false;

        /// <summary>
        /// IR可用信号原始线圈值（PLC侧：通常“不可用=1”）
        /// 仅用于日志/诊断，不参与业务判断。
        /// </summary>
        public bool IRAvailableRawCoil { set; get; } = false;

    }




    public class PressureParamter : ObservableObject
    {
        public UInt16 Pressure { set; get; } = 1000;

        public float Max_Pressure { set; get; } = 1100;
        public float Min_Pressure { set; get; } = 950;
    }
    public class ResParameter : ObservableObject
    {
        // 阻值上限阈值：用于 RES OK/NG 判定
        // 兜底默认值：当 PLC D2020 读取失败时使用
        public float Max_Res { set; get; } = 50;
        public float Min_Res { set; get; } = 14;
    }

}
