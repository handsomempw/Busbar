using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.Record;
using BusbarCompressionSystem.Utils;
using GalaSoft.MvvmLight;
using System;
using System.IO;
using System.Windows;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        // 持久化相关方法拆分到独立文件，方便维护
        private void SaveXmlSafely<T>(string filename, T data)
        {
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempFile = $"{filename}.tmp";
            string backupFile = $"{filename}.bak";

            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new XmlSerializer(typeof(T));
                    serializer.Serialize(stream, data);
                    stream.Flush();
                }

                if (File.Exists(filename))
                {
                    File.Replace(tempFile, filename, backupFile, true);
                    if (File.Exists(backupFile))
                    {
                        File.Delete(backupFile);
                    }
                }
                else
                {
                    File.Move(tempFile, filename);
                }
            }
            catch
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                throw;
            }
        }

        #region 数据保存加载
        #region 过程数据
        public void SaveProcessmodel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
            SaveXmlSafely(filename, DataModel.Processmodel);
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
            SaveXmlSafely(filename, DataModel.Settingmodel);
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
                
                // 加载独立的耐压状态映射配置文件
                LoadTvStatusMappings();
            }
            catch (Exception ex)
            {
                DataModel.Settingmodel = new SettingModel();

                MessageBox.Show($"配置数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");
                
                // 加载独立的耐压状态映射配置文件
                LoadTvStatusMappings();
            }
            
            // 订阅AT9620日志事件，将设备日志转发到应用日志
            DataModel.Settingmodel.AT9620_1.LogMessage += (s, e) => writeLog($"[耐压1] {e.Message}");
            DataModel.Settingmodel.AT9620_2.LogMessage += (s, e) => writeLog($"[耐压2] {e.Message}");
            DataModel.Settingmodel.AT9620_3.LogMessage += (s, e) => writeLog($"[耐压3] {e.Message}");
        }
        #endregion

        #region 日志数据
        public void SaveRecordModel()
        {
            string filename = $"{Environment.CurrentDirectory}\\配置\\日志数据.xml";
            SaveXmlSafely(filename, DataModel.Recordmodel);
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

        /// <summary>
        /// 加载耐压状态映射配置文件
        /// 文件路径：配置/耐压状态映射.xml
        /// 格式：&lt;StatusMappings&gt;&lt;Map Code="状态代码" Display="中文描述" /&gt;&lt;/StatusMappings&gt;
        /// </summary>
        private void LoadTvStatusMappings()
        {
            try
            {
                string xmlPath = Path.Combine(Environment.CurrentDirectory, "配置", "耐压状态映射.xml");

                if (!File.Exists(xmlPath))
                {
                    // 首次运行：生成默认配置文件
                    if (TvStatusTranslator.SaveDefaultXml(xmlPath, out string saveError))
                    {
                        writeLog($"已生成默认耐压状态映射配置文件: {xmlPath}");
                    }
                    else
                    {
                        writeLog($"生成默认耐压状态映射配置文件失败: {saveError}");
                    }
                }
                else
                {
                    // 加载现有配置文件
                    if (TvStatusTranslator.LoadFromXml(xmlPath, out string loadError))
                    {
                        writeLog($"已加载耐压状态映射配置: {xmlPath}");
                    }
                    else
                    {
                        writeLog($"耐压状态映射配置加载失败，使用默认配置: {loadError}");
                    }
                }
            }
            catch (Exception ex)
            {
                writeLog($"耐压状态映射配置处理异常: {ex.Message}");
            }
        }

        private string GetLocalizedTvStatus(string status)
        {
            return TvStatusTranslator.Translate(status);
        }
        #endregion

        #endregion
    }
}
