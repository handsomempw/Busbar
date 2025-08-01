using BusbarCompressionSystem.Model.Setting;
using BusbarCompressionSystem.Model.Setting1;
using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model
{
    public class SettingModel : ObservableObject
    {

        [XmlIgnore]
        public HWindow HWindow1 { get; set; } = null;
        [XmlIgnore]
        public HWindow HWindow2 { get; set; } = null;
        [XmlIgnore]
        public HWindow HWindow3 { get; set; } = null;
        [XmlIgnore]
        public HWindow HWindow4 { get; set; } = null;
        [XmlIgnore]
        public HWindow HWindow5 { get; set; } = null;
        #region 相机配置

        [XmlIgnore]
        [XmlElement("相机配置1")]
        public Camera.DATA camedata1 { set; get; } = new Camera.DATA();
        [XmlIgnore]
        [XmlElement("相机配置2")]
        public Camera.DATA camedata2 { set; get; } = new Camera.DATA();
        [XmlIgnore]
        [XmlElement("相机配置3")]
        public Camera.DATA camedata3 { set; get; } = new Camera.DATA();
        [XmlIgnore]
        [XmlElement("相机配置4")]
        public Camera.DATA camedata4 { set; get; } = new Camera.DATA();
        [XmlIgnore]
        [XmlElement("相机配置5")]
        public Camera.DATA camedata5 { set; get; } = new Camera.DATA();

        #endregion



        [XmlElement("照片保存设置")]
        public ImageSaveSetting ImageSaveSetting { get; set; } = new ImageSaveSetting();

        [XmlElement("扫码器")]
        public Scanner.ScannerModel ScannerModel { set; get; } = new Scanner.ScannerModel();

        [XmlElement("霍尼韦尔扫码器")]

        public Honeywell.HF800 HF800 { set; get; } = new Honeywell.HF800();


        public string ScannerMode { set; get; } = "HF800";




        #region PLC配置
        [XmlElement("PLC通讯IP")]
        public string PLC_IP { set; get; } = "127.0.0.1";
        [XmlElement("PLC通讯端口")]
        public int PLC_Port { set; get; } = 502;
        [XmlElement("信号交互地址")]
        public int AddressStart { set; get; } = 1000;

        [XmlElement("SN起始地址")]
        public int AddressSN { set; get; } = 800;

        [XmlElement("阻值起始地址")]
        public int AddressRes { set; get; } = 1200;
        #endregion


        [XmlElement("机器人")]
        public TcpSetting RobotConnect { set; get; } = new TcpSetting();
        [XmlIgnore]
        public TcpServerHelper.TCPServerH TcpServerRobot { set; get; } = new TcpServerHelper.TCPServerH();

        #region 耐压测试设备
        [XmlElement("耐压1")]
        public AT9620.AT9620 AT9620_1 = new AT9620.AT9620();
        [XmlElement("耐压2")]
        public AT9620.AT9620 AT9620_2 = new AT9620.AT9620();
        [XmlElement("耐压3")]
        public AT9620.AT9620 AT9620_3 = new AT9620.AT9620();
        #endregion

        #region 设备配置参数
        public SETTING_DATA SETTING_DATA { set; get; } = new SETTING_DATA();
        #endregion


        [XmlIgnore]
        public F7DataBase.Sqlserver Sqlserver { set; get; } = new F7DataBase.Sqlserver();

    }

    public enum IOstatus
    {
        连接失败,
        高电平,
        低电平
    }

    public class IO : ObservableObject
    {

        private int _IOstatus = -1;
        [XmlIgnore]
        public int IOstatus
        {
            get
            {
                return _IOstatus;
            }
            set
            {
                _IOstatus = value;
                RaisePropertyChanged(() => IOstatusBackGround);
                RaisePropertyChanged(() => IOStatusForeGround);
            }
        }
        [XmlIgnore]
        public SolidColorBrush IOstatusBackGround
        {
            get
            {
                if (_IOstatus == -1)
                {
                    return Brushes.WhiteSmoke;
                }
                else if (_IOstatus == 0)
                {
                    return Brushes.Black;
                }
                else
                {
                    return Brushes.GreenYellow;
                }
            }
        }
        public SolidColorBrush IOStatusForeGround
        {
            get
            {
                if (_IOstatus == -1)
                {
                    return Brushes.Black;
                }
                else if (_IOstatus == 0)
                {
                    return Brushes.White;
                }
                else
                {
                    return Brushes.OrangeRed;
                }

            }
        }
    }
}
