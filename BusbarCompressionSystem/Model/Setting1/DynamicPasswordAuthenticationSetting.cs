using GalaSoft.MvvmLight;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting1
{
    /// <summary>
    /// 动态密码认证配置
    ///
    /// 说明：
    /// - 用于替换项目内“固定口令”方式的权限开启逻辑
    /// - 配置会随 SettingModel 一起序列化到 `配置\\配置数据.xml`，便于现场维护
    /// </summary>
    public class DynamicPasswordAuthenticationSetting : ObservableObject
    {
        /// <summary>
        /// 是否启用动态密码认证（默认启用）
        /// </summary>
        [XmlElement("启用")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 严格模式：开启后将强制使用生产模式（忽略 TestMode 配置）
        /// </summary>
        [XmlElement("严格模式")]
        public bool StrictMode { get; set; } = false;

        /// <summary>
        /// 测试模式：仅用于开发/联调；生产部署建议配合 StrictMode 关闭
        /// </summary>
        [XmlElement("测试模式")]
        public bool TestMode { get; set; } = true;

        /// <summary>
        /// 应用名称：用于动态密码服务端识别来源系统
        /// </summary>
        [XmlElement("应用名称")]
        public string ApplicationName { get; set; } = "BusbarCompressionSystem";

        /// <summary>
        /// 权限等级（1-10）：数值越大权限越高
        /// </summary>
        [XmlElement("权限等级")]
        public int PrivilegeLevel { get; set; } = 5;

        /// <summary>
        /// 动态密码有效期（分钟）
        /// </summary>
        [XmlElement("有效期分钟")]
        public int PeriodMinutes { get; set; } = 60;

        /// <summary>
        /// 指定接收人工号：
        /// - 为空：使用 RequestPassword() 由系统自动选择合适接收人
        /// - 不为空：使用 RequestPasswordManualReceiver() 指定管理员
        /// </summary>
        [XmlElement("接收人工号")]
        public string ReceiverNo { get; set; } = string.Empty;
    }
}

