/*
 * 主要业务逻辑控制器
 *
 * MVVM架构中的ViewModel层核心，负责所有业务逻辑：
 * 1. 硬件设备控制：相机、PLC、测试仪器、机器人协调工作
 * 2. 生产流程管理：产品检测、测试、数据记录的完整流程
 * 3. 数据处理：XML序列化保存/加载配置，数据库交互
 * 4. 算法集成：Halcon视觉算法、位置检测算法等
 * 5. 多线程管理：并发处理多工位测试和检测任务
 *
 * 核心业务流程：
 * 产品进站 → 视觉检测 → 耐压测试 → 数据记录 → MES报工
 */

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
        #region 静态锁对象
        /// <summary>
        /// 尺寸测量日志写入锁
        /// </summary>
        private static readonly object _measurementLogLock = new object();
        #endregion

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
            
            // 订阅AT9620日志事件，将设备日志转发到应用日志
            DataModel.Settingmodel.AT9620_1.LogMessage += (s, e) => writeLog($"[耐压1] {e.Message}");
            DataModel.Settingmodel.AT9620_2.LogMessage += (s, e) => writeLog($"[耐压2] {e.Message}");
            DataModel.Settingmodel.AT9620_3.LogMessage += (s, e) => writeLog($"[耐压3] {e.Message}");
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
        /// 处理扫码数据，完成产品SN的解码、验证和数据库记录创建
        /// </summary>
        /// <param name="snstr">扫码原始数据字符串</param>
        /// <returns>
        /// 返回错误信息字符串，如果处理成功则返回空字符串
        /// 可能的错误信息包括：
        /// - 标签解码失败
        /// - 混批错误（不同规格产品）
        /// - 工单号查询失败
        /// - 物料编码查询失败
        /// </returns>
        /// <remarks>
        /// 处理流程：
        /// 1. 解码扫码数据获取产品SN
        /// 2. 根据SN查询MES系统获取工单号和物料编码
        /// 3. 验证物料编码是否与当前产线规格一致（防止混批）
        /// 4. 验证数据完整性（工单号和物料编码不能为空）
        /// 5. 将SN和工单号写入PLC
        /// 6. 在本地数据库创建生产记录
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
                writeLog($"点检扫码原始数据: {snstr}");
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(snstr);
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(snstr);

                PLC_Writestring(DataModel.Settingmodel.AddressSN.ToString(), $"{snstr};{wocode}");
                writeLog($"写入PLC地址 {DataModel.Settingmodel.AddressSN}: {snstr};{wocode}");

                sqlite.CREATENEWLINE(wocode, partnoid, snstr, DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, DateTime.Now);
                writeLog($"创建数据库记录: wocode={wocode}, partnoid={partnoid}, SN={snstr}, 工位={DataModel.Settingmodel.SETTING_DATA.StationCode}, 设备={DataModel.Settingmodel.SETTING_DATA.MachineID}");

                writeLog($"✓ 点检扫码处理成功！");
                return string.Empty;
            }

            //if (string.IsNullOrEmpty(DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN))
            //{
            
            // 记录扫码原始数据
            writeLog($"扫码原始数据: {snstr}");
            
            // 解码SN
            string sn = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.DecodeSN(snstr);
            writeLog($"解码后的SN: {(string.IsNullOrEmpty(sn) ? "解码失败" : sn)}");
            
            if (!string.IsNullOrEmpty(sn))
            {
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(sn);
                writeLog($"查询WOCODE: {(string.IsNullOrEmpty(wocode) ? "查询失败" : wocode)}");
                
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(sn);
                writeLog($"查询PartNOID: {(string.IsNullOrEmpty(partnoid) ? "查询失败" : partnoid)}");
               
                if (partnoid != DataModel.Processmodel.PartNOID)
                {
                    // 【日志】混批报错详细信息
                    writeLog($"❌ 检测到混批! 不允许不同规格产品混合作业", true);
                    writeLog($"  - 产线当前规格: {DataModel.Processmodel.PartNOID}", true);
                    writeLog($"  - 扫码产品规格: {partnoid}", true);
                    writeLog($"  - 产品SN: {sn}", true);
                    writeLog($"  - 工单号: {wocode}", true);
                    writeLog($"========== 扫码处理失败（混批错误）==========", true);
                    return $"混批错误！禁止不同规格产品混合作业\n当前规格: {DataModel.Processmodel.PartNOID}\n扫码规格: {partnoid}";
                }

                // 数据完整性检查
                if (string.IsNullOrEmpty(wocode))
                {
                    return "关联批次号读取失败";
                }
                if (string.IsNullOrEmpty(partnoid))
                {
                    return "关联规格信息读取失败";
                }
                
                // 写入PLC和数据库
                writeLog($"写入PLC地址 {DataModel.Settingmodel.AddressSN}: {sn};{wocode}");
                PLC_Writestring(DataModel.Settingmodel.AddressSN.ToString(), $"{sn};{wocode}");
                
                writeLog($"创建数据库记录: wocode={wocode}, partnoid={partnoid}, SN={sn}, 工位={DataModel.Settingmodel.SETTING_DATA.StationCode}, 设备={DataModel.Settingmodel.SETTING_DATA.MachineID}");
                bool dbResult = sqlite.CREATENEWLINE(wocode, partnoid, sn, DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, DateTime.Now);
                
                if (!dbResult)
                {
                    writeLog($"⚠ 数据库记录创建失败！wocode={wocode}, SN={sn}", true);
                }
                
                writeLog($"✓ 扫码处理成功！");
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

        // 中文说明：电测原始数据日志（按SN分文件，每天清空目录下所有txt）
        private readonly object _electricalRawLogLock = new object();
        private static readonly string ElectricalRawLogDir = Path.Combine(Environment.CurrentDirectory, "识别过程日志", "电测原始数据");
        private static readonly string ElectricalRawLogCleanupMarkerPath = Path.Combine(ElectricalRawLogDir, ".last_cleanup_date");

        private void WriteElectricalRawDataLog(int tvIndex, Productinfo productInfo, double voltage, double current, double time, string status)
        {
            try
            {
                if (productInfo == null || string.IsNullOrWhiteSpace(productInfo.SN))
                {
                    return;
                }

                lock (_electricalRawLogLock)
                {
                    Directory.CreateDirectory(ElectricalRawLogDir);

                    // 每天清空：第一次写入当天日志时，清理目录下所有txt（保留marker文件）
                    string today = DateTime.Now.ToString("yyyyMMdd");
                    string lastCleanup = "";
                    try
                    {
                        if (File.Exists(ElectricalRawLogCleanupMarkerPath))
                        {
                            lastCleanup = File.ReadAllText(ElectricalRawLogCleanupMarkerPath)?.Trim() ?? "";
                        }
                    }
                    catch
                    {
                        // 读marker失败不影响主流程，后续会尝试重新写入
                    }

                    if (!string.Equals(lastCleanup, today, StringComparison.Ordinal))
                    {
                        try
                        {
                            foreach (var file in Directory.GetFiles(ElectricalRawLogDir, "*.txt", SearchOption.TopDirectoryOnly))
                            {
                                try { File.Delete(file); } catch { }
                            }
                        }
                        catch
                        {
                            // 清理失败不影响主流程，至少保证本次能写入
                        }

                        try { File.WriteAllText(ElectricalRawLogCleanupMarkerPath, today); } catch { }
                    }

                    string filePath = Path.Combine(ElectricalRawLogDir, $"{productInfo.SN}.txt");
                    bool isNewFile = !File.Exists(filePath);

                    using (var sw = new StreamWriter(filePath, true))
                    {
                        if (isNewFile)
                        {
                            // 字段说明：时间戳、耐压机台、SN、工单、料号、实时电压/电流/时间、仪器状态（原始）
                            sw.WriteLine("时间\t机台\tSN\tWOCODE\tPartNOID\t电压\t电流\t时间\t状态");
                        }

                        sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\tTV{tvIndex}\t{productInfo.SN}\t{productInfo.WOCODE}\t{productInfo.PartNOID}\t{voltage}\t{current}\t{time}\t{status}");
                    }
                }
            }
            catch
            {
                // 中文说明：原始数据日志不允许影响主流程，异常直接吞掉
            }
        }

        private void OnReceive1(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel1.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel1.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel1.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel1.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel1.Voltage);
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel1.Current);
                DataModel.Processmodel.TVTestTestModel1.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 中文说明：电测阶段原始数据落盘（按SN区分，每天清空）
                WriteElectricalRawDataLog(1, DataModel.Processmodel.TVTestTestModel1.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);
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
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel2.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel2.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel2.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel2.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel2.Voltage);
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel2.Current);
                DataModel.Processmodel.TVTestTestModel2.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 中文说明：电测阶段原始数据落盘（按SN区分，每天清空）
                WriteElectricalRawDataLog(2, DataModel.Processmodel.TVTestTestModel2.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);
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
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel3.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel3.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel3.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel3.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel3.Voltage);
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel3.Current);
                DataModel.Processmodel.TVTestTestModel3.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 中文说明：电测阶段原始数据落盘（按SN区分，每天清空）
                WriteElectricalRawDataLog(3, DataModel.Processmodel.TVTestTestModel3.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);

            }
            catch (Exception ex)
            {
            }
        }

        #endregion
        #region PLC通讯

        /// <summary>
        /// 用于跟踪PLC连接状态，避免重复记录日志
        /// true表示上次循环时PLC已连接，false表示上次循环时PLC未连接
        /// </summary>
        private bool _lastPLCConnectedStatus = false;

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
                        // 如果上次连接失败，现在连接成功，记录一次恢复日志
                        if (!_lastPLCConnectedStatus)
                        {
                            writeLog("PLC通讯已恢复，重新连接成功", true);
                        }
                        // 更新连接状态为已连接
                        _lastPLCConnectedStatus = true;
                        
                        #region 读取数据
                        var readresult = modbusTcp.ReadUInt16(DataModel.Settingmodel.AddressStart.ToString(), 20);
                        var secondScanResult = modbusTcp.ReadUInt16(DataModel.Settingmodel.SecondScanTrigAddress.ToString(), 1);

                        if (readresult.IsSuccess)
                        {
                            int ScanTrig = readresult.Content[0];
                            int TakePhoto1Trig = readresult.Content[2];
                            int TV1Trig = readresult.Content[6];
                            int TV2Trig = readresult.Content[8];
                            // 【优化】不再读取第三个耐压仪器的触发信号（设备已更新，不再使用第三个仪器）
                            // int TV3Trig = readresult.Content[10];

                            int SecondScanTrig = secondScanResult.IsSuccess ? secondScanResult.Content[0] : 0;

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

                            #region 第二扫码触发
                            try
                            {
                                if (SecondScanTrig == 1 & DataModel.Processmodel.SecondScan_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                     {
                                         SecondScannerProcess();
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
                                // TV1Trig=1: ACW交流耐压测试
                                // TV1Trig=2: DCW直流耐压测试
                                // TV1Trig=3: 停止测试
                                if (TV1Trig == 1 & DataModel.Processmodel.TV1_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV1Process_ACW();
                                    }).Start();
                                }
                                else if (TV1Trig == 2 & DataModel.Processmodel.TV1_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV1Process_DCW();
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
                                // TV2Trig=1: ACW交流耐压测试
                                // TV2Trig=2: DCW直流耐压测试
                                // TV2Trig=3: 停止测试
                                if (TV2Trig == 1 & DataModel.Processmodel.TV2_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV2Process_ACW();
                                    }).Start();
                                }
                                else if (TV2Trig == 2 & DataModel.Processmodel.TV2_Trig_IO.IOstatus == 0)
                                {
                                    new Thread(() =>
                                    {
                                        TV2Process_DCW();
                                    }).Start();
                                }
                                if ((TV2Trig == 3) & DataModel.Processmodel.TV2_Trig_IO.IOstatus != TV2Trig)
                                {
                                    DataModel.Settingmodel.AT9620_2.stop = true;
                                }
                            }
                            catch {; }
                            #endregion

                            #region 耐压3触发【已禁用】
                            // 【优化】第三个耐压仪器已不再使用，注释掉触发逻辑
                            //try
                            //{
                            //    if ((TV3Trig == 1 || TV3Trig == 2) & DataModel.Processmodel.TV3_Trig_IO.IOstatus == 0)
                            //    {
                            //        new Thread(() =>
                            //        {
                            //            TV3Process();
                            //        }).Start();
                            //    }
                            //    if ((TV3Trig == 3) & DataModel.Processmodel.TV3_Trig_IO.IOstatus != TV3Trig)
                            //    {
                            //        DataModel.Settingmodel.AT9620_3.stop = true;
                            //    }
                            //}
                            //catch {; }
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
                            DataModel.Processmodel.SecondScan_Trig_IO.IOstatus = SecondScanTrig;
                            DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = TakePhoto1Trig;
                            DataModel.Processmodel.TV1_Trig_IO.IOstatus = TV1Trig;
                            DataModel.Processmodel.TV2_Trig_IO.IOstatus = TV2Trig;
                            // 【优化】第三个耐压仪器已禁用，设置状态为-1（不可用）
                            DataModel.Processmodel.TV3_Trig_IO.IOstatus = -1;
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
                        DataModel.Processmodel.SecondScan_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.TV3_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res1_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res2_Trig_IO.IOstatus = -1;
                        DataModel.Processmodel.Res3_Trig_IO.IOstatus = -1;
                        
                        // 只在状态从连接成功变为失败时记录一次日志，避免重复刷屏
                        if (_lastPLCConnectedStatus)
                        {
                            writeLog("PLC通讯失败，所有触发信号置为-1", true);
                        }
                        // 更新连接状态为未连接
                        _lastPLCConnectedStatus = false;
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
        public void SecondScannerProcess()
        {
            if (DataModel.Settingmodel.SecondScannerMode == "HF800")
            {
                var r = DataModel.Settingmodel.SecondHF800.Scanner();
                string s = r.value.Replace("\r", "").Replace("\n", "");
                DataModel.Processmodel.sninputstr = s;
                //SQLITEDATABASE.sqlite.CREATENEWLINE("1", "1", s);
                PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(r.Status == Honeywell.Status.OK ? 1 : 2));

                if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(s))
                {
                    // 写入SN到D1150
                    PLC_Writestring(DataModel.Settingmodel.SecondScanSNAddress.ToString(), s);
                }
            }
            else
            {
                var r = DataModel.Settingmodel.SecondScannerModel.Scanner();
                string s = r.receivestring.Replace("\r", "").Replace("\n", "");
                if (!String.IsNullOrEmpty(s))
                {
                    DataModel.Processmodel.sninputstr = s;

                    PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(r.IsSuccess ? 1 : 2));

                    if (r.IsSuccess)
                    {
                        // 写入SN到D1150
                        PLC_Writestring(DataModel.Settingmodel.SecondScanSNAddress.ToString(), s);
                    }
                }
                else
                {
                    PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(2));
                }
            }
        }
        /// <summary>
        /// 拍照留底工位处理流程：触发三个相机同时拍照并等待完成。
        /// </summary>
        /// <remarks>
        /// 触发条件：PLC拍照触发信号（M地址）由0变1时调用
        /// 
        /// 处理流程：
        /// 1. 从PLC读取产品编码（格式：SN;WOCODE），解析出SN和工单号
        /// 2. 重置三个相机的完成标志位
        /// 3. 同时触发三个相机执行拍照
        /// 4. 轮询等待三个相机全部完成（超时时间约3秒，每200ms检查一次，最多15次）
        /// 5. 根据拍照结果更新数据库记录，并创建内存中的产品过程记录
        /// 6. 向PLC写入完成信号：1=成功，2=超时失败
        /// 
        /// PLC交互地址：
        /// - 读取：D{AddressSN} - 产品编码字符串
        /// - 写入：D{AddressStart+3} - 拍照完成状态（1=OK, 2=NG）
        /// </remarks>
        public void TakePhoto1Process()
        {

            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN);
            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[拍照留底] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                writeLog($"[拍照留底] ❌ 产品编码读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 实际分段数={ss.Length}", true);
                //writeLog($"[拍照留底] PLC地址: D{DataModel.Settingmodel.AddressSN}, 请检查PLC寄存器值是否正确", true);
                // 记录到数据库异常日志便于统计
                SQLITEDATABASE.sqlite.WriteErrorLog("[PLC数据异常]TakePhoto1-产品编码格式错误", 
                    $"原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN}");
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
                    bool updateResult = SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, true);
                    if (!updateResult)
                    {
                        writeLog($"[拍照留底] ⚠ UpdateTakePhoto1更新true失败! SN={DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN}", true);
                    }
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
                    bool updateResult = SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, false);
                    if (!updateResult)
                    {
                        writeLog($"[拍照留底] ⚠ UpdateTakePhoto1更新false(超时或者异常)失败! SN={DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN}", true);
                    }
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




        /// <summary>
        /// 执行ACW交流耐压测试（TV1工位）
        /// </summary>
        /// <remarks>
        /// 当TV1Trig=1时调用此方法。
        /// 触发前统一下发ACW参数到AT9620设备。
        /// </remarks>
        public void TV1Process_ACW()
        {
            writeLog($"[耐压1-ACW] 开始ACW交流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压1-ACW] 开始下发ACW参数");
            DataModel.Settingmodel.AT9620_1.TVParameter = DataModel.Processmodel.ACWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_1.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压1-ACW] ❌ ACW参数下发失败: {downloadResult.Error}", true);
                PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.ACW;
            writeLog($"[耐压1-ACW] ACW参数下发成功");
            Thread.Sleep(500);
            
            // 执行测试（复用现有逻辑）
            TV1Process_Core("ACW");
        }

        /// <summary>
        /// 执行DCW直流耐压测试（TV1工位）
        /// </summary>
        /// <remarks>
        /// 当TV1Trig=2时调用此方法。
        /// 触发前统一下发DCW参数到AT9620设备。
        /// </remarks>
        public void TV1Process_DCW()
        {
            writeLog($"[耐压1-DCW] 开始DCW直流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压1-DCW] 开始下发DCW参数");
            DataModel.Settingmodel.AT9620_1.TVParameter = DataModel.Processmodel.DCWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_1.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压1-DCW] ❌ DCW参数下发失败: {downloadResult.Error}", true);
                PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.DCW;
            writeLog($"[耐压1-DCW] DCW参数下发成功");
            Thread.Sleep(500);

            // 执行测试（复用现有逻辑）
            TV1Process_Core("DCW");
        }

        /// <summary>
        /// TV1耐压测试核心逻辑（ACW/DCW共用）
        /// </summary>
        /// <param name="testType">测试类型标识，用于日志区分（"ACW"或"DCW"）</param>
        private void TV1Process_Core(string testType)
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                DataModel.Processmodel.TVTestTestModel1.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压1-{testType}] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                writeLog($"[耐压1-{testType}] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                SQLITEDATABASE.sqlite.WriteErrorLog($"[PLC数据异常]TV1-{testType}-产品编码格式错误", 
                    $"原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
            DataModel.Processmodel.TVTestTestModel1.Res = res;

            DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;

            // 清空上一次测试残留，避免本次启动失败时沿用旧信息导致“结果/说明”不一致
            DataModel.Processmodel.TVTestTestModel1.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel1.Current = 0;
            DataModel.Processmodel.TVTestTestModel1.Time = 0;

            var r = DataModel.Settingmodel.AT9620_1.Start();
            // 记录耐压失败原因到界面日志，便于首件异常定位
            if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
            {
                // 将失败原因写入状态/信息，避免首件启动失败时界面仍显示上一件PASS
                //DataModel.Processmodel.TVTestTestModel1.Status = r.Error;
                //DataModel.Processmodel.TVTestTestModel1.TVInfo = r.Error;
                writeLog($"[耐压1-{testType}] 失败原因: {r.Error}", true);
            }

            // 获取翻译后的状态并添加测试模式前缀
            var rawTvInfo = DataModel.Processmodel.TVTestTestModel1.TVInfo;
            var translatedTvInfo = GetLocalizedTvStatus(rawTvInfo);
            bool isACW = testType == "ACW";
            var localizedTvInfo1 = Utils.TvStatusTranslator.AddTestModePrefix(translatedTvInfo, isACW);
            DataModel.Processmodel.TVTestTestModel1.TVInfo = localizedTvInfo1;

            string wocode = DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE;
            string partnoid = DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID;
            string sn = DataModel.Processmodel.TVTestTestModel1.Productinfo.SN;

            // 判断是否为双测模式的第二次测试
            bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isACW, testType);

            bool updateTvResult;
            if (isSecondTest)
            {
                // 双测模式第二次测试：插入新记录
                updateTvResult = sqlite.InsertTV_SecondTest(wocode, partnoid, sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                    localizedTvInfo1,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1);
                writeLog($"[耐压1-{testType}] 双测模式第二次测试，插入新记录");
            }
            else
            {
                // 单测模式或双测模式第一次测试：更新现有记录
                updateTvResult = sqlite.UpdateTV(wocode, partnoid, sn,
                    res,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                    localizedTvInfo1,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1);
            }
            
            if (!updateTvResult)
            {
                writeLog($"[耐压1-{testType}] ⚠ 数据库更新失败! SN={sn}, RES={res}", true);
            }

            updatetv(sn, res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1);

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

            writeLog($"[耐压1-{testType}] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}");
            PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), 1);
        }

        /// <summary>
        /// 检查当前测试是否为双测模式的第二次测试
        ///
        /// 业务逻辑说明：
        /// 在双测模式下，同一个产品需要进行交流(ACW)和直流(DCW)两种耐压测试。
        /// 系统会在TVInfo字段中添加测试模式前缀([ACW]或[DCW])来区分测试类型。
        /// 当检测到已有记录使用另一种测试模式时，说明当前是第二次测试。
        ///
        /// 处理逻辑：
        /// 1. 检查数据库中该SN的最新测试记录
        /// 2. 如果当前是ACW测试但数据库中有DCW记录，则为第二次测试
        /// 3. 如果当前是DCW测试但数据库中有ACW记录，则为第二次测试
        /// 4. 第二次测试时会插入新记录，而不是更新现有记录
        ///
        /// 设计考虑：
        /// - 通过TVInfo前缀标识测试模式，避免修改数据库结构
        /// - 确保双测模式下每种测试都有独立记录，便于数据追溯
        /// - 异常处理确保方法失败时不影响主测试流程
        /// </summary>
        /// <param name="wocode">工单号</param>
        /// <param name="partnoid">规格ID</param>
        /// <param name="sn">产品序列号</param>
        /// <param name="isACW">当前是否为交流测试</param>
        /// <param name="testType">测试类型标识("ACW"或"DCW")</param>
        /// <returns>true=第二次测试，false=第一次测试</returns>
        private bool CheckIsSecondTest(string wocode, string partnoid, string sn, bool isACW, string testType)
        {
            try
            {
                // 获取数据库连接字符串（按工单、规格、SN生成独立的数据库文件）
                string connstr = sqlite.CheckDataBase(wocode, partnoid, sn);
                if (!string.IsNullOrEmpty(connstr))
                {
                    // 查询该SN产品最新的测试记录，只获取TVInfo字段用于判断测试模式
                    // 使用ORDER BY id DESC LIMIT 1确保获取最新的记录
                    string checkSql = $"SELECT TVInfo FROM BusbarCompressionData WHERE sn='{sn}' ORDER BY id DESC LIMIT 1";
                    var dt = sqlite.Read(checkSql, connstr);
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        // 获取最新记录的TVInfo信息，包含测试模式前缀
                        string existingTvInfo = dt.Rows[0]["TVInfo"]?.ToString() ?? "";

                        // 双测模式判断逻辑：
                        // 如果当前是ACW测试但数据库中已有DCW记录 → 第二次测试
                        // 如果当前是DCW测试但数据库中已有ACW记录 → 第二次测试
                        // 这种设计确保每种测试模式都有独立的记录
                        if ((isACW && existingTvInfo.StartsWith("[DCW]")) ||
                            (!isACW && existingTvInfo.StartsWith("[ACW]")))
                        {
                            writeLog($"[耐压-{testType}] 检测到双测模式，当前为第二次测试，已有记录TVInfo={existingTvInfo}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录异常但不影响主流程，双测模式检查失败时按第一次测试处理
                writeLog($"[耐压-{testType}] 检查双测模式时异常: {ex.Message}", true);
            }
            return false;
        }

        /// <summary>
        /// 原TV1Process方法（保留兼容，内部调用TV1Process_ACW）
        /// </summary>
        public void TV1Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel1.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压1] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                writeLog($"[耐压1] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                SQLITEDATABASE.sqlite.WriteErrorLog("[PLC数据异常]TV1-产品编码格式错误", 
                    $"原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
            DataModel.Processmodel.TVTestTestModel1.Res = res;

            DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;


            var r = DataModel.Settingmodel.AT9620_1.Start();
            var localizedTvInfo1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.TVInfo);
            DataModel.Processmodel.TVTestTestModel1.TVInfo = localizedTvInfo1;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1
                );
            if (!updateTvResult)
            {
                writeLog($"[耐压1] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel1.Productinfo.SN}, RES={res}", true);
            }

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

        /// <summary>
        /// 执行ACW交流耐压测试（TV2工位）
        /// </summary>
        /// <remarks>
        /// 当TV2Trig=1时调用此方法。
        /// 触发前统一下发ACW参数到AT9620设备。
        /// </remarks>
        public void TV2Process_ACW()
        {
            writeLog($"[耐压2-ACW] 开始ACW交流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压2-ACW] 开始下发ACW参数");
            DataModel.Settingmodel.AT9620_2.TVParameter = DataModel.Processmodel.ACWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_2.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压2-ACW] ❌ ACW参数下发失败: {downloadResult.Error}", true);
                PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.ACW;
            writeLog($"[耐压2-ACW] ACW参数下发成功");
            Thread.Sleep(500);

            // 执行测试（复用现有逻辑）
            TV2Process_Core("ACW");
        }

        /// <summary>
        /// 执行DCW直流耐压测试（TV2工位）
        /// </summary>
        /// <remarks>
        /// 当TV2Trig=2时调用此方法。
        /// 触发前统一下发DCW参数到AT9620设备。
        /// </remarks>
        public void TV2Process_DCW()
        {
            writeLog($"[耐压2-DCW] 开始DCW直流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压2-DCW] 开始下发DCW参数");
            DataModel.Settingmodel.AT9620_2.TVParameter = DataModel.Processmodel.DCWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_2.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压2-DCW] ❌ DCW参数下发失败: {downloadResult.Error}", true);
                PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.DCW;
            writeLog($"[耐压2-DCW] DCW参数下发成功");
            Thread.Sleep(500);

            // 执行测试（复用现有逻辑）
            TV2Process_Core("DCW");
        }

        /// <summary>
        /// TV2耐压测试核心逻辑（ACW/DCW共用）
        /// </summary>
        /// <param name="testType">测试类型标识，用于日志区分（"ACW"或"DCW"）</param>
        private void TV2Process_Core(string testType)
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                DataModel.Processmodel.TVTestTestModel2.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压2-{testType}] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                writeLog($"[耐压2-{testType}] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                SQLITEDATABASE.sqlite.WriteErrorLog($"[PLC数据异常]TV2-{testType}-产品编码格式错误", 
                    $"原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25 * 2}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
            DataModel.Processmodel.TVTestTestModel2.Res = res;

            DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;

            // 清空上一次测试残留，避免本次启动失败时沿用旧信息导致“结果/说明”不一致
            DataModel.Processmodel.TVTestTestModel2.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel2.Current = 0;
            DataModel.Processmodel.TVTestTestModel2.Time = 0;

            var r = DataModel.Settingmodel.AT9620_2.Start();
            // 记录耐压失败原因到界面日志，便于首件异常定位
            if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
            {
                // 将失败原因写入状态/信息，避免首件启动失败时界面仍显示上一件PASS
                //DataModel.Processmodel.TVTestTestModel2.Status = r.Error;
                //DataModel.Processmodel.TVTestTestModel2.TVInfo = r.Error;
                writeLog($"[耐压2-{testType}] 失败原因: {r.Error}", true);
            }

            // 获取翻译后的状态并添加测试模式前缀
            var rawTvInfo = DataModel.Processmodel.TVTestTestModel2.TVInfo;
            var translatedTvInfo = GetLocalizedTvStatus(rawTvInfo);
            bool isACW = testType == "ACW";
            var localizedTvInfo2 = Utils.TvStatusTranslator.AddTestModePrefix(translatedTvInfo, isACW);
            DataModel.Processmodel.TVTestTestModel2.TVInfo = localizedTvInfo2;

            string wocode = DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE;
            string partnoid = DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID;
            string sn = DataModel.Processmodel.TVTestTestModel2.Productinfo.SN;

            // 判断是否为双测模式的第二次测试
            bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isACW, testType);

            bool updateTvResult;
            if (isSecondTest)
            {
                // 双测模式第二次测试：插入新记录
                updateTvResult = sqlite.InsertTV_SecondTest(wocode, partnoid, sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                    localizedTvInfo2,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2);
                writeLog($"[耐压2-{testType}] 双测模式第二次测试，插入新记录");
            }
            else
            {
                // 单测模式或双测模式第一次测试：更新现有记录
                updateTvResult = sqlite.UpdateTV(wocode, partnoid, sn,
                    res,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                    localizedTvInfo2,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2);
            }
            
            if (!updateTvResult)
            {
                writeLog($"[耐压2-{testType}] ⚠ 数据库更新失败! SN={sn}, RES={res}", true);
            }

            updatetv(sn, res,
                DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                localizedTvInfo2,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID2);

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

            writeLog($"[耐压2-{testType}] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}");
            PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), 1);
        }

        /// <summary>
        /// 原TV2Process方法（保留兼容，内部调用TV2Process_ACW）
        /// </summary>
        public void TV2Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel2.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压2] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                //writeLog($"耐压2产品编号读取错误:{s}");
                writeLog($"[耐压2] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                writeLog($"[耐压2] PLC地址: D{DataModel.Settingmodel.AddressSN + 25 * 2}, 请检查PLC寄存器值", true);
                SQLITEDATABASE.sqlite.WriteErrorLog("[PLC数据异常]TV2-产品编码格式错误", 
                    $"原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25 * 2}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
            DataModel.Processmodel.TVTestTestModel2.Res = res;

            DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;
            var r = DataModel.Settingmodel.AT9620_2.Start();
            var localizedTvInfo2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.TVInfo);
            DataModel.Processmodel.TVTestTestModel2.TVInfo = localizedTvInfo2;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
               DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
               localizedTvInfo2,
               DataModel.Settingmodel.SETTING_DATA.TVMeterID2
               );
            if (!updateTvResult)
            {
                writeLog($"[耐压2] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel2.Productinfo.SN}, RES={res}", true);
            }

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
                writeLog($"[耐压3] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                //writeLog($"耐压3产品编号读取错误:{s}");
                writeLog($"[耐压3] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                SQLITEDATABASE.sqlite.WriteErrorLog("[PLC数据异常]TV3-产品编码格式错误", 
                    $"原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25 * 3}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
            DataModel.Processmodel.TVTestTestModel3.Res = res;

            DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = 0;

            var r = DataModel.Settingmodel.AT9620_3.Start();
            var localizedTvInfo3 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel3.TVInfo);
            DataModel.Processmodel.TVTestTestModel3.TVInfo = localizedTvInfo3;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
               DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
               localizedTvInfo3,
               DataModel.Settingmodel.SETTING_DATA.TVMeterID3
               );
            if (!updateTvResult)
            {
                writeLog($"[耐压3] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel3.Productinfo.SN}, RES={res}", true);
            }

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
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
                DataModel.Processmodel.TVTestTestModel1.Res = res;
                writeLog($"阻值1读取完成 D{DataModel.Settingmodel.AddressRes}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25); // TV1工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        UpdateResValue(sn, res);
                        writeLog($"阻值1已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值1] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值1] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
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
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
                DataModel.Processmodel.TVTestTestModel2.Res = res;
                writeLog($"阻值2读取完成 D{DataModel.Settingmodel.AddressRes + 2}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2); // TV2工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        UpdateResValue(sn, res);
                        writeLog($"阻值2已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值2] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值2] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
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
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
                DataModel.Processmodel.TVTestTestModel3.Res = res;
                writeLog($"阻值3读取完成 D{DataModel.Settingmodel.AddressRes + 4}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 3); // TV3工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        UpdateResValue(sn, res);
                        writeLog($"阻值3已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值3] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值3] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
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
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
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
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATETV_EXCEPTION", $"更新耐压/阻值数据失败: {ex.Message}", SN);
                }
            }));
        }

        /// <summary>
        /// 根据产品 SN 仅更新阻值字段到 ProductInfoRecord
        /// 业务含义：
        /// - 在阻值测试完成后立即调用，确保阻值数据及时更新到内存记录；
        /// - 即使后续阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值进行判断。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        /// <param name="res">阻值测量值</param>
        private void UpdateResValue(string SN, float res)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.Res = res;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATERES_EXCEPTION", $"更新阻值数据失败: {ex.Message}", SN);
                }
            }));
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
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
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
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATEPRESSURE_EXCEPTION", $"更新压力数据失败: {ex.Message}", SN);
                }
            }));
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
        /// 将测试模式写入PLC地址D1012
        /// </summary>
        /// <remarks>
        /// 测试模式值：
        /// - 0: 只测交流(ACW Only)
        /// - 1: 只测直流(DCW Only)
        /// - 2: 先交后直(ACW then DCW)
        /// - 3: 先直后交(DCW then ACW)
        /// </remarks>
        /// <returns>true: 写入成功; false: 写入失败</returns>
        public bool WriteTestModeToPLC()
        {
            try
            {
                ushort modeValue = (ushort)DataModel.Settingmodel.CurrentTestMode;
                string address = DataModel.Settingmodel.TestModeAddress.ToString();
                
                writeLog($"[测试模式] 写入PLC地址D{address}, 值={modeValue} ({DataModel.Settingmodel.CurrentTestMode})");
                
                bool result = PLC_write(address, modeValue);
                
                if (!result)
                {
                    writeLog($"[测试模式] ❌ 写入PLC失败! 地址=D{address}, 值={modeValue}", true);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                writeLog($"[测试模式] ❌ 写入PLC异常: {ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// 将指定测试模式写入PLC
        /// </summary>
        /// <param name="testMode">要写入的测试模式</param>
        /// <returns>true: 写入成功; false: 写入失败</returns>
        public bool WriteTestModeToPLC(AT9620.ElectricalTestMode testMode)
        {
            DataModel.Settingmodel.CurrentTestMode = testMode;
            return WriteTestModeToPLC();
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
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
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
                }
                catch (Exception ex)
                {
                    // 同时写入UI日志与数据库错误日志，便于现场快速定位“内存更新失败/对象不存在/线程异常”等问题
                    writeLog($"[AOI] 更新内存外观结果失败：SN={SN}, 结果={(result ? "OK" : "NG")}, 异常={ex.Message}", true);
                    sqlite.WriteErrorLog("UPDATETAKEPHOTO2_EXCEPTION", $"更新AOI外观数据失败: {ex.Message}", SN);
                }
            }));
        }

        /// <summary>
        /// 检查所有AOI工具是否都为NG状态
        /// 用于AOI NG点检：只有所有工具都为NG才算点检通过
        /// </summary>
        /// <returns>true: 所有工具都为NG; false: 存在非NG工具</returns>
        private bool CheckAllAOIToolsNG()
        {
            try
            {
                if (DataModel.FaraVisionDataModel.Processmodel.Tools.Count == 0)
                {
                    writeLog("AOI_NG点检->无工具配置，返回false");
                    return false;
                }

                foreach (var tool in DataModel.FaraVisionDataModel.Processmodel.Tools)
                {
                    if (tool.ToolStatus != ToolStatus.NG)
                    {
                        writeLog($"AOI_NG点检->工具[{tool.Name}]状态为{tool.ToolStatus}，不是NG");
                        return false;
                    }
                }

                writeLog($"AOI_NG点检->所有{DataModel.FaraVisionDataModel.Processmodel.Tools.Count}个工具均为NG");
                return true;
            }
            catch (Exception ex)
            {
                writeLog($"AOI_NG点检->检查工具状态异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 向PLC写入AOI NG点检信号
        /// </summary>
        /// <param name="value">1: 点检通过; 0: 点检失败</param>
        private void WriteAOI_NG_InspectionSignal(UInt16 value)
        {
                try
                {
                    ModbusTcpNet modbusTcp = new ModbusTcpNet();
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    
                    var connectresult = modbusTcp.ConnectServer();
                    if (connectresult.IsSuccess)
                    {
                        // M寄存器是线圈（Coil），使用WriteCoil方法写入Bool值
                        bool boolValue = (value == 1);
                        var writeResult = modbusTcp.WriteCoil(DataModel.Settingmodel.AOI_NG_InspectionAddress.ToString(), boolValue);
                        modbusTcp.ConnectClose();
                        
                        if (writeResult.IsSuccess)
                        {
                            writeLog($"AOI_NG点检->向PLC地址M{DataModel.Settingmodel.AOI_NG_InspectionAddress}写入{value}成功");
                        }
                        else
                        {
                                writeLog($"AOI_NG点检->向PLC地址M{DataModel.Settingmodel.AOI_NG_InspectionAddress}写入{value}失败: {writeResult.Message}");
                            }
                        }
                    else
                    {
                            writeLog($"AOI_NG点检->PLC连接失败: {connectresult.Message}");
                        }
                    }
                catch (Exception ex)
                {
                        writeLog($"AOI_NG点检->写入PLC信号异常: {ex.Message}");
                    }
                }


        private bool PLC_write(float result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int maxRetry = 6;
            for (int i = 0; i < maxRetry; i++)
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
                    writeLog($"视觉写入PLC信号异常1") ;
                }
                Thread.Sleep(100);
            }

            return false;
        }

        private bool PLC_write(string address, UInt16 result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int maxRetry = 6;
            for (int i = 0; i < maxRetry; i++)
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
                    writeLog($"视觉写入PLC信号异常2") ;
                }
                Thread.Sleep(100);
            }

            return false;
        }

        private bool PLC_write(string address, float result)
        {
            writeLog($"视觉->PLC:{result}、{(result == 1 ? "OK" : "NG")}", false);

            int maxRetry = 6;
            for (int i = 0; i < maxRetry; i++)
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
                    writeLog($"视觉写入PLC信号异常3");
                }
                Thread.Sleep(100);
            }

            return false;
        }
        public UInt16 PLC_ReadUint16(int address)
{
    ModbusTcpNet modbusTcp = new ModbusTcpNet();
    int maxRetry = 6;
    
    for (int i = 0; i < maxRetry; i++)
    {
        try
        {
            modbusTcp.ConnectTimeOut = 1000; // 优化超时设置
            modbusTcp.ReceiveTimeOut = 1000;
            modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
            modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
            modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;

            var connectresult = modbusTcp.ConnectServer();
            if (connectresult.IsSuccess)
            {
                var r = modbusTcp.ReadUInt16(address.ToString(), 1);
                modbusTcp.ConnectClose();
                
                if (r.IsSuccess)
                { 
                    return r.Content[0]; 
                }
                else
                {
                   // 若最后一次也失败，记录日志
                   if (i == maxRetry - 1)
                       writeLog($"[PLC通讯] 读取UInt16失败(D{address}): {r.Message}", false);
                }
            }
            else
            {
                 if (i == maxRetry - 1)
                       writeLog($"[PLC通讯] 连接失败(D{address}): {connectresult.Message}", false);
            }
        }
        catch (Exception ex)
        {
             if (i == maxRetry - 1)
                  writeLog($"[PLC通讯] 读取UInt16异常(D{address}): {ex.Message}", false);
        }
        
        // 简单延时重试
        Thread.Sleep(50);
    }
    
    return 0;
}
        public float PLC_ReadFloat(int address)
{
    ModbusTcpNet modbusTcp = new ModbusTcpNet();
    int maxRetry = 6;

    for (int i = 0; i < maxRetry; i++)
    {
        try
        {
            modbusTcp.ConnectTimeOut = 1000; // 优化超时
            modbusTcp.ReceiveTimeOut = 1000;
            modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
            modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
            modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
            
            var connectresult = modbusTcp.ConnectServer();
            if (connectresult.IsSuccess)
            {
                var r = modbusTcp.ReadFloat(address.ToString(), 1);
                modbusTcp.ConnectClose();
                
                if (r.IsSuccess)
                { 
                    return r.Content[0]; 
                }
                else
                {
                   if (i == maxRetry - 1)
                       writeLog($"[PLC通讯] 读取Float失败(D{address}): {r.Message}", false);
                }
            }
            else
            {
                 if (i == maxRetry - 1)
                       writeLog($"[PLC通讯] 连接失败(D{address}): {connectresult.Message}", false);
            }
        }
        catch (Exception ex)
        {
             if (i == maxRetry - 1)
                 writeLog($"[PLC通讯] 读取Float异常(D{address}): {ex.Message}", false);
        }
        Thread.Sleep(50);
    }

    return float.NaN;
}
        /// <summary>
        /// 从PLC读取字符串数据
        /// </summary>
        /// <param name="address">PLC寄存器地址（整数类型，例如：D100表示地址100）</param>
        /// <returns>成功时返回读取到的字符串（已去除空字符），失败时返回空字符串</returns>
        /// <remarks>
        /// 1. 支持自动重试机制，最多重试6次，提高通信可靠性
        /// 2. 每次读取25个字符长度的字符串数据
        /// 3. 自动去除字符串中的空字符（\0）
        /// 4. 连接失败或读取失败时会记录日志（仅在最后一次重试时记录，避免日志过多）
        /// 5. 使用CDAB数据格式进行通信
        /// </remarks>
        public string PLC_Readstring(int address)
{
    // 创建Modbus TCP通信对象
    ModbusTcpNet modbusTcp = new ModbusTcpNet();
    // 设置最大重试次数为6次
    int maxRetry = 6;
    
    // 循环重试机制：如果第一次读取失败，会自动重试，最多重试6次
    for(int i=0; i<maxRetry; i++)
    {
        try
        {
            modbusTcp.ConnectTimeOut = 1000;
            modbusTcp.ReceiveTimeOut = 1000;
            modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
            // 从配置模型中获取PLC的端口号，通常是502（Modbus TCP标准端口）
            modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
            // 设置数据格式为CDAB（字节序），确保数据解析正确
            modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
            
            // 尝试连接到PLC服务器
            var connectresult = modbusTcp.ConnectServer();
            // 判断连接是否成功
            if (connectresult.IsSuccess)
            {
                // 连接成功，开始读取字符串数据
                // address.ToString()：将整数地址转换为字符串格式（如：100 -> "100"）
                // 25：读取25个字符长度的字符串（PLC中字符串通常占用多个寄存器，这里读取25个字符）
                var r = modbusTcp.ReadString(address.ToString(), 25);
                // 读取完成后立即关闭连接，释放网络资源
                modbusTcp.ConnectClose();
                // 判断读取操作是否成功
                if (r.IsSuccess)
                { 
                    // 读取成功，返回读取到的字符串内容
                    // Replace("\0", "")：去除字符串中的空字符（\0），因为PLC返回的字符串可能包含填充的空字符
                    return r.Content.Replace("\0", ""); 
                }
                else
                {
                   // 读取失败，如果这是最后一次重试（i == maxRetry - 1），则记录错误日志
                   // 只在最后一次重试时记录日志，避免重复记录相同的错误信息
                   if (i == maxRetry - 1)
                       writeLog($"[PLC通讯] 读取String失败(D{address}): {r.Message}", false);
                }
            }
            else
            {
                // 连接失败，如果这是最后一次重试，则记录连接失败的日志
                if (i == maxRetry - 1)
                       writeLog($"[PLC通讯] 连接失败(D{address}): {connectresult.Message}", false);
            }
        }
        catch (Exception ex)
        {
             // 捕获异常（如网络异常、超时异常等），如果这是最后一次重试，则记录异常日志
             if (i == maxRetry - 1)
                 writeLog($"[PLC通讯] 读取String异常(D{address}): {ex.Message}", false);
        }
        // 每次重试前等待50毫秒，避免频繁重试对PLC造成压力，也给网络一些恢复时间
        Thread.Sleep(50);
    }

    // 如果所有重试都失败了，返回空字符串，表示读取失败
    return string.Empty;
}
        public bool PLC_Writestring(string address, string data)
        {
            int maxRetry = 3;
            for (int i = 0; i < maxRetry; i++)
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
                        if (r.IsSuccess)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    writeLog($"PLC_Writestring写入PLC异常");
                }
                Thread.Sleep(40);
            }
            return false;

        }

        public bool PLC_ReadTVAvailable()
        {
            int maxRetry = 3;
            for (int i = 0; i < maxRetry; i++)
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
                        // 【优化】不再读取第三个耐压仪器的PLC状态（设备已更新，不再使用第三个仪器）
                        // var r3 = modbusTcp.ReadCoil(DataModel.Settingmodel.Meter3AvailableAddress.ToString(), 1);
                        modbusTcp.Write(DataModel.Settingmodel.ShankHandAddress.ToString(), (UInt16)1);
                        modbusTcp.Write(DataModel.Settingmodel.DeviceAvailableAddress.ToString(), DataModel.Processmodel.allow_start);
                        modbusTcp.ConnectClose();
                        if (r1.IsSuccess)
                        {
                            DataModel.Processmodel.TVAvailable.TV1Available = !r1.Content[0];
                            DataModel.Processmodel.TVAvailable.TV2Available = !r2.Content[0];
                            // 【优化】强制设置第三个仪器为不可用状态
                            DataModel.Processmodel.TVAvailable.TV3Available = false;
                        }

                        // 【优化】只返回前两个仪器的状态，不再检查第三个仪器
                        if (r1.IsSuccess & r2.IsSuccess)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (i == maxRetry - 1)
                    {
                        writeLog($"PLC读取仪器可用状态异常: {ex.Message}", true);
                    }
                }
                Thread.Sleep(50);
            }

            return false;
        }

        #endregion
        #region 通用数据日志
        private object writeLog_Locker = new object();

        private object writeBug_Locker = new object();

        /// <summary>
        /// 写入日志信息到文件和界面显示
        /// </summary>
        /// <param name="LogContent">日志内容</param>
        /// <param name="showdatarecord">是否在界面上显示日志记录，默认为 true</param>
        /// <remarks>
        /// 该方法执行以下操作：
        /// 1. 创建带时间戳的日志项
        /// 2. 如果 showdatarecord 为 true，则在 UI 线程中更新界面日志列表（最多保留 200 条）
        /// 3. 将日志写入到按日期命名的文本文件中（格式：yyyyMMdd.txt）
        /// 4. 日志文件保存路径：程序目录\日志\日志\
        /// </remarks>
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

        #region 尺寸测量日志
        /// <summary>
        /// 写入尺寸测量结果到日志文件（线程安全）
        /// 文件路径：日志\尺寸测量结果\{yyyyMMdd}.txt
        /// 格式：[时间戳] 规格|批号|SN码|工具名称|测量类型|测量范围|真实尺寸|结果
        /// </summary>
        /// <param name="tool">测量工具模型</param>
        /// <param name="productInfo">产品信息</param>
        /// <param name="measureTime">测量时间</param>
        private void WriteMeasurementLog(ToolModel tool, Productinfo productInfo, DateTime measureTime)
        {
            lock (_measurementLogLock)  // 确保线程安全
            {
                try
                {
                    // 构建日志内容（单行记录）
                    string logContent = $"[{measureTime:yyyy-MM-dd HH:mm:ss.fff}] " +
                        $"{productInfo.PartNOID}|" +
                        $"{productInfo.WOCODE}|" +
                        $"{productInfo.SN}|" +
                        $"{tool.Name}|" +
                        $"{tool.MeasureType}|" +
                        $"{tool.MinMeasureValue:F3}~{tool.MaxMeasureValue:F3}|" +
                        $"{tool.ActualMeasureValue:F3}|" +
                        $"{GetMeasurementStatusText(tool.ToolStatus)}";

                    // 确定文件路径（按日期分文件）
                    string filename = $"{Environment.CurrentDirectory}\\日志\\尺寸测量结果\\{measureTime:yyyyMMdd}.txt";
                    string dir = Path.GetDirectoryName(filename);

                    // 创建目录（如果不存在）
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    // 追加写入日志
                    using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                    {
                        sw.WriteLine(logContent);
                    }
                }
                catch (Exception ex)
                {
                    // 写入失败时记录到错误日志，不影响主流程
                    writeError($"写入尺寸测量日志失败: {ex.Message}\r\n{ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// 获取测量状态的文本描述
        /// </summary>
        /// <param name="status">工具状态</param>
        /// <returns>状态文本（OK/NG/NG2）</returns>
        private string GetMeasurementStatusText(ToolStatus status)
        {
            switch (status)
            {
                case ToolStatus.OK:
                    return "OK";
                case ToolStatus.NG:
                    return "NG";
                case ToolStatus.NG2:
                    return "NG2";
                default:
                    return status.ToString();
            }
        }
        #endregion

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
        /// <summary>
        /// 初始化拍照留底和AOI检测的相机事件订阅。
        /// </summary>
        /// <remarks>
        /// 订阅5个相机的图像接收事件：
        /// - 相机1-3：拍照留底工位使用，接收到图像后保存并设置完成标志
        /// - 相机4：AOI外观检测工位使用，接收到图像后进行视觉识别
        /// - 相机5：预留相机（如有需要）
        /// 
        /// 调用时机：
        /// - 系统初始化时调用一次，建立相机与事件处理函数的绑定关系
        /// </remarks>
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

        /// <summary>
        /// 相机3图像接收事件处理函数（拍照留底工位）。
        /// </summary>
        /// <remarks>
        /// 处理流程：
        /// 1. 接收相机3传来的图像数据（MyEventArgs包含图像、宽度、高度）
        /// 2. 在UI线程中显示图像到HWindow3窗口
        /// 3. 保存图像到本地文件系统（文件名包含SN、工位、相机编号）
        /// 4. 设置相机3完成标志位（finished = true）
        /// 5. 触发垃圾回收释放内存
        /// 
        /// 调用时机：
        /// - 相机3执行软触发拍照后，图像采集完成时自动触发
        /// - 在 TakePhoto1Process() 中会轮询检查此完成标志位
        /// 
        /// 注意：
        /// - 此方法在相机回调线程中执行，需要通过Dispatcher切换到UI线程操作界面
        /// </remarks>
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


        /// <summary>
        /// 处理相机接收到的图像并显示到指定窗口（拍照留底工位）。
        /// </summary>
        /// <param name="hwindow">Halcon显示窗口对象</param>
        /// <param name="Image">相机采集的图像对象（HObject）</param>
        /// <param name="H">图像高度（像素）</param>
        /// <param name="W">图像宽度（像素）</param>
        /// <param name="Index">相机编号（1-5）</param>
        /// <remarks>
        /// 处理流程：
        /// 1. 检查图像通道数（CountChannels）
        /// 2. 如果是单通道灰度图，转换为三通道RGB图（Compose3）
        /// 3. 清空显示窗口（ClearWindow）
        /// 4. 在窗口中显示图像（DispObj）
        /// 
        /// 用途：
        /// - 拍照留底工位的三个相机（1-3）接收图像后调用
        /// - 仅用于图像显示，不进行视觉识别
        /// 
        /// 注意：
        /// - 单通道图像会被转换为三通道后释放原图像，避免内存泄漏
        /// </remarks>
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

        /// <summary>
        /// AOI外观检测工位的图像接收和视觉识别处理（相机4）。
        /// </summary>
        /// <param name="Image">相机采集的图像对象（HObject）</param>
        /// <param name="H">图像高度（像素）</param>
        /// <param name="W">图像宽度（像素）</param>
        /// <remarks>
        /// 处理流程：
        /// 1. 图像预处理：单通道转三通道，显示到AOI窗口
        /// 2. 遍历所有配置的视觉工具（Tools），执行对应的检测算法：
        ///    - 二维码识别：读取二维码并可选上报给扫码汇报软件
        ///    - 模板匹配：检测产品位置、角度偏移是否在允许范围内
        ///    - 面积检测：计算区域面积是否在阈值范围内
        ///    - 尺寸测量：测量产品尺寸是否符合规格
        /// 3. 根据检测结果保存图像到OK/NG目录
        /// 4. 综合判断所有工具的结果：
        ///    - 全部OK → 状态=OK
        ///    - 有NG2（缺件/测量失败）→ 状态=NG2
        ///    - 有NG（不合格）→ 状态=NG
        /// 5. 更新数据库中的外观检测结果（UpdateTakePhoto2）
        /// 6. 可选发送结果给机器人（SendMsgRobot）
        /// 
        /// 调用时机：
        /// - 相机4（AOI工位）接收到图像后自动触发
        /// - 在 OnCamera4Receive() 事件处理函数中调用
        /// 
        /// 注意：
        /// - 此方法包含复杂的视觉算法，执行时间较长（通常几百毫秒）
        /// - 需要在UI线程中执行，以便更新界面显示
        /// - 内存管理：及时释放Bitmap和HObject，避免GDI句柄泄漏
        /// </remarks>
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
                                    // 测量同时重绘，便于返回主界面直接看到叠加预览
                                    double measureValue = MeasureDimension(Image, tool, hwindow, true);
                                    // 追加一次预览，确保HALCON结果同步到WPF显示
                                    PreviewDimensionMeasurement(Image, tool, hwindow);
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

                        #region 保存结果到数据库和更新状态
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
                            bool updateAoiDbOk = sqlite.UpdateTakePhoto2(
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                                status == 0);
                            if (!updateAoiDbOk)
                            {
                                // 写库失败必须在UI日志可见，否则会出现“UI/DB不一致、CHECK2判定异常”难排查
                                writeLog($"[AOI] 写入数据库TAKEPHOTO2失败：WOCODE={DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE}, PartNOID={DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID}, SN={DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN}, 结果={(status == 0 ? "OK" : "NG")}", true);
                            }
                            #endregion
                        }
                        #endregion

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

                        #region 保存尺寸测量日志
                        // 仅当测试模式为尺寸测量时，记录测量结果到日志文件
                        if (tool.TestMode == TestModes.尺寸测量)
                        {
                            try
                            {
                                // 直接使用AOI工位的产品信息（扫码时已填充，包含完整的SN、WOCODE、PartNOID）
                                Productinfo productInfo = DataModel.Processmodel.TakePhotoTestMode2.Productinfo;
                                
                                // 安全检查：如果产品信息为空或SN为空，使用备用方案
                                if (productInfo == null || string.IsNullOrEmpty(productInfo.SN))
                                {
                                    // 备用方案：从SNList获取SN
                                    string sn = "";
                                    try
                                    {
                                        sn = DataModel.FaraVisionDataModel.Processmodel.SNList[tool.ProductPositionNO];
                                    }
                                    catch
                                    {
                                        sn = DataModel.FaraVisionDataModel.Processmodel.BarcodeStr ?? "UNKNOWN";
                                    }
                                    
                                    productInfo = new Productinfo
                                    {
                                        SN = sn,
                                        WOCODE = "",
                                        PartNOID = ""
                                    };
                                }

                                // 写入测量日志
                                WriteMeasurementLog(tool, productInfo, DateTime.Now);
                            }
                            catch (Exception ex)
                            {
                                // 日志写入失败不影响主流程，仅记录错误
                                writeLog($"保存尺寸测量日志异常: {ex.Message}", false);
                            }
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
                    // Debug: 读取所有相关的5个地址块数据，用于排查偏移或数据错位问题
                    /*
                    string s_base = PLC_Readstring(DataModel.Settingmodel.AddressSN);
                    string s_25 = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25);
                    string s_50 = PLC_Readstring(DataModel.Settingmodel.AddressSN + 50);
                    string s_75 = PLC_Readstring(DataModel.Settingmodel.AddressSN + 75);
                    string s_100 = PLC_Readstring(DataModel.Settingmodel.AddressSN + 100); // 这是 target

                    writeLog($"[CHECK1 Debug] 全地址数据读取详情:",false);
                    writeLog($"  AddressSN+0   (地址{DataModel.Settingmodel.AddressSN}) : [{s_base}]", false);
                    writeLog($"  AddressSN+25  (地址{DataModel.Settingmodel.AddressSN + 25}) : [{s_25}]", false);
                    writeLog($"  AddressSN+50  (地址{DataModel.Settingmodel.AddressSN + 50}) : [{s_50}]", false);
                    writeLog($"  AddressSN+75  (地址{DataModel.Settingmodel.AddressSN + 75}) : [{s_75}]", false);
                    writeLog($"  AddressSN+100 (地址{DataModel.Settingmodel.AddressSN + 100}) : [{s_100}] <== 当前使用的Target", false);

                    string s = s_100;
                    */

                    // writeLog($"[CHECK1 Debug] PLC原始读取值: [{s}]"); // 上方已记录，此行可省略
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 100);
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
                        writeLog($"视觉检测产品编号读取错误! 原始内容:[{s}], 分割长度:{ss.Length}", true);
                        for (int k = 0; k < ss.Length; k++)
                        {
                            writeLog($"  -> 分割项[{k}]: {ss[k]}", false);
                        }
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
                                    resultstr = "阻值或压力测试不合格";
                                }
                                else if (pi.TVMaxVoltage == 0 || pi.TVMaxVoltage == -1 || !pi.TVResult)
                                {
                                    MSG = "NG2";
                                    resultstr = "耐压测试不合格";
                                }
                                // 压力失败判定为NG3（与阻值同级）
                                // 直接使用本地变量PressureResult，避免异步更新时序问题导致误判
                                // 因为updatepressure()是异步的(BeginInvoke)，此时pi.Pressure_Result可能还未更新
                                else if (!PressureResult)
                                {
                                    MSG = "NG3";
                                    resultstr = "阻值或压力测试不合格";
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
                        // Check1返回值：0=全部合格, 1=拍照不良, 2=耐压不良, 3=阻值或压力不良
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
                                    resultstr = "阻值或压力测试不合格";
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
                                // 特殊逻辑：AOI NG点检需要所有工具都为NG才算通过
                                bool isAoiNGInspection = currentSN == DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN;

                                if (isAoiNGInspection)
                                {
                                    // AOI NG点检：检查所有工具是否都为NG
                                    bool allToolsNG = CheckAllAOIToolsNG();

                                    if (allToolsNG)
                                    {
                                        MSG = "NG4";
                                        resultstr = "AOI_NG点检通过(所有工具均为NG)";
                                        // 向PLC写入点检通过信号
                                        WriteAOI_NG_InspectionSignal(1);
                                    }
                                    else
                                    {
                                        MSG = "NG4";
                                        resultstr = "AOI_NG点检不通过(存在工具非NG)";
                                        // 向PLC写入点检失败信号
                                        WriteAOI_NG_InspectionSignal(0);
                                    }
                                }
                                else
                                {
                                    // AOI OK点检：正常判断AOI结果
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
                            }
                            else
                            {
                                // 耐压点检 SN：完整判断拍照、耐压、阻值、压力、AOI
                                if (!pi.TakePhoto1)
                                {
                                    MSG = "NG1";
                                    resultstr = "拍照留底不良";
                                }
                                // 先判断阻值，再判断耐压，阻值大于14为不合格
                                else if (pi.Res > 14)
                                {
                                    MSG = "NG3";
                                    resultstr = "阻值或压力测试不合格";
                                }
                                else if (pi.TVMaxVoltage == 0 || pi.TVMaxVoltage == -1 || !pi.TVResult)
                                {
                                    MSG = "NG2";
                                    resultstr = "耐压测试不合格";
                                }
                                // 判断压力结果
                                else if (!pi.Pressure_Result)
                                {
                                    MSG = "NG3";
                                    resultstr = "阻值或压力测试不合格";
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
                                    resultstr = "阻值或压力测试不合格";
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
