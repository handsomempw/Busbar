using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.Record;
using SQLITEDATABASE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BusbarCompressionSystem.ViewModel
{
    /// <summary>
    /// 双Y并行仅电测模式：2工位扫码、D1020/D1021 流程结束归档（替代 CHECK/机器人）。
    /// </summary>
    public partial class MainViewModel
    {
        private readonly object _dualYMesLock = new object();
        private int _dualYStation1FlowEndProcessing = 0;
        private int _dualYStation2FlowEndProcessing = 0;
        private bool? _lastDualYModeLogged = null;

        /// <summary>
        /// 运行时双Y模式判定：以 PLC M3050 为准；部署标志仅影响启动期初始化。
        /// </summary>
        public bool IsDualYElectricalTestModeActive()
        {
            return DataModel != null
                && DataModel.Processmodel != null
                && DataModel.Processmodel.DualYElectricalTestModeActive;
        }

        /// <summary>
        /// 双Y专用部署判定。
        /// 该配置用于启动阶段资源边界，双Y专用机台跳过机器人 TCP 与相机硬件初始化；运行中的产品流向仍以 PLC M3050 为准。
        /// </summary>
        public bool IsDualYElectricalTestDeployment()
        {
            return DataModel != null
                && DataModel.Settingmodel != null
                && DataModel.Settingmodel.SETTING_DATA != null
                && DataModel.Settingmodel.SETTING_DATA.DualYElectricalTestDeployment;
        }

        /// <summary>
        /// 从 PLC 读取 M3050 并刷新 <see cref="Processmodel.DualYElectricalTestModeActive"/>。
        /// </summary>
        internal void RefreshDualYModeFromPlc(bool coilValue)
        {
            if (DataModel?.Processmodel == null)
            {
                return;
            }

            DataModel.Processmodel.DualYElectricalTestModeActive = coilValue;
            if (_lastDualYModeLogged == null || _lastDualYModeLogged.Value != coilValue)
            {
                _lastDualYModeLogged = coilValue;
                writeLog($"[双Y电测] M{DataModel.Settingmodel.DualYElectricalTestModeCoilAddress}={(coilValue ? 1 : 0)}，模式={(coilValue ? "仅电测" : "标准产线")}", true);
            }
        }

        private bool TryBeginDualYFlowEndProcessing(int stationIndex)
        {
            if (stationIndex == 1)
            {
                return Interlocked.Exchange(ref _dualYStation1FlowEndProcessing, 1) == 0;
            }

            return Interlocked.Exchange(ref _dualYStation2FlowEndProcessing, 1) == 0;
        }

        private void EndDualYFlowEndProcessing(int stationIndex)
        {
            if (stationIndex == 1)
            {
                Interlocked.Exchange(ref _dualYStation1FlowEndProcessing, 0);
                return;
            }

            Interlocked.Exchange(ref _dualYStation2FlowEndProcessing, 0);
        }

        /// <summary>
        /// 根据当前电测模式得到双Y流程结束阶段必须存在的耐压过程数据。
        /// 单测模式只要求对应 ACW 或 DCW 行；双测模式要求 ACW 与 DCW 均完成，顺序由 PLC 测试流程控制。
        /// </summary>
        /// <param name="requireAcw">返回 true 表示本轮归档必须存在有效 ACW 过程行。</param>
        /// <param name="requireDcw">返回 true 表示本轮归档必须存在有效 DCW 过程行。</param>
        /// <returns>用于日志展示的当前测试模式名称。</returns>
        private string GetDualYRequiredElectricalModes(out bool requireAcw, out bool requireDcw)
        {
            requireAcw = false;
            requireDcw = false;

            switch (DataModel.Settingmodel.CurrentTestMode)
            {
                case AT9620.ElectricalTestMode.ACWOnly:
                    requireAcw = true;
                    break;
                case AT9620.ElectricalTestMode.DCWOnly:
                    requireDcw = true;
                    break;
                case AT9620.ElectricalTestMode.ACWThenDCW:
                case AT9620.ElectricalTestMode.DCWThenACW:
                    requireAcw = true;
                    requireDcw = true;
                    break;
                default:
                    requireAcw = true;
                    break;
            }

            return DataModel.Settingmodel.CurrentTestModeDisplay;
        }

        /// <summary>
        /// 双Y 2工位扫码：D1120 触发，写入 D950，逻辑对标工位1 <see cref="ScannerProcess"/>。
        /// </summary>
        public void DualYStation2ScannerProcess()
        {
            try
            {
                string scanRaw = string.Empty;
                bool hardwareOk = false;

                if (DataModel.Settingmodel.DualYStation2ScannerMode == "HF800")
                {
                    var r = DataModel.Settingmodel.DualYStation2HF800.Scanner();
                    if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(r.value))
                    {
                        scanRaw = r.value.Replace("\r", "").Replace("\n", "").Trim();
                        hardwareOk = true;
                    }
                }
                else
                {
                    var r = DataModel.Settingmodel.DualYStation2ScannerModel.Scanner();
                    scanRaw = r.receivestring?.Replace("\r", "").Replace("\n", "").Trim() ?? string.Empty;
                    hardwareOk = r.IsSuccess && !string.IsNullOrEmpty(scanRaw);
                }

                if (!hardwareOk)
                {
                    PLC_write(DataModel.Settingmodel.DualYStation2ScanResultAddress.ToString(), (UInt16)2);
                    return;
                }

                string scanError = ScanDualYStationSn(scanRaw, 2);
                bool businessOk = string.IsNullOrEmpty(scanError);
                PLC_write(DataModel.Settingmodel.DualYStation2ScanResultAddress.ToString(), (UInt16)(businessOk ? 1 : 2));
                if (!businessOk)
                {
                    writeLog($"[双Y-2工位扫码] 业务失败: {scanError}", true);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[双Y-2工位扫码] 异常: {ex.Message}", true);
                try
                {
                    PLC_write(DataModel.Settingmodel.DualYStation2ScanResultAddress.ToString(), (UInt16)2);
                }
                catch { }
            }
        }

        /// <summary>
        /// 在 WPF 线程刷新指定双Y工位的操作员展示状态。该入口负责界面摘要，
        /// 原业务流程继续负责 PLC 反馈、SQLite 数据和 MES 报工。
        /// </summary>
        /// <param name="stationIndex">双Y物理工位号，取值 1 或 2。</param>
        /// <param name="update">针对工位展示状态执行的刷新动作。</param>
        private void UpdateDualYStationDisplay(int stationIndex, Action<DualYStationDisplayState> update)
        {
            if (update == null || DataModel?.Processmodel == null)
            {
                return;
            }

            DualYStationDisplayState display = stationIndex == 1
                ? DataModel.Processmodel.DualYStation1Display
                : DataModel.Processmodel.DualYStation2Display;

            Action apply = () => update(display);
            if (App.Current?.Dispatcher != null && !App.Current.Dispatcher.CheckAccess())
            {
                App.Current.Dispatcher.BeginInvoke(apply);
                return;
            }

            apply();
        }

        /// <summary>
        /// D1020/D1021 流程结束：读 SN/压力、电测校验、ACW/DCW 各一条 MES 过程数据、报工一次。
        /// </summary>
        /// <param name="stationIndex">1 或 2。</param>
        public void DualYFlowEndProcess(int stationIndex)
        {
            if (stationIndex != 1 && stationIndex != 2)
            {
                return;
            }

            if (!TryBeginDualYFlowEndProcessing(stationIndex))
            {
                writeLog($"[双Y-工位{stationIndex}流程结束] 上一笔归档仍在处理，忽略重复触发。", true);
                return;
            }

            UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档处理中"));

            try
            {
                int snAddress = stationIndex == 1
                    ? DataModel.Settingmodel.AddressSN + 25
                    : DataModel.Settingmodel.AddressSN + 50;

                string snCode;
                string woCode;
                string rawCode;
                if (!TryReadProductCodeFromPlc(snAddress, $"双Y-工位{stationIndex}流程结束", out snCode, out woCode, out rawCode, 3, 500))
                {
                    writeLog($"[双Y-工位{stationIndex}流程结束] 产品编码读取失败，已中止归档。原始值=[{rawCode}]", true);
                    UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档失败", "产品码读取失败"));
                    return;
                }

                UpdateDualYStationDisplay(stationIndex, display =>
                {
                    display.BeginProduct(snCode, woCode);
                    display.UpdateWorkflow("归档处理中");
                });

                string partnoid = DataModel.Processmodel.PartNOID;
                bool requireAcw;
                bool requireDcw;
                string requiredModeText = GetDualYRequiredElectricalModes(out requireAcw, out requireDcw);
                writeLog($"[双Y归档] 开始，工位={stationIndex}, SN={snCode}, WO={woCode}, 模式={requiredModeText}");

                UInt16 averagePressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure);
                UInt16 maxPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 2);
                UInt16 minPressure = PLC_ReadUint16(DataModel.Settingmodel.AddressPressure + 4);
                bool pressureResult = maxPressure <= DataModel.Processmodel.PressureParamter.Max_Pressure
                    && minPressure >= DataModel.Processmodel.PressureParamter.Min_Pressure;

                bool pressureDbOk = sqlite.UpdatePressureForElectricalRows(woCode, partnoid, snCode, averagePressure, maxPressure, minPressure, pressureResult);
                if (!pressureDbOk)
                {
                    writeLog($"[双Y归档] SQLite压力写入失败，工位={stationIndex}, SN={snCode}, WO={woCode}", true);
                }
                updatepressure(snCode, averagePressure, maxPressure, minPressure, pressureResult);
                TracePressureNg3IfFailed($"双Y-工位{stationIndex}流程结束",
                    maxPressure, minPressure, averagePressure, pressureResult, snCode, woCode);

                int checkCode = sqlite.CheckElectricalOnlyDualTest(
                    woCode, partnoid, snCode, DataModel.Processmodel.ResParameter.Max_Res, requireAcw, requireDcw);
                if (!pressureResult && checkCode == 0)
                {
                    checkCode = 3;
                }
                string resultstr = MapElectricalCheckCodeToResultString(checkCode);
                writeLog($"[双Y归档] 综合判定完成，工位={stationIndex}, SN={snCode}, 结果={resultstr}");

                bool mesOk;
                bool reportOk;
                lock (_dualYMesLock)
                {
                    mesOk = SaveDualYElectricalProcessDataToMes(snCode, woCode, partnoid, resultstr, requireAcw, requireDcw);
                    reportOk = report(woCode, snCode, resultstr);
                }

                if (!reportOk)
                {
                    writeLog($"[双Y归档] 报工失败，工位={stationIndex}, SN={snCode}, WO={woCode}, 结果={resultstr}", true);
                }

                string archiveState = mesOk && reportOk ? "归档完成" : "归档异常";
                string archiveResult = $"{resultstr} / MES:{(mesOk ? "成功" : "失败")} / 报工:{(reportOk ? "成功" : "失败")}";
                UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow(archiveState, archiveResult));
                writeLog($"[双Y归档] 完成，工位={stationIndex}, SN={snCode}, 结果={resultstr}, MES过程数据={(mesOk ? "成功" : "失败")}, 报工={(reportOk ? "成功" : "失败")}");
            }
            catch (Exception ex)
            {
                writeLog($"[双Y归档] 异常，工位={stationIndex}, {ex.Message}", true);
                sqlite.WriteErrorLog("[双Y流程结束异常]归档失败", ex.Message, string.Empty, string.Empty);
                UpdateDualYStationDisplay(stationIndex, display => display.UpdateWorkflow("归档异常", "请查看运行日志"));
            }
            finally
            {
                EndDualYFlowEndProcessing(stationIndex);
            }
        }

        /// <summary>
        /// 双Y扫码成功后刷新对应工位卡片并建立 UI 过程行，替代拍照留底 <see cref="newline"/> 在双Y模式下的角色。
        /// 同一 SN 在 Y1/Y2 分别保留占位行；升级前未标记工位的占位行可由当前工位认领一次。
        /// </summary>
        /// <param name="sn">MES 解码和工单校验通过的产品 SN。</param>
        /// <param name="wocode">与当前产品对应的工单号，用于工位卡片和过程记录显示。</param>
        /// <param name="partnoid">当前产品规格；为空时沿用运行中的产品规格。</param>
        /// <param name="dualYStationIndex">扫码来源的双Y物理工位号，取值 1 或 2。</param>
        internal void EnsureProductInfoRecord(string sn, string wocode, string partnoid, int dualYStationIndex)
        {
            if (string.IsNullOrWhiteSpace(sn))
            {
                return;
            }

            string stationCode = DataModel.Settingmodel.SETTING_DATA.StationCode;
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    UpdateDualYStationDisplay(dualYStationIndex, display => display.BeginProduct(sn, wocode));

                    foreach (var existing in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (existing?.Productinfo?.SN == sn
                            && (existing.DualYStationIndex == dualYStationIndex || existing.DualYStationIndex == 0)
                            && string.IsNullOrWhiteSpace(existing.TVInfo)
                            && existing.TVMaxVoltage == 0
                            && string.IsNullOrWhiteSpace(existing.TVMeterID))
                        {
                            if (existing.DualYStationIndex == 0)
                            {
                                existing.DualYStationIndex = dualYStationIndex;
                            }

                            return;
                        }
                    }

                    var record = new ProductInfoRecord
                    {
                        StationCode = stationCode,
                        EQUIPMENTID = DataModel.Settingmodel.SETTING_DATA.MachineID,
                        DualYStationIndex = dualYStationIndex,
                        Productinfo = new Productinfo
                        {
                            SN = sn,
                            WOCODE = wocode ?? string.Empty,
                            PartNOID = string.IsNullOrWhiteSpace(partnoid) ? DataModel.Processmodel.PartNOID : partnoid
                        },
                        TakePhoto1 = false,
                        DateTime = DateTime.Now,
                        TestMode = string.Empty
                    };
                    DataModel.Recordmodel.ProductInfoRecords.Insert(0, record);
                    writeLog($"[双Y-工位{dualYStationIndex}扫码] 已建立界面过程行 SN={sn}");
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("[双Y]EnsureProductInfoRecord失败", ex.Message, sn, wocode);
                }
            }));
        }

        private static string MapElectricalCheckCodeToResultString(int checkCode)
        {
            switch (checkCode)
            {
                case 0: return "合格";
                case 2: return "耐压测试不合格";
                case 3: return "阻值或压力测试不合格";
                default: return "耐压测试不合格";
            }
        }

        /// <summary>
        /// 按当前测试模式上传双Y MES过程数据。
        /// 单测模式上传一条 ACW 或 DCW 过程数据；双测模式上传 ACW 与 DCW 各一条，报工仍由流程结束阶段统一执行一次。
        /// </summary>
        /// <param name="sn">当前流程结束工位从 PLC 产品码区读取到的 SN。</param>
        /// <param name="wocode">当前 SN 对应工单号。</param>
        /// <param name="partnoid">当前生产规格编码。</param>
        /// <param name="resultstr">本轮流程结束综合结果，用于 MES 过程数据最终结果字段。</param>
        /// <param name="requireAcw">true 表示本轮应上传 ACW 过程数据。</param>
        /// <param name="requireDcw">true 表示本轮应上传 DCW 过程数据。</param>
        /// <returns>所有期望过程数据均存在且上传成功时返回 true。</returns>
        private bool SaveDualYElectricalProcessDataToMes(string sn, string wocode, string partnoid, string resultstr, bool requireAcw, bool requireDcw)
        {
            string stationCode = DataModel.Settingmodel.SETTING_DATA.StationCode;
            string machineId = DataModel.Settingmodel.SETTING_DATA.MachineID;

            List<sqlite.ElectricalTestProcessRow> rows = sqlite.GetElectricalTestProcessRows(wocode, partnoid, sn);
            rows = rows
                .Where(row => (requireAcw && string.Equals(row.TestMode, "ACW", StringComparison.OrdinalIgnoreCase))
                    || (requireDcw && string.Equals(row.TestMode, "DCW", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (rows.Count == 0)
            {
                writeLog($"[双Y归档] MES过程数据上传失败，SN={sn}, WO={wocode}, 原因=本地期望电测记录为空", true);
                return false;
            }

            bool allOk = true;
            bool hasAcw = rows.Any(row => string.Equals(row.TestMode, "ACW", StringComparison.OrdinalIgnoreCase));
            bool hasDcw = rows.Any(row => string.Equals(row.TestMode, "DCW", StringComparison.OrdinalIgnoreCase));
            if ((requireAcw && !hasAcw) || (requireDcw && !hasDcw))
            {
                writeLog($"[双Y归档] MES过程数据不完整，SN={sn}, WO={wocode}, 期望ACW={requireAcw}, 实际ACW={hasAcw}, 期望DCW={requireDcw}, 实际DCW={hasDcw}", true);
                allOk = false;
            }

            foreach (var row in rows)
            {
                var persistedTvInfo = GetLocalizedTvStatus(row.TVInfo);
                bool saveOk = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                    stationCode, machineId, partnoid, wocode, sn,
                    row.TakePhoto1, row.Res, row.TVMaxVoltage, row.TVMaxCurrent, row.TVMeterID, persistedTvInfo, row.TVResult,
                    row.PressureMax, row.PressureAverage, row.PressureMin, row.PressureResult, row.TakePhoto2, resultstr);

                writeLog($"[双Y归档] MES过程数据上传{(saveOk ? "成功" : "失败")}，SN={sn}, WO={wocode}, 模式={row.TestMode}, 结果={resultstr}", !saveOk);
                allOk = allOk && saveOk;
            }

            return allOk;
        }
    }
}
