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
        /// 双Y专用配置本轮启动时的加载状态。已有文件损坏且无法恢复时，运行态从旧配置兼容值创建，关闭时保留损坏文件供维护排查。
        /// </summary>
        private bool _dualYConfigLoadFailed;

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
        /// AOI 工程配方结构完整性。工具 XML 加载失败，或 XML、示教图、SHM 文件重排中断且自动恢复失败时置位，工程保存会停止，防止半残配方覆盖现场目录。
        /// 模板匹配/定位工具的 .shm 未就绪使用 <see cref="_aoiShapeModelNotReady"/>，不进入本保护位，以免挡住修模型和生产检测。
        /// </summary>
        private bool _aoiProjectLoadFailed;

        /// <summary>
        /// 当前 AOI 工程中模板匹配或模板定位工具的形状模型未就绪。
        /// 由工程加载时的 <c>InitShm</c> 置位；仅阻止工程切换成功后写入视觉配置中的工程名称，不阻止工程保存、生产检测和单工具执行前的模型门禁。
        /// </summary>
        private bool _aoiShapeModelNotReady;

        /// <summary>
        /// 系统配置 XML 统一保存入口。调用方传入本轮加载状态，首装默认对象可落盘，已有文件损坏时保护现场 XML。
        /// 配置恢复副本集中写入“配置\备份”；AOI 工程工具使用独立保存入口和按工程名维护的单份完整快照。
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

        /// <summary>
        /// AOI 工程工具 XML 保存入口。工具参数经过临时文件校验和原子替换；完整工程快照在全部工具保存成功后按需覆盖。
        /// 该入口保持参数审计、工具顺序、示教图片、模型文件、检测流程和设备通信的原有业务边界。
        /// </summary>
        /// <typeparam name="T">AOI 工具 XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">当前 AOI 工程目录中的工具 XML 完整路径。</param>
        /// <param name="data">准备保存的工具配置对象。</param>
        /// <param name="loadFailed">本轮工程加载是否发生不可恢复的失败。</param>
        /// <returns>工具 XML 完成更新或内容保持一致时返回 <c>true</c>。</returns>
        private bool SaveProjectXmlSafely<T>(string filename, T data, bool loadFailed = false) where T : class
        {
            return ConfigXmlSaveHelper.TrySaveProjectFile(filename, data, loadFailed, message => writeLog(message));
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

        public void SaveSettingModel()
        {
            string filename = GetConfigPath("配置数据.xml");
            SaveXmlSafely(filename, DataModel.Settingmodel, _settingConfigLoadFailed);
            SaveDualYConfiguration();
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
                MessageBox.Show($"配置数据.xml加载异常，软件本轮使用默认内存配置；关闭软件时会跳过该文件保存，现场 XML 文件会保留供维护排查:\r\n{ex.Message}");
                
                // 加载独立的耐压状态映射配置文件
                LoadTvStatusMappings();
                LoadHipotCommParameters();
            }

            LoadDualYConfiguration();
            
            // 订阅AT9620日志事件，将设备日志转发到应用日志
            DataModel.Settingmodel.AT9620_1.LogMessage += (s, e) => writeLog($"[耐压1] {e.Message}");
            DataModel.Settingmodel.AT9620_2.LogMessage += (s, e) => writeLog($"[耐压2] {e.Message}");
            DataModel.Settingmodel.AT9620_3.LogMessage += (s, e) => writeLog($"[耐压3] {e.Message}");
        }

        /// <summary>
        /// 保存双Y专用部署、Y1/Y2扫码器和 PLC 地址到独立 XML。
        /// 普通扫码器继续随“配置数据.xml”保存，两个部署可分别维护现场设备参数。
        /// </summary>
        public void SaveDualYConfiguration()
        {
            string filename = GetConfigPath("双Y电测配置.xml");
            if (DataModel.DualYConfiguration == null)
            {
                DataModel.DualYConfiguration = new DualYElectricalTestConfiguration();
            }

            DataModel.DualYConfiguration.EnsureDefaults();
            SaveXmlSafely(filename, DataModel.DualYConfiguration, _dualYConfigLoadFailed);
        }

        /// <summary>
        /// 加载“配置\双Y电测配置.xml”。首次升级缺少该文件时，从旧“配置数据.xml”复制部署、扫码器和地址并立即生成模板；
        /// 已有文件损坏时使用旧配置兼容值维持本轮运行，同时禁止默认对象覆盖损坏样本。
        /// </summary>
        private void LoadDualYConfiguration()
        {
            string filename = GetConfigPath("双Y电测配置.xml");
            bool dedicatedFileExists = File.Exists(filename);

            try
            {
                ConfigLoadResult<DualYElectricalTestConfiguration> result = ConfigXmlSaveHelper.TryLoad(
                    filename,
                    () => DualYElectricalTestConfiguration.CreateFromLegacy(DataModel.Settingmodel),
                    message => writeLog(message));

                if (!dedicatedFileExists && !result.RestoredFromBackup && !result.LoadFailed)
                {
                    DataModel.DualYConfiguration = DualYElectricalTestConfiguration.CreateFromLegacy(DataModel.Settingmodel);
                    _dualYConfigLoadFailed = false;
                    SaveDualYConfiguration();
                    writeLog("[双Y配置] 已从配置数据.xml迁移并生成双Y电测配置.xml；普通扫码配置继续保留在原文件");
                    return;
                }

                if (result.LoadFailed)
                {
                    DataModel.DualYConfiguration = DualYElectricalTestConfiguration.CreateFromLegacy(DataModel.Settingmodel);
                    _dualYConfigLoadFailed = true;
                    MessageBox.Show(
                        "双Y电测配置.xml 无法读取，软件本轮使用配置数据.xml中的兼容参数。\r\n" +
                        "关闭软件时会保留原文件，请联系设备维护人员检查配置备份。",
                        "双Y配置异常",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                DataModel.DualYConfiguration = result.Data ?? new DualYElectricalTestConfiguration();
                DataModel.DualYConfiguration.EnsureDefaults();
                _dualYConfigLoadFailed = false;

                if (result.RestoredFromBackup)
                {
                    writeLog($"[双Y配置] 已从备份恢复：{Path.GetFileName(result.RestoredFrom)}");
                }
                else
                {
                    writeLog("[双Y配置] 已加载双Y电测配置.xml");
                }
            }
            catch (Exception ex)
            {
                DataModel.DualYConfiguration = DualYElectricalTestConfiguration.CreateFromLegacy(DataModel.Settingmodel);
                _dualYConfigLoadFailed = true;
                writeLog($"[双Y配置] 加载异常，使用旧配置兼容参数：{ex}", true);
                MessageBox.Show(
                    "双Y电测配置加载异常，软件本轮使用兼容参数。\r\n" +
                    "详细原因已写入运行日志，请联系设备维护人员处理。",
                    "双Y配置异常",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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

        /// <summary>
        /// 生成耐压结果的本地追溯状态。
        /// 仪器收到已知结束态时沿用状态映射；通信、参数回读、采样或超时异常使用固定“通信异常”标记，
        /// 让 CHECK 阶段区分“仪器正常测试得到 NG”和“本轮没有形成仪器结果”，前者可按工艺报工，后者只留档并跳过报工。
        /// </summary>
        /// <param name="result">AT9620 本轮 Start 返回值，包含可信结束态标记。</param>
        /// <param name="rawStatus">本轮仪器状态原文，用于正常结束态翻译。</param>
        /// <param name="isAcw">true 表示 ACW，false 表示 DCW；用于写入模式前缀。</param>
        /// <returns>带 ACW/DCW 前缀的本地追溯状态。</returns>
        private static string BuildPersistedTvStatus(global::AT9620.Result result, string rawStatus, bool isAcw)
        {
            bool hasTrustedTerminalStatus = (result != null && result.Success)
                || TvStatusTranslator.IsTrustedTerminalStatus(rawStatus);
            string status = !hasTrustedTerminalStatus
                ? TvStatusTranslator.CommunicationFailureStatus
                : TvStatusTranslator.Translate(rawStatus);
            return TvStatusTranslator.AddTestModePrefix(status, isAcw);
        }
        #endregion

        #endregion
    }
}
