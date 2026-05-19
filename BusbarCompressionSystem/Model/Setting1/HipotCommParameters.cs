using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting1
{
    /// <summary>
    /// 耐压仪 TCP 通信参数 POCO：供 <see cref="System.Xml.Serialization.XmlSerializer"/> 读写独立配置文件。
    /// 配置文件用于现场单独调整耐压仪通信节拍，启动后加载并下发到 <c>AT9620_*</c> 实例。
    /// 该模型只覆盖通信超时、重试间隔和发送后等待，不影响测量流程、判定口径和历史记录结构。
    /// </summary>
    [XmlRoot("耐压仪通信参数")]
    public class HipotCommParameters
    {
        /// <summary>
        /// Fetch 指令单次接收超时（毫秒）。
        /// 默认值 200 适合现场常规应答节拍；启动后写入耐压仪实例，用于单次接收和轮询等待。
        /// </summary>
        [XmlElement("Fetch单次接收超时毫秒")]
        public int FetchReceiveTimeoutMs { get; set; } = 200;

        /// <summary>
        /// Fetch 指令最大尝试次数（含首次发送）。
        /// 该值决定一次读取的完整重试窗口：总次数 = 首发 1 次 + 重试 (N - 1) 次；默认值 3 保持现有现场节拍。
        /// </summary>
        [XmlElement("Fetch最大尝试次数")]
        public int FetchMaxAttempts { get; set; } = 3;

        /// <summary>
        /// Fetch 指令重试间隔（毫秒）。
        /// 该值决定一次失败后的再次发包等待时长；默认值 100 保持现有现场节拍。
        /// </summary>
        [XmlElement("Fetch重试间隔毫秒")]
        public int FetchRetryDelayMs { get; set; } = 100;

        /// <summary>
        /// 其它耐压命令单次接收超时（毫秒）。
        /// 适用于 rp?、FUNC:SOUR:STEP? 这类单次等待更长的命令；默认值 3000 保持现有现场节拍。
        /// </summary>
        [XmlElement("其它命令单次接收超时毫秒")]
        public int OtherCommandReceiveTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 每次发送后、Receive 前的等待（毫秒）。
        /// 该值用于控制发包后的最小节拍，默认值 100 保持现有现场节拍。
        /// </summary>
        [XmlElement("发送后等待毫秒")]
        public int PostSendDelayMs { get; set; } = 100;
    }
}
