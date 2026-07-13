using System;
using System.Collections.Generic;
using System.IO;
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
    /// <para>主文件优先生效，<c>.bak</c> 保留替换前版本，<c>.lastgood</c> 保留最近一次可反序列化版本；加载失败时保护磁盘文件免受默认对象覆盖。</para>
    /// </summary>
    internal static class ConfigXmlSaveHelper
    {
        private const string BackupExtension = ".bak";
        private const string LastGoodExtension = ".lastgood";

        /// <summary>
        /// 按主文件、<c>.bak</c>、<c>.lastgood</c> 的顺序加载 XML。主文件损坏时自动恢复可用备份；首装无候选文件时返回默认对象并允许首次保存。
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
        /// <returns>返回 true 表示正式文件保存完成；返回 false 表示保存被保护策略拦截或写入失败。</returns>
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
                SaveValidatedXml(filename, data, log);
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
        /// 在启动加载前复制指定目录下的 XML 文件到 exe 目录的备份区。该快照保留现场原始文件状态，便于维护人员在自动恢复后追溯启动前样本。
        /// </summary>
        /// <param name="sourceDir">待备份目录，通常为 <c>配置</c> 目录或当前 AOI 工程目录。</param>
        /// <param name="backupRoot">备份根目录，建议位于 exe 目录下，便于现场替换程序时一并查看。</param>
        /// <param name="categoryName">备份分类名称，用于区分系统配置和 AOI 工程配方。</param>
        /// <param name="log">诊断日志入口，用于记录备份数量和失败原因。</param>
        public static void BackupXmlFiles(string sourceDir, string backupRoot, string categoryName, Action<string> log = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                {
                    return;
                }

                string[] xmlFiles = Directory.GetFiles(sourceDir, "*.xml", SearchOption.TopDirectoryOnly);
                if (xmlFiles.Length == 0)
                {
                    return;
                }

                string targetDir = Path.Combine(
                    backupRoot,
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                    SanitizeDirectoryName(categoryName));

                Directory.CreateDirectory(targetDir);

                int copiedCount = 0;
                foreach (string sourceFile in xmlFiles)
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
                    log?.Invoke($"[配置备份] 已备份{copiedCount}个XML到：{targetDir}");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[配置备份] 目录备份失败：{sourceDir}，{ex.Message}");
            }
        }

        /// <summary>
        /// 判断主文件或任一备份文件是否曾经存在。该判断用于区分首装无文件和已有文件损坏两类现场状态。
        /// </summary>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <returns>返回 true 表示主文件、<c>.bak</c> 或 <c>.lastgood</c> 中至少存在一个。</returns>
        private static bool HasAnyPersistedFile(string filename)
        {
            return File.Exists(filename)
                || File.Exists(filename + BackupExtension)
                || File.Exists(filename + LastGoodExtension);
        }

        /// <summary>
        /// 返回同一配置文件的恢复候选顺序。主文件优先保证现场最新设置生效，备份文件用于文件损坏后的自动恢复。
        /// </summary>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <returns>按加载优先级排列的候选文件路径。</returns>
        private static IEnumerable<string> GetLoadCandidates(string filename)
        {
            yield return filename;
            yield return filename + BackupExtension;
            yield return filename + LastGoodExtension;
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
        /// 将 XML 写入临时文件并完成可读性校验，再替换正式文件。正式文件替换前会保存为 <c>.bak</c>，替换成功后刷新 <c>.lastgood</c>。
        /// </summary>
        /// <typeparam name="T">XML 根节点对应的数据模型类型。</typeparam>
        /// <param name="filename">主 XML 文件完整路径。</param>
        /// <param name="data">准备写入的数据对象。</param>
        /// <param name="log">诊断日志入口，备份副本刷新失败时记录原因。</param>
        private static void SaveValidatedXml<T>(string filename, T data, Action<string> log) where T : class
        {
            EnsureDirectory(filename);

            string tempFile = filename + ".tmp";
            string backupFile = filename + BackupExtension;
            string lastGoodFile = filename + LastGoodExtension;

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

                if (File.Exists(filename))
                {
                    TryCopyFile(filename, backupFile, log);
                    ReplaceExistingFile(tempFile, filename);
                }
                else
                {
                    File.Move(tempFile, filename);
                }

                TryCopyFile(filename, lastGoodFile, log);
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
        /// 替换已存在的正式文件，并把替换过程产生的系统备份限制在临时文件名内。业务备份由 <c>.bak</c> 和 <c>.lastgood</c> 承担。
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
