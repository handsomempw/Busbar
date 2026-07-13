using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.Record;
using BusbarCompressionSystem.Model.Setting1;
using BusbarCompressionSystem.Utils;
using GalaSoft.MvvmLight;
using System;
using System.IO;
using System.Windows;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        /// <summary>
        /// 系统过程数据本轮启动时的加载状态。已有 XML 损坏且无法从备份恢复时，关闭软件保存会跳过该文件，保留现场样本供维护排查。
        /// </summary>
        private bool _processConfigLoadFailed;

        /// <summary>
        /// 系统主配置本轮启动时的加载状态。该配置承载设备参数、MES/PLC/耐压仪通信和权限策略，加载失败后禁止默认对象覆盖现场文件。
        /// </summary>
        private bool _settingConfigLoadFailed;

        /// <summary>
        /// 系统记录数据本轮启动时的加载状态。已有 XML 损坏且无法恢复时，自动保存会跳过该文件，避免清空现场追溯数据。
        /// </summary>
        private bool _recordConfigLoadFailed;

        /// <summary>
        /// 视觉主配置本轮启动时的加载状态。该配置决定 AOI 工程目录、工程名称和相机相关参数，加载失败后保存流程会保护磁盘文件。
        /// </summary>
        private bool _faraVisionSettingConfigLoadFailed;

        /// <summary>
        /// 视觉记录数据本轮启动时的加载状态。已有 XML 损坏且无法恢复时，自动保存会跳过该文件，避免追溯记录被默认对象覆盖。
        /// </summary>
        private bool _faraVisionRecordConfigLoadFailed;

        /// <summary>
        /// AOI 工程工具 XML 本轮加载状态。任一 <c>Tool*.xml</c> 无法从备份恢复时，工程保存会停止，防止工具顺序压缩后误删现场配方。
        /// </summary>
        private bool _aoiProjectLoadFailed;

        /// <summary>
        /// 持久化文件统一保存入口。调用方传入本轮加载状态，首装默认对象可落盘，已有文件损坏时保护现场 XML。
        /// </summary>
        /// <typeparam name="T">XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">目标 XML 文件完整路径。</param>
        /// <param name="data">准备保存的数据对象。</param>
        /// <param name="loadFailed">本轮启动时该文件是否发生不可恢复的加载失败。</param>
        /// <returns>返回 true 表示文件保存完成；返回 false 表示保护策略拦截或写入失败。</returns>
        private bool SaveXmlSafely<T>(string filename, T data, bool loadFailed = false) where T : class
        {
            return ConfigXmlSaveHelper.TrySave(filename, data, loadFailed, message => writeLog(message));
        }

        private string GetConfigPath(string fileName)
        {
            return Path.Combine(Environment.CurrentDirectory, "配置", fileName);
        }

        /// <summary>
        /// 在系统配置加载前备份 exe 目录下的 <c>配置</c> XML。快照用于保留启动前现场样本，不改变后续配置加载、备份恢复和默认模板生成流程。
        /// </summary>
        public void BackupConfigXmlsBeforeLoad()
        {
            string configDir = Path.Combine(Environment.CurrentDirectory, "配置");
            string backupRoot = Path.Combine(Environment.CurrentDirectory, "配置备份");
            ConfigXmlSaveHelper.BackupXmlFiles(configDir, backupRoot, "系统配置", message => writeLog(message));
        }

        #region 数据保存加载
        #region 过程数据
        public void SaveProcessmodel()
        {
            string filename = GetConfigPath("过程数据.xml");
            SaveXmlSafely(filename, DataModel.Processmodel, _processConfigLoadFailed);
        }
        public void LoadProcessmodel()
        {
            try
            {
                string filename = GetConfigPath("过程数据.xml");
                ConfigLoadResult<Processmodel> result = ConfigXmlSaveHelper.TryLoad(
                    filename,
                    () => new Processmodel(),
                    message => writeLog(message));

                DataModel.Processmodel = result.Data ?? new Processmodel();
                _processConfigLoadFailed = result.LoadFailed;
            }
            catch (Exception ex)
            {
                DataModel.Processmodel = new Processmodel();
                _processConfigLoadFailed = true;
                writeLog($"[配置加载] 过程数据.xml加载异常：{ex.Message}");
            }
        }
        #endregion

        #region 配置数据

        /// <summary>
        /// 配置数据 XML 无「报工失败报警使能」节点时，反序列化会得到 false；缺节点时按业务默认视为开启。
        /// </summary>
        private void ApplyReportFailAlarmDefaultFromXml(string xmlText)
        {
            if (DataModel?.Settingmodel?.SETTING_DATA == null || string.IsNullOrEmpty(xmlText))
            {
                return;
            }

            if (xmlText.IndexOf("报工失败报警使能", StringComparison.Ordinal) < 0)
            {
                DataModel.Settingmodel.SETTING_DATA.ReportFailAlarmEnabled = true;
            }
        }

        /// <summary>
        /// 兼容旧版现场配置中的动态密码节点缺失场景。
        /// 现场升级保留 <c>配置\配置数据.xml</c> 时沿用已保存值；缺少动态密码认证节点时按生产认证口径补齐，避免设备权限默认落入开发联调通道。
        /// </summary>
        /// <param name="xmlText">从现场主配置文件读取的原始 XML 文本，用于判断旧配置是否声明过动态密码认证节点。</param>
        private void ApplyDynamicPasswordProductionDefaultFromXml(string xmlText)
        {
            if (DataModel?.Settingmodel?.SETTING_DATA == null)
            {
                return;
            }

            if (DataModel.Settingmodel.SETTING_DATA.DynamicPasswordAuth == null)
            {
                DataModel.Settingmodel.SETTING_DATA.DynamicPasswordAuth = new DynamicPasswordAuthenticationSetting();
            }

            if (string.IsNullOrEmpty(xmlText) ||
                xmlText.IndexOf("严格模式", StringComparison.Ordinal) < 0 ||
                xmlText.IndexOf("测试模式", StringComparison.Ordinal) < 0)
            {
                DataModel.Settingmodel.SETTING_DATA.DynamicPasswordAuth.StrictMode = true;
                DataModel.Settingmodel.SETTING_DATA.DynamicPasswordAuth.TestMode = false;
            }
        }

        public void SaveSettingModel()
        {
            string filename = GetConfigPath("配置数据.xml");
            SaveXmlSafely(filename, DataModel.Settingmodel, _settingConfigLoadFailed);
        }
        public void LoadSettingModel()
        {
            try
            {
                string filename = GetConfigPath("配置数据.xml");
                ConfigLoadResult<SettingModel> result = ConfigXmlSaveHelper.TryLoad(
                    filename,
                    () => new SettingModel(),
                    message => writeLog(message));

                DataModel.Settingmodel = result.Data ?? new SettingModel();
                _settingConfigLoadFailed = result.LoadFailed;

                ApplyReportFailAlarmDefaultFromXml(result.XmlText);
                ApplyDynamicPasswordProductionDefaultFromXml(result.XmlText);

                if (result.RestoredFromBackup)
                {
                    writeLog($"配置数据.xml已从备份恢复：{Path.GetFileName(result.RestoredFrom)}");
                }
                else if (result.LoadFailed)
                {
                    MessageBox.Show("配置数据.xml加载失败且无法从备份恢复，软件本轮使用默认内存配置；关闭软件时会跳过该文件保存，现场 XML 文件会保留供维护排查。");
                }
                
                // 加载独立的耐压状态映射配置文件
                LoadTvStatusMappings();
                LoadHipotCommParameters();
            }
            catch (Exception ex)
            {
                DataModel.Settingmodel = new SettingModel();
                _settingConfigLoadFailed = true;
                ApplyDynamicPasswordProductionDefaultFromXml(null);

                MessageBox.Show($"配置数据.xml加载异常，软件本轮使用默认内存配置；关闭软件时会跳过该文件保存，现场 XML 文件会保留供维护排查:\r\n{ex.Message}");
                
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
            string filename = GetConfigPath("日志数据.xml");
            SaveXmlSafely(filename, DataModel.Recordmodel, _recordConfigLoadFailed);
        }
        public void LoadRecordModel()
        {
            try
            {
                string filename = GetConfigPath("日志数据.xml");
                ConfigLoadResult<RecordModel> result = ConfigXmlSaveHelper.TryLoad(
                    filename,
                    () => new RecordModel(),
                    message => writeLog(message));

                DataModel.Recordmodel = result.Data ?? new RecordModel();
                _recordConfigLoadFailed = result.LoadFailed;
            }
            catch (Exception ex)
            {
                DataModel.Recordmodel = new RecordModel();
                _recordConfigLoadFailed = true;
                writeLog($"[配置加载] 日志数据.xml加载异常：{ex.Message}");
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
                string xmlPath = GetConfigPath("耐压状态映射.xml");

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
                string xmlPath = GetConfigPath("耐压仪通信参数.xml");
                HipotCommParameters p;
                bool shouldCreateTemplate = !File.Exists(xmlPath);

                ConfigLoadResult<HipotCommParameters> result = ConfigXmlSaveHelper.TryLoad(
                    xmlPath,
                    () => new HipotCommParameters(),
                    message => writeLog(message));

                p = result.Data ?? new HipotCommParameters();

                if (shouldCreateTemplate && !result.LoadFailed)
                {
                    SaveXmlSafely(xmlPath, p);
                    writeLog($"已生成默认耐压仪通信参数: {xmlPath}");
                }
                else if (result.RestoredFromBackup)
                {
                    writeLog($"耐压仪通信参数已从备份恢复: {Path.GetFileName(result.RestoredFrom)}");
                }
                else if (result.LoadFailed)
                {
                    writeLog("耐压仪通信参数加载失败且无法从备份恢复，本轮使用默认通信参数");
                }
                else
                {
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
            applyOne(DataModel.Settingmodel.AT9620_3);
        }

        private string GetLocalizedTvStatus(string status)
        {
            return TvStatusTranslator.Translate(status);
        }
        #endregion

        #endregion
    }
}
