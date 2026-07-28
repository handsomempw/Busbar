using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BusbarCompressionSystem.Utils
{
    /// <summary>
    /// 软件关闭的进程级兜底。配置采用临时文件校验和原子替换，关闭流程超过时限时可安全终止残留前台线程并保留正式配置。
    /// </summary>
    internal sealed class ShutdownWatchdog
    {
        /// <summary>
        /// 关闭流程撤销信号。标准部署在 AOI 工程保存失败且操作员选择继续运行时触发该信号。
        /// </summary>
        private readonly ManualResetEventSlim _cancelSignal = new ManualResetEventSlim(false);

        /// <summary>
        /// 创建进程退出计时器。计时线程使用后台模式，不影响正常 WPF 退出。
        /// </summary>
        /// <param name="timeout">配置保存和设备释放允许占用的最长时间。</param>
        private ShutdownWatchdog(TimeSpan timeout)
        {
            var thread = new Thread(() => WaitForExit(timeout))
            {
                IsBackground = true,
                Name = "软件退出看门线程"
            };
            thread.Start();
        }

        /// <summary>
        /// 启动关闭时限。操作员确认继续运行时可调用 <see cref="Cancel"/>；正常关闭后该后台线程随进程结束。
        /// </summary>
        /// <param name="timeout">保存和设备释放允许占用的最长时间。</param>
        /// <returns>可在取消关闭时撤销的看门对象。</returns>
        public static ShutdownWatchdog Start(TimeSpan timeout)
        {
            return new ShutdownWatchdog(timeout);
        }

        /// <summary>
        /// 撤销本次关闭时限，用于 AOI 工程保存失败后操作员选择继续运行软件的场景。
        /// </summary>
        public void Cancel()
        {
            _cancelSignal.Set();
        }

        /// <summary>
        /// 等待关闭流程结束或撤销信号；达到时限时写入诊断日志并终止残留进程。
        /// </summary>
        /// <param name="timeout">本次退出流程时限。</param>
        private void WaitForExit(TimeSpan timeout)
        {
            if (_cancelSignal.Wait(timeout))
            {
                return;
            }

            WriteTimeoutLog(timeout);
            Environment.Exit(0);
        }

        /// <summary>
        /// 将强制退出原因写入当天错误日志，供现场区分正常退出和超时兜底。
        /// 日志失败时仍继续完成进程退出。
        /// </summary>
        /// <param name="timeout">触发兜底的配置保存与设备释放时限。</param>
        private static void WriteTimeoutLog(TimeSpan timeout)
        {
            try
            {
                string directory = Path.Combine(Environment.CurrentDirectory, "日志", "错误");
                Directory.CreateDirectory(directory);
                string filename = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd") + ".txt");
                string message = string.Format(
                    "[{0:yyyy-MM-dd HH:mm:ss.fff}][软件退出] 关闭流程超过 {1:F0} 秒，执行进程退出兜底。{2}",
                    DateTime.Now,
                    timeout.TotalSeconds,
                    Environment.NewLine);
                File.AppendAllText(filename, message, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
