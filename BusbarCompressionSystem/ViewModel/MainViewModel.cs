using BusbarCompressionSystem.Model;
using GalaSoft.MvvmLight;
using System.IO;
using System.Windows;
using System.Xml.Serialization;
using System;
using AT9620;
using HslCommunication.ModBus;
using System.Data;
using System.Threading;
using System.Text;
using HalconDotNet;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Camera;
using TcpServerHelper;
using Panuon.WPF.UI;
using BusbarCompressionSystem.Model.Record;
using SQLITEDATABASE;
using BusbarCompressionSystem.Model.FaraVision.Tool.QRCode;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.FaraVision;
using PositionDetect;
using System.Drawing;
using System.Linq;
using Honeywell;
using System.Runtime.InteropServices; // 用于GDI句柄管理
using BusbarCompressionSystem.Utils;

namespace BusbarCompressionSystem.ViewModel
{
    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// <para>
    /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
    /// </para>
    /// <para>
    /// You can also use Blend to data bind with the tool's support.
    /// </para>
    /// <para>
    /// See http://www.galasoft.ch/mvvm
    /// </para>
    /// </summary>
    public partial class MainViewModel : ViewModelBase
    {
        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public MainViewModel()
        {
            ////if (IsInDesignMode)
            ////{
            ////    // Code runs in Blend --> create design time data.
            ////}
            ////else
            ////{
            ////    // Code runs "for real"
            ////}
            ///
            Initrelaycommand();
        }

        public DataModel DataModel { get; set; } = new DataModel();

        private void SaveXmlSafely<T>(string filename, T data)
        {
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempFile = $"{filename}.tmp";
            string backupFile = $"{filename}.bak";

            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new XmlSerializer(typeof(T));
                    serializer.Serialize(stream, data);
                    stream.Flush();
                }

                if (File.Exists(filename))
                {
                    File.Replace(tempFile, filename, backupFile, true);
                    if (File.Exists(backupFile))
                    {
                        File.Delete(backupFile);
                    }
                }
                else
                {
                    File.Move(tempFile, filename);
                }
            }
            catch
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                throw;
            }
        }

        #region 数据保存加载
        #region 过程数据
        public void SaveProcessmodel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
            SaveXmlSafely(filename, DataModel.Processmodel);
        }
        public void LoadProcessmodel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(filename))
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var serializer = new XmlSerializer(typeof(Processmodel));
                        DataModel.Processmodel = serializer.Deserialize(stream) as Processmodel;
                    }
                }
                else
                {
                    DataModel.Processmodel = new Processmodel();
                }
            }
            catch (Exception ex)
            {
                DataModel.Processmodel = new Processmodel();

            }
        }
        #endregion

        #region 配置数据
        public void SaveSettingModel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\配置数据.xml";
            SaveXmlSafely(filename, DataModel.Settingmodel);
        }
        public void LoadSettingModel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\配置数据.xml";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(filename))
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var serializer = new XmlSerializer(typeof(SettingModel));
                        DataModel.Settingmodel = serializer.Deserialize(stream) as SettingModel;
                    }
                }
                else
                {
                    DataModel.Settingmodel = new SettingModel();
                }
                
                // 加载独立的耐压状态映射配置文件
                LoadTvStatusMappings();
            }
            catch (Exception ex)
            {
                DataModel.Settingmodel = new SettingModel();

                MessageBox.Show($"配置数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");
                
                // 加载独立的耐压状态映射配置文件
                LoadTvStatusMappings();
            }
        }
        #endregion

        #region 日志数据
        public void SaveRecordModel()
        {
            string filename = $"{Environment.CurrentDirectory}\\配置\\日志数据.xml";
            SaveXmlSafely(filename, DataModel.Recordmodel);
        }
        public void LoadRecordModel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\日志数据.xml";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(filename))
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var serializer = new XmlSerializer(typeof(RecordModel));
                        DataModel.Recordmodel = serializer.Deserialize(stream) as RecordModel;
                    }
                }
                else
                {
                    DataModel.Recordmodel = new RecordModel();
                }
            }
            catch (Exception ex)
            {
                DataModel.Recordmodel = new RecordModel();
                //MessageBox.Show($"日志数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");

            }
        }

        /// <summary>
        /// 加载耐压状态映射配置文件
        /// 文件路径：配置/耐压状态映射.xml
        /// 格式：&lt;StatusMappings&gt;&lt;Map Code="状态代码" Display="中文描述" /&gt;&lt;/StatusMappings&gt;
        /// </summary>
        private void LoadTvStatusMappings()
        {
            try
            {
                string xmlPath = Path.Combine(Environment.CurrentDirectory, "配置", "耐压状态映射.xml");

                if (!File.Exists(xmlPath))
                {
                    // 首次运行：生成默认配置文件
                    if (TvStatusTranslator.SaveDefaultXml(xmlPath, out string saveError))
                    {
                        writeLog($"已生成默认耐压状态映射配置文件: {xmlPath}");
                    }
                    else
                    {
                        writeLog($"生成默认耐压状态映射配置文件失败: {saveError}");
                    }
                }
                else
                {
                    // 加载现有配置文件
                    if (TvStatusTranslator.LoadFromXml(xmlPath, out string loadError))
                    {
                        writeLog($"已加载耐压状态映射配置: {xmlPath}");
                    }
                    else
                    {
                        writeLog($"耐压状态映射配置加载失败，使用默认配置: {loadError}");
                    }
                }
            }
            catch (Exception ex)
            {
                writeLog($"耐压状态映射配置处理异常: {ex.Message}");
            }
        }

        private string GetLocalizedTvStatus(string status)
        {
            return TvStatusTranslator.Translate(status);
        }
        #endregion

        #endregion
        #region 操作
        /// <summary>
        /// 解析并校验产品编号（ScanSN的核心实现）
        /// </summary>
        /// <param name="snstr">扫码器读取的原始字符串</param>
        /// <returns>空字符串表示成功；非空字符串表示失败原因</returns>
        /// <remarks>
        /// 业务逻辑：
        /// 1. 点检SN码（INSPECTION_TV_OK/NG, INSPECTION_AOI_OK/NG）：跳过MES校验，直接创建本地记录
        /// 2. 普通SN码：调用MES解析获取产品信息，校验规格一致性后创建本地记录
        /// </remarks>
        public string _ScanSN(string snstr)
        {
            // 点检SN码特殊处理：跳过MES校验，直接返回成功
            // 扩展为四个点检 SN：耐压点检 OK/NG + AOI 点检 OK/NG
            if (snstr == DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN ||
                snstr == DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN ||
                snstr == DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN ||
                snstr == DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN)
            {
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(snstr);
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(snstr);
                PLC_Writestring(DataModel.Settingmodel.AddressSN.ToString(), $"{snstr};{wocode}");
                sqlite.CREATENEWLINE(wocode, partnoid, snstr, DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, DateTime.Now);
                return string.Empty;
            }

            //if (string.IsNullOrEmpty(DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN))
            //{
            string sn = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.DecodeSN(snstr);
            if (!string.IsNullOrEmpty(sn))
            {
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(sn);
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(sn);
                if (partnoid != DataModel.Processmodel.PartNOID)
                {
                    return $"不同规格产品禁止混合作业:{partnoid},{DataModel.Processmodel.PartNOID}";
                }

                if (string.IsNullOrEmpty(wocode))
                {
                    return "关联批次号读取失败";
                }
                if (string.IsNullOrEmpty(partnoid))
                {
                    return "关联规格信息读取失败";
                }
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN = sn;
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE = wocode;
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID = partnoid;
                PLC_Writestring(DataModel.Settingmodel.AddressSN.ToString(), $"{sn};{wocode}");
                sqlite.CREATENEWLINE(wocode, partnoid, sn, DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, DateTime.Now);
                return string.Empty;

            }
            else
            {
                return "标签读取失败,请确认该产品编号是否正常";
            }
            //}
            //else
            //{
            //    return "拍照位已经有产品编号，请勿重复扫码";
            //}
        }

        /// <summary>
        /// 扫描产品编号并进行MES校验
        /// </summary>
        /// <remarks>
        /// 触发方式：手动扫码（Enter键/按钮）或 自动扫码（PLC触发）
        /// 点检SN码会跳过MES校验，普通SN进行完整校验
        /// </remarks>
        /// <returns>true: 扫码成功; false: 扫码失败</returns>
        public bool ScanSN()
        {
            string s = _ScanSN(DataModel.Processmodel.sninputstr);
            if (!string.IsNullOrEmpty(s))
            {
                writeLog(s, true);
                return false;
            }
            else
            {
                DataModel.Processmodel.sninputstr = string.Empty;
                return true;
            }
            DataModel.Processmodel.TakePhotoTestModel.error = s;

        }
        #endregion
        #region 耐压测试

        public void InitAt9620()
        {
            DataModel.Settingmodel.AT9620_1.DataReceived += OnReceive1;
            DataModel.Settingmodel.AT9620_2.DataReceived += OnReceive2;
            DataModel.Settingmodel.AT9620_3.DataReceived += OnReceive3;
        }

        private void OnReceive1(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                DataModel.Processmodel.TVTestTestModel1.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel1.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel1.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel1.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel1.Voltage);
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel1.Current);
                DataModel.Processmodel.TVTestTestModel1.TVInfo = myEventArgs.ResultTVProcess.Value.status;
            }
            catch (Exception ex)
            {
            }
        }
        private void OnReceive2(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                DataModel.Processmodel.TVTestTestModel2.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel2.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel2.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel2.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel2.Voltage);
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel2.Current);
                DataModel.Processmodel.TVTestTestModel2.TVInfo = myEventArgs.ResultTVProcess.Value.status;
            }
            catch (Exception ex)
            {
            }
        }
        private void OnReceive3(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                DataModel.Processmodel.TVTestTestModel3.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel3.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel3.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel3.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel3.Voltage);
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel3.Current);
                DataModel.Processmodel.TVTestTestModel3.TVInfo = myEventArgs.ResultTVProcess.Value.status;

            }
            catch (Exception ex)
            {
            }
        }

        #endregion
        #region PLC通讯



        public void PLC_Start()
        {
            Thread t = new Thread(PLC_Process);
            t.Start();

            //Thread t2 = new Thread(shakehand2);
            //t2.Start();
        }
        private void PLC_Process()
        {

            while (true)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;

                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        #region 读取数据
                        var readresult = modbusTcp.ReadUInt16(DataModel.Settingmodel.AddressStart.ToString(), 20);

                        if (readresult.IsSuccess)
                        {
                            int ScanTrig = readresult.Content[0];
                            int TakePhoto1Trig = readresult.Content[2];
                            int TV1Trig = readresult.Content[6];
                            int TV2Trig = readresult.Content[8];
                            int TV3Trig = readresult.Content[10];

                            #region 扫码触发
                            try
                            {
                                if (ScanTrig == 1 & DataModel.Processmodel.Scan_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                     {
                                         ScannerProcess();
                                     }).Start();
                                }
                            }
                            catch {; }
                            #endregion

                            #region 拍照触发
                            try
                            {
                                if (TakePhoto1Trig == 1 & DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TakePhoto1Process();
                                    }).Start();
                                }
                            }
                            catch {; }
                            #endregion

                            #region 耐压1触发
                            try
                            {
                                if ((TV1Trig == 1 || TV1Trig == 2) & DataModel.Processmodel.TV1_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV1Process();
                                    }).Start();
                                }
                                if ((TV1Trig == 3) & DataModel.Processmodel.TV1_Trig_IO.IOstatus != TV1Trig)
                                {
                                    DataModel.Settingmodel.AT9620_1.stop = true;
                                }
                            }
                            catch {; }
                            #endregion

                            #region 耐压2触发
                            try
                            {
                                if ((TV2Trig == 1 || TV2Trig == 2) & DataModel.Processmodel.TV2_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV2Process();
                                    }).Start();
                                }
                                if ((TV2Trig == 3) & DataModel.Processmodel.TV2_Trig_IO.IOstatus != TV2Trig)
                                {
                                    DataModel.Settingmodel.AT9620_2.stop = true;
                                }
                            }
                            catch {; }
                            #endregion

                            #region 耐压3触发
                            try
                            {
                                if ((TV3Trig == 1 || TV3Trig == 2) & DataModel.Processmodel.TV3_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV3Process();
                                    }).Start();
                                }
                                if ((TV3Trig == 3) & DataModel.Processmodel.TV3_Trig_IO.IOstatus != TV3Trig)
                                {
                                    DataModel.Settingmodel.AT9620_3.stop = true;
                                }
                            }
                            catch {; }
                            #endregion

                            #region 阻值触发
                            // 读取阻值触发信号（M地址，Bool类型）
                            var res1TrigResult = modbusTcp.ReadCoil(DataModel.Settingmodel.Res1TrigAddress.ToString(), 1);
                            if (!res1TrigResult.IsSuccess)
                            {
                                writeLog($"阻值1触发信号读取失败 M{DataModel.Settingmodel.Res1TrigAddress}, Err:{res1TrigResult.Message}");
                            }
                            var res2TrigResult = modbusTcp.ReadCoil(DataModel.Settingmodel.Res2TrigAddress.ToString(), 1);
                            if (!res2TrigResult.IsSuccess)
                            {
                                writeLog($"阻值2触发信号读取失败 M{DataModel.Settingmodel.Res2TrigAddress}, Err:{res2TrigResult.Message}");
                            }
                            var res3TrigResult = modbusTcp.ReadCoil(DataModel.Settingmodel.Res3TrigAddress.ToString(), 1);
                            if (!res3TrigResult.IsSuccess)
                            {
                                writeLog($"阻值3触发信号读取失败 M{DataModel.Settingmodel.Res3TrigAddress}, Err:{res3TrigResult.Message}");
                            }
                            
                            // 判断读取阻值1触发信号（M地址，Bool类型）的结果，若通信成功且内容为true，则Res1Trig为1，否则为0
                            int Res1Trig = res1TrigResult.IsSuccess && res1TrigResult.Content[0] ? 1 : 0;
                            int Res2Trig = res2TrigResult.IsSuccess && res2TrigResult.Content[0] ? 1 : 0;
                            int Res3Trig = res3TrigResult.IsSuccess && res3TrigResult.Content[0] ? 1 : 0;

                            // 触发状态变更日志（避免刷屏，仅在状态变化时记录）
                            if (Res1Trig != DataModel.Processmodel.Res1_Trig_IO.IOstatus)
                            {
                                writeLog($"阻值1触发状态 M{DataModel.Settingmodel.Res1TrigAddress} 变更为 {Res1Trig}");
                            }
                            if (Res2Trig != DataModel.Processmodel.Res2_Trig_IO.IOstatus)
                            {
                                writeLog($"阻值2触发状态 M{DataModel.Settingmodel.Res2TrigAddress} 变更为 {Res2Trig}");
                            }
                            if (Res3Trig != DataModel.Processmodel.Res3_Trig_IO.IOstatus)
                            {
                                writeLog($"阻值3触发状态 M{DataModel.Settingmodel.Res3TrigAddress} 变更为 {Res3Trig}");
                            }

                            // 阻值1触发
                            try
                            {
                                if (Res1Trig == 1 & DataModel.Processmodel.Res1_Trig_IO.IOstatus == 0)
                                {
                                    writeLog($"阻值1触发=1(M{DataModel.Settingmodel.Res1TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes}");
                                    new Thread(() => { Res1Process(); }).Start();
                                }
                            }
                            catch {; }

                            // 阻值2触发
                            try
                            {
                                if (Res2Trig == 1 & DataModel.Processmodel.Res2_Trig_IO.IOstatus == 0)
                                {
                                    writeLog($"阻值2触发=1(M{DataModel.Settingmodel.Res2TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes + 2}");
                                    new Thread(() => { Res2Process(); }).Start();
                                }
                            }
                            catch {; }

                            // 阻值3触发
                            try
                            {
                                if (Res3Trig == 1 & DataModel.Processmodel.Res3_Trig_IO.IOstatus == 0)
                                {
                                    writeLog($"阻值3触发=1(M{DataModel.Settingmodel.Res3TrigAddress}), 准备读取阻值地址D{DataModel.Settingmodel.AddressRes + 4}");
                                    new Thread(() => { Res3Process(); }).Start();
                                }
                            }
                            catch {; }
                            #endregion

                            #region 数据复制刷新
                            DataModel.Processmodel.Scan_Trig_IO.IOstatus = ScanTrig;
                            DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = TakePhoto1Trig;
                            DataModel.Processmodel.TV1_Trig_IO.IOstatus = TV1Trig;
                            DataModel.Processmodel.TV2_Trig_IO.IOstatus = TV2Trig;
                            DataModel.Processmodel.TV3_Trig_IO.IOstatus = TV3Trig;
                            DataModel.Processmodel.Res1_Trig_IO.IOstatus = Res1Trig;
                            DataModel.Processmodel.Res2_Trig_IO.IOstatus = Res2Trig;
                            DataModel.Processmodel.Res3_Trig_IO.IOstatus = Res3Trig;

                            #endregion
                        }
                        #endregion
                        modbusTcp.ConnectClose();

                    }
                    else
                    {
                        #region 通讯失败数据置为-1
                        DataModel.Processmodel.Scan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV3_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res3_Trig_IO.IOstatus = -1;

                        #endregion
                    }
                    //modbusTcp?.ConnectClose();

                }
                catch {; }
                Thread.Sleep(100);
            }
        }


        public void PLC_shankhand()
        {

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(1500);
                    try
                    {
                        PLC_ReadTVAvailable();
                    }
                    catch (Exception e)
                    { continue; }

                }
            }).Start();

        }



        public void ScannerProcess()
        {
            try
            {
                if (DataModel.Settingmodel.ScannerMode == "HF800")
                {
                    var r = DataModel.Settingmodel.HF800.Scanner();
                    // 添加空值检查，防止r.value为null时崩溃
                    if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(r.value))
                    {
                        string s = r.value.Replace("\r", "").Replace("\n", "").Trim();
                        DataModel.Processmodel.sninputstr = s;
                        //SQLITEDATABASE.sqlite.CREATENEWLINE("1", "1", s);
                        //PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)1);
                        var R = ScanSN();
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(R ? 1 : 2));
                    }
                    else
                    {
                        // 扫码失败，写入PLC失败状态
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                    }
                }
                else
                {
                    var r = DataModel.Settingmodel.ScannerModel.Scanner();
                    string s = r.receivestring?.Replace("\r", "").Replace("\n", "").Trim() ?? "";
                    if (!String.IsNullOrEmpty(s))
                    {
                        //s = "7Y00000000" + ((byte)(new Random().NextDouble() * 10)).ToString();
                        //s = $"7Y0000{DateTime.Now.ToString("MMss")}";
                        DataModel.Processmodel.sninputstr = s;
                        var R = ScanSN();
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(r.IsSuccess && R ? 1 : 2));
                    }
                    else
                    {
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                    }
                }
            }
            catch (Exception ex)
            {
                // 异常处理，防止崩溃，写入PLC失败状态
                try
                {
                    PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                }
                catch { }
            }
        }

        public void TakePhoto1Process()
        {

            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN);
            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
            }
            else
            {

                writeLog($"拍照留底产品编号读取错误:{s}");

            }


            DataModel.Settingmodel.camedata1.CameraModel.finished = false;
            DataModel.Settingmodel.camedata2.CameraModel.finished = false;
            DataModel.Settingmodel.camedata3.CameraModel.finished = false;




            DataModel.Settingmodel.camedata1.CameraModel.camera.bnTriggerExec_Click();
            DataModel.Settingmodel.camedata2.CameraModel.camera.bnTriggerExec_Click();
            DataModel.Settingmodel.camedata3.CameraModel.camera.bnTriggerExec_Click();

            int i = 0;
            while (true)
            {
                Thread.Sleep(200);
                if ((DataModel.Settingmodel.camedata1.CameraModel.finished
                & DataModel.Settingmodel.camedata2.CameraModel.finished
                & DataModel.Settingmodel.camedata3.CameraModel.finished
                ))
                {
                    SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, true);
                    newline(true);
                    PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 1);
                    break;
                }
                if (i++ > 15)
                {
                    #region 超时未完成拍照

                    if (!DataModel.Settingmodel.camedata1.CameraModel.finished)
                    {
                        writeLog("相机1拍照超时");
                    }

                    if (!DataModel.Settingmodel.camedata2.CameraModel.finished)
                    {
                        writeLog("相机2拍照超时");
                    }

                    if (!DataModel.Settingmodel.camedata3.CameraModel.finished)
                    {
                        writeLog("相机3拍照超时");
                    }
                    SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, false);
                    newline(false);
                    PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 2);

                    break;
                    #endregion
                }

            }
        }

        /// <summary>
        /// 创建一条新的产品过程数据记录并加入到 DataModel.Recordmodel.ProductInfoRecords 集合中。
        /// 业务含义：
        /// - 在拍照留底完成后调用，将当前产品的基础信息、拍照结果缓存在内存集合里；
        /// - 后续耐压、压力、AOI 等过程都会通过 SN 在该集合中找到对应记录并补充数据，
        ///   最终在 CHECK1 / CHECK2 中做综合判定和保存到 MES。
        /// </summary>
        /// <param name="takephoto1">拍照留底是否成功</param>
        private void newline(bool takephoto1)
        {
            try
            {
                ProductInfoRecord p = new ProductInfoRecord()
                {
                    StationCode = DataModel.Settingmodel.SETTING_DATA.StationCode,
                    EQUIPMENTID = DataModel.Settingmodel.SETTING_DATA.MachineID,
                    Productinfo = DataModel.Processmodel.TakePhotoTestModel.Productinfo,
                    TakePhoto1 = takephoto1
                };
                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    DataModel.Recordmodel.ProductInfoRecords.Insert(0, p);
                }));
            }
            catch (Exception ex) { }
        }




        public void TV1Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel1.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
            }
            else
            {
                writeLog($"耐压1产品编号读取错误:{s}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
            DataModel.Processmodel.TVTestTestModel1.Res = res;

            DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;


            var r = DataModel.Settingmodel.AT9620_1.Start();
            var localizedTvInfo1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.TVInfo);
            DataModel.Processmodel.TVTestTestModel1.TVInfo = localizedTvInfo1;

            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1
                );

            updatetv(DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1
                );

            //if (r.Success)
            if (!r.Success)
            {
                var failureStatus1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus1,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                    r.Recordstr
                    );
            }

            PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), 1);
        }

        public void TV2Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel2.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
            }
            else
            {
                writeLog($"耐压2产品编号读取错误:{s}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
            DataModel.Processmodel.TVTestTestModel2.Res = res;

            DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;
            var r = DataModel.Settingmodel.AT9620_2.Start();
            var localizedTvInfo2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.TVInfo);
            DataModel.Processmodel.TVTestTestModel2.TVInfo = localizedTvInfo2;

            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
               DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
               localizedTvInfo2,
               DataModel.Settingmodel.SETTING_DATA.TVMeterID2
               );

            updatetv(DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                res, DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                localizedTvInfo2,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID2

                );

            if (!r.Success)
            {
                var failureStatus2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus2,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                    r.Recordstr
                    );
            }

            PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), 1);

        }
        public void TV3Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 3);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel3.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
            }
            else
            {
                writeLog($"耐压3产品编号读取错误:{s}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
            DataModel.Processmodel.TVTestTestModel3.Res = res;

            DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = 0;

            var r = DataModel.Settingmodel.AT9620_3.Start();
            var localizedTvInfo3 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel3.TVInfo);
            DataModel.Processmodel.TVTestTestModel3.TVInfo = localizedTvInfo3;

            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
               DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
               localizedTvInfo3,
               DataModel.Settingmodel.SETTING_DATA.TVMeterID3
               );

            updatetv(DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage
                , r.Success,
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
                localizedTvInfo3,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID3
                );

            if (!r.Success)
            {
                var failureStatus3 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel3.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus3,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID3,
                    r.Recordstr
                    );
            }
            PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), 1);

        }

        /// <summary>
        /// 阻值1读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3035，读取地址：D1200
        /// </summary>
        public void Res1Process()
        {
            try
            {
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
                DataModel.Processmodel.TVTestTestModel1.Res = res;
                writeLog($"阻值1读取完成 D{DataModel.Settingmodel.AddressRes}={res}");
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值1读取异常 M{DataModel.Settingmodel.Res1TrigAddress}/D{DataModel.Settingmodel.AddressRes}: {ex.Message}");
            }
        }

        /// <summary>
        /// 阻值2读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3036，读取地址：D1202
        /// </summary>
        public void Res2Process()
        {
            try
            {
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
                DataModel.Processmodel.TVTestTestModel2.Res = res;
                writeLog($"阻值2读取完成 D{DataModel.Settingmodel.AddressRes + 2}={res}");
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值2读取异常 M{DataModel.Settingmodel.Res2TrigAddress}/D{DataModel.Settingmodel.AddressRes + 2}: {ex.Message}");
            }
        }

        /// <summary>
        /// 阻值3读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3037，读取地址：D1204
        /// </summary>
        public void Res3Process()
        {
            try
            {
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
                DataModel.Processmodel.TVTestTestModel3.Res = res;
                writeLog($"阻值3读取完成 D{DataModel.Settingmodel.AddressRes + 4}={res}");
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值3读取异常 M{DataModel.Settingmodel.Res3TrigAddress}/D{DataModel.Settingmodel.AddressRes + 4}: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据产品 SN 更新 DataModel.Recordmodel.ProductInfoRecords 中对应记录的耐压/阻值相关数据。
        /// 业务含义：
        /// - 在各 TV 测试工位完成后调用，将阻值、电压、电流、结果等写入内存记录；
        /// - CHECK1 / CHECK2 以及点检 SN 的综合判定会直接使用这里更新过的数据。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        private void updatetv(string SN, float res, float maxvoltage, bool result, float maxcurrent, string tvinfo, string tvmeterid)
        {
            try
            {

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.Res = res;
                            p.TVMaxVoltage = maxvoltage;
                            p.TVMaxCurrent = maxcurrent;
                            p.TVResult = result;
                            p.TVInfo = tvinfo;
                            p.TVMeterID = tvmeterid;
                            break;
                        }
                    }
                }));
            }
            catch (Exception ex) { }
        }
        /// <summary>
        /// 根据产品 SN 更新 DataModel.Recordmodel.ProductInfoRecords 中对应记录的压力测试数据。
        /// 业务含义：
        /// - 在电测压力监控完成后调用，将平均值、最大值、最小值及判定结果写入内存记录；
        /// - 这些压力数据会一并在 SaveBusBarData 报存到 MES，方便后续追溯。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        private void updatepressure(string SN, UInt16 Pressure_Average, UInt16 Pressure_Max, UInt16 Pressure_Min, bool Pressure_Result)
        {
            try
            {

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.Pressure_Average = Pressure_Average;
                            p.Pressure_Max = Pressure_Max;
                            p.Pressure_Min = Pressure_Min;
                            p.Pressure_Result = Pressure_Result;

                            break;
                        }
                    }
                }));
            }
            catch (Exception ex) { }
        }


        public bool Download_PressureParameter()
        {
            try
            {
                var r1 = PLC_write(DataModel.Settingmodel.MaxPressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Max_Pressure);
                var r2 = PLC_write(DataModel.Settingmodel.MinPressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Min_Pressure);
                var r3 = PLC_write(DataModel.Settingmodel.Pressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Pressure);
                return r1 & r2 & r3;
            }
            catch {; }
            return false;
        }



        /// <summary>
        /// 根据产品 SN 更新 DataModel.Recordmodel.ProductInfoRecords 中对应记录的 AOI 外观检测结果。
        /// 业务含义：
        /// - AOI 工具完成判定后调用，将外观 OK/NG 结果及时间写入内存记录；
        /// - CHECK2 以及点检 SN 的最终综合判定，会把 AppearanceInspection 作为外观工序的依据。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        private void updatetakephoto2(string SN, bool result, DateTime dt)
        {
            try
            {

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.AppearanceInspection = result;
                            p.DateTime = dt;
                            break;
                        }
                    }
                }));
            }
            catch (Exception ex) { }
        }


        private bool PLC_write(float result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int i = 0;
            while (i++ < 4)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.Write((DataModel.Settingmodel.AddressStart + 1).ToString(), result);
                        modbusTcp.ConnectClose();
                        if (r.IsSuccess)
                        { return true; }
                    }
                }
                catch
                {
                    ;
                }
                Thread.Sleep(100);
            }

            return false;
        }

        private bool PLC_write(string address, UInt16 result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int i = 0;
            while (i++ < 4)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.Write((address).ToString(), (UInt16)result);
                        modbusTcp.ConnectClose();
                        if (r.IsSuccess)
                        { return true; }
                    }
                }
                catch
                {
                    ;
                }
                Thread.Sleep(100);
            }

            return false;
        }

        private bool PLC_write(string address, float result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int i = 0;
            while (i++ < 4)
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                try
                {
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    var connectresult = modbusTcp.ConnectServer();

                    if (connectresult.IsSuccess)
                    {
                        var r = modbusTcp.Write((address).ToString(), result);
                        modbusTcp.ConnectClose();
                        if (r.IsSuccess)
                        { return true; }
                    }
                }
                catch
                {
                    ;
                }
                Thread.Sleep(100);
            }

            return false;
        }
        public UInt16 PLC_ReadUint16(int address)
        {
            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            try
            {
                modbusTcp.ConnectTimeOut = 1;
                modbusTcp.ReceiveTimeOut = 1;
                modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                var connectresult = modbusTcp.ConnectServer();

                if (connectresult.IsSuccess)
                {
                    var r = modbusTcp.ReadUInt16(address.ToString(), 1);
                    modbusTcp.ConnectClose();
                    if (r.IsSuccess)
                    { return r.Content[0]; }
                }
            }
            catch
            {
                ;
            }
            return 0;

        }
        public float PLC_ReadFloat(int address)
        {
            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            try
            {
                modbusTcp.ConnectTimeOut = 1;
                modbusTcp.ReceiveTimeOut = 1;
                modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                var connectresult = modbusTcp.ConnectServer();

                if (connectresult.IsSuccess)
                {
                    var r = modbusTcp.ReadFloat(address.ToString(), 1);
                    modbusTcp.ConnectClose();
                    if (r.IsSuccess)
                    { return r.Content[0]; }
                }
            }
            catch
            {
                ;
            }
            return float.NaN;

        }
        public string PLC_Readstring(int address)
        {
            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            try
            {
                modbusTcp.ConnectTimeOut = 1;
                modbusTcp.ReceiveTimeOut = 1;
                modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                var connectresult = modbusTcp.ConnectServer();

                if (connectresult.IsSuccess)
                {
                    var r = modbusTcp.ReadString(address.ToString(), 25);
                    modbusTcp.ConnectClose();
                    if (r.IsSuccess)
                    { return r.Content.Replace("\0", ""); }
                }
            }
            catch
            {
                ;
            }
            return string.Empty;

        }
        public bool PLC_Writestring(string address, string data)
        {
            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            try
            {
                modbusTcp.ConnectTimeOut = 1;
                modbusTcp.ReceiveTimeOut = 1;
                modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                var connectresult = modbusTcp.ConnectServer();

                if (connectresult.IsSuccess)
                {
                    var r = modbusTcp.WriteUnicodeString(address.ToString(), data);
                    modbusTcp.ConnectClose();
                    return r.IsSuccess;
                }
            }
            catch
            {
                ;
            }
            return false;

        }

        public bool PLC_ReadTVAvailable()
        {

            ModbusTcpNet modbusTcp = new ModbusTcpNet();
            try
            {
                modbusTcp.ConnectTimeOut = 1;
                modbusTcp.ReceiveTimeOut = 1;
                modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                var connectresult = modbusTcp.ConnectServer();

                if (connectresult.IsSuccess)
                {
                    var r1 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter1AvailableAddress.ToString(), 1);
                    var r2 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter2AvailableAddress.ToString(), 1);
                    var r3 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter3AvailableAddress.ToString(), 1);
                    modbusTcp.Write(DataModel.Settingmodel.ShankHandAddress.ToString(), (UInt16)1);
                    modbusTcp.Write(DataModel.Settingmodel.DeviceAvailableAddress.ToString(), DataModel.Processmodel.allow_start);
                    modbusTcp.ConnectClose();
                    if (r1.IsSuccess)
                    {
                        DataModel.Processmodel.TVAvailable.TV1Available = !r1.Content[0];
                        DataModel.Processmodel.TVAvailable.TV2Available = !r2.Content[0];
                        DataModel.Processmodel.TVAvailable.TV3Available = !r3.Content[0];
                    }

                    return r1.IsSuccess & r2.IsSuccess & r3.IsSuccess;
                }
            }
            catch
            {
                ;
            }

            return false;
        }

        #endregion
        #region 通用数据日志
        private object writeLog_Locker = new object();

        private object writeBug_Locker = new object();

        internal void writeLog(string LogContent, bool showdatarecord = true)
        {
            if (string.IsNullOrEmpty(LogContent)) { return; }

            //lock (writeLog_Locker)
            //{
            try
            {

                logitem logitem = new logitem()
                {
                    log = LogContent
                };
                string logstr = $"[{logitem.DateTime.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{LogContent}";
                if (showdatarecord)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        int num = 200;
                        if (DataModel.Recordmodel.workLog.Count > num)
                        {
                            //DataModel.Recordmodel.workLog.RemoveAt(DataModel.Recordmodel.workLog.Count - 1);
                            DataModel.Recordmodel.workLog.Clear();
                        }
                        DataModel.Recordmodel.workLog.Insert(0, logitem);
                    });
                }

                string filename = $"{Environment.CurrentDirectory}\\日志\\日志\\{DateTime.Now.ToString("yyyyMMdd")}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    sw.WriteLine(logstr);
                    sw.Close();
                }
            }
            catch (Exception ex)
            {
                writeError(ex.Message + Environment.NewLine + ex.StackTrace);
            }
            //}
        }

        internal void writeError(string Content)
        {

            try
            {
                int num = 200;
                string bugstr = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{Content}";
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (DataModel.Recordmodel.ErrorLog.Count > num)
                    {
                        //DataModel.Recordmodel.workLog.RemoveAt(DataModel.Recordmodel.workLog.Count - 1);
                        DataModel.Recordmodel.ErrorLog.Clear();
                    }
                    DataModel.Recordmodel.ErrorLog.Insert(0, bugstr);
                });
                string filename = $"{Environment.CurrentDirectory}\\日志\\错误\\{DateTime.Now.ToString("yyyyMMdd")}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    sw.WriteLine(bugstr);
                    sw.Close();
                }
            }
            catch (Exception)
            {
            }

        }
        #endregion
        #region 相机操作
        #region 相机初始化
        public void InitCamera()
        {
            DataModel.Settingmodel.camedata1.init1($"{Environment.CurrentDirectory}\\配置\\相机配置1.xml");
            DataModel.Settingmodel.camedata2.init1($"{Environment.CurrentDirectory}\\配置\\相机配置2.xml");
            DataModel.Settingmodel.camedata3.init1($"{Environment.CurrentDirectory}\\配置\\相机配置3.xml");
            DataModel.Settingmodel.camedata4.init1($"{Environment.CurrentDirectory}\\配置\\相机配置4.xml");
            DataModel.Settingmodel.camedata5.init1($"{Environment.CurrentDirectory}\\配置\\相机配置5.xml");

            DataModel.Settingmodel.camedata1.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata2.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata3.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata4.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata5.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;

            DataModel.Settingmodel.camedata1.init2();
            DataModel.Settingmodel.camedata2.init2();
            DataModel.Settingmodel.camedata3.init2();
            DataModel.Settingmodel.camedata4.init2();
            DataModel.Settingmodel.camedata5.init2();

            start();
        }

        public void CloseCamera()
        {
            DataModel.Settingmodel.camedata1.closing($"{Environment.CurrentDirectory}\\配置\\相机配置1.xml");
            DataModel.Settingmodel.camedata2.closing($"{Environment.CurrentDirectory}\\配置\\相机配置2.xml");
            DataModel.Settingmodel.camedata3.closing($"{Environment.CurrentDirectory}\\配置\\相机配置3.xml");
            DataModel.Settingmodel.camedata4.closing($"{Environment.CurrentDirectory}\\配置\\相机配置4.xml");
            DataModel.Settingmodel.camedata5.closing($"{Environment.CurrentDirectory}\\配置\\相机配置5.xml");
        }

        #endregion



        #region 拍照留底
        public void start()
        {
            DataModel.Settingmodel.camedata1.CameraModel.camera.ImageReceived += OnCamera1Receive;
            DataModel.Settingmodel.camedata2.CameraModel.camera.ImageReceived += OnCamera2Receive;
            DataModel.Settingmodel.camedata3.CameraModel.camera.ImageReceived += OnCamera3Receive;
            DataModel.Settingmodel.camedata4.CameraModel.camera.ImageReceived += OnCamera4Receive;
            DataModel.Settingmodel.camedata5.CameraModel.camera.ImageReceived += OnCamera5Receive;
        }

        private void OnCameraErrorReceive(object sender, EventArgs e)
        {
            try
            {
                Camera.ErrorEventArgs errorEventArgs = e as Camera.ErrorEventArgs;
                NoticeBox.Show($"{errorEventArgs.Error}", $"相机错误-{errorEventArgs.CameraID}", MessageBoxIcon.Error, true, 10000);
            }
            catch (Exception ex) { }
        }

        private void OnCamera1Receive(object sender, EventArgs e)
        {

            try
            {
                writeLog($"相机->视觉:接收照片", false);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnReceiveProcess(DataModel.Settingmodel.HWindow1, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 1);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, "拍照留底", 1, "OK");
                    DataModel.Settingmodel.camedata1.CameraModel.finished = true;

                    GC.Collect();
                }));

            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }
        private void OnCamera2Receive(object sender, EventArgs e)
        {

            try
            {
                writeLog($"相机->视觉:接收照片", false);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnReceiveProcess(DataModel.Settingmodel.HWindow2, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 2);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, "拍照留底", 2, "OK");
                    DataModel.Settingmodel.camedata2.CameraModel.finished = true;

                    GC.Collect();
                }));

            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }

        private void OnCamera3Receive(object sender, EventArgs e)
        {
            try
            {
                writeLog($"相机->视觉:接收照片", false);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnReceiveProcess(DataModel.Settingmodel.HWindow3, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 3);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, "拍照留底", 3, "OK");
                    DataModel.Settingmodel.camedata3.CameraModel.finished = true;
                    GC.Collect();
                }));
            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }


        private void OnCamera4Receive(object sender, EventArgs e)
        {

            try
            {
                writeLog($"相机->视觉:接收照片", false);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    //#region 仅保存照片
                    //OnReceiveProcess(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 4);
                    //SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, "外观检测", 1, "OK");
                    //SendMsgRobot("OK");
                    //#endregion


                    #region AOI识别
                    //OnReceiveProcessAOI(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 4);
                    OnReceiveProcessAOI(myEventArgs.Image, myEventArgs.Height, myEventArgs.Width);

                    #endregion

                    GC.Collect();
                }));

            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }
        private void OnCamera5Receive(object sender, EventArgs e)
        {

            try
            {
                writeLog($"相机->视觉:接收照片", false);
                MyEventArgs myEventArgs = e as MyEventArgs;
                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    //#region 仅保存照片
                    //OnReceiveProcess(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 5);
                    //SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, "外观检测", 1, "OK");

                    //if (DataModel.Processmodel.CMD == "A5")
                    //{
                    //    DateTime dt = DateTime.Now;
                    //    updatetakephoto2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, true, dt);
                    //    sqlite.UpdateTakePhoto2(
                    //        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                    //        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                    //        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                    //        true);
                    //}
                    //SendMsgRobot("OK");
                    //#endregion

                    #region AOI识别
                    OnReceiveProcessAOI(myEventArgs.Image, myEventArgs.Height, myEventArgs.Width);
                    #endregion

                    GC.Collect();
                }));
            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }


        public void OnReceiveProcess(HWindow hwindow, HObject Image, int H, int W, int Index)
        {
            #region 图片接收
            //Image
            HOperatorSet.CountChannels(Image, out var channels);
            if (channels == 1)
            {
                HOperatorSet.Compose3(Image, Image, Image, out var multiChannelImage);
                Image.Dispose();
                Image = multiChannelImage;
            }
            hwindow.ClearWindow();
            hwindow.DispObj(Image);
            #endregion
        }

        public void OnReceiveProcessAOI(HObject Image, int H, int W)
        {
            try
            {
                #region 图片接收
                //Image
                HOperatorSet.CountChannels(Image, out var channels);
                if (channels == 1)
                {
                    HOperatorSet.Compose3(Image, Image, Image, out var multiChannelImage);
                    Image.Dispose();
                    Image = multiChannelImage;
                }

                HWindow hwindow = DataModel.FaraVisionDataModel.Settingmodel.HWindow;
                hwindow.ClearWindow();
                //hwindow.SetPart(0, 0, H - 1, W - 1);
                hwindow.DispObj(Image);
                for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
                {
                    if (DataModel.FaraVisionDataModel.Processmodel.Tools[i].Command == DataModel.FaraVisionDataModel.Processmodel.RCMD)
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();

                        if (i == 0)
                        {
                            DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.识别中;
                        }
                        DataModel.FaraVisionDataModel.Processmodel.ToolIndex = i + 1;
                        ToolModel tool = DataModel.FaraVisionDataModel.Processmodel.Tools[i];
                        Bitmap bmp;
                        try
                        {
                            var dst = GetReducedImage(DataModel.FaraVisionDataModel.Settingmodel.ImageSize, DataModel.FaraVisionDataModel.Settingmodel.ImageSize, Image);
                            Hobject2Bitmap.HobjectToBitmap24(dst, out bmp);
                            // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                            IntPtr hBitmap = bmp.GetHbitmap();
                            try
                            {
                                tool.CurrentBitmapSource = null;
                                tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            }
                            finally
                            {
                                DeleteObject(hBitmap); // 释放GDI句柄
                            }
                            bmp?.Dispose();
                            dst?.Dispose();
                        }
                        catch (Exception e)
                        {; }



                        //Bitmap bmp;
                        //try
                        //{
                        //    Hobject2Bitmap.HobjectToBitmap24(Image, out bmp);

                        //    using (Bitmap bmp1 = GetReducedImage(DataModel.Settingmodel.ImageSize, DataModel.Settingmodel.ImageSize, bmp))
                        //    {
                        //        tool.CurrentBitmapSource = null;
                        //        tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bmp1.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        //    }
                        //    bmp.Dispose();
                        //}
                        //catch (Exception ex)
                        //{

                        //}


                        ClearTool(tool);
                        tool.ToolStatus = ToolStatus.识别中;
                        try
                        {
                            if (tool.TestMode == TestModes.二维码)
                            {
                                #region 读取二维码
                                List<Reg.Barcode> barcodelist = null;

                                try
                                {
                                    barcodelist = tool.Reg.ReadBarcode(Image, 1, tool.BarCodeROI.Row1, tool.BarCodeROI.Col1, tool.BarCodeROI.Row2, tool.BarCodeROI.Col2, true);
                                    if (barcodelist.Count > 0)
                                    {
                                        tool.BarcodeStr = barcodelist[0].codestr;
                                        //  NoticeBox.Show(barcodelist[0].codestr, "二维码读取成功", MessageBoxIcon.Success, true, 5000);
                                        //if (tool.SendBarcodeData)
                                        //{
                                        //    SendMsgSoft(tool.BarcodeStr);
                                        //}
                                        writeLog($"二维码读取成功:{barcodelist[0].codestr}");

                                        if (tool.MPMode)
                                        {
                                            DataModel.FaraVisionDataModel.Processmodel.Barcodes += barcodelist[0].codestr + ";";
                                        }
                                        if (tool.SendBarcode)
                                        {
                                            tool.ToolStatus = ToolStatus.等待中;

                                            if (DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.Connect(DataModel.FaraVisionDataModel.Settingmodel.BarcodeReporter.RemoteIP, DataModel.FaraVisionDataModel.Settingmodel.BarcodeReporter.RemotePort))
                                            {

                                                DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.SendMsg($"{DataModel.FaraVisionDataModel.Processmodel.Scannerstr};{DataModel.FaraVisionDataModel.Processmodel.Barcodes}{tool.FinishedCode}\r\n");
                                                string s = DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.ReceiveMsg(10000);
                                                s = s.Replace("\r", "").Replace("\n", "");
                                                if (!string.IsNullOrEmpty(s))
                                                {
                                                    App.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                    {
                                                        DataModel.FaraVisionDataModel.Processmodel.SNList.Add(s);
                                                    }));
                                                    tool.ToolStatus = ToolStatus.OK;
                                                    writeLog($"扫码汇报软件返回成品编号:{s}");
                                                    DataModel.FaraVisionDataModel.Processmodel.Barcodes = string.Empty;


                                                }
                                                else
                                                {
                                                    tool.ToolStatus = ToolStatus.NG2;
                                                    writeLog($"扫码汇报软件返回为空");

                                                }

                                                DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.DisConnect();
                                            }
                                            else
                                            {
                                                tool.ToolStatus = ToolStatus.NG2;
                                                writeLog($"连接扫码汇报软件NG2");
                                            }


                                        }
                                        else
                                        {
                                            tool.ToolStatus = ToolStatus.OK;
                                        }



                                        //#region 发送二维码给上位机设备
                                        //DataModel.Settingmodel.TcpServerSoft.SendMessage(tool.BarcodeStr);
                                        //Thread.Sleep(tool.Delaytimes);
                                        //string s = DataModel.Settingmodel.TcpServerSoft.GetMsg();

                                        //writeLog($"二维码校验返回错误:{s}");

                                        //if (string.IsNullOrEmpty(s) || s != "OK")
                                        //{

                                        //}
                                        //else
                                        //{

                                        //}

                                        //if (!string.IsNullOrEmpty(tool.FinishedCode))
                                        //{
                                        //    DataModel.Settingmodel.TcpServerSoft.SendMessage(tool.FinishedCode);
                                        //    Thread.Sleep(tool.Delaytimes);
                                        //    string sn = DataModel.Settingmodel.TcpServerSoft.GetMsg();
                                        //    if (!string.IsNullOrEmpty(sn))
                                        //    {
                                        //        App.Current.Dispatcher.BeginInvoke(new Action(() =>
                                        //        {
                                        //            DataModel.Processmodel.SNList.Add(sn);
                                        //        }));
                                        //    }
                                        //}

                                        //#endregion


                                    }
                                    else
                                    {
                                        tool.BarcodeStr = string.Empty;
                                        writeLog($"二维码读取失败:{barcodelist[0].codestr}");
                                        tool.ToolStatus = ToolStatus.NG;

                                    }
                                }
                                catch
                                {
                                    ;
                                }

                                #endregion
                            }
                            else if (tool.TestMode == TestModes.模板匹配)
                            {
                                #region 读取位置
                                /// NG  未安装好  NG2:缺少配件  OK:合格              

                                try
                                {
                                    ShapeMatch.Result shapmatchresult = new ShapeMatch.Result();
                                    tool.ShapeMatch.BasicData.matchcenter_X = tool.InitX;
                                    tool.ShapeMatch.BasicData.matchcenter_Y = tool.InitY;
                                    tool.ShapeMatch.BasicData.matchcenterX_Basic = tool.InitX;
                                    tool.ShapeMatch.BasicData.matchcenterY_Basic = tool.InitY;
                                    tool.ShapeMatch.BasicData.productcenter_X = tool.InitX;
                                    tool.ShapeMatch.BasicData.productcenter_Y = tool.InitY;
                                    //shapmatchresult = tool.ShapeMatch.Match(Image, 1, tool.PositionROI.Row1, tool.PositionROI.Col1, tool.PositionROI.Row2, tool.PositionROI.Col2, -180, 180, false);
                                    shapmatchresult = tool.ShapeMatch.Match(Image, 1, tool.PositionROI.Row1, tool.PositionROI.Col1, tool.PositionROI.Row2, tool.PositionROI.Col2, (int)-tool.AllowAngleDelta, (int)tool.AllowAngleDelta, false);
                                    var Result_Data = tool.ShapeMatch.Analysis_Result(shapmatchresult);
                                    if (Result_Data != null)
                                    {
                                        tool.ActualX = Result_Data.X_actual;
                                        tool.ActualY = Result_Data.Y_actual;
                                        tool.ActualAngle = (Result_Data.angle / Math.PI * 180.0);
                                        tool.ActualScore = Result_Data.score;
                                        tool.DeltaX = Result_Data.deltaX_actual * tool.K / 1000;
                                        tool.DeltaY = Result_Data.deltaY_actual * tool.K / 1000;


                                        if (tool.ActualScore >= tool.MinScore)
                                        {
                                            if (Math.Abs(tool.DeltaX) < tool.Allow_X_Delta &&
                                                Math.Abs(tool.DeltaY) < tool.Allow_Y_Delta &&
                                                Math.Abs(tool.ActualAngle) < tool.AllowAngleDelta
                                                )
                                            {
                                                //if (tool.SendPositionData)
                                                //{
                                                //    SendMsgRobot($"{sendstr},");
                                                //}
                                                tool.ToolStatus = ToolStatus.OK;
                                            }
                                            else
                                            {
                                                tool.ToolStatus = ToolStatus.NG;

                                            }

                                        }
                                        else
                                        {
                                            tool.ToolStatus = ToolStatus.NG2;
                                        }

                                    }
                                    else
                                    {
                                        tool.ToolStatus = ToolStatus.NG2;
                                    }
                                }
                                catch
                                {
                                    ;
                                }


                                #endregion
                            }
                            else if (tool.TestMode == TestModes.面积)
                            {
                                #region 读取面积

                                int Areaint = CoculateDimension(Image, tool, hwindow, false);
                                tool.ActualDimension = Areaint;

                                if (Areaint >= tool.MinDimension && Areaint <= tool.MaxDimension)
                                {
                                    tool.ToolStatus = ToolStatus.OK;
                                }
                                else
                                {
                                    tool.ToolStatus = ToolStatus.NG;
                                }

                                GC.Collect();
                                #endregion
                            }
                            else if (tool.TestMode == TestModes.尺寸测量)
                            {
                                #region 尺寸测量

                                try
                                {
                                    double measureValue = MeasureDimension(Image, tool, hwindow, false);
                                    tool.ActualMeasureValue = measureValue;

                                    if (measureValue >= 0 && measureValue >= tool.MinMeasureValue && measureValue <= tool.MaxMeasureValue)
                                    {
                                        tool.ToolStatus = ToolStatus.OK;
                                    }
                                    else if (measureValue < 0)
                                    {
                                        tool.ToolStatus = ToolStatus.NG2; // 测量失败
                                    }
                                    else
                                    {
                                        tool.ToolStatus = ToolStatus.NG; // 测量值超出范围
                                    }
                                }
                                catch (Exception ex)
                                {
                                    tool.ToolStatus = ToolStatus.NG2;
                                    writeLog($"尺寸测量失败: {ex.Message}", false);
                                }

                                GC.Collect();
                                #endregion
                            }
                        }
                        catch {; }

                        writeLog($"视觉->视觉:计算完成", false);
                        string s1 = $"{DataModel.FaraVisionDataModel.Processmodel.Tools[i].Name}:识别耗时:{stopwatch.ElapsedMilliseconds}ms";
                        Save_record(s1);
                        stopwatch.Restart();

                        #region 保存图片
                        try
                        {
                            if (tool.ToolStatus == ToolStatus.OK)
                            {
                                if (DataModel.Settingmodel.ImageSaveSetting.SaveOK)
                                {

                                    string savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\OK\\{DataModel.FaraVisionDataModel.Processmodel.BarcodeStr}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    try
                                    {
                                        savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\OK\\{DataModel.FaraVisionDataModel.Processmodel.SNList[DataModel.FaraVisionDataModel.Processmodel.Tools[i].ProductPositionNO]}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    }
                                    catch {; }

                                    string dir = Path.GetDirectoryName(savefilename);
                                    if (!Directory.Exists(dir))
                                    { Directory.CreateDirectory(dir); }
                                    HOperatorSet.WriteImage(Image, "jpg", 0, savefilename);
                                }
                            }
                            else
                            {
                                if (DataModel.Settingmodel.ImageSaveSetting.SaveNG)
                                {
                                    string savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\NG\\{DataModel.FaraVisionDataModel.Processmodel.BarcodeStr}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    try
                                    {
                                        savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\NG\\{DataModel.FaraVisionDataModel.Processmodel.SNList[DataModel.FaraVisionDataModel.Processmodel.Tools[i].ProductPositionNO]}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    }
                                    catch {; }
                                    string dir = Path.GetDirectoryName(savefilename);
                                    if (!Directory.Exists(dir))
                                    { Directory.CreateDirectory(dir); }
                                    HOperatorSet.WriteImage(Image, "jpg", 0, savefilename);

                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            writeLog($"视觉->保存照片:保存失败：{ex.ToString()}", false);
                        }

                        #endregion

                        if (tool.SendStatus)
                        {

                            int status = -1;

                            var r = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                     where ToolModel.Command == DataModel.FaraVisionDataModel.Processmodel.RCMD
                                     select ToolModel);
                            var wait = (from ToolModel in r
                                        where (ToolModel.ToolStatus == ToolStatus.等待中 || ToolModel.ToolStatus == ToolStatus.识别中)
                                        select ToolModel);

                            if (wait.Count() == 0)
                            {
                                var ok = (from ToolModel in r
                                          where (ToolModel.ToolStatus == ToolStatus.OK)
                                          select ToolModel);

                                if (ok.Count() == r.Count())
                                {
                                    status = 0;
                                }
                                else
                                {
                                    var NG2 = (from ToolModel in r
                                               where (ToolModel.ToolStatus == ToolStatus.NG2)
                                               select ToolModel);
                                    if (NG2.Count() > 0)
                                    {
                                        status = 2;
                                    }
                                    else
                                    {
                                        status = 1;
                                    }

                                }
                            }

                            switch (status)
                            {

                                case 0:
                                    {
                                        SendMsgRobot(tool.OKCMD.Trim());
                                        break;
                                    }
                                case 1:
                                    {
                                        SendMsgRobot(tool.NG1CMD.Trim());
                                        break;
                                    }
                                case 2:
                                    {
                                        SendMsgRobot(tool.NG2CMD.Trim());
                                        break;
                                    }
                                default:
                                    {
                                        break;
                                    }
                            }
                        }


                        if (i == DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1)
                        {
                            int status = -1;
                            int c = DataModel.FaraVisionDataModel.Processmodel.Tools.Count();


                            var ok = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                      where (ToolModel.ToolStatus == ToolStatus.OK)
                                      select ToolModel);
                            var NG1 = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                       where (ToolModel.ToolStatus == ToolStatus.NG)
                                       select ToolModel);
                            var NG2 = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                       where (ToolModel.ToolStatus == ToolStatus.NG2)
                                       select ToolModel);
                            var wait = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                        where (ToolModel.ToolStatus == ToolStatus.等待中 || ToolModel.ToolStatus == ToolStatus.识别中)
                                        select ToolModel);

                            if (ok.Count() == c)
                            {
                                status = 0;
                            }
                            else if (NG2.Count() > 0)
                            {
                                status = 2;
                            }
                            else
                            {
                                status = 1;
                            }

                            if (status == 0)
                            {
                                DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.OK;
                                //PLC_write((UInt16)1);
                            }
                            else if (status == 2)
                            {
                                DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.NG2;
                                //PLC_write((UInt16)3);
                            }
                            else
                            {
                                DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.NG;
                                //PLC_write((UInt16)2);
                            }

                            #region 保存拍照记录到本地
                            DateTime dt = DateTime.Now;
                            updatetakephoto2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, status == 0, dt);
                            sqlite.UpdateTakePhoto2(
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                              status == 0);
                            #endregion
                        }






                        writeLog($"视觉->视觉:保存完成", false);

                        string s2 = $"{DataModel.FaraVisionDataModel.Processmodel.Tools[i].Name}:保存图片发送结果耗时:{stopwatch.ElapsedMilliseconds}ms";
                        Save_record(s2);




                    }
                }
                #endregion

            }
            catch (Exception ex) {; }

        }
        public void ClearTools()
        {
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                ClearTool(DataModel.FaraVisionDataModel.Processmodel.Tools[i]);
            }
        }

        public void ClearTool(ToolModel tool)
        {
            tool.BarcodeStr = string.Empty;
            tool.DeltaX = 0;
            tool.DeltaY = 0;
            tool.ActualScore = 0;
            tool.ActualX = 0;
            tool.ActualY = 0;
            tool.ActualAngle = 0;
            tool.ActualDimension = 0;
            tool.ToolStatus = ToolStatus.等待中;

        }

        #endregion

        #region 外观检测
        #endregion
        #endregion

        #region 机器人
        public void SendMsgRobot(string cmd)
        {
            DataModel.Settingmodel.TcpServerRobot.SendMessage(cmd);
            writeLog($"视觉->机器人:{cmd}");
        }

        public void InitRobotServer()
        {

            DataModel.Settingmodel.TcpServerRobot = new TCPServerH();


            DataModel.Settingmodel.TcpServerRobot.ClientConnected += RobotTcpServer_ClientConnected;
            DataModel.Settingmodel.TcpServerRobot.ClientDisconnected += RobotTcpServer_ClientDisconnected;
            DataModel.Settingmodel.TcpServerRobot.MessageReceived += RobotTcpServer_MessageReceived;
            new Thread(() =>
            {
                try
                {
                    DataModel.Settingmodel.TcpServerRobot.StartListener(DataModel.Settingmodel.RobotConnect.LocalIP, DataModel.Settingmodel.RobotConnect.LocalPort);
                    writeLog("TCP服务端启动侦听[等待机器人连线]");
                }
                catch (Exception ex2)
                {
                    writeError(ex2.Message + Environment.NewLine + ex2.StackTrace);
                }
            }).Start(); ;

        }
        private void RobotTcpServer_ClientConnected(TCPServerH sender, object e)
        {
            try
            {
                TCPevent TCPevent = (TCPevent)e;
                if (TCPevent.Msg == "客户端连接")
                {
                    writeLog("机器人已经连接");
                    DataModel.Settingmodel.RobotConnect.IsConnected = true;
                }
                else if (TCPevent.Msg == "客户端重新连接")
                {
                    writeLog("机器人重新连接");
                    DataModel.Settingmodel.RobotConnect.IsConnected = true;
                }
            }
            catch (Exception ex) {; }
        }

        private void RobotTcpServer_ClientDisconnected(TCPServerH sender, object e)
        {
            try
            {
                TCPevent TCPevent = (TCPevent)e;
                if (TCPevent.Msg == "客户端掉线")
                {
                    writeLog("机器人已经离线");
                    DataModel.Settingmodel.RobotConnect.IsConnected = false;
                }
                else if (TCPevent.Msg == "发送失败，客户端掉线")
                {
                    writeLog("发送失败，客户端掉线");
                    DataModel.Settingmodel.RobotConnect.IsConnected = false;
                }
                else
                {
                    writeLog(TCPevent.Msg);
                    DataModel.Settingmodel.RobotConnect.IsConnected = false;
                }
            }
            catch (Exception ex) {; }
        }

        public void Save_record(string info)
        {
            if (DataModel.FaraVisionDataModel.Settingmodel.SaveProcessData)
            {
                DateTime dt = DateTime.Now;
                string filename = $"{Environment.CurrentDirectory}\\识别过程日志\\{dt.ToString("yyyy-MM-dd")}\\{dt.ToString("yyyyMMddHH")}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    sw.WriteLine($"[{dt.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{info}");
                }
            }
        }

        /// <summary>
        /// 机器人TCP服务端消息接收处理方法
        /// 业务流程：机器人作为客户端连接本视觉系统，通过指令驱动各工位的检测流程
        /// 
        /// 指令类型说明：
        /// 1. "A"开头指令 - 拍照触发指令（如A1、A2等）
        ///    - 根据指令匹配视觉工具配置中的Command字段
        ///    - 自动切换对应相机的曝光时间
        ///    - 触发相机拍照并启动视觉检测流程
        /// 
        /// 2. "CHECK1" - 第一次数据校验（外观检测前）
        ///    - 从PLC读取当前产品编号（SN）
        ///    - 从PLC读取压力测试数据（平均值、最大值、最小值）
        ///    - 调用sqlite.Check1校验拍照留底、耐压测试、阻值测试
        ///    - 根据校验结果返回OK/NG1/NG2/NG3给机器人
        ///    - 保存过程数据到MES服务器并报工
        /// 
        /// 3. "CHECK2" - 第二次数据校验（最终出站前）
        ///    - 调用sqlite.Check2校验所有工序（包括外观检测）
        ///    - 根据校验结果返回OK/NG1~NG4给机器人
        ///    - 保存过程数据到MES服务器并报工（第二工站）
        /// 
        /// - 机器人控制产品流转节奏，视觉系统被动响应
        /// - 通过两次CHECK实现分段校验：CHECK1拦截前工序不良品，CHECK2最终全检
        /// - 所有结果通过TCP即时反馈给机器人，实现自动分拣
        /// </summary>
        private void RobotTcpServer_MessageReceived(TCPServerH sender, object e)
        {
            try
            {

                TCPevent TCPevent = (TCPevent)e;
                string cmd = (string)TCPevent.Msg;
                writeLog($"机器人->视觉:{cmd}");
                DataModel.Processmodel.CMD = cmd;
                DataModel.FaraVisionDataModel.Processmodel.RCMD = cmd;

                if (cmd.StartsWith("A"))
                {

                    //#region 临时拍照代码
                    //string s = cmd.Replace("A", "");
                    //int cmdint = -1;
                    //if (int.TryParse(s, out cmdint))
                    //{
                    //    if (cmdint <= -1)
                    //    {
                    //        DataModel.Settingmodel.camedata4.CameraModel.camera.bnTriggerExec_Click();
                    //    }
                    //    else
                    //    {
                    //        DataModel.Settingmodel.camedata5.CameraModel.camera.bnTriggerExec_Click();
                    //    }
                    //}

                    //#endregion


                    #region 正确拍照代码

                    for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
                    {
                        if (DataModel.FaraVisionDataModel.Processmodel.Tools[i].Command == cmd)
                        {
                            if (i == 0)
                            {
                                ClearTools();
                            }
                            writeLog($"机器人->视觉:{cmd}开始设置参数", false);
                            DataModel.FaraVisionDataModel.Processmodel.ToolIndex = i + 1;
                            int cameraindex = DataModel.FaraVisionDataModel.Processmodel.Tools[i].CameraIndex;
                            DataModel.FaraVisionDataModel.Processmodel.CameraList[cameraindex].CameraModel.exposuretime = DataModel.FaraVisionDataModel.Processmodel.Tools[i].ExposureTime;
                            DataModel.FaraVisionDataModel.Processmodel.CameraList[cameraindex].CameraModel.camera.Exposure = DataModel.FaraVisionDataModel.Processmodel.Tools[i].ExposureTime;
                            DataModel.FaraVisionDataModel.Processmodel.CameraList[cameraindex].CameraModel.camera.bnSetParam_Click();
                            Thread.Sleep(DataModel.FaraVisionDataModel.Settingmodel.delaytime);
                            writeLog($"机器人->视觉:{cmd}开始触发", false);
                            DataModel.FaraVisionDataModel.Processmodel.CameraList[cameraindex].CameraModel.camera.bnTriggerExec_Click();
                            writeLog($"机器人->视觉:{cmd}触发完成", false);

                            break;
                        }
                    }


                    #endregion






                }
                else if (cmd == "CHECK1")
                {
                    #region 读取产品编号
                    // 从PLC的第5个SN地址读取产品信息（AddressSN + 25*4）
                    // PLC中存储格式："产品序列号;工单号"，通过分号分隔
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 4);
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                        DataModel.Processmodel.TakePhotoTestMode2.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                        App.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {

                            DataModel.FaraVisionDataModel.Processmodel.SNList.Clear();
                            DataModel.FaraVisionDataModel.Processmodel.SNList.Add(ss[0]);
                        }));
                    }
                    else
                    {
                        writeLog($"视觉检测产品编号读取错误:{s}");
                    }

                    #endregion

                    // 点检SN码特殊处理：跳过校验和报工，但保留其他流程
                    // 这里的点检 SN 包含「耐压点检」和「AOI 点检」两类 SN 码
                    bool isInspectionSN = ss.Length == 2 &&
                        (ss[0] == DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN ||
                         ss[0] == DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN ||
                         ss[0] == DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN ||
                         ss[0] == DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN);

                    // AOI 点检 SN：在 CHECK1 中无论实际测试结果如何，都强制返回 OK
                    bool isAoiInspectionSN = ss.Length == 2 &&
                        (ss[0] == DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN ||
                         ss[0] == DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN);

                    if (isInspectionSN)
                    {
                        // 从 ProductInfoRecords 取数并做综合判断的整体思路：
                        // 1. 先用 SN 在集合中找到对应的 ProductInfoRecord；
                        // 2. 使用记录中的拍照、耐压、阻值、压力等字段进行一次「内存级」综合判定；
                        // 3. 不再调用 sqlite.Check1，避免重复访问数据库；
                        // 4. 仅将最终判定结果连同过程数据一起 SaveBusBarData 到 MES，不做报工。
                        // 点检SN码：和正常流程一样读取压力数据，但跳过数据库校验和报工
                        #region 读取压力数据
                        // 从PLC读取电测过程中的压力监控数据
                        // AddressPressure: 平均压力, +2: 最大压力, +4: 最小压力
                        UInt16 AveragePressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure);
                        UInt16 MaxPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 2);
                        UInt16 MinPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 4);

                        // 压力判定逻辑：最大值不超标 且 最小值达标（确保压接到位且不过压）
                        bool PressureResult = MaxPressure <= DataModel.Processmodel.PressureParamter.Max_Pressure && MinPressure >= DataModel.Processmodel.PressureParamter.Min_Pressure;

                        sqlite.UpdatePressure(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                           AveragePressure, MaxPressure, MinPressure, PressureResult);
                        updatepressure(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, AveragePressure, MaxPressure, MinPressure, PressureResult);

                        #endregion

                        // 点检SN码：根据实际测试数据进行综合判断（不调用数据库校验）
                        string MSG = "NG1";
                        string resultstr = "拍照留底不良";
                        
                        // 从 ProductInfoRecords 中获取实际测试数据（拍照留底、耐压、阻值、压力等）
                        ProductInfoRecord pi = null;
                        foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                        {
                            if (DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN == p.Productinfo.SN)
                            {
                                pi = p;
                                break;
                            }
                        }

                        if (pi != null)
                        {
                            if (isAoiInspectionSN)
                            {
                                // AOI 点检 SN：CHECK1 场景下直接视为 OK（不考虑耐压/阻值等结果）
                                MSG = "OK";
                                resultstr = "合格";
                            }
                            else
                            {
                                // 非 AOI 点检 SN：根据实际测试数据综合判断（逻辑同 Check1）
                                if (!pi.TakePhoto1)
                                {
                                    MSG = "NG1";
                                    resultstr = "拍照留底不良";
                                }
                                // 先判断阻值，再判断耐压，阻值大于14为不合格
                                else if (pi.Res > 14)
                                {
                                    MSG = "NG3";
                                    resultstr = "阻值测试不合格";
                                }
                                else if (pi.TVMaxVoltage == 0 || pi.TVMaxVoltage == -1 || !pi.TVResult)
                                {
                                    MSG = "NG2";
                                    resultstr = "耐压测试不合格";
                                }
                                else
                                {
                                    MSG = "OK";
                                    resultstr = "合格";
                                }
                            }
                        }

                        #region 保存过程数据到服务器
                        // 这里仍然把 ProductInfoRecords 中汇总的过程数据保存到 MES，
                        // 但不会触发报工，仅用于过程追溯和点检记录留档。
                        if (pi != null)
                        {
                            var persistedTvInfo = GetLocalizedTvStatus(pi.TVInfo);
                            MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                               DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, pi.Productinfo.PartNOID, pi.Productinfo.WOCODE, pi.Productinfo.SN,
                                pi.TakePhoto1, pi.Res, pi.TVMaxVoltage, pi.TVMaxCurrent, pi.TVMeterID, persistedTvInfo, pi.TVResult,
                                pi.Pressure_Max, pi.Pressure_Average, pi.Pressure_Min, pi.Pressure_Result, pi.AppearanceInspection, resultstr);
                        }
                        #endregion
                        // 点检SN码不进行报工，跳过 report 调用

                        writeLog($"数据校验1->点检SN码->结果:{resultstr}");
                        SendMsgRobot(MSG);
                    }
                    else
                    {
                        // 正常产品流程
                        #region 读取压力数据
                        // 从PLC读取电测过程中的压力监控数据
                        // AddressPressure: 平均压力, +2: 最大压力, +4: 最小压力
                        UInt16 AveragePressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure);
                        UInt16 MaxPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 2);
                        UInt16 MinPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 4);

                        // 压力判定逻辑：最大值不超标 且 最小值达标（确保压接到位且不过压）
                        bool PressureResult = MaxPressure <= DataModel.Processmodel.PressureParamter.Max_Pressure && MinPressure >= DataModel.Processmodel.PressureParamter.Min_Pressure;

                        sqlite.UpdatePressure(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                           AveragePressure, MaxPressure, MinPressure, PressureResult);
                        updatepressure(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, AveragePressure, MaxPressure, MinPressure, PressureResult);

                        #endregion


                        // 调用Check1进行第一次综合校验（拍照留底、耐压测试、阻值测试）
                        var r = sqlite.Check1(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN);
                        string MSG = "NG1";
                        // 初始化resultstr为"拍照留底不良"作为默认值（兜底）
                        // 实际会根据Check1返回值在switch中被覆盖
                        string resultstr = "拍照留底不良";
                        // 根据Check1返回的错误代码，映射为机器人可识别的消息和中文结果描述
                        // Check1返回值：0=全部合格, 1=拍照不良, 2=耐压不良, 3=阻值不良
                        // 注意：case 4(AOI不良)在Check1中不会出现，仅在Check2中才有
                        switch (r)
                        {
                            case 0:
                                {
                                    MSG = "OK";
                                    resultstr = "合格";
                                    break;
                                }
                            case 1:
                                {
                                    MSG = "NG1";
                                    resultstr = "拍照留底不良";
                                    break;
                                }
                            case 2:
                                {
                                    MSG = "NG2";
                                    resultstr = "耐压测试不合格";
                                    break;
                                }
                            case 3:
                                {
                                    MSG = "NG3";
                                    resultstr = "阻值测试不合格";
                                    break;
                                }
                            case 4:
                                {
                                    MSG = "NG4";
                                    resultstr = "AOI测试不合格";
                                    break;
                                }
                        }

                        //if (MSG != "OK")
                        //{

                        #region 保存过程数据到服务器

                        // CHECK1 正常流程下，同样通过 SN 在 ProductInfoRecords 中找到对应记录，
                        // 再将其中汇总好的过程数据一次性写入 MES。
                        foreach (var pi in DataModel.Recordmodel.ProductInfoRecords)
                        {
                            if (DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN == pi.Productinfo.SN)
                            {
                                var persistedTvInfo = GetLocalizedTvStatus(pi.TVInfo);
                                MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                                   DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, pi.Productinfo.PartNOID, pi.Productinfo.WOCODE, pi.Productinfo.SN,
                                    pi.TakePhoto1, pi.Res, pi.TVMaxVoltage, pi.TVMaxCurrent, pi.TVMeterID, persistedTvInfo, pi.TVResult,
                                    pi.Pressure_Max, pi.Pressure_Average, pi.Pressure_Min, pi.Pressure_Result, pi.AppearanceInspection, resultstr);
                                break;
                            }
                        }
                        #endregion
                        #region 汇报结果数据
                        report(ss[1], ss[0], resultstr);
                        #endregion

                        //}

                        writeLog($"数据校验1->结果:{resultstr}");
                        SendMsgRobot(MSG);
                    }

                }
                else if (cmd == "CHECK2")
                {
                    // SendMsgRobot("OK");

                    // 点检SN码特殊处理：跳过校验和报工，但保留其他流程
                    // 这里的点检 SN 同样包含「耐压点检」和「AOI 点检」两类 SN 码
                    string currentSN = DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN;
                    bool isInspectionSN = !string.IsNullOrEmpty(currentSN) &&
                        (currentSN == DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN ||
                         currentSN == DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN ||
                         currentSN == DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN ||
                         currentSN == DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN);

                    // 判断是否是 AOI 点检 SN（在 CHECK2 中只需检查 AOI 结果，不检查耐压、阻值）
                    bool isAoiInspectionSN = !string.IsNullOrEmpty(currentSN) &&
                        (currentSN == DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN ||
                         currentSN == DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN);

                    if (isInspectionSN)
                    {
                        // 从 ProductInfoRecords 取数并做综合判断的整体思路：
                        // 1. 先用 SN 在集合中找到对应的 ProductInfoRecord；
                        // 2. 使用记录中的拍照、耐压、阻值、AOI、压力等字段进行一次「内存级」综合判定；
                        // 3. 不再调用 sqlite.Check2，避免重复访问数据库；
                        // 4. 仅将最终判定结果连同过程数据一起 SaveBusBarData 到 MES，不做报工。
                        // 点检SN码：根据实际测试数据（包括AOI结果）进行综合判断，跳过数据库校验和报工
                        string MSG = "NG1";
                        string resultstr = "拍照留底不良";
                        
                        // 从 ProductInfoRecords 中获取实际测试数据（拍照留底、耐压、阻值、AOI、压力等）
                        ProductInfoRecord pi = null;
                        foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                        {
                            if (DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN == p.Productinfo.SN)
                            {
                                pi = p;
                                break;
                            }
                        }

                        if (pi != null)
                        {
                            // 区分 AOI 点检和耐压点检的判断逻辑
                            if (isAoiInspectionSN)
                            {
                                // AOI 点检 SN：只判断 AOI 结果，跳过拍照、耐压、阻值检查
                                if (!pi.AppearanceInspection)
                                {
                                    MSG = "NG4";
                                    resultstr = "AOI测试不合格";
                                }
                                else
                                {
                                    MSG = "OK";
                                    resultstr = "合格";
                                }
                            }
                            else
                            {
                                // 耐压点检 SN：完整判断拍照、耐压、阻值、AOI
                                if (!pi.TakePhoto1)
                                {
                                    MSG = "NG1";
                                    resultstr = "拍照留底不良";
                                }
                                // 先判断阻值，再判断耐压，阻值大于14为不合格
                                else if (pi.Res > 14)
                                {
                                    MSG = "NG3";
                                    resultstr = "阻值测试不合格";
                                }
                                else if (pi.TVMaxVoltage == 0 || pi.TVMaxVoltage == -1 || !pi.TVResult)
                                {
                                    MSG = "NG2";
                                    resultstr = "耐压测试不合格";
                                }
                                else if (!pi.AppearanceInspection)
                                {
                                    MSG = "NG4";
                                    resultstr = "AOI测试不合格";
                                }
                                else
                                {
                                    MSG = "OK";
                                    resultstr = "合格";
                                }
                            }
                        }

                        #region 保存过程数据到服务器
                        // 点检场景下，同样把综合判定后的一条完整过程数据写入 MES，
                        // 便于区分「正式生产」与「点检」记录，且不触发报工。
                        if (pi != null)
                        {
                            var persistedTvInfo = GetLocalizedTvStatus(pi.TVInfo);
                            MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                            DataModel.Settingmodel.SETTING_DATA.StationCode2, DataModel.Settingmodel.SETTING_DATA.MachineID, pi.Productinfo.PartNOID, pi.Productinfo.WOCODE, pi.Productinfo.SN,
                            pi.TakePhoto1, pi.Res, pi.TVMaxVoltage, pi.TVMaxCurrent, pi.TVMeterID, persistedTvInfo, pi.TVResult,
                            pi.Pressure_Max, pi.Pressure_Average, pi.Pressure_Min, pi.Pressure_Result, pi.AppearanceInspection, resultstr);
                        }
                        #endregion
                        // 点检SN码不进行报工，跳过 report2 调用

                        writeLog($"数据校验2->点检SN码->结果:{resultstr}");
                        SendMsgRobot(MSG);
                    }
                    else
                    {
                        // 正常产品流程
                        var r = sqlite.Check2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN);
                        string MSG = "NG1";
                        string resultstr = "拍照留底不良";
                        switch (r)
                        {
                            case 0:
                                {
                                    MSG = "OK";
                                    resultstr = "合格";
                                    break;
                                }
                            case 1:
                                {
                                    MSG = "NG1";
                                    resultstr = "拍照留底不良";
                                    break;
                                }
                            case 2:
                                {
                                    MSG = "NG2";
                                    resultstr = "耐压测试不合格";
                                    break;
                                }
                            case 3:
                                {
                                    MSG = "NG3";
                                    resultstr = "阻值测试不合格";
                                    break;
                                }
                            case 4:
                                {
                                    MSG = "NG4";
                                    resultstr = "AOI测试不合格";
                                    break;
                                }
                        }

                        writeLog($"数据校验2->结果:{resultstr}");
                        SendMsgRobot(MSG);

                        #region 保存过程数据到服务器

                        foreach (var pi in DataModel.Recordmodel.ProductInfoRecords)
                        {
                            if (DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN == pi.Productinfo.SN)
                            {
                                var persistedTvInfo = GetLocalizedTvStatus(pi.TVInfo);
                                MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                                DataModel.Settingmodel.SETTING_DATA.StationCode2, DataModel.Settingmodel.SETTING_DATA.MachineID, pi.Productinfo.PartNOID, pi.Productinfo.WOCODE, pi.Productinfo.SN,
                                pi.TakePhoto1, pi.Res, pi.TVMaxVoltage, pi.TVMaxCurrent, pi.TVMeterID, persistedTvInfo, pi.TVResult,
                                pi.Pressure_Max, pi.Pressure_Average, pi.Pressure_Min, pi.Pressure_Result, pi.AppearanceInspection, resultstr);
                                break;
                            }
                        }

                        #endregion
                        #region 汇报结果数据                    
                        report2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, resultstr);
                        #endregion
                    }

                }


            }
            catch (Exception ex) {; }
        }





        private bool report(string wocode, string sn, string result)
        {
            try
            {
                var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Save_EquipmentRecord_mes(
                        DataModel.Settingmodel.SETTING_DATA.StationCode,
                        wocode,
                        sn,
                        DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                        DataModel.Settingmodel.SETTING_DATA.MachineID,
                        result == "OK" ? "合格" : result,
                        DataModel.Settingmodel.SETTING_DATA.StandardCode
                        );

                if (r)
                {
                    writeLog($"{sn}:{result};报工:True");
                }
                else
                {
                    writeLog($"{sn}:{result};报工:False(业务逻辑问题请查看日志)");
                }
                return r;
            }
            catch (Exception ex)
            {
                writeLog($"{sn}:{result};报工:False(程序异常)");
                writeError($"报工程序异常: SN={sn}, 工单={wocode}, 异常={ex.Message}");
                return false;
            }
        }
        private bool report2(string wocode, string sn, string result)
        {
            try
            {
                var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Save_EquipmentRecord_mes(
                        DataModel.Settingmodel.SETTING_DATA.StationCode2,
                        wocode,
                        sn,
                        DataModel.Settingmodel.SETTING_DATA.ProcedureName2,
                        DataModel.Settingmodel.SETTING_DATA.MachineID,
                        result == "OK" ? "合格" : result,
                        DataModel.Settingmodel.SETTING_DATA.StandardCode2
                        );

                if (r)
                {
                    writeLog($"{sn}:{result};报工2:True");
                }
                else
                {
                    writeLog($"{sn}:{result};报工2:False(业务逻辑问题请查看日志)");
                }
                return r;
            }
            catch (Exception ex)
            {
                writeLog($"{sn}:{result};报工2:False(程序异常)");
                writeError($"报工2程序异常: SN={sn}, 工单={wocode}, 异常={ex.Message}");
                return false;
            }
        }




        #endregion

        #region 照片存储
        public void SaveImage(HObject Image, string sn, string type, int index, string Result)
        {
            string savefilename = $"{DataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\{DateTime.Now.ToString("yyyyMMdd")}\\{type}\\{Result}\\{sn}-{index.ToString("00")}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
            string dir = Path.GetDirectoryName(savefilename);
            if (!Directory.Exists(dir))
            { Directory.CreateDirectory(dir); }
            HOperatorSet.WriteImage(Image, "jpg", 0, savefilename);
        }
        #endregion

        #region 性能日志
        /// <summary>
        /// 统一的性能诊断日志方法
        /// 写入性能诊断日志到独立文件,不影响现有业务日志
        /// 超过1秒的耗时会用特殊格式突出显示
        /// </summary>
        /// <param name="component">组件标识，如BUSINESS、UI等</param>
        /// <param name="tag">日志标签,如 SCAN_SUCCESS、PROCESS_COMPLETE等</param>
        /// <param name="message">日志消息</param>
        /// <param name="elapsedMs">耗时(毫秒),可选</param>
        /// <param name="extraInfo">额外信息,可选</param>
        private void writePerfLog(string component, string tag, string message, long? elapsedMs = null, string extraInfo = null)
        {
            try
            {
                // 获取线程ID
                int threadId = Thread.CurrentThread.ManagedThreadId;
                string threadName = Thread.CurrentThread.Name ?? (threadId == 1 ? "UI-Thread" : $"Thread-{threadId}");

                // 获取设备ID(从配置中读取)
                string deviceId = DataModel.Settingmodel.SETTING_DATA?.MachineID ?? "Unknown";

                // 检测是否为异常耗时（超过1秒）
                bool isAbnormalTime = elapsedMs.HasValue && elapsedMs.Value > 1000;
                string perfLevel = isAbnormalTime ? "PERF-异常" : "PERF";

                // 构建日志内容
                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}]");
                logBuilder.Append($"[{threadName}]");
                logBuilder.Append($"[{component}_{tag}]");
                logBuilder.Append($"[{perfLevel}]");
                logBuilder.Append($"[Device-{deviceId}] {message}");

                if (elapsedMs.HasValue)
                {
                    string timeDisplay = isAbnormalTime ?
                        $"耗时={elapsedMs.Value}ms[异常!!!]" :
                        $"耗时={elapsedMs.Value}ms";
                    logBuilder.Append($" | {timeDisplay}");
                }

                if (!string.IsNullOrEmpty(extraInfo))
                {
                    logBuilder.Append($" | {extraInfo}");
                }

                string logContent = logBuilder.ToString();

                // 写入独立的性能日志文件
                string filename = $"{Environment.CurrentDirectory}\\日志\\性能诊断\\{DateTime.Now.ToString("yyyyMMdd")}_performance.log";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 使用文件锁确保多线程安全
                lock (writeLog_Locker)
                {
                    using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                    {
                        sw.WriteLine(logContent);
                        sw.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                // 性能日志失败不应影响业务,仅记录到错误日志
                try
                {
                    writeError($"性能日志写入失败: {ex.Message}");
                }
                catch { }
            }
        }
        #endregion
    }
}