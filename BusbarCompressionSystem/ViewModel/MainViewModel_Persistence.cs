using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.Record;
using BusbarCompressionSystem.Model.Setting1;
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
                LoadHipotCommParameters();
            }
            catch (Exception ex)
            {
                DataModel.Settingmodel = new SettingModel();

                MessageBox.Show($"配置数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");
                
                // 加载独立的耐压状态映射配置文件
                LoadTvStatusMappings();
                LoadHipotCommParameters();
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

        /// <summary>
        /// 加载耐压仪 TCP 通信参数（Fetch 短超时、重试次数与间隔、其它命令超时、发送后等待等），并通过
        /// <see cref="ApplyHipotCommParametersToAllMeters"/> 写入各台 <see cref="AT9620.AT9620"/> 实例属性。
        /// 文件路径：<c>配置\耐压仪通信参数.xml</c>，与 <c>配置数据.xml</c> 分离，元素标签中文便于现场直接编辑。
        /// 若文件不存在：用默认 <see cref="HipotCommParameters"/> 经 <see cref="SaveXmlSafely{T}"/> 生成模板。
        /// 反序列化或 IO 异常时记录日志并回退为默认参数，避免软件无法启动。
        /// </summary>
        private void LoadHipotCommParameters()
        {
            try
            {
                string xmlPath = Path.Combine(Environment.CurrentDirectory, "配置", "耐压仪通信参数.xml");
                HipotCommParameters p;

                if (!File.Exists(xmlPath))
                {
                    p = new HipotCommParameters();
                    SaveXmlSafely(xmlPath, p);
                    writeLog($"已生成默认耐压仪通信参数: {xmlPath}");
                }
                else
                {
                    using (var stream = File.OpenRead(xmlPath))
                    {
                        var serializer = new XmlSerializer(typeof(HipotCommParameters));
                        p = serializer.Deserialize(stream) as HipotCommParameters ?? new HipotCommParameters();
                    }
                    writeLog($"已加载耐压仪通信参数: {xmlPath}");
                }

                ApplyHipotCommParametersToAllMeters(p);
            }
            catch (Exception ex)
            {
                writeLog($"耐压仪通信参数处理异常: {ex.Message}");
                ApplyHipotCommParametersToAllMeters(new HipotCommParameters());
            }
        }

        /// <summary>
        /// 将同一份 XML 解析结果同步到当前 <see cref="DataModel.Settingmodel"/> 下的各台耐压仪对象。
        /// 所有耐压仪表共用一份 <c>耐压仪通信参数.xml</c>，现场改一处即可统一行为；仪器侧属性名为英文，与中文标签 XML 由 POCO 映射。
        /// </summary>
        private void ApplyHipotCommParametersToAllMeters(HipotCommParameters p)
        {
            if (p == null)
            {
                p = new HipotCommParameters();
            }

            void applyOne(AT9620.AT9620 m)
            {
                m.FetchReceiveTimeoutMs = p.FetchReceiveTimeoutMs;
                m.FetchMaxAttempts = p.FetchMaxAttempts;
                m.FetchRetryDelayMs = p.FetchRetryDelayMs;
                m.OtherCommandReceiveTimeoutMs = p.OtherCommandReceiveTimeoutMs;
                m.PostSendDelayMs = p.PostSendDelayMs;
            }

            applyOne(DataModel.Settingmodel.AT9620_1);
            applyOne(DataModel.Settingmodel.AT9620_2);
            //applyOne(DataModel.Settingmodel.AT9620_3);
        }

        private string GetLocalizedTvStatus(string status)
        {
            return TvStatusTranslator.Translate(status);
        }
        #endregion

        #endregion
    }
}
