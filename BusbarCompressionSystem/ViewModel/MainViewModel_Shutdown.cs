using System;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel
    {
        /// <summary>
        /// 停止设备触发入口并释放电测、扫码和 TCP 连接。
        /// 先停止 PLC 轮询并置位仪器 stop，等待扫码/电测/归档线程在连接仍存活时发出停机或放电命令，再断开通信。
        /// 双Y与标准部署均关闭耐压仪、绝缘电阻仪和各自扫码器；标准部署另外关闭机器人服务端和标准扫码器连接。
        /// </summary>
        /// <param name="includeStandardCommunication">标准生产窗口关闭时传 true，用于释放机器人和标准扫码通信；双Y专用窗口传 false。</param>
        public void ShutdownRuntimeConnections(bool includeStandardCommunication)
        {
            StopRuntimeWorkers();

            // 先置位 stop，保持连接，让在测线程发送 FUNCtion:STOP / STAT:DISC。
            try { if (DataModel?.Settingmodel?.AT9620_1 != null) DataModel.Settingmodel.AT9620_1.stop = true; } catch { }
            try { if (DataModel?.Settingmodel?.AT9620_2 != null) DataModel.Settingmodel.AT9620_2.stop = true; } catch { }
            try { if (DataModel?.Settingmodel?.AT9620_3 != null) DataModel.Settingmodel.AT9620_3.stop = true; } catch { }
            try { if (DataModel?.Settingmodel?.AT6835FL_1 != null) DataModel.Settingmodel.AT6835FL_1.stop = true; } catch { }

            JoinRuntimeBusinessWorkers();

            TryShutdownResource("耐压仪1", () => DataModel.Settingmodel.AT9620_1.Shutdown());
            TryShutdownResource("耐压仪2", () => DataModel.Settingmodel.AT9620_2.Shutdown());
            TryShutdownResource("耐压仪3", () => DataModel.Settingmodel.AT9620_3.Shutdown());
            TryShutdownResource("绝缘电阻仪", () => DataModel.Settingmodel.AT6835FL_1.Shutdown());

            TryShutdownResource("双Y-Y1扫码器", () => DataModel.DualYConfiguration?.Station1HF800?.disconnect());
            TryShutdownResource("双Y-Y2扫码器", () => DataModel.DualYConfiguration?.Station2HF800?.disconnect());

            if (includeStandardCommunication)
            {
                TryShutdownResource("标准上料扫码器", () => DataModel.Settingmodel.HF800.disconnect());
                TryShutdownResource("标准下料扫码器", () => DataModel.Settingmodel.SecondHF800.disconnect());
                TryShutdownResource("机器人TCP服务端", () => DataModel.Settingmodel.TcpServerRobot?.StopListener());
            }
        }

        /// <summary>
        /// 释放单项运行资源并记录结果。一个设备释放异常只进入诊断日志，其余设备继续按顺序关闭。
        /// </summary>
        /// <param name="resourceName">写入退出日志的设备或通信资源名称。</param>
        /// <param name="shutdown">该资源的断开、停止监听或关闭连接动作。</param>
        private void TryShutdownResource(string resourceName, Action shutdown)
        {
            try
            {
                shutdown?.Invoke();
                writeLog($"[软件退出] {resourceName}已释放");
            }
            catch (Exception ex)
            {
                writeLog($"[软件退出] {resourceName}释放异常：{ex}", true);
            }
        }
    }
}
