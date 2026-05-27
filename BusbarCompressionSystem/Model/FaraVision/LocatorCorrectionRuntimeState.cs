using System;

namespace BusbarCompressionSystem.FaraVision
{
    /// <summary>
    /// 单次 AOI 帧内、同一触发指令下的模板定位矫正运行态。
    /// 不落工程 XML，仅在当前图像处理周期内供后续工具计算生效 ROI。
    /// </summary>
    public class LocatorCorrectionRuntimeState
    {
        /// <summary>
        /// 定位工具是否已对本指令启用 ROI 跟随矫正。
        /// </summary>
        public bool FollowEnabled { get; set; }

        /// <summary>
        /// 本帧是否已成功得到可用变换（参考位姿已配置且匹配分值达标）。
        /// </summary>
        public bool TransformActive { get; set; }

        /// <summary>
        /// 本帧承担定位源的工具序号。
        /// </summary>
        public int LocatorToolIndex { get; set; }

        /// <summary>
        /// 本帧承担定位源的工具名称。
        /// </summary>
        public string LocatorToolName { get; set; } = string.Empty;

        /// <summary>
        /// 示教参考匹配点行坐标（px）。
        /// </summary>
        public double RefRow { get; set; }

        /// <summary>
        /// 示教参考匹配点列坐标（px）。
        /// </summary>
        public double RefCol { get; set; }

        /// <summary>
        /// 示教参考匹配角（rad）。
        /// </summary>
        public double RefAngleRad { get; set; }

        /// <summary>
        /// 本帧实测匹配点行坐标（px）。
        /// </summary>
        public double ActRow { get; set; }

        /// <summary>
        /// 本帧实测匹配点列坐标（px）。
        /// </summary>
        public double ActCol { get; set; }

        /// <summary>
        /// 本帧实测匹配角（rad）。
        /// </summary>
        public double ActAngleRad { get; set; }

        /// <summary>
        /// 本帧匹配分值。
        /// </summary>
        public double MatchScore { get; set; }

        /// <summary>
        /// 定位摘要，供追溯日志与界面诊断。
        /// </summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// 清空本帧定位矫正状态，避免上一张图的变换污染当前帧。
        /// </summary>
        public void Reset()
        {
            FollowEnabled = false;
            TransformActive = false;
            LocatorToolIndex = 0;
            LocatorToolName = string.Empty;
            RefRow = 0;
            RefCol = 0;
            RefAngleRad = 0;
            ActRow = 0;
            ActCol = 0;
            ActAngleRad = 0;
            MatchScore = 0;
            Summary = string.Empty;
        }
    }
}
