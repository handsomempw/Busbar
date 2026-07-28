using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BusbarCompressionSystem.Utils
{
    /// <summary>
    /// 在创建 WPF 主窗口和业务模型前读取双Y部署选择。
    /// 该入口只解析轻量 XML，使双Y专用机台可以直接进入纯电测窗口，避免标准窗口提前创建 HALCON 和相机相关控件。
    /// </summary>
    internal static class DualYDeploymentBootstrap
    {
        /// <summary>
        /// 判断本次启动是否进入双Y专用窗口。优先读取独立配置及其恢复副本，升级场景兼容旧“配置数据.xml”部署节点。
        /// </summary>
        /// <returns>独立配置或旧配置明确启用双Y专用部署时返回 true。</returns>
        public static bool ShouldUseDualYWindow()
        {
            string configDirectory = Path.Combine(Environment.CurrentDirectory, "配置");
            string dedicatedFile = Path.Combine(configDirectory, "双Y电测配置.xml");
            string recoveryDirectory = Path.Combine(configDirectory, "备份");

            var dedicatedCandidates = new[]
            {
                dedicatedFile,
                Path.Combine(recoveryDirectory, "双Y电测配置.xml.lastgood"),
                Path.Combine(recoveryDirectory, "双Y电测配置.xml.bak")
            };

            bool value;
            if (TryReadBooleanElement(dedicatedCandidates, "双Y专用部署", out value))
            {
                return value;
            }

            string legacyFile = Path.Combine(configDirectory, "配置数据.xml");
            var legacyCandidates = new[]
            {
                legacyFile,
                Path.Combine(recoveryDirectory, "配置数据.xml.lastgood"),
                Path.Combine(recoveryDirectory, "配置数据.xml.bak")
            };

            return TryReadBooleanElement(legacyCandidates, "双Y电测部署", out value) && value;
        }

        /// <summary>
        /// 按正式文件、最后有效副本和备份副本的顺序读取部署布尔值。
        /// 单个候选损坏时继续检查下一份恢复文件，正式配置加载阶段负责记录完整异常。
        /// </summary>
        /// <param name="candidates">按恢复优先级排列的配置文件完整路径。</param>
        /// <param name="elementName">需要读取的部署节点名称。</param>
        /// <param name="value">成功读取时返回节点布尔值，兼容 true/false 和 1/0。</param>
        /// <returns>找到并解析有效部署节点时返回 true。</returns>
        private static bool TryReadBooleanElement(IEnumerable<string> candidates, string elementName, out bool value)
        {
            value = false;
            foreach (string filename in candidates.Where(File.Exists))
            {
                try
                {
                    XElement element = XDocument.Load(filename)
                        .Descendants()
                        .FirstOrDefault(node => string.Equals(node.Name.LocalName, elementName, StringComparison.Ordinal));
                    if (element == null)
                    {
                        continue;
                    }

                    string text = (element.Value ?? string.Empty).Trim();
                    if (bool.TryParse(text, out value))
                    {
                        return true;
                    }

                    if (text == "1" || text == "0")
                    {
                        value = text == "1";
                        return true;
                    }
                }
                catch
                {
                    // 下一个恢复副本继续承担启动选型；完整错误由正式配置加载入口记录。
                }
            }

            return false;
        }
    }
}
