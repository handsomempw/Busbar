using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace PositionDetect
{
    public class ShapeMatch : ObservableObject
    {
        private const double MatchMaxOverlap = 0.5;
        private const string MatchSubPixel = "least_squares";
        private const int MatchNumLevels = 4;
        private const double MatchGreediness = 0.9;

        public HTuple modelID = null;
        //public HWindowControlWPF HWindow = null;
        public BasicData BasicData = new BasicData();
        public HWindow HWindow = null;

        /// <summary>
        /// 形状模型是否已成功加载，供 AOI 与示教流程在调用 HALCON 匹配前做门禁。
        /// 仅反映最近一次 <see cref="init"/> 的结果，不参与工程 XML 持久化。
        /// </summary>
        public bool ModelLoaded { get; private set; }

        /// <summary>
        /// 最近一次成功加载的形状模型完整路径，用于工程启动、切换工程和执行前检查内存模型是否对应当前 Tool 序号。
        /// </summary>
        public string LoadedModelPath { get; private set; } = string.Empty;

        /// <summary>
        /// 最近一次成功加载的形状模型文件时间，用于保存模型后自动识别磁盘文件是否已更新。
        /// </summary>
        public DateTime LoadedModelWriteTimeUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// 判断内存中的形状模型是否对应指定文件，且磁盘文件未晚于本次加载时间。
        /// 文件在加载后被移走时，内存句柄仍可服务当前进程，生产检测继续使用已加载模型。
        /// </summary>
        /// <param name="filename">工程目录下 Tool 序号对应的 .shm 文件。</param>
        /// <returns>true 表示当前内存模型可直接用于本次检测；false 表示需要重新读取磁盘模型。</returns>
        public bool IsModelFileCurrent(string filename)
        {
            if (!ModelLoaded || string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            string fullPath = System.IO.Path.GetFullPath(filename);
            if (!string.Equals(LoadedModelPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return true;
            }

            return System.IO.File.GetLastWriteTimeUtc(fullPath) <= LoadedModelWriteTimeUtc;
        }

        /// <summary>
        /// 从磁盘读取形状模型（.shm），供模板匹配算子使用。
        /// 新文件通过 HALCON 读取成功后才替换当前句柄；保存中断、文件损坏或手动重载失败时保留当前可用模型，保证本次生产周期的检测状态连续。
        /// </summary>
        /// <param name="filename">形状模型完整路径；由工程目录与工具序号拼出。</param>
        /// <returns>加载成功返回 true；文件无效或 HALCON 读失败返回 false，已有模型继续保留其加载状态。</returns>
        public bool init(string filename)
        {
            HTuple loadedModelId = null;
            try
            {
                HOperatorSet.ReadShapeModel(filename, out loadedModelId);
                if (loadedModelId == null || loadedModelId.Length == 0)
                {
                    ClearShapeModelHandle(loadedModelId);
                    return false;
                }

                string loadedModelPath = System.IO.Path.GetFullPath(filename);
                DateTime loadedModelWriteTimeUtc = System.IO.File.Exists(loadedModelPath)
                    ? System.IO.File.GetLastWriteTimeUtc(loadedModelPath)
                    : DateTime.MinValue;

                HTuple previousModelId = modelID;
                modelID = loadedModelId;
                loadedModelId = null;
                ModelLoaded = true;
                LoadedModelPath = loadedModelPath;
                LoadedModelWriteTimeUtc = loadedModelWriteTimeUtc;
                ClearShapeModelHandle(previousModelId);
                return true;
            }
            catch (Exception)
            {
                ClearShapeModelHandle(loadedModelId);
                return false;
            }
        }

        /// <summary>
        /// 在工程切换、工具移除或进程关闭前释放当前形状模型句柄。
        /// 工程 XML 仅保存工具参数；该方法只释放当前进程的 HALCON 资源，不删除工程目录中的 .shm 文件。
        /// </summary>
        public void ReleaseModel()
        {
            ClearModel();
        }

        /// <summary>
        /// 释放当前形状模型句柄并重置加载状态。
        /// </summary>
        private void ClearModel()
        {
            ClearShapeModelHandle(modelID);

            modelID = null;
            ModelLoaded = false;
            LoadedModelPath = string.Empty;
            LoadedModelWriteTimeUtc = DateTime.MinValue;
        }

        /// <summary>
        /// 释放单个 HALCON 形状模型句柄。
        /// 读取候选模型失败时调用该入口，当前业务模型句柄保持由调用方管理。
        /// </summary>
        /// <param name="shapeModelId">待释放的 HALCON 形状模型句柄。</param>
        private static void ClearShapeModelHandle(HTuple shapeModelId)
        {
            if (shapeModelId != null && shapeModelId.Length > 0)
            {
                try
                {
                    HOperatorSet.ClearShapeModel(shapeModelId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"HALCON 形状模型句柄释放失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 校验模板匹配 ROI 是否可用于 HALCON 矩形生成。
        /// 预览、设基准与在线匹配均使用 PositionROI 的行列顺序，避免不同入口出现区域偏移。
        /// </summary>
        /// <param name="roiRow1">ROI 起始行（px），对应工程 PositionROI.Row1。</param>
        /// <param name="roiCol1">ROI 起始列（px），对应工程 PositionROI.Col1。</param>
        /// <param name="roiRow2">ROI 结束行（px），对应工程 PositionROI.Row2。</param>
        /// <param name="roiCol2">ROI 结束列（px），对应工程 PositionROI.Col2。</param>
        /// <param name="imageHeight">当前图像高度（px）。</param>
        /// <param name="imageWidth">当前图像宽度（px）。</param>
        /// <param name="errorInfo">校验失败时的可读原因，供追溯日志与上层 NG2 说明。</param>
        /// <returns>ROI 合法返回 true；非法返回 false，此时不应调用 FindShapeModel。</returns>
        private static bool TryValidateMatchRoi(int roiRow1, int roiCol1, int roiRow2, int roiCol2, int imageHeight, int imageWidth, out string errorInfo)
        {
            if (imageHeight <= 0 || imageWidth <= 0)
            {
                errorInfo = "图像尺寸无效";
                return false;
            }

            if (roiRow1 > roiRow2 || roiCol1 > roiCol2)
            {
                errorInfo = "ROI 起止坐标颠倒";
                return false;
            }

            if (roiRow1 < 0 || roiCol1 < 0 || roiRow2 >= imageHeight || roiCol2 >= imageWidth)
            {
                errorInfo = "ROI 超出图像范围";
                return false;
            }

            if (roiRow1 == roiRow2 || roiCol1 == roiCol2)
            {
                errorInfo = "ROI 面积为 0";
                return false;
            }

            errorInfo = null;
            return true;
        }

        /// <summary>
        /// 将界面“允许角度偏差 ±δ”换算为 HALCON FindShapeModel 的 AngleStart / AngleExtent。
        /// HALCON 第 4 个角参是从 AngleStart 起向正方向扫过的宽度，不是最大角；±δ 应对应 Start=-δ、Extent=2δ。
        /// </summary>
        /// <param name="allowAngleDeltaDeg">工具配置的允许角度偏差（deg），有效范围为大于 0 且不超过 180。</param>
        /// <param name="angleStartDeg">输出的 AngleStart（deg）。</param>
        /// <param name="angleExtentDeg">输出的 AngleExtent（deg）。</param>
        /// <exception cref="ArgumentOutOfRangeException">角度偏差无效时抛出，由预览、设基准或在线流程转换为可读失败原因。</exception>
        public static void GetFindShapeModelAngles(double allowAngleDeltaDeg, out double angleStartDeg, out double angleExtentDeg)
        {
            if (double.IsNaN(allowAngleDeltaDeg) || double.IsInfinity(allowAngleDeltaDeg) ||
                allowAngleDeltaDeg <= 0 || allowAngleDeltaDeg > 180)
            {
                throw new ArgumentOutOfRangeException(nameof(allowAngleDeltaDeg), "允许角度偏差须大于 0deg 且不超过 180deg");
            }

            angleStartDeg = -allowAngleDeltaDeg;
            angleExtentDeg = allowAngleDeltaDeg * 2.0;
        }

        /// <summary>
        /// 在指定 PositionROI 内按生产固定参数执行 FindShapeModel。
        /// 模型设置预览与在线匹配共用该入口，保证 ROI、金字塔层级、重叠率、亚像素方式和贪婪度一致；合格分值仍由上层业务门禁处理。
        /// </summary>
        /// <param name="image">待搜索图像，坐标单位为 px。</param>
        /// <param name="shapeModelId">已生成或已加载的 HALCON 形状模型句柄；该方法不接管句柄生命周期。</param>
        /// <param name="numMatches">最多返回的候选数量；模板工具当前传入 1。</param>
        /// <param name="roiRow1">PositionROI 起始行，单位 px。</param>
        /// <param name="roiCol1">PositionROI 起始列，单位 px。</param>
        /// <param name="roiRow2">PositionROI 结束行，单位 px。</param>
        /// <param name="roiCol2">PositionROI 结束列，单位 px。</param>
        /// <param name="angleStartDeg">HALCON 搜索起始角，单位 deg。</param>
        /// <param name="angleExtentDeg">从起始角向正方向搜索的宽度，单位 deg。</param>
        /// <param name="candidateMinScore">HALCON 候选搜索下限，范围 0.1～1.0。</param>
        /// <param name="rows">候选中心行集合；调用方负责 Dispose。</param>
        /// <param name="columns">候选中心列集合；调用方负责 Dispose。</param>
        /// <param name="angles">候选角度集合，单位 rad；调用方负责 Dispose。</param>
        /// <param name="scores">候选分值集合；调用方负责 Dispose。</param>
        /// <param name="errorInfo">ROI、参数、模型句柄或 HALCON 调用失败时的可读原因。</param>
        /// <returns>true 表示搜索调用完成；候选数量可以为 0。false 表示本次搜索条件无效或 HALCON 执行失败。</returns>
        internal static bool TryFindShapeModelInRoi(
            HObject image,
            HTuple shapeModelId,
            int numMatches,
            int roiRow1,
            int roiCol1,
            int roiRow2,
            int roiCol2,
            double angleStartDeg,
            double angleExtentDeg,
            double candidateMinScore,
            out HTuple rows,
            out HTuple columns,
            out HTuple angles,
            out HTuple scores,
            out string errorInfo)
        {
            rows = new HTuple();
            columns = new HTuple();
            angles = new HTuple();
            scores = new HTuple();
            HObject roi = null;
            HObject reducedImage = null;
            HTuple width = null;
            HTuple height = null;

            try
            {
                if (image == null)
                {
                    errorInfo = "待匹配图像为空";
                    return false;
                }

                if (shapeModelId == null || shapeModelId.Length == 0)
                {
                    errorInfo = "形状模型未加载";
                    return false;
                }

                HOperatorSet.GetImageSize(image, out width, out height);
                if (!TryValidateMatchRoi(roiRow1, roiCol1, roiRow2, roiCol2, (int)height.I, (int)width.I, out errorInfo))
                {
                    return false;
                }

                if (double.IsNaN(candidateMinScore) || double.IsInfinity(candidateMinScore) ||
                    candidateMinScore < 0.1 || candidateMinScore > 1.0)
                {
                    errorInfo = "候选搜索下限须在 0.1～1.0 之间";
                    return false;
                }

                if (double.IsNaN(angleStartDeg) || double.IsInfinity(angleStartDeg) ||
                    double.IsNaN(angleExtentDeg) || double.IsInfinity(angleExtentDeg) ||
                    angleExtentDeg <= 0 || angleExtentDeg > 360)
                {
                    errorInfo = "角度搜索范围无效";
                    return false;
                }

                HOperatorSet.GenRectangle1(out roi, roiRow1, roiCol1, roiRow2, roiCol2);
                HOperatorSet.ReduceDomain(image, roi, out reducedImage);

                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                    rows.Dispose();
                    columns.Dispose();
                    angles.Dispose();
                    scores.Dispose();
                    HOperatorSet.FindShapeModel(
                        reducedImage,
                        shapeModelId,
                        (new HTuple(angleStartDeg)).TupleRad(),
                        (new HTuple(angleExtentDeg)).TupleRad(),
                        candidateMinScore,
                        numMatches,
                        MatchMaxOverlap,
                        MatchSubPixel,
                        MatchNumLevels,
                        MatchGreediness,
                        out rows,
                        out columns,
                        out angles,
                        out scores);
                }

                errorInfo = null;
                return true;
            }
            catch (Exception ex)
            {
                errorInfo = ex.Message;
                return false;
            }
            finally
            {
                width?.Dispose();
                height?.Dispose();
                roi?.Dispose();
                reducedImage?.Dispose();
            }
        }

        /// <summary>
        /// 在指定 ROI 内执行形状模板匹配，可选将轮廓与 ROI 绘制到 HALCON 窗口。
        /// 候选搜索下限仅影响 FindShapeModel 是否返回实例；合格判定由上层工具的 MinScore 负责。
        /// </summary>
        /// <param name="image">待匹配图像；单位 px。</param>
        /// <param name="num">最多返回的匹配实例数量。</param>
        /// <param name="row1">搜索 ROI 起始行（px），历史约定对应 PositionROI.Row1。</param>
        /// <param name="row2">搜索 ROI 起始列（px），历史约定对应 PositionROI.Col1。</param>
        /// <param name="col1">搜索 ROI 结束行（px），历史约定对应 PositionROI.Row2。</param>
        /// <param name="col2">搜索 ROI 结束列（px），历史约定对应 PositionROI.Col2。</param>
        /// <param name="angleStartDeg">HALCON AngleStart（deg）。</param>
        /// <param name="angleExtentDeg">HALCON AngleExtent（deg），从 AngleStart 起向正方向搜索的宽度。</param>
        /// <param name="redraw">true 时在 HWindow 绘制底图、轮廓与 ROI；false 时仅计算，供 AOI 在线检测使用。</param>
        /// <param name="candidateMinScore">FindShapeModel 候选搜索下限（0~1）；与工具合格分值 MinScore 分离。</param>
        /// <returns>匹配结果；<see cref="Result.IsSuccess"/> 为 false 时表示模型未加载、ROI 非法或 HALCON 异常；无候选时 IsSuccess 仍为 true，但 points 为空并写入 ErrorInfo。</returns>
        public Result Match(HObject image, int num, int row1, int row2, int col1, int col2, double angleStartDeg = -20, double angleExtentDeg = 40, bool redraw = true, double candidateMinScore = 0.5)
        {
            HObject ho_ROI_0 = null;
            HObject ho_ModelContours = null;
            HObject ho_ContoursAffinTrans = null;
            HTuple hv_Row = null;
            HTuple hv_Column = null;
            HTuple hv_Angle = null;
            HTuple hv_Score = null;
            HTuple width = null;
            HTuple height = null;
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                if (!ModelLoaded || modelID == null || modelID.Length == 0)
                {
                    return new Result() { IsSuccess = false, ErrorInfo = "形状模型未加载", ElapsedMs = stopwatch.ElapsedMilliseconds };
                }

                HOperatorSet.GetImageSize(image, out width, out height);

                bool canDraw = redraw && HWindow != null;

                if (canDraw)
                {
                    HWindow.ClearWindow();
                    HWindow.SetPart(0, 0, (int)height.I - 1, (int)width.I - 1);
                    HWindow.DispObj(image);
                }

                if (!TryFindShapeModelInRoi(
                    image,
                    modelID,
                    num,
                    row1,
                    row2,
                    col1,
                    col2,
                    angleStartDeg,
                    angleExtentDeg,
                    candidateMinScore,
                    out hv_Row,
                    out hv_Column,
                    out hv_Angle,
                    out hv_Score,
                    out string searchError))
                {
                    return new Result() { IsSuccess = false, ErrorInfo = searchError, ElapsedMs = stopwatch.ElapsedMilliseconds };
                }

                Result r = new Result()
                {
                    points = new List<Result_Parameter>()
                };

                if (canDraw)
                {
                    HOperatorSet.GenRectangle1(out ho_ROI_0, row1, row2, col1, col2);
                    HOperatorSet.GetShapeModelContours(out ho_ModelContours, modelID, 1);
                    HWindow.SetLineWidth(2);
                    HWindow.SetColor("red");
                }

                Debug.WriteLine("分值");
                int matchCount = hv_Row != null && hv_Row.Length > 0 ? hv_Row.Length : 0;
                for (int i = 0; i < matchCount; i++)
                {
                    Debug.WriteLine($"{hv_Column[i].D},{hv_Row[i].D},{hv_Angle[i].D},{hv_Score[i].D}");

                    Result_Parameter rp = new Result_Parameter()
                    {
                        row = hv_Row[i].D,
                        column = hv_Column[i].D,
                        angle = hv_Angle[i].D,
                        score = hv_Score[i].D,
                    };
                    r.points.Add(rp);

                    if (canDraw)
                    {
                        HTuple hv_HomMat2D = new HTuple();
                        hv_HomMat2D.Dispose();
                        HOperatorSet.VectorAngleToRigid(0, 0, 0, hv_Row[i], hv_Column[i], hv_Angle[i], out hv_HomMat2D);
                        ho_ContoursAffinTrans?.Dispose();
                        HOperatorSet.AffineTransContourXld(ho_ModelContours, out ho_ContoursAffinTrans, hv_HomMat2D);
                        HWindow.DispObj(ho_ContoursAffinTrans);
                        hv_HomMat2D.Dispose();
                    }
                }

                if (canDraw)
                {
                    HWindow.SetDraw("margin");
                    HWindow.SetColor("yellow");
                    HWindow.DispObj(ho_ROI_0);
                    HWindow.SetColor("green");
                }

                r.IsSuccess = true;
                r.ElapsedMs = stopwatch.ElapsedMilliseconds;
                if (matchCount == 0)
                {
                    r.ErrorInfo = $"未找到候选（搜索角{angleStartDeg:F1}°起宽度{angleExtentDeg:F1}°，候选下限{candidateMinScore:F2}）";
                }
                return r;
            }
            catch (Exception ex)
            {
                return new Result() { IsSuccess = false, ErrorInfo = ex.ToString(), ElapsedMs = stopwatch.ElapsedMilliseconds };
            }
            finally
            {
                width?.Dispose();
                height?.Dispose();
                hv_Row?.Dispose();
                hv_Column?.Dispose();
                hv_Angle?.Dispose();
                hv_Score?.Dispose();
                ho_ROI_0?.Dispose();
                ho_ModelContours?.Dispose();
                ho_ContoursAffinTrans?.Dispose();
            }
        }

        /// <summary>
        /// 将匹配点换算为相对基准的实际坐标与偏差，供模板匹配判定与基准点写入使用。
        /// 无候选点时返回 null，由上层区分“搜不到”与“搜到但不合格”。
        /// </summary>
        /// <param name="r">FindShapeModel 返回的匹配结果。</param>
        /// <returns>首个候选换算后的结果；失败或无候选时返回 null。</returns>
        public ResultData Analysis_Result(Result r)
        {
            if (!r.IsSuccess || r.points == null || r.points.Count < 1)
            {
                return null;
            }

            ResultData ResultData = new ResultData();
            var rp = r.points[0];

            double deltaX = rp.column - BasicData.matchcenterX_Basic;
            double deltaY = rp.row - BasicData.matchcenterY_Basic;

            double radius = Math.Sqrt(Math.Pow(BasicData.productcenter_X - BasicData.matchcenter_X, 2) +
                Math.Pow(BasicData.productcenter_Y - BasicData.matchcenter_Y, 2));
            double angle = 0;
            if (radius != 0)
            {
                angle = Math.Asin((BasicData.productcenter_Y - BasicData.matchcenter_Y) / radius);
                if (BasicData.matchcenterX_Basic > BasicData.productcenter_X)
                {
                    angle = Math.PI - angle;
                }
            }
            double r_X = Math.Cos(-rp.angle + angle) * radius;
            double r_Y = Math.Sin(-rp.angle + angle) * radius;

            double b_X = Math.Cos(angle) * radius;
            double b_Y = Math.Sin(angle) * radius;

            ResultData.X_actual = (deltaX + r_X + BasicData.matchcenterX_Basic) * BasicData.K;
            ResultData.Y_actual = (deltaY + r_Y + BasicData.matchcenterY_Basic) * BasicData.K;

            ResultData.deltaX_actual = (deltaX + r_X - b_X) * BasicData.K;
            ResultData.deltaY_actual = (deltaY + r_Y - b_Y) * BasicData.K;
            ResultData.angle = rp.angle;
            ResultData.score = rp.score;

            ResultData.isSuccess = true;
            return ResultData;
        }

        public struct Result
        {
            public List<Result_Parameter> points;
            public bool IsSuccess;
            public string ErrorInfo;
            /// <summary>
            /// 本次匹配耗时（ms），供示教状态栏与诊断日志使用。
            /// </summary>
            public long ElapsedMs;
        }
        public struct Result_Parameter
        {
            public double row, column, angle, score;
        }


    }
    public class BasicData : ObservableObject
    {
        public double K { set; get; } = 1;
        public double matchcenter_X = 0;
        public double matchcenter_Y = 0;
        public double productcenter_X = 0;
        public double productcenter_Y = 0;
        public double matchcenterX_Basic = 0;
        public double matchcenterY_Basic = 0;
        public double dirX = 1;
        public double dirY = 1;
    }

    public class ResultData : ObservableObject
    {
        public double X_actual { set; get; } = 0;
        public double Y_actual { set; get; } = 0;
        public double deltaX_actual { set; get; } = 0;
        public double deltaY_actual { set; get; } = 0;
        public double angle { set; get; } = 0;
        public double score = 0;
        public bool isSuccess { set; get; } = false;
    }

}
