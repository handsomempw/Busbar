using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting1
{
    /// <summary>
    /// 耐压仪 TCP 通信参数 POCO：供 <see cref="System.Xml.Serialization.XmlSerializer"/> 读写独立配置文件。
    /// 落地 XML 根/元素为中文标签（现场可视化），本类 C# 属性为英文，与 <c>[XmlRoot] / [XmlElement]</c> 一一对应。
    /// 数值由启动时加载并赋给 <c>AT9620_*</c> 实例，用于控制 Fetch 短超时加重试与其它命令单次长超时等行为。
    /// </summary>
    [XmlRoot("耐压仪通信参数")]
    public class HipotCommParameters
    {
        /// <summary>Fetch? 单次 Socket 接收超时（毫秒）。</summary>
        [XmlElement("Fetch单次接收超时毫秒")]
        public int FetchReceiveTimeoutMs { get; set; } = 200;

        /// <summary>
        /// Fetch? 最大尝试次数（含首次发送）：共 N 次，即 1 次首发 + (N - 1) 次重试。
        /// </summary>
        [XmlElement("Fetch最大尝试次数")]
        public int FetchMaxAttempts { get; set; } = 3;

        [XmlElement("Fetch重试间隔毫秒")]
        public int FetchRetryDelayMs { get; set; } = 100;

        /// <summary>rp?、FUNC:SOUR:STEP? 等单次接收超时（毫秒）。</summary>
        [XmlElement("其它命令单次接收超时毫秒")]
        public int OtherCommandReceiveTimeoutMs { get; set; } = 3000;

        /// <summary>每次发送后、Receive 前的等待（毫秒）。</summary>
        [XmlElement("发送后等待毫秒")]
        public int PostSendDelayMs { get; set; } = 100;
    }
}
