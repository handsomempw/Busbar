using HslCommunication.ModBus;
using Panuon.WPF.UI;
using System;
using System.Windows;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel
    {
        /// <summary>
        /// 报工失败后的统一处置：按配置决定是否写 M3045 与弹窗提示。
        /// </summary>
        /// <param name="sn">报工失败对应的产品 SN。</param>
        /// <param name="reason">MES 或程序异常返回的失败说明，用于弹窗与日志。</param>
        /// <remarks>
        /// 「报工失败报警使能」为 false 时仅记录追溯日志，不写 PLC、不弹窗；进站联锁完全由现场/PLC 自行处理。
        /// 使能为 true 时：PLC 联锁由 M3045 执行，弹窗通过 BeginInvoke 投递，不阻塞报工/机器人回调线程。
        /// </remarks>
        private void HandleReportWorkFailure(string sn, string reason)
        {
            string displayReason = string.IsNullOrWhiteSpace(reason) ? "MES报工返回失败" : reason.Trim();
            string displaySn = sn ?? string.Empty;
            int addr = DataModel.Settingmodel.ReportFailBlockCoilAddress;

            if (DataModel.Settingmodel.SETTING_DATA == null
                || !DataModel.Settingmodel.SETTING_DATA.ReportFailAlarmEnabled)
            {
                writeLog($"[报工失败追溯] SN={displaySn}, 报工失败报警使能=关闭, 未写M{addr}/未弹窗, 原因={displayReason}", true);
                return;
            }

            bool plcWriteOk = WriteReportFailBlockCoil(true);
            string plcLogPart = plcWriteOk
                ? $"M{addr}=1(已确认写入)"
                : $"M{addr}置1失败(未确认写入)";
            writeLog($"[报工失败追溯] SN={displaySn}, {plcLogPart}, 原因={displayReason}", true);
            ShowReportFailBlockedDialog(displaySn, displayReason, plcWriteOk);
        }

        /// <summary>
        /// 向 PLC 写入报工失败禁止进站线圈（M 寄存器）。
        /// </summary>
        /// <param name="block">true 禁止进站；false 允许进站（本业务不在上位机侧主动写 0）。</param>
        /// <returns>true 表示 PLC 已确认写入；false 表示连接、写入或程序异常。</returns>
        private bool WriteReportFailBlockCoil(bool block)
        {
            int addr = DataModel.Settingmodel.ReportFailBlockCoilAddress;
            try
            {
                ModbusTcpNet modbusTcp = new ModbusTcpNet();
                modbusTcp.ConnectTimeOut = 1000;
                modbusTcp.ReceiveTimeOut = 1000;
                modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;

                var connectresult = modbusTcp.ConnectServer();
                if (!connectresult.IsSuccess)
                {
                    writePlcError($"[报工失败禁止进站] PLC连接失败，无法写入M{addr}={(block ? 1 : 0)}: {connectresult.Message}");
                    return false;
                }

                var writeResult = modbusTcp.WriteCoil(addr.ToString(), block);
                modbusTcp.ConnectClose();
                if (!writeResult.IsSuccess)
                {
                    writePlcError($"[报工失败禁止进站] 写入M{addr}={(block ? 1 : 0)}失败: {writeResult.Message}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                writePlcError($"[报工失败禁止进站] 写入M{addr}异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 在 UI 线程异步弹出提示，告知报工失败 SN 及原因。
        /// </summary>
        /// <remarks>
        /// 使用 BeginInvoke，报工线程在写 M3045 与记日志后即可返回，不等待操作员关闭弹窗。
        /// </remarks>
        /// <param name="sn">报工失败产品 SN。</param>
        /// <param name="reason">MES 或程序异常返回的失败说明。</param>
        /// <param name="plcWriteOk">PLC 禁止进站线圈写入结果，用于提示操作员现场联锁状态。</param>
        private void ShowReportFailBlockedDialog(string sn, string reason, bool plcWriteOk)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        int addr = DataModel.Settingmodel.ReportFailBlockCoilAddress;
                        string plcMessage = plcWriteOk
                            ? $"PLC禁止进站信号已写入 M{addr}=1"
                            : $"PLC禁止进站信号写入失败，请立即人工停线/确认 M{addr} 与现场进站状态";
                        MessageBoxX.Show(
                            $"报工失败！\nSN：{sn}\n原因：{reason}\n{plcMessage}\n\n请手动取出产品处理",
                            "报工失败 - 禁止进站",
                            MessageBoxButton.OK,
                            MessageBoxIcon.Error);
                    }
                    catch (Exception ex)
                    {
                        writeLog($"报工失败弹窗异常: {ex.Message}", true);
                    }
                }));
            }
            catch (Exception ex)
            {
                writeLog($"报工失败弹窗投递异常: {ex.Message}", true);
            }
        }
    }
}
