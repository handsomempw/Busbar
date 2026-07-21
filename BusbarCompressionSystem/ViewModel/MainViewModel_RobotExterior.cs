using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.Record;
using BusbarCompressionSystem.Utils;
using GalaSoft.MvvmLight;
using Panuon.WPF.UI;
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
        /// <summary>
        /// 机器人掉线告警的显示门闩。
        /// 同一轮断线只向操作员弹出一次保留型提示，重连后允许再次提示，避免重复告警覆盖现场判断。
        /// </summary>
        private int robotDisconnectNoticeShown = 0;

        /// <summary>
        /// 将视觉结果发送给机器人，作为 CHECK 与AOI流程之间的联动回执。
        /// 发送成功时同步记录通讯日志；发送失败由底层 TCP 事件和断线提示接管。
        /// </summary>
        /// <param name="cmd">发送给机器人控制器的指令码，如 OK、NG1、NG2、NG3 或 NG4。</param>
        public void SendMsgRobot(string cmd)
        {
            DataModel.Settingmodel.TcpServerRobot.SendMessage(cmd);
            writeLog($"视觉->机器人:{cmd}");
        }

        /// <summary>
        /// 在机器人通讯掉线时向操作员弹出需要人工关闭的提示。
        /// 该提示挂在机器人联动流程上，用于提醒现场暂停依赖 CHECK 指令并检查机器人连接。
        /// </summary>
        /// <param name="message">提示正文，说明当前机器人掉线状态和现场处置要求。</param>
        private void ShowRobotDisconnectNotice(string message)
        {
            if (System.Threading.Interlocked.Exchange(ref robotDisconnectNoticeShown, 1) == 1)
            {
                return;
            }

            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                NoticeBox.Show(message, "机器人离线", MessageBoxIcon.Error, true);
            }));
        }

        /// <summary>
        /// 判断消息处理过程中的异常是否属于机器人链路断开。
        /// 该判断覆盖发送时的写入失败和远程主机主动关闭连接两类现场表现。
        /// </summary>
        /// <param name="ex">机器人指令处理期间抛出的异常。</param>
        /// <returns>返回 true 时按机器人断线处理并弹出人工确认提示。</returns>
        private static bool IsRobotDisconnectException(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            string message = ex.Message ?? string.Empty;
            return message.Contains("无法将数据写入传输连接")
                || message.Contains("远程主机强迫关闭")
                || message.Contains("发送失败，客户端掉线")
                || message.Contains("客户端掉线");
        }

        /// <summary>
        /// 启动机器人 TCP 监听并挂接上线、掉线和消息接收事件。
        /// 该入口只负责建立机器人联动通道，后续 CHECK 流程由消息回调驱动。
        /// </summary>
        public void InitRobotServer()
        {
            if (IsDualYElectricalTestDeployment())
            {
                writeLog("[双Y电测] 部署模式跳过机器人TCP监听");
                return;
            }

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
        /// <summary>
        /// 记录机器人上线或重连状态，并清理断线提示门闩。
        /// </summary>
        /// <param name="sender">机器人 TCP 服务端实例。</param>
        /// <param name="e">连接事件载体，包含客户端上线或重连的业务文本。</param>
        private void RobotTcpServer_ClientConnected(TCPServerH sender, object e)
        {
            try
            {
                TCPevent TCPevent = (TCPevent)e;
                if (TCPevent.Msg == "客户端连接")
                {
                    writeLog("机器人已经连接");
                    DataModel.Settingmodel.RobotConnect.IsConnected = true;
                    System.Threading.Interlocked.Exchange(ref robotDisconnectNoticeShown, 0);
                }
                else if (TCPevent.Msg == "客户端重新连接")
                {
                    writeLog("机器人重新连接");
                    DataModel.Settingmodel.RobotConnect.IsConnected = true;
                    System.Threading.Interlocked.Exchange(ref robotDisconnectNoticeShown, 0);
                }
            }
            catch (Exception ex) {; }
        }

        /// <summary>
        /// 记录机器人掉线状态，并向操作员弹出需要人工关闭的告警。
        /// 该提示用于提醒现场机器人链路中断后先完成人工确认
        /// </summary>
        /// <param name="sender">机器人 TCP 服务端实例。</param>
        /// <param name="e">断线事件载体，包含客户端掉线或发送失败的业务文本。</param>
        private void RobotTcpServer_ClientDisconnected(TCPServerH sender, object e)
        {
            try
            {
                TCPevent TCPevent = (TCPevent)e;
                if (TCPevent.Msg == "客户端掉线")
                {
                    writeLog("机器人已经离线");
                    DataModel.Settingmodel.RobotConnect.IsConnected = false;
                    ShowRobotDisconnectNotice("机器人通讯已断开，请检查机器人连接");
                }
                else if (TCPevent.Msg == "发送失败，客户端掉线")
                {
                    writeLog("发送失败，客户端掉线");
                    DataModel.Settingmodel.RobotConnect.IsConnected = false;
                    ShowRobotDisconnectNotice("消息发送失败，请检查机器人连接");
                }
                else
                {
                    writeLog(TCPevent.Msg);
                    DataModel.Settingmodel.RobotConnect.IsConnected = false;
                    ShowRobotDisconnectNotice("请检查机器人TCP连接");
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
        /// 压力判定 NG3 时写入数据库追踪日志。
        /// </summary>
        /// <param name="stageTag">流程阶段标识，如 CHECK1、点检CHECK1。</param>
        /// <param name="maxPressure">PLC 读取的最大压力。</param>
        /// <param name="minPressure">PLC 读取的最小压力。</param>
        /// <param name="averagePressure">PLC 读取的平均压力。</param>
        /// <param name="pressureResult">当前判定的压力合格标志。</param>
        /// <param name="sn">产品 SN。</param>
        /// <param name="wocode">批次号。</param>
        private void TracePressureNg3IfFailed(string stageTag, UInt16 maxPressure, UInt16 minPressure, UInt16 averagePressure, bool pressureResult, string sn, string wocode)
        {
            if (pressureResult)
            {
                return;
            }

            sqlite.WritePressureThresholdNg3Trace(stageTag,
                maxPressure, minPressure, averagePressure,
                DataModel.Processmodel.PressureParamter.Max_Pressure,
                DataModel.Processmodel.PressureParamter.Min_Pressure,
                sn, wocode);
        }

        /// <summary>
        /// 标准产线 CHECK 阶段的 MES 过程数据上传入口。
        /// CHECK1 按当前测试模式上传本轮应存在的 ACW/DCW/IR 过程行，用于补齐前段电测追溯；
        /// CHECK2 从同一模式口径中上传一条最终/AOI 快照行，用于保留第二工站最终判定，避免同一轮合格产品在 MES 过程表中重复生成多条最终行。
        /// </summary>
        /// <param name="stageTag">当前 CHECK 阶段标识，用于日志区分 CHECK1 与 CHECK2。</param>
        /// <param name="stationCode">MES 过程数据工站号；CHECK1 使用一工站，CHECK2 使用二工站。</param>
        /// <param name="machineId">当前设备编号，用于 MES 过程数据设备维度追溯。</param>
        /// <param name="partnoid">当前产品规格编码，来自扫码建账或 MES 解析结果。</param>
        /// <param name="wocode">当前产品工单号，用于定位本地 SQLite 工单库并写入 MES。</param>
        /// <param name="sn">当前产品序列号，用于读取同一产品的 ACW/DCW/IR 过程数据。</param>
        /// <param name="resultstr">当前 CHECK 综合判定结果，作为 MES 过程数据的最终业务结果文本。</param>
        /// <param name="uploadAllModes">true 表示按本轮测试模式上传全部期望电测过程；false 表示仅上传一条最终/AOI 快照。</param>
        /// <returns>全部目标过程行写入 MES 成功时返回 true；任一目标行失败时返回 false，并写入现场日志。</returns>
        private bool SaveStandardElectricalProcessDataToMes(string stageTag, string stationCode, string machineId,
            string partnoid, string wocode, string sn, string resultstr, bool uploadAllModes)
        {
            List<sqlite.ElectricalTestProcessRow> rows = sqlite.GetStandardElectricalTestProcessRows(wocode, partnoid, sn);
            if (rows.Count == 0)
            {
                writeLog($"[{stageTag}] MES过程数据上传失败，SN={sn}, WO={wocode}, 原因=本地未找到ACW/DCW/IR电测过程行", true);
                return false;
            }

            bool includeIr = uploadAllModes && ShouldExpectStandardIrUpload(rows);
            List<string> expectedModes = GetStandardExpectedElectricalModes(includeIr);
            List<sqlite.ElectricalTestProcessRow> expectedRows = SelectStandardRowsByExpectedModes(rows, expectedModes, stageTag, sn, wocode);
            bool allOk = expectedRows.Count == expectedModes.Count;

            List<sqlite.ElectricalTestProcessRow> uploadRows;
            if (uploadAllModes)
            {
                uploadRows = expectedRows;
            }
            else
            {
                var finalRow = SelectStandardFinalSnapshotRow(expectedRows);
                uploadRows = finalRow == null
                    ? new List<sqlite.ElectricalTestProcessRow>()
                    : new List<sqlite.ElectricalTestProcessRow> { finalRow };
            }

            if (uploadRows.Count == 0)
            {
                writeLog($"[{stageTag}] MES过程数据上传失败，SN={sn}, WO={wocode}, 原因=本轮测试模式未找到可上传电测行，期望模式={string.Join(",", expectedModes.ToArray())}", true);
                return false;
            }

            foreach (var row in uploadRows)
            {
                var persistedTvInfo = GetLocalizedTvStatus(row.TVInfo);
                bool saveOk = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                    stationCode, machineId, partnoid, wocode, sn,
                    row.TakePhoto1, row.Res, row.TVMaxVoltage, row.TVMaxCurrent, row.TVMeterID, persistedTvInfo, row.TVResult,
                    row.PressureMax, row.PressureAverage, row.PressureMin, row.PressureResult, row.TakePhoto2, resultstr);

                writeLog($"[{stageTag}] MES过程数据上传{(saveOk ? "成功" : "失败")}，SN={sn}, WO={wocode}, 模式={row.TestMode}, TVInfo={persistedTvInfo}, 结果={resultstr}", !saveOk);
                allOk = allOk && saveOk;
            }

            return allOk;
        }

        /// <summary>
        /// 判断标准 CHECK1 是否应把 IR 作为本轮 MES 过程数据上传对象。
        /// IR 仪器可用表示现场流程具备 IR 测试条件；SQLite 已存在 IR 行表示本轮产品已经产生绝缘电阻结果，两者任一成立都需要保留 IR 过程追溯。
        /// </summary>
        /// <param name="rows">同一产品从 SQLite 读取到的 ACW/DCW/IR 最新过程快照。</param>
        /// <returns>true 表示 CHECK1 期望上传 IR 过程行；false 表示本轮只按 ACW/DCW 测试模式上传。</returns>
        private bool ShouldExpectStandardIrUpload(List<sqlite.ElectricalTestProcessRow> rows)
        {
            bool irMeterAvailable = DataModel != null
                && DataModel.Processmodel != null
                && DataModel.Processmodel.TVAvailable != null
                && DataModel.Processmodel.TVAvailable.IRAvailable;

            bool hasIrRow = rows != null && rows.Any(row => string.Equals(row.TestMode, "IR", StringComparison.OrdinalIgnoreCase));
            return irMeterAvailable || hasIrRow;
        }

        /// <summary>
        /// 根据当前测试模式生成标准产线 CHECK 阶段的期望电测模式清单。
        /// 该清单决定 MES 过程数据读取边界：单测只取对应 ACW 或 DCW，双测取 ACW 与 DCW，IR 按现场可用状态或已产生的 IR 行追加。
        /// </summary>
        /// <param name="includeIr">true 表示本轮 CHECK1 需要包含 IR 过程追溯；false 表示仅生成 ACW/DCW 期望模式。</param>
        /// <returns>按工艺顺序排列的期望电测模式，值为 ACW、DCW、IR。</returns>
        private List<string> GetStandardExpectedElectricalModes(bool includeIr)
        {
            var expectedModes = new List<string>();
            switch (DataModel.Settingmodel.CurrentTestMode)
            {
                case AT9620.ElectricalTestMode.ACWOnly:
                    expectedModes.Add("ACW");
                    break;
                case AT9620.ElectricalTestMode.DCWOnly:
                    expectedModes.Add("DCW");
                    break;
                case AT9620.ElectricalTestMode.ACWThenDCW:
                    expectedModes.Add("ACW");
                    expectedModes.Add("DCW");
                    break;
                case AT9620.ElectricalTestMode.DCWThenACW:
                    expectedModes.Add("DCW");
                    expectedModes.Add("ACW");
                    break;
                default:
                    expectedModes.Add("ACW");
                    writeLog($"[MES过程数据] 测试模式未识别，按只测交流口径上传，当前模式={DataModel.Settingmodel.CurrentTestMode}", true);
                    break;
            }

            if (includeIr)
            {
                expectedModes.Add("IR");
            }

            return expectedModes;
        }

        /// <summary>
        /// 从 SQLite 电测快照中按期望模式各选择最新一条过程行。
        /// 当前测试模式的第一个非 IR 行作为本轮电测起点；只有 ID 不早于该起点的行允许进入 MES 上传，避免同一 SN 上一轮未清掉的模式行混入本轮。
        /// </summary>
        /// <param name="rows">同一产品从 SQLite 读取到的 ACW/DCW/IR 最新过程快照。</param>
        /// <param name="expectedModes">本轮测试模式要求上传的 ACW/DCW/IR 模式清单。</param>
        /// <param name="stageTag">当前 CHECK 阶段标识，用于现场日志定位。</param>
        /// <param name="sn">当前产品序列号，用于日志追溯。</param>
        /// <param name="wocode">当前产品工单号，用于日志追溯。</param>
        /// <returns>按期望模式顺序排列的 MES 上传候选行；缺失模式不会生成占位行。</returns>
        private List<sqlite.ElectricalTestProcessRow> SelectStandardRowsByExpectedModes(
            List<sqlite.ElectricalTestProcessRow> rows, List<string> expectedModes, string stageTag, string sn, string wocode)
        {
            var selectedRows = new List<sqlite.ElectricalTestProcessRow>();
            var missingModes = new List<string>();
            string firstNonIrMode = expectedModes.FirstOrDefault(mode => !string.Equals(mode, "IR", StringComparison.OrdinalIgnoreCase));
            var roundStartRow = rows
                .Where(item => string.Equals(item.TestMode, firstNonIrMode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Id)
                .FirstOrDefault();

            if (roundStartRow == null)
            {
                writeLog($"[{stageTag}] MES过程数据上传失败，SN={sn}, WO={wocode}, 原因=无法定位本轮电测起点，期望首模式={firstNonIrMode}", true);
                return selectedRows;
            }

            foreach (string mode in expectedModes)
            {
                var row = rows
                    .Where(item => string.Equals(item.TestMode, mode, StringComparison.OrdinalIgnoreCase))
                    .Where(item => item.Id >= roundStartRow.Id)
                    .OrderByDescending(item => item.Id)
                    .FirstOrDefault();

                if (row == null)
                {
                    missingModes.Add(mode);
                    continue;
                }

                selectedRows.Add(row);
            }

            if (missingModes.Count > 0)
            {
                writeLog($"[{stageTag}] MES过程数据缺少期望电测行，SN={sn}, WO={wocode}, 当前测试模式={DataModel.Settingmodel.CurrentTestModeDisplay}, 缺少模式={string.Join(",", missingModes.ToArray())}", true);
            }

            return selectedRows;
        }

        /// <summary>
        /// 选择 CHECK2 写入 MES 的最终/AOI 快照行。
        /// CHECK2 的 AOI 判定写回当前测试模式期望的最新非 IR 电测行；IR 作为 CHECK1 过程追溯，不承载第二工站最终/AOI 快照。
        /// </summary>
        /// <param name="rows">同一产品按当前测试模式筛选后的 ACW/DCW 过程快照。</param>
        /// <returns>用于 CHECK2 单条 MES 过程数据上传的快照行；没有可用行时返回 null。</returns>
        private static sqlite.ElectricalTestProcessRow SelectStandardFinalSnapshotRow(List<sqlite.ElectricalTestProcessRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return null;
            }

            var latestNonIrRow = rows
                .Where(row => !string.Equals(row.TestMode, "IR", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.Id)
                .FirstOrDefault();

            return latestNonIrRow;
        }

        /// <summary>
        /// 完成 IR 点检标准件在 CHECK 阶段的结果结算与机器人分流回执。
        /// 实测 OK 始终返回 OK，实测 NG 始终返回 NG2；IR NG 标准件被正确识别时仍进入 NG 分流。
        /// 本次扫码上下文隔离固定 SN 的相邻点检轮次；点检期望用于日志和 MES 留档，D1015 与机器人持续表达仪器实测结果。
        /// </summary>
        /// <param name="stageTag">CHECK1 或 CHECK2 阶段名称，用于日志和现场追溯。</param>
        /// <param name="stationCode">MES 过程数据工站号；CHECK1 使用一工站，CHECK2 使用二工站。</param>
        /// <param name="productInfo">CHECK 从 PLC 产品码解析的 SN、工单和当前料号。</param>
        private void HandleIrInspectionCheck(string stageTag, string stationCode, Productinfo productInfo)
        {
            if (productInfo == null
                || string.IsNullOrWhiteSpace(productInfo.SN)
                || string.IsNullOrWhiteSpace(productInfo.WOCODE))
            {
                writeLog($"[{stageTag}] IR点检产品码不完整，已返回NG2进入NG分流。", true);
                SendMsgRobot("NG2");
                return;
            }

            InspectionRunContext resultContext;
            bool currentRunKnown;
            bool hasCurrentRunResult = TryGetIrInspectionResult(
                productInfo.SN,
                productInfo.WOCODE,
                out resultContext,
                out currentRunKnown);

            if (!hasCurrentRunResult)
            {
                string missingReason = currentRunKnown
                    ? "本轮IR测试结果尚未形成"
                    : "本次点检扫码上下文缺失";
                writeLog($"[{stageTag}] IR点检结算失败，SN={productInfo.SN}, WO={productInfo.WOCODE}, 原因={missingReason}，已返回NG2进入NG分流。", true);
                SendMsgRobot("NG2");
                return;
            }

            bool actualOk = resultContext.IrActualOk.Value;
            bool expectedOk = IsIrOkInspectionSn(productInfo.SN);
            bool inspectionPassed = actualOk == expectedOk;
            string expectedText = expectedOk ? "OK" : "NG";
            string actualText = actualOk ? "OK" : "NG";
            string robotMessage = actualOk ? "OK" : "NG2";
            string resultstr = $"IR_{expectedText}点检{(inspectionPassed ? "通过" : "不通过")}(实测{actualText})";

            string inspectionPartNoId = string.IsNullOrWhiteSpace(resultContext.PartNoId)
                ? productInfo.PartNOID
                : resultContext.PartNoId;
            bool hasArchiveContext = !string.IsNullOrWhiteSpace(inspectionPartNoId);
            bool saveOk = false;
            string mesArchiveStatus;
            if (hasArchiveContext)
            {
                string persistedTvInfo = GetLocalizedTvStatus(resultContext.IrInfo);
                saveOk = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                    stationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    inspectionPartNoId,
                    productInfo.WOCODE,
                    productInfo.SN,
                    false,
                    resultContext.ContactResistance,
                    resultContext.IrResistance,
                    resultContext.IrLeakCurrent,
                    resultContext.IrMeterId,
                    persistedTvInfo,
                    actualOk,
                    0,
                    0,
                    0,
                    false,
                    false,
                    resultstr);
                mesArchiveStatus = saveOk ? "成功" : "失败";
            }
            else
            {
                mesArchiveStatus = "跳过(点检料号为空)";
                writeLog($"[{stageTag}] IR点检MES留档跳过，SN={productInfo.SN}, WO={productInfo.WOCODE}, 原因=点检料号为空", true);
            }

            writeLog(
                $"[{stageTag}] IR点检结算，SN={productInfo.SN}, 期望={expectedText}, 实测={actualText}, 点检={(inspectionPassed ? "通过" : "不通过")}, 机器人返回={robotMessage}, SQLite留档={(resultContext.IrProcessRowSaved ? "成功" : "失败")}, MES留档={mesArchiveStatus}",
                !inspectionPassed || !resultContext.IrProcessRowSaved || !hasArchiveContext || !saveOk);
            SendMsgRobot(robotMessage);
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
        ///    - 按测试模式从 SQLite 上传本轮期望电测过程数据到 MES，并报工一次
        /// 
        /// 3. "CHECK2" - 第二次数据校验（最终出站前）
        ///    - 调用sqlite.Check2校验所有工序（包括外观检测）
        ///    - 根据校验结果返回OK/NG1~NG4给机器人
        ///    - 上传一条最终/AOI 快照到 MES，并按第二工站报工一次
        /// 
        /// - 机器人控制产品流转节奏，视觉系统被动响应
        /// - 通过两次CHECK实现分段校验：CHECK1拦截前工序不良品，CHECK2最终全检
        /// - 所有结果通过TCP即时反馈给机器人，实现自动分拣
        /// </summary>
        private void RobotTcpServer_MessageReceived(TCPServerH sender, object e)
        {
            string cmd = string.Empty;
            try
            {

                TCPevent TCPevent = (TCPevent)e;
                string rawCmd = (string)TCPevent.Msg ?? string.Empty;
                cmd = rawCmd.Trim(' ', '\t', '\r', '\n', '\0');
                if (cmd != rawCmd)
                {
                    writeLog($"机器人->视觉:{cmd}");
                }
                else
                {
                    writeLog($"机器人->视觉:{cmd}");
                }
                if (IsDualYElectricalTestDeployment() || IsDualYElectricalTestModeActive())
                {
                    writeLog($"[双Y电测] 忽略机器人TCP消息: {cmd}", true);
                    return;
                }

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
                            Thread.Sleep(DataModel.FaraVisionDataModel.Settingmodel.ACommandTriggerWaitMs);
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

                    // CHECK1是机器人交互关口：读码失败时不使用旧SN，也不主动回NG。
                    // 现场约定是不回机器人结果即可阻止继续流转，由机器人/PLC超时或重试机制接管。
                    // writeLog($"[CHECK1 Debug] PLC原始读取值: [{s}]"); // 上方已记录，此行可省略
                    string snCode;
                    string woCode;
                    string rawCode;
                    if (!TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 100, "CHECK1", out snCode, out woCode, out rawCode, 3, 500))
                    {
                        DataModel.Processmodel.TakePhotoTestMode2.Productinfo = new Productinfo()
                        {
                            SN = string.Empty,
                            WOCODE = string.Empty,
                            PartNOID = DataModel.Processmodel.PartNOID
                        };
                        writeLog("[CHECK1] 产品编码读取失败，已中止本次CHECK1且不回机器人结果，等待现场超时/重试机制接管。", true);
                        return;
                    }

                    string[] ss = new string[] { snCode, woCode };
                    int inspectionMatchCount = GetInspectionSnMatchCount(snCode);
                    if (inspectionMatchCount > 1)
                    {
                        writeLog($"[CHECK1] 点检SN配置重复，SN={snCode}, 命中配置数={inspectionMatchCount}，已返回NG2进入NG分流。", true);
                        SendMsgRobot("NG2");
                        return;
                    }

                    string productPartNoId = ResolveInspectionPartNo(
                        snCode, woCode, DataModel.Processmodel.PartNOID, "CHECK1");
                    //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                    DataModel.Processmodel.TakePhotoTestMode2.Productinfo = new Productinfo() { SN = snCode, WOCODE = woCode, PartNOID = productPartNoId };
                    App.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        DataModel.FaraVisionDataModel.Processmodel.SNList.Clear();
                        DataModel.FaraVisionDataModel.Processmodel.SNList.Add(snCode);
                    }));

                    #endregion

                    // 点检标准件跳过正常报工，耐压、IR、AOI 分别使用自己的过程结果口径。
                    bool isInspectionSN = IsInspectionSn(ss[0]);

                    // AOI 点检 SN：在 CHECK1 中无论实际测试结果如何，都强制返回 OK
                    bool isAoiInspectionSN = IsAoiInspectionSn(ss[0]);
                    bool isIrInspectionSN = IsIrInspectionSn(ss[0]);

                    if (isIrInspectionSN)
                    {
                        HandleIrInspectionCheck(
                            "CHECK1",
                            DataModel.Settingmodel.SETTING_DATA.StationCode,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo);
                        return;
                    }

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
                        TracePressureNg3IfFailed("点检CHECK1",
                            MaxPressure, MinPressure, AveragePressure, PressureResult,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE);

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
                                // 耐压点检 SN：根据实际测试数据综合判断（逻辑同 Check1）
                                if (!pi.TakePhoto1)
                                {
                                    MSG = "NG1";
                                    resultstr = "拍照留底不良";
                                }
                                // 先判断阻值，再判断耐压；RES 大于阈值为不合格
                                else if (pi.Res > DataModel.Processmodel.ResParameter.Max_Res)
                                {
                                    sqlite.WriteErrorLog("[追踪]点检阻值阈值判定NG3",
                                        $"RES={pi.Res} > Max_Res={DataModel.Processmodel.ResParameter.Max_Res}",
                                        pi.Productinfo?.SN ?? "", pi.Productinfo?.WOCODE ?? "");
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
                            TracePressureNg3IfFailed("CHECK1",
                                MaxPressure, MinPressure, AveragePressure, PressureResult,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE);
                        }

                        #endregion


                        // 调用Check1进行第一次综合校验（拍照留底、耐压测试、阻值测试）
                        // AOI-only模式：当耐压工位均不可用时，不伪造耐压记录，CHECK阶段跳过全部电测校验（TV/RES/压力）
                        bool isAoiOnlyMode = IsAoiOnlyMode;
                        var r = sqlite.Check1(
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                            DataModel.Processmodel.ResParameter.Max_Res,
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

                        SaveStandardElectricalProcessDataToMes(
                            "CHECK1",
                            DataModel.Settingmodel.SETTING_DATA.StationCode,
                            DataModel.Settingmodel.SETTING_DATA.MachineID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                            resultstr,
                            uploadAllModes: true);
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

                    // CHECK2 继续保留点检专用分支，用实测结果完成最终机器人分流。
                    string currentSN = DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN;
                    int inspectionMatchCount = GetInspectionSnMatchCount(currentSN);
                    if (inspectionMatchCount > 1)
                    {
                        writeLog($"[CHECK2] 点检SN配置重复，SN={currentSN}, 命中配置数={inspectionMatchCount}，已返回NG2进入NG分流。", true);
                        SendMsgRobot("NG2");
                        return;
                    }

                    bool isInspectionSN = IsInspectionSn(currentSN);

                    // 判断是否是 AOI 点检 SN（在 CHECK2 中只需检查 AOI 结果，不检查耐压、阻值）
                    bool isAoiInspectionSN = IsAoiInspectionSn(currentSN);
                    bool isIrInspectionSN = IsIrInspectionSn(currentSN);

                    if (isIrInspectionSN)
                    {
                        HandleIrInspectionCheck(
                            "CHECK2",
                            DataModel.Settingmodel.SETTING_DATA.StationCode2,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo);
                        return;
                    }

                    if (isInspectionSN)
                    {
                        // 从 ProductInfoRecords 取数并做综合判断的整体思路：
                        // 1. 先用 SN 在集合中找到对应的 ProductInfoRecord；
                         // 2. 使用记录中的拍照、耐压、阻值、压力等字段进行一次「内存级」综合判定；
                        // 3. 不再调用 sqlite.Check2，避免重复访问数据库；
                        // 4. 仅将最终判定结果连同过程数据一起 SaveBusBarData 到 MES，不做报工。
                         // 点检SN码：耐压点检按电测与压力结果判断，AOI结果仅保留过程追溯；AOI点检使用独立判定口径。
                        string MSG = "NG1";
                        string resultstr = "拍照留底不良";
                        
                         // 从 ProductInfoRecords 中获取实际测试数据（拍照留底、耐压、阻值、压力和 AOI 追溯结果）
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
                                // AOI NG点检：全部判定工具须为 NG 或 NG2，才向 PLC 写通过信号
                                bool isAoiNGInspection = IsConfiguredInspectionSn(
                                    currentSN, DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN);

                                if (isAoiNGInspection)
                                {
                                    bool allToolsNG = CheckAllAOIToolsNG();

                                    if (allToolsNG)
                                    {
                                        MSG = "NG4";
                                        resultstr = "AOI_NG点检通过(所有工具均为NG/NG2)";
                                        WriteAOI_NG_InspectionSignal(1);
                                    }
                                    else
                                    {
                                        MSG = "NG4";
                                        resultstr = "AOI_NG点检不通过(存在OK或未完成工具)";
                                        WriteAOI_NG_InspectionSignal(0);
                                    }
                                }
                                else
                                {
                                    // AOI OK 点检按整轮工具状态判定
                                    bool allToolsOK = CheckAllAOIToolsOK();
                                    pi.AppearanceInspection = allToolsOK;

                                    if (!allToolsOK)
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
                                 // 耐压点检 SN：判断拍照、耐压、阻值和压力；AOI 结果保留在记录中供追溯。
                                if (!pi.TakePhoto1)
                                {
                                    MSG = "NG1";
                                    resultstr = "拍照留底不良";
                                }
                                // 先判断阻值，再判断耐压；RES 大于阈值为不合格
                                else if (pi.Res > DataModel.Processmodel.ResParameter.Max_Res)
                                {
                                    sqlite.WriteErrorLog("[追踪]点检阻值阈值判定NG3",
                                        $"RES={pi.Res} > Max_Res={DataModel.Processmodel.ResParameter.Max_Res}",
                                        pi.Productinfo?.SN ?? "", pi.Productinfo?.WOCODE ?? "");
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
                            DataModel.Processmodel.ResParameter.Max_Res,
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

                        SaveStandardElectricalProcessDataToMes(
                            "CHECK2",
                            DataModel.Settingmodel.SETTING_DATA.StationCode2,
                            DataModel.Settingmodel.SETTING_DATA.MachineID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                            DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                            resultstr,
                            uploadAllModes: false);

                        #endregion
                        #region 汇报结果数据                    
                        report2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, resultstr);
                        #endregion
                    }

                }
                else
                {
                    writeLog($"[机器人交互] 未知指令 cmd={cmd}，详情查看日志\\机器人通信中的日志。", true);
                }


            }
            catch (Exception ex)
            {
                if (IsRobotDisconnectException(ex))
                {
                    DataModel.Settingmodel.RobotConnect.IsConnected = false;
                    ShowRobotDisconnectNotice("机器人消息处理方法异常RobotTcpServer_MessageReceived");
                }

                writeLog($"[机器人交互] ❌ 指令处理异常 cmd={cmd}: {ex.Message}", true);
                sqlite.WriteErrorLog("[机器人交互异常]指令处理失败", $"cmd={cmd}, 异常: {ex.Message}, 堆栈: {ex.StackTrace}");
            }
        }





        private bool report(string wocode, string sn, string result)
        {
            try
            {
                string failMessage;
                var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Save_EquipmentRecord_mes(
                        DataModel.Settingmodel.SETTING_DATA.StationCode,
                        wocode,
                        sn,
                        DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                        DataModel.Settingmodel.SETTING_DATA.MachineID,
                        result == "OK" ? "合格" : result,
                        DataModel.Settingmodel.SETTING_DATA.StandardCode,
                        out failMessage);

                if (r)
                {
                    writeLog($"{sn}:{result};报工:True");
                }
                else
                {
                    writeLog($"{sn}:{result};报工:False;{failMessage}");
                    HandleReportWorkFailure(sn, failMessage);
                }
                return r;
            }
            catch (Exception ex)
            {
                writeLog($"{sn}:{result};报工:False(程序异常)");
                writeError($"报工程序异常: SN={sn}, 工单={wocode}, 异常={ex.Message}");
                HandleReportWorkFailure(sn, ex.Message);
                return false;
            }
        }
        private bool report2(string wocode, string sn, string result)
        {
            try
            {
                string failMessage;
                var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Save_EquipmentRecord_mes(
                        DataModel.Settingmodel.SETTING_DATA.StationCode2,
                        wocode,
                        sn,
                        DataModel.Settingmodel.SETTING_DATA.ProcedureName2,
                        DataModel.Settingmodel.SETTING_DATA.MachineID,
                        result == "OK" ? "合格" : result,
                        DataModel.Settingmodel.SETTING_DATA.StandardCode2,
                        out failMessage);

                if (r)
                {
                    writeLog($"{sn}:{result};报工2:True");
                }
                else
                {
                    writeLog($"{sn}:{result};报工2:False;{failMessage}");
                    HandleReportWorkFailure(sn, failMessage);
                }
                return r;
            }
            catch (Exception ex)
            {
                writeLog($"{sn}:{result};报工2:False(程序异常)");
                writeError($"报工2程序异常: SN={sn}, 工单={wocode}, 异常={ex.Message}");
                HandleReportWorkFailure(sn, ex.Message);
                return false;
            }
        }




        #endregion

    }
}
