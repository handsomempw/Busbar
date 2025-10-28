using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BusbarCompressionSystem
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 应用程序启动时的初始化
        /// </summary>
        /// <param name="e">启动参数</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 注册全局异常处理器，防止应用程序闪退
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        /// <summary>
        /// 全局异常处理器 - 防止应用程序闪退
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">异常事件参数</param>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                // 使用现有的错误日志系统记录异常
                LogExceptionToExistingSystem(e.Exception);

                // 显示简洁的错误消息（符合用户偏好的简洁显示原则）
                MessageBox.Show($"程序异常：{e.Exception.Message}\n\n详情已记录到错误日志",
                               "系统异常",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);

                // 标记异常已处理，防止应用程序崩溃
                e.Handled = true;
            }
            catch (Exception ex)
            {
                // 如果异常处理本身出错，记录到Windows事件日志
                System.Diagnostics.EventLog.WriteEntry("BusbarCompressionSystem",
                    $"全局异常处理器失败: {ex.Message}",
                    System.Diagnostics.EventLogEntryType.Error);
            }
        }

        /// <summary>
        /// 使用现有日志系统记录异常信息
        /// </summary>
        /// <param name="exception">异常对象</param>
        private void LogExceptionToExistingSystem(Exception exception)
        {
            try
            {
                // 构建异常信息字符串（与现有日志格式保持一致）
                string errorContent = $"全局异常捕获 - {exception.GetType().Name}: {exception.Message}";

                // 使用现有的错误日志目录结构：日志\错误\yyyyMMdd.txt
                string filename = $"{Environment.CurrentDirectory}\\日志\\错误\\{DateTime.Now.ToString("yyyyMMdd")}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 构建详细日志内容（保持现有格式）
                string logContent = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{errorContent}\n" +
                                  $"堆栈跟踪: {exception.StackTrace}\n" +
                                  $"内部异常: {exception.InnerException?.Message ?? "无"}\n" +
                                  $"{"".PadRight(80, '-')}\n";

                // 写入到现有错误日志文件
                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    sw.WriteLine(logContent);
                    sw.Close();
                }
            }
            catch
            {
                // 如果日志记录失败，忽略错误（避免递归异常）
            }
        }
    }
}
