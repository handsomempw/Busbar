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
                SQLITEDATABASE.sqlite.CREATENEWLINE(wocode, partnoid, sn);
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


        public void ScannerProcess()
        {
            if (DataModel.Settingmodel.ScannerMode == "HF800")
            {
                var r = DataModel.Settingmodel.HF800.Scanner();
                string s = r.value.Replace("\r", "").Replace("\n", "");
                DataModel.Processmodel.sninputstr = s;
                SQLITEDATABASE.sqlite.CREATENEWLINE("1", "1", s);
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
            var r = DataModel.Settingmodel.AT9620_1.Start();

            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                10,
                r.Success
                );

            updatetv(DataModel.Processmodel.TVTestTestModel1.Productinfo.SN, res, 10, r.Success);
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
            var r = DataModel.Settingmodel.AT9620_2.Start();
            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
               res,
               10,
               r.Success
               );

            updatetv(DataModel.Processmodel.TVTestTestModel2.Productinfo.SN, res, 10, r.Success);
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
            var r = DataModel.Settingmodel.AT9620_3.Start();
            sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
               res,
               10,
               r.Success
               );

            updatetv(DataModel.Processmodel.TVTestTestModel3.Productinfo.SN, res, 10, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), 1);

        }

        private void updatetv(string SN, float res, float maxvoltage, bool result)
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
                            p.TVResult = result;
                            break;
                        }
                    }
                }));
            }
            catch (Exception ex) { }
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


        private bool PLC_write(UInt16 result)
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
                        var r = modbusTcp.Write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)result);
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
            lock (writeBug_Locker)
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
                NoticeBox.Show($"{errorEventArgs.Error}", $"相机错误-{errorEventArgs.CameraID}", MessageBoxIcon.Error,true,10000);
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
                    OnReceiveProcess(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 4);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, "外观检测", 1, "OK");
                    SendMsgRobot("OK");
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
                    OnReceiveProcess(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 5);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, "外观检测", 1, "OK");

                    if (DataModel.Processmodel.CMD == "A5")
                    {
                        DateTime dt = DateTime.Now;
                        updatetakephoto2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, true, dt);
                        sqlite.UpdateTakePhoto2(
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                            true);
                    }
                    SendMsgRobot("OK");

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

        private void RobotTcpServer_MessageReceived(TCPServerH sender, object e)
        {
            try
            {

                TCPevent TCPevent = (TCPevent)e;
                string cmd = (string)TCPevent.Msg;
                writeLog($"机器人->视觉:{cmd}");
                DataModel.Processmodel.CMD = cmd;

                if (cmd.StartsWith("A"))
                {
                    string s = cmd.Replace("A", "");
                    int cmdint = -1;
                    if (int.TryParse(s, out cmdint))
                    {
                        if (cmdint <= -1)
                        {
                            DataModel.Settingmodel.camedata4.CameraModel.camera.bnTriggerExec_Click();
                        }
                        else
                        {
                            DataModel.Settingmodel.camedata5.CameraModel.camera.bnTriggerExec_Click();
                        }
                    }
                }
                else if (cmd == "CHECK1")
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 4);
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                        DataModel.Processmodel.TakePhotoTestMode2.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                    }
                    else
                    {

                        writeLog($"视觉检测产品编号读取错误:{s}");

                    }

                    var r = sqlite.Check1(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN);
                    string MSG = "NG1";
                    switch (r)
                    {
                        case 0:
                            {
                                MSG = "OK";
                                break;
                            }
                        case 1:
                            {
                                MSG = "NG1";
                                break;
                            }
                        case 2:
                            {
                                MSG = "NG2";
                                break;
                            }
                        case 3:
                            {
                                MSG = "NG3";
                                break;
                            }
                        case 4:
                            {
                                MSG = "NG4";
                                break;
                            }
                    }


                    report(ss[1], ss[0], MSG);

                    SendMsgRobot(MSG);

                }
                else if (cmd == "CHECK2")
                {
                    // SendMsgRobot("OK");

                    var r = sqlite.Check2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN);
                    string MSG = "NG1";
                    switch (r)
                    {
                        case 0:
                            {
                                MSG = "OK";
                                break;
                            }
                        case 1:
                            {
                                MSG = "NG1";
                                break;
                            }
                        case 2:
                            {
                                MSG = "NG2";
                                break;
                            }
                        case 3:
                            {
                                MSG = "NG3";
                                break;
                            }
                        case 4:
                            {
                                MSG = "NG4";
                                break;
                            }
                    }

                    SendMsgRobot(MSG);


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
        public void ClearTools()
        {
            //for (int i = 0; i < DataModel.Processmodel.Tools.Count; i++)
            //{
            //    ClearTool(DataModel.Processmodel.Tools[i]);
            //}
        }

        //public void ClearTool(ToolModel tool)
        //{
        //    tool.BarcodeStr = string.Empty;
        //    tool.DeltaX = 0;
        //    tool.DeltaY = 0;
        //    tool.ActualScore = 0;
        //    tool.ActualX = 0;
        //    tool.ActualY = 0;
        //    tool.ActualAngle = 0;
        //    tool.ActualDimension = 0;
        //    tool.ToolStatus = ToolStatus.等待中;

        //}



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