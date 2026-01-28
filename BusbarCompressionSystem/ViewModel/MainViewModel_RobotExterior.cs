using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.Record;
using BusbarCompressionSystem.Utils;
using GalaSoft.MvvmLight;
using SQLITEDATABASE;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TcpServerHelper;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        // 机器人与外观检测相关方法拆分到独立文件，便于维护
        #region 外观检测
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
                        // AOI-only模式下跳过全部电测（TV/RES/压力），压力数据不读取/不落库，避免产生默认值干扰追溯
                        if (!IsAoiOnlyMode)
                        {
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
                        }

                        #endregion


                        // 调用Check1进行第一次综合校验（拍照留底、耐压测试、阻值测试）
                        // AOI-only模式：当耐压工位均不可用时，不伪造耐压记录，CHECK阶段跳过全部电测校验（TV/RES/压力）
                        bool isAoiOnlyMode = IsAoiOnlyMode;
                        var r = sqlite.Check1(
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                            aoiOnlyMode: isAoiOnlyMode);
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
                        // AOI-only模式：当耐压工位均不可用时，不伪造耐压记录，CHECK阶段跳过全部电测校验（TV/RES/压力）
                        bool isAoiOnlyMode = IsAoiOnlyMode;
                        var r = sqlite.Check2(
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                            aoiOnlyMode: isAoiOnlyMode);
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

    }
}
