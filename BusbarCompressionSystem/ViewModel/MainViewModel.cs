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


        #region 数据保存加载
        #region 过程数据
        public void SaveProcessmodel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var stream = File.Open(filename, FileMode.Create))
            {
                var serializer = new XmlSerializer(typeof(Processmodel));
                serializer.Serialize(stream, DataModel.Processmodel);
            }
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
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var stream = File.Open(filename, FileMode.Create))
            {
                var serializer = new XmlSerializer(typeof(SettingModel));
                serializer.Serialize(stream, DataModel.Settingmodel);
            }
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
            }
            catch (Exception ex)
            {
                DataModel.Settingmodel = new SettingModel();

                MessageBox.Show($"配置数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");

            }
        }
        #endregion

        #region 日志数据
        public void SaveRecordModel()
        {
            string filename = $"{Environment.CurrentDirectory}\\配置\\日志数据.xml";
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using (var stream = File.Open(filename, FileMode.Create))
            {
                var serializer = new XmlSerializer(typeof(RecordModel));
                serializer.Serialize(stream, DataModel.Recordmodel);
            }
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
        #endregion

        #endregion
        #region 操作
        public string _ScanSN(string snstr)
        {
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


                            #region 数据复制刷新
                            DataModel.Processmodel.Scan_Trig_IO.IOstatus = ScanTrig;
                            DataModel.Processmodel.TakePhoto1_Trig_IO.IOstatus = TakePhoto1Trig;
                            DataModel.Processmodel.TV1_Trig_IO.IOstatus = TV1Trig;
                            DataModel.Processmodel.TV2_Trig_IO.IOstatus = TV2Trig;
                            DataModel.Processmodel.TV3_Trig_IO.IOstatus = TV3Trig;



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
            if (DataModel.Settingmodel.ScannerMode == "HF800")
            {
                var r = DataModel.Settingmodel.HF800.Scanner();
                string s = r.value.Replace("\r", "").Replace("\n", "");
                DataModel.Processmodel.sninputstr = s;
                //SQLITEDATABASE.sqlite.CREATENEWLINE("1", "1", s);
                PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(r.Status == Honeywell.Status.OK ? 1 : 2));

            }
            else
            {
                var r = DataModel.Settingmodel.ScannerModel.Scanner();
                string s = r.receivestring.Replace("\r", "").Replace("\n", "");
                if (!String.IsNullOrEmpty(s))
                {
                    //s = "7Y00000000" + ((byte)(new Random().NextDouble() * 10)).ToString();
                    //s = $"7Y0000{DateTime.Now.ToString("MMss")}";

                    DataModel.Processmodel.sninputstr = s;

                    var R = ScanSN();


                    PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(r.IsSuccess & R ? 1 : 2));
                }
                else
                {
                    PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(2));

                }
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

            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                DataModel.Processmodel.TVTestTestModel1.TVInfo,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1
                );

            updatetv(DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                DataModel.Processmodel.TVTestTestModel1.TVInfo,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1
                );

            //if (r.Success)
            if (!r.Success)
            {
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    DataModel.Processmodel.TVTestTestModel1.Status,
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



            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
                 DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                DataModel.Processmodel.TVTestTestModel2.TVInfo,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID2
               );

            updatetv(DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                res, DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                DataModel.Processmodel.TVTestTestModel2.TVInfo,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID2

                );

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
            //DataModel.Processmodel.TVTestTestModel3.Productinfo.SN = s;
            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
            DataModel.Processmodel.TVTestTestModel3.Res = res;
            DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = 0;

            var r = DataModel.Settingmodel.AT9620_3.Start();


            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
                DataModel.Processmodel.TVTestTestModel3.TVInfo,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID3
               );

            updatetv(DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage
                , r.Success,
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
                DataModel.Processmodel.TVTestTestModel3.TVInfo,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID3
                );
            PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), 1);

        }

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
                            tool.CurrentBitmapSource = null;
                            tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bmp.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
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
                            updatetakephoto2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, status==0, dt);
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


                    #region 读取压力数据
                    UInt16 AveragePressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure);
                    UInt16 MaxPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 2);
                    UInt16 MinPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 4);

                    bool PressureResult = MaxPressure <= DataModel.Processmodel.PressureParamter.Max_Pressure && MinPressure >= DataModel.Processmodel.PressureParamter.Min_Pressure;

                    sqlite.UpdatePressure(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                       AveragePressure, MaxPressure, MinPressure, PressureResult);
                    updatepressure(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, AveragePressure, MaxPressure, MinPressure, PressureResult);

                    #endregion


                    var r = sqlite.Check1(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN);
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

                    if (MSG != "OK")
                    {

                        #region 保存过程数据到服务器

                        foreach (var pi in DataModel.Recordmodel.ProductInfoRecords)
                        {
                            if (DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN == pi.Productinfo.SN)
                            {
                                MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                                   DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, pi.Productinfo.PartNOID, pi.Productinfo.WOCODE, pi.Productinfo.SN,
                                    pi.TakePhoto1, pi.Res, pi.TVMaxVoltage, pi.TVMaxCurrent, pi.TVMeterID, pi.TVInfo, pi.TVResult,
                                    pi.Pressure_Max, pi.Pressure_Average, pi.Pressure_Min, pi.Pressure_Result, pi.AppearanceInspection, resultstr);
                                break;
                            }
                        }
                        #endregion
                        #region 汇报结果数据
                        report(ss[1], ss[0], resultstr);
                        #endregion

                    }

                    writeLog($"数据校验1->结果:{resultstr}");
                    SendMsgRobot(MSG);

                }
                else if (cmd == "CHECK2")
                {
                    // SendMsgRobot("OK");

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
                            MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                            DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, pi.Productinfo.PartNOID, pi.Productinfo.WOCODE, pi.Productinfo.SN,
                            pi.TakePhoto1, pi.Res, pi.TVMaxVoltage, pi.TVMaxCurrent, pi.TVMeterID, pi.TVInfo, pi.TVResult,
                            pi.Pressure_Max, pi.Pressure_Average, pi.Pressure_Min, pi.Pressure_Result, pi.AppearanceInspection, resultstr);
                            break;
                        }
                    }

                    #endregion
                    #region 汇报结果数据                    
                    report(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, resultstr);
                    #endregion

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
                writeLog($"{sn}:{result};报工:{r}");
                return r;
            }
            catch (Exception ex) {; }
            return false;
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
    }
}