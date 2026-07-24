using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Utils
{
    /// <summary>
    /// XML 配置加载结果。启动加载、窗口关闭保存和工程配方保存通过该结果区分首装默认配置、备份恢复和现场文件损坏。
    /// </summary>
    /// <typeparam name="T">XML 根节点对应的数据模型类型。</typeparam>
    internal sealed class ConfigLoadResult<T> where T : class
    {
        /// <summary>
        /// 本轮进入内存的数据对象。正常加载和备份恢复时来自 XML；首装无文件或恢复失败时来自调用方提供的默认对象。
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// 表示主文件或备份文件曾经存在，但本轮启动均无法反序列化。保存流程依据该标志保护现场文件，避免默认对象覆盖磁盘配置。
        /// </summary>
        public bool LoadFailed { get; set; }

        /// <summary>
        /// 表示主文件不可用时已从备份文件恢复。现场日志可据此确认恢复来源和恢复时点。
        /// </summary>
        public bool RestoredFromBackup { get; set; }

        /// <summary>
        /// 实际用于恢复的备份文件路径。该值仅用于日志和现场排查。
        /// </summary>
        public string RestoredFrom { get; set; }

        /// <summary>
        /// 成功加载的原始 XML 文本。兼容旧配置节点缺失时，调用方可依据原文判断应补齐的业务默认值。
        /// </summary>
        public string XmlText { get; set; }
    }

    /// <summary>
    /// 现场 XML 配置和 AOI 工程工具 XML 的安全读写入口。
    /// <para>系统配置恢复副本集中保存在“配置\备份”；AOI 工程在“配置备份”下按工程名保留一份完整快照，仅在配方实际变更后覆盖。</para>
    /// <para>加载失败时保存入口保护磁盘文件，防止默认对象覆盖现场参数。</para>
    /// </summary>
    internal static class ConfigXmlSaveHelper
    {
        private const string BackupExtension = ".bak";
        private const string LastGoodExtension = ".lastgood";

        /// <summary>
        /// 配置 XML 恢复副本目录名。该目录位于正式“配置”目录下，只承载自动恢复文件，不参与 AOI 工程管理。
        /// </summary>
        private const string RecoveryDirectoryName = "备份";

        private const int MaxTimestampBackupDirectories = 30;
        private const string RecoveryReadmeFileName = "readme.txt";

        /// <summary>
        /// 按候选顺序加载 XML。系统配置损坏时从“配置\备份”自动恢复；AOI 工具 XML 只读取工程内主文件。
        /// </summary>
        /// <typeparam name="T">XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <param name="createDefault">无候选文件或恢复失败时创建内存默认对象的函数。</param>
        /// <param name="log">诊断日志入口，用于记录恢复来源、首装状态和损坏状态。</param>
        /// <returns>返回加载结果，调用方据此决定是否允许后续保存。</returns>
        public static ConfigLoadResult<T> TryLoad<T>(string filename, Func<T> createDefault, Action<string> log = null) where T : class
        {
            foreach (string sourceFile in GetLoadCandidates(filename))
            {
                T data;
                string xmlText;
                if (!TryDeserialize(sourceFile, out data, out xmlText))
                {
                    continue;
                }

                if (!string.Equals(sourceFile, filename, StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke($"[配置加载] 从 {Path.GetFileName(sourceFile)} 恢复 {Path.GetFileName(filename)}");
                    TryCopyFile(sourceFile, filename, log);
                }

                return new ConfigLoadResult<T>
                {
                    Data = data,
                    LoadFailed = false,
                    RestoredFromBackup = !string.Equals(sourceFile, filename, StringComparison.OrdinalIgnoreCase),
                    RestoredFrom = sourceFile,
                    XmlText = xmlText
                };
            }

            bool hasPersistedFile = HasAnyPersistedFile(filename);
            if (hasPersistedFile)
            {
                log?.Invoke($"[配置加载] 失败：{Path.GetFileName(filename)} 损坏且无法从备份恢复，使用内存默认对象");
            }
            else
            {
                log?.Invoke($"[配置加载] 首次部署：{Path.GetFileName(filename)} 尚不存在，使用内存默认对象");
            }

            return new ConfigLoadResult<T>
            {
                Data = createDefault(),
                LoadFailed = hasPersistedFile
            };
        }

        /// <summary>
        /// 保存 XML 到正式文件。加载失败场景跳过写回，正常场景写入临时文件、校验临时文件并替换正式文件。
        /// </summary>
        /// <typeparam name="T">XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <param name="data">准备落盘的数据对象。</param>
        /// <param name="loadFailed">本轮启动时该文件是否发生不可恢复的加载失败。</param>
        /// <param name="log">诊断日志入口，用于记录跳过、成功和失败原因。</param>
        /// <returns>正式文件完成更新或内容保持一致时返回 <c>true</c>；保护策略拦截或写入失败时返回 <c>false</c>。</returns>
        public static bool TrySave<T>(string filename, T data, bool loadFailed, Action<string> log = null) where T : class
        {
            if (data == null)
            {
                log?.Invoke($"[配置保存] 跳过：{Path.GetFileName(filename)} 数据为空");
                return false;
            }

            if (loadFailed)
            {
                log?.Invoke($"[配置保存] 跳过：{Path.GetFileName(filename)} 本次加载失败，内存对象不适合作为现场配置");
                return false;
            }

            try
            {
                SaveValidatedXml(filename, data, true, log);
                log?.Invoke($"[配置保存] 成功：{Path.GetFileName(filename)}");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置保存] 失败：{Path.GetFileName(filename)}，{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存 AOI 工具 XML。工具参数经过临时文件校验和原子替换；旁路恢复文件不参与工程工具读写。
        /// <para>完整工程恢复样本由保存成功后的单份工程快照提供，不改变工具顺序、参数审计、检测执行或设备通信。</para>
        /// </summary>
        /// <typeparam name="T">AOI 工具 XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">当前 AOI 工程目录中的工具 XML 完整路径。</param>
        /// <param name="data">准备落盘的工具配置对象。</param>
        /// <param name="loadFailed">本轮工程加载是否发生不可恢复的失败；为 <c>true</c> 时保护现场工程文件。</param>
        /// <param name="log">诊断日志入口，用于记录保存跳过、成功和失败原因。</param>
        /// <returns>工具 XML 完成更新或内容保持一致时返回 <c>true</c>。</returns>
        public static bool TrySaveProjectFile<T>(string filename, T data, bool loadFailed, Action<string> log = null) where T : class
        {
            if (data == null)
            {
                log?.Invoke($"[AOI工程保存] 跳过：{Path.GetFileName(filename)} 工具数据为空");
                return false;
            }

            if (loadFailed)
            {
                log?.Invoke($"[AOI工程保存] 跳过：{Path.GetFileName(filename)} 本轮工程加载失败，保留现场工程文件");
                return false;
            }

            try
            {
                SaveValidatedXml(filename, data, false, log);
                log?.Invoke($"[AOI工程保存] 成功：{Path.GetFileName(filename)}");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[AOI工程保存] 失败：{Path.GetFileName(filename)}，{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 在启动加载前复制“配置”目录中的正式 XML 到 exe 目录的时间戳备份区。该快照保留现场原始文件状态，便于维护人员追溯启动前样本。
        /// </summary>
        /// <param name="sourceDir">exe 目录下的正式“配置”目录。</param>
        /// <param name="backupRoot">备份根目录，建议位于 exe 目录下，便于现场替换程序时一并查看。</param>
        /// <param name="categoryName">备份分类名称，用于区分系统配置和 AOI 工程配方。</param>
        /// <param name="log">诊断日志入口，用于记录备份数量和失败原因。</param>
        public static void BackupXmlFiles(string sourceDir, string backupRoot, string categoryName, Action<string> log = null)
        {
            try
            {
                PrepareBackupRoot(backupRoot, log);

                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                {
                    return;
                }

                string[] sourceFiles = Directory.GetFiles(sourceDir, "*.xml", SearchOption.TopDirectoryOnly);
                if (sourceFiles.Length == 0)
                {
                    return;
                }

                string targetDir = Path.Combine(
                    backupRoot,
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                    SanitizeDirectoryName(categoryName));

                Directory.CreateDirectory(targetDir);

                int copiedCount = 0;
                foreach (string sourceFile in sourceFiles)
                {
                    try
                    {
                        string targetFile = Path.Combine(targetDir, Path.GetFileName(sourceFile));
                        File.Copy(sourceFile, targetFile, true);
                        copiedCount++;
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"[配置备份] 文件备份失败：{Path.GetFileName(sourceFile)}，{ex.Message}");
                    }
                }

                if (copiedCount > 0)
                {
                    log?.Invoke($"[配置备份] 已备份{copiedCount}个配置XML到：{targetDir}");
                }

                PruneTimestampBackupDirectories(backupRoot, log);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置备份] 目录备份失败：{sourceDir}，{ex.Message}");
            }
        }

        /// <summary>
        /// 在 AOI 工程配方成功保存后同步单份完整工程快照。每个工程名对应固定备份目录，内容无变化时跳过复制。
        /// <para>快照先写入临时目录并完成文件校验，再整体替换正式目录；任一步失败都保留原正式快照，避免恢复副本出现跨版本混装。</para>
        /// <para>该快照供人工整批恢复工具 XML、示教图片和视觉模型，不参与运行时加载、检测判定或设备通信。</para>
        /// </summary>
        /// <param name="sourceDir">当前 AOI 工程绝对目录。</param>
        /// <param name="backupRoot">exe 目录下的配置备份根目录。</param>
        /// <param name="categoryName">包含 AOI 工程名称的备份分类。</param>
        /// <param name="log">诊断日志入口，用于记录跳过、同步数量和单文件失败原因。</param>
        /// <returns>快照内容已同步、源目录不存在或当前工程没有需要备份的文件时返回 <c>true</c>；临时目录复制、校验或目录替换失败时返回 <c>false</c>。</returns>
        public static bool BackupProjectFilesIfChanged(string sourceDir, string backupRoot, string categoryName, Action<string> log = null)
        {
            string stagingDir = null;
            string retiredDir = null;
            try
            {
                PrepareBackupRoot(backupRoot, log);

                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                {
                    return true;
                }

                string[] sourceFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                if (sourceFiles.Length == 0)
                {
                    return true;
                }

                string safeCategory = SanitizeDirectoryName(categoryName);
                string targetDir = Path.Combine(backupRoot, safeCategory);
                if (!ProjectBackupNeedsRefresh(sourceDir, targetDir, sourceFiles))
                {
                    log?.Invoke($"[配置备份] AOI工程内容未变化，跳过备份：{safeCategory}");
                    return true;
                }

                stagingDir = Path.Combine(backupRoot, safeCategory + ".staging");
                retiredDir = Path.Combine(backupRoot, safeCategory + ".retired");
                TryDeleteDirectory(stagingDir, log);
                TryDeleteDirectory(retiredDir, log);
                Directory.CreateDirectory(stagingDir);

                foreach (string sourceFile in sourceFiles)
                {
                    string relativePath = GetRelativePath(sourceDir, sourceFile);
                    string stagingFile = Path.Combine(stagingDir, relativePath);
                    EnsureDirectory(stagingFile);
                    File.Copy(sourceFile, stagingFile, true);
                }

                foreach (string sourceFile in sourceFiles)
                {
                    string relativePath = GetRelativePath(sourceDir, sourceFile);
                    string stagingFile = Path.Combine(stagingDir, relativePath);
                    if (!File.Exists(stagingFile) || !FilesHaveSameContent(sourceFile, stagingFile))
                    {
                        log?.Invoke($"[配置备份] AOI工程临时快照校验失败：{relativePath}");
                        TryDeleteDirectory(stagingDir, log);
                        stagingDir = null;
                        return false;
                    }
                }

                if (Directory.Exists(targetDir))
                {
                    Directory.Move(targetDir, retiredDir);
                }

                try
                {
                    Directory.Move(stagingDir, targetDir);
                    stagingDir = null;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[配置备份] AOI工程正式快照替换失败：{safeCategory}，{ex.Message}");
                    if (!Directory.Exists(targetDir) && Directory.Exists(retiredDir))
                    {
                        Directory.Move(retiredDir, targetDir);
                        retiredDir = null;
                    }

                    TryDeleteDirectory(stagingDir, log);
                    stagingDir = null;
                    return false;
                }

                TryDeleteDirectory(retiredDir, log);
                retiredDir = null;
                log?.Invoke($"[配置备份] 已同步{sourceFiles.Length}个AOI工程文件到：{targetDir}");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置备份] AOI工程备份失败：{sourceDir}，{ex.Message}");
                TryDeleteDirectory(stagingDir, log);
                if (!string.IsNullOrWhiteSpace(retiredDir)
                    && Directory.Exists(retiredDir)
                    && (string.IsNullOrWhiteSpace(categoryName)
                        || !Directory.Exists(Path.Combine(backupRoot, SanitizeDirectoryName(categoryName)))))
                {
                    try
                    {
                        Directory.Move(retiredDir, Path.Combine(backupRoot, SanitizeDirectoryName(categoryName)));
                    }
                    catch (Exception restoreEx)
                    {
                        log?.Invoke($"[配置备份] AOI工程正式快照回退失败：{restoreEx.Message}");
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 删除临时或已退役的 AOI 工程快照目录。
        /// 仅用于快照发布过程中的 staging/retired 收尾；正式快照目录由目录整体替换接管，避免复制中途清理造成混版。
        /// </summary>
        /// <param name="directory">待删除的快照临时目录或退役目录。</param>
        /// <param name="log">诊断日志入口，记录删除失败原因供现场排查磁盘占用。</param>
        private static void TryDeleteDirectory(string directory, Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置备份] 临时快照目录删除失败：{directory}，{ex.Message}");
            }
        }

        /// <summary>
        /// 准备现场配置备份根目录。该入口在系统配置和 AOI 工程快照前执行，确保恢复说明持续可用，并把时间戳系统配置快照限制在最近 30 份。
        /// </summary>
        /// <param name="backupRoot">exe 目录下的配置备份根目录。</param>
        /// <param name="log">诊断日志入口，用于记录恢复说明生成或历史目录清理失败。</param>
        private static void PrepareBackupRoot(string backupRoot, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(backupRoot))
            {
                return;
            }

            Directory.CreateDirectory(backupRoot);
            WriteRecoveryReadme(backupRoot, log);
            PruneTimestampBackupDirectories(backupRoot, log);
        }

        /// <summary>
        /// 在备份根目录生成现场恢复说明。该文件指导维护人员恢复系统配置和 AOI 工程快照，程序加载、测量、判定和设备通信保持原有入口。
        /// </summary>
        /// <param name="backupRoot">exe 目录下的配置备份根目录。</param>
        /// <param name="log">诊断日志入口，用于记录说明文件写入失败原因。</param>
        private static void WriteRecoveryReadme(string backupRoot, Action<string> log)
        {
            const string content =
                "配置备份与恢复说明\r\n" +
                "\r\n" +
                "1. 本目录由软件自动管理。\r\n" +
                "2. 系统配置位于 yyyyMMdd_HHmmss 时间戳目录的“系统配置”中，软件保留最近30个时间戳目录。\r\n" +
                "3. AOI 工程位于固定目录“AOI工程_工程名”中，每个工程只保留一份完整快照（工具 XML、图片、模型和子目录）；配方实际变更并保存成功后才会覆盖。\r\n" +
                "4. 正常生产时无需手工修改、移动或删除这些文件。\r\n" +
                "5. 需要人工恢复时：先关闭软件，备份当前故障文件，再把对应目录中的文件复制回原“配置”或 AOI 工程目录。\r\n" +
                "6. 配置 XML 的 .bak 和 .lastgood 集中位于“配置\\备份”目录，由软件自动恢复；AOI 工程主文件损坏时不会自动旁路回滚，需人工从本目录恢复。\r\n" +
                "7. 恢复后请核对当前工程、相机、PLC、MES、电测参数和点检配置，再恢复设备生产。\r\n";

            try
            {
                string readmePath = Path.Combine(backupRoot, RecoveryReadmeFileName);
                if (!File.Exists(readmePath) || !string.Equals(File.ReadAllText(readmePath), content, StringComparison.Ordinal))
                {
                    File.WriteAllText(readmePath, content, new UTF8Encoding(true));
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置备份] 恢复说明生成失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 清理超过保留上限的系统配置时间戳快照。仅识别 <c>yyyyMMdd_HHmmss</c> 目录；AOI 工程固定目录、恢复说明和人工目录保持原样。
        /// </summary>
        /// <param name="backupRoot">exe 目录下的配置备份根目录。</param>
        /// <param name="log">诊断日志入口，用于记录被清理目录和单个目录删除失败原因。</param>
        private static void PruneTimestampBackupDirectories(string backupRoot, Action<string> log)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
                {
                    return;
                }

                var timestampDirectories = new List<KeyValuePair<DateTime, DirectoryInfo>>();
                foreach (DirectoryInfo directory in new DirectoryInfo(backupRoot).GetDirectories())
                {
                    DateTime timestamp;
                    if (DateTime.TryParseExact(
                        directory.Name,
                        "yyyyMMdd_HHmmss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out timestamp))
                    {
                        timestampDirectories.Add(new KeyValuePair<DateTime, DirectoryInfo>(timestamp, directory));
                    }
                }

                DirectoryInfo[] expiredDirectories = timestampDirectories
                    .OrderByDescending(item => item.Key)
                    .ThenByDescending(item => item.Value.Name, StringComparer.Ordinal)
                    .Skip(MaxTimestampBackupDirectories)
                    .Select(item => item.Value)
                    .ToArray();

                foreach (DirectoryInfo directory in expiredDirectories)
                {
                    try
                    {
                        directory.Delete(true);
                        log?.Invoke($"[配置备份] 已清理超出保留数量的旧备份：{directory.Name}");
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"[配置备份] 旧备份清理失败：{directory.FullName}，{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置备份] 保留策略执行失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 判断主文件或配置集中恢复副本是否曾经存在。该判断用于区分首装无文件和已有文件损坏两类现场状态。
        /// </summary>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <returns>返回 true 表示主文件或配置恢复副本中至少存在一个。</returns>
        private static bool HasAnyPersistedFile(string filename)
        {
            return GetLoadCandidates(filename).Any(File.Exists);
        }

        /// <summary>
        /// 返回同一文件的恢复候选顺序。系统配置优先主文件，其次“配置\备份”中的恢复副本；AOI 工具只使用工程内主文件。
        /// </summary>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <returns>按加载优先级排列的候选文件路径。</returns>
        private static IEnumerable<string> GetLoadCandidates(string filename)
        {
            yield return filename;

            if (IsConfigurationFile(filename))
            {
                yield return GetRecoveryFilePath(filename, BackupExtension);
                yield return GetRecoveryFilePath(filename, LastGoodExtension);
            }
        }

        /// <summary>
        /// 判断目标 XML 是否属于 exe 目录下的正式“配置”目录。该边界决定恢复副本进入“配置\备份”，AOI 工程不走自动旁路恢复。
        /// </summary>
        /// <param name="filename">待分类的 XML 完整路径。</param>
        /// <returns>文件直属目录名为“配置”时返回 <c>true</c>。</returns>
        private static bool IsConfigurationFile(string filename)
        {
            string directory = Path.GetDirectoryName(filename);
            return !string.IsNullOrWhiteSpace(directory)
                && string.Equals(new DirectoryInfo(directory).Name, "配置", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取配置 XML 在集中恢复目录中的副本路径。文件名保留正式 XML 名称和恢复扩展名，便于现场按名称配对。
        /// </summary>
        /// <param name="filename">正式配置 XML 完整路径。</param>
        /// <param name="extension"><c>.bak</c> 或 <c>.lastgood</c> 恢复扩展名。</param>
        /// <returns>位于“配置\备份”目录中的恢复文件完整路径。</returns>
        private static string GetRecoveryFilePath(string filename, string extension)
        {
            string directory = Path.GetDirectoryName(filename) ?? string.Empty;
            return Path.Combine(directory, RecoveryDirectoryName, Path.GetFileName(filename) + extension);
        }

        /// <summary>
        /// 判断 AOI 工程快照是否需要刷新。备份目录缺失、文件数量不一致或任一文件内容不同时返回 <c>true</c>。
        /// </summary>
        /// <param name="sourceDir">当前 AOI 工程绝对目录。</param>
        /// <param name="targetDir">该工程对应的固定备份目录。</param>
        /// <param name="sourceFiles">工程内待比对的全部文件。</param>
        /// <returns>需要覆盖备份时返回 <c>true</c>。</returns>
        private static bool ProjectBackupNeedsRefresh(string sourceDir, string targetDir, string[] sourceFiles)
        {
            if (!Directory.Exists(targetDir))
            {
                return true;
            }

            string[] backupFiles = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            if (backupFiles.Length != sourceFiles.Length)
            {
                return true;
            }

            var backupRelativePaths = new HashSet<string>(
                backupFiles.Select(file => GetRelativePath(targetDir, file)),
                StringComparer.OrdinalIgnoreCase);

            foreach (string sourceFile in sourceFiles)
            {
                string relativePath = GetRelativePath(sourceDir, sourceFile);
                if (!backupRelativePaths.Contains(relativePath))
                {
                    return true;
                }

                string targetFile = Path.Combine(targetDir, relativePath);
                if (!FilesHaveSameContent(sourceFile, targetFile))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 删除工程快照中已不存在于正式工程的陈旧文件，使备份目录与当前配方目录保持一致。
        /// </summary>
        /// <param name="targetDir">该工程对应的固定备份目录。</param>
        /// <param name="retainedRelativePaths">本轮正式工程中应保留的相对路径集合。</param>
        /// <param name="log">现场诊断日志入口。</param>
        private static void RemoveObsoleteProjectBackupFiles(
            string targetDir,
            HashSet<string> retainedRelativePaths,
            Action<string> log)
        {
            if (!Directory.Exists(targetDir))
            {
                return;
            }

            foreach (string backupFile in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(targetDir, backupFile);
                if (retainedRelativePaths.Contains(relativePath))
                {
                    continue;
                }

                try
                {
                    File.Delete(backupFile);
                    log?.Invoke($"[配置备份] 已移除陈旧AOI工程备份文件：{relativePath}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[配置备份] 陈旧AOI工程备份文件删除失败：{relativePath}，{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 读取并反序列化指定 XML 文件。该方法只表达文件是否可用于生产配置，异常由上层转换成恢复、首装或保存保护策略。
        /// </summary>
        /// <typeparam name="T">XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">待读取的 XML 文件路径。</param>
        /// <param name="data">成功时返回反序列化后的数据对象。</param>
        /// <param name="xmlText">成功时返回原始 XML 文本，供旧配置兼容判断使用。</param>
        /// <returns>返回 true 表示文件存在且可反序列化为指定模型。</returns>
        private static bool TryDeserialize<T>(string filename, out T data, out string xmlText) where T : class
        {
            data = null;
            xmlText = null;

            try
            {
                if (!File.Exists(filename))
                {
                    return false;
                }

                xmlText = File.ReadAllText(filename);
                using (var stream = File.OpenRead(filename))
                {
                    var serializer = new XmlSerializer(typeof(T));
                    data = serializer.Deserialize(stream) as T;
                }

                return data != null;
            }
            catch
            {
                data = null;
                xmlText = null;
                return false;
            }
        }

        /// <summary>
        /// 将 XML 写入临时文件并完成可读性校验，再替换正式文件。配置 XML 刷新集中恢复副本，AOI 工具 XML 不维护旁路恢复文件。
        /// </summary>
        /// <typeparam name="T">XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <param name="data">准备写入的数据对象。</param>
        /// <param name="maintainRecoveryCopies">配置 XML 为 <c>true</c>；AOI 工具 XML 为 <c>false</c>。</param>
        /// <param name="log">诊断日志入口，备份副本刷新失败时记录原因。</param>
        private static void SaveValidatedXml<T>(string filename, T data, bool maintainRecoveryCopies, Action<string> log) where T : class
        {
            EnsureDirectory(filename);

            string tempFile = filename + ".tmp";
            string backupFile = maintainRecoveryCopies ? GetRecoveryFilePath(filename, BackupExtension) : null;
            string lastGoodFile = maintainRecoveryCopies ? GetRecoveryFilePath(filename, LastGoodExtension) : null;
            string logPrefix = maintainRecoveryCopies ? "[配置保存]" : "[AOI工程保存]";

            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new XmlSerializer(typeof(T));
                    serializer.Serialize(stream, data);
                    stream.Flush();
                }

                T verified;
                string ignoredXmlText;
                if (!TryDeserialize(tempFile, out verified, out ignoredXmlText))
                {
                    throw new InvalidOperationException("临时文件校验失败");
                }

                if (File.Exists(filename) && FilesHaveSameContent(tempFile, filename))
                {
                    if (maintainRecoveryCopies)
                    {
                        TryCopyFile(filename, lastGoodFile, log);
                    }

                    log?.Invoke($"{logPrefix} 内容未变化，跳过替换：{Path.GetFileName(filename)}");
                    return;
                }

                if (File.Exists(filename))
                {
                    if (maintainRecoveryCopies)
                    {
                        TryCopyFile(filename, backupFile, log);
                    }
                    ReplaceExistingFile(tempFile, filename);
                }
                else
                {
                    File.Move(tempFile, filename);
                }

                if (maintainRecoveryCopies)
                {
                    TryCopyFile(filename, lastGoodFile, log);
                }
            }
            finally
            {
                TryDeleteFile(tempFile);
            }
        }

        /// <summary>
        /// 将可用备份复制回目标路径。复制失败只影响磁盘修复或备份刷新，本轮已加载到内存的数据仍可继续服务生产流程。
        /// </summary>
        /// <param name="sourceFile">已确认可读取的来源文件路径。</param>
        /// <param name="destinationFile">待刷新目标文件路径。</param>
        /// <param name="log">诊断日志入口，用于记录复制失败原因。</param>
        private static void TryCopyFile(string sourceFile, string destinationFile, Action<string> log)
        {
            try
            {
                if (File.Exists(destinationFile) && FilesHaveSameContent(sourceFile, destinationFile))
                {
                    return;
                }

                EnsureDirectory(destinationFile);

                string tempCopy = destinationFile + ".copytmp";
                File.Copy(sourceFile, tempCopy, true);
                if (File.Exists(destinationFile))
                {
                    ReplaceExistingFile(tempCopy, destinationFile);
                }
                else
                {
                    File.Move(tempCopy, destinationFile);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置备份] 刷新失败：{Path.GetFileName(destinationFile)}，{ex.Message}");
            }
            finally
            {
                TryDeleteFile(destinationFile + ".copytmp");
            }
        }

        /// <summary>
        /// 按文件长度和字节内容判断两个落盘文件是否一致。保存入口据此跳过重复替换，降低关闭保存和权限回收自动保存产生的磁盘写入。
        /// </summary>
        /// <param name="firstFile">已序列化临时文件或来源文件。</param>
        /// <param name="secondFile">正式文件或恢复副本。</param>
        /// <returns>两个文件均存在且字节内容完全一致时返回 <c>true</c>。</returns>
        private static bool FilesHaveSameContent(string firstFile, string secondFile)
        {
            var firstInfo = new FileInfo(firstFile);
            var secondInfo = new FileInfo(secondFile);
            if (!firstInfo.Exists || !secondInfo.Exists || firstInfo.Length != secondInfo.Length)
            {
                return false;
            }

            const int bufferSize = 81920;
            byte[] firstBuffer = new byte[bufferSize];
            byte[] secondBuffer = new byte[bufferSize];
            using (var firstStream = new FileStream(firstFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize))
            using (var secondStream = new FileStream(secondFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize))
            {
                int firstRead;
                while ((firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length)) > 0)
                {
                    int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
                    if (firstRead != secondRead)
                    {
                        return false;
                    }

                    for (int i = 0; i < firstRead; i++)
                    {
                        if (firstBuffer[i] != secondBuffer[i])
                        {
                            return false;
                        }
                    }
                }

                return secondStream.ReadByte() == -1;
            }
        }

        /// <summary>
        /// 获取 AOI 工程文件相对于工程根目录的路径。完整快照据此保留图片、模型和其他业务子目录结构。
        /// </summary>
        /// <param name="rootDirectory">当前 AOI 工程根目录。</param>
        /// <param name="filename">工程内待备份文件完整路径。</param>
        /// <returns>工程根目录内的相对路径；路径超出根目录时返回文件名作为保护性回退。</returns>
        private static string GetRelativePath(string rootDirectory, string filename)
        {
            string normalizedRoot = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedFile = Path.GetFullPath(filename);
            if (normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFile.Substring(normalizedRoot.Length);
            }

            return Path.GetFileName(filename);
        }

        /// <summary>
        /// 确保目标文件所在目录存在。配置目录和工程目录由现场参数决定，保存和恢复前统一创建目录。
        /// </summary>
        /// <param name="filename">目标文件完整路径。</param>
        private static void EnsureDirectory(string filename)
        {
            string dir = Path.GetDirectoryName(filename);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        /// <summary>
        /// 将工程名或配置分类名转换成可用目录名。目录名只用于备份路径，不参与工程选择、检测流程或外部通信。
        /// </summary>
        /// <param name="name">来自业务配置的分类名称。</param>
        /// <returns>返回可用于 Windows 目录名的文本。</returns>
        private static string SanitizeDirectoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "XML";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    if (chars[i] == invalidChars[j])
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            return new string(chars);
        }

        /// <summary>
        /// 替换已存在的正式文件，并把替换过程产生的系统备份限制在临时文件名内。配置恢复由“配置\备份”承担，AOI 工程恢复由固定工程快照承担。
        /// </summary>
        /// <param name="sourceFile">已校验的临时来源文件。</param>
        /// <param name="destinationFile">待替换的正式文件。</param>
        private static void ReplaceExistingFile(string sourceFile, string destinationFile)
        {
            string replaceBackupFile = destinationFile + ".replacebak";
            TryDeleteFile(replaceBackupFile);
            File.Replace(sourceFile, destinationFile, replaceBackupFile);
            TryDeleteFile(replaceBackupFile);
        }

        /// <summary>
        /// 清理临时文件。清理失败保留给下次保存覆盖处理，现场主配置和备份文件不受该结果影响。
        /// </summary>
        /// <param name="filename">待清理的临时文件路径。</param>
        private static void TryDeleteFile(string filename)
        {
            try
            {
                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }
            }
            catch
            {
            }
        }
    }
}
