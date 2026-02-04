using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.Record;
using GalaSoft.MvvmLight;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        // 日志相关方法拆分到独立文件，便于维护
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

                // 【日志落盘加锁】现场存在多线程写同一日志文件的情况，不加锁容易产生 txt 写入异常并刷屏
                lock (writeLog_Locker)
                {
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
                // 【错误落盘加锁】避免多线程同时写入导致“txt 相关异常”反复出现
                lock (writeBug_Locker)
                {
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
            }
            catch (Exception)
            {
            }

        }

        /// <summary>
        /// PLC 相关错误专用落盘：避免高频 PLC 异常刷屏污染“日志\\错误”。
        /// 输出路径：日志\\PLC异常\\{yyyyMMdd}.txt
        /// 说明：UI 错误列表仍会显示该条目（便于现场快速看到），但落盘独立归档。
        /// </summary>
        internal void writePlcError(string Content)
        {
            try
            {
                int num = 200;
                string bugstr = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF")}]{Content}";
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (DataModel.Recordmodel.ErrorLog.Count > num)
                    {
                        DataModel.Recordmodel.ErrorLog.Clear();
                    }
                    DataModel.Recordmodel.ErrorLog.Insert(0, bugstr);
                });

                lock (writeBug_Locker)
                {
                    string filename = $"{Environment.CurrentDirectory}\\日志\\PLC异常\\{DateTime.Now.ToString("yyyyMMdd")}.txt";
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
            }
            catch (Exception)
            {
            }
        }
        #endregion
    }
}
