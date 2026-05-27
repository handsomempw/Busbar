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
        /// 重复加载时会先释放旧模型句柄，避免编辑/切换工程后句柄残留。
        /// </summary>
        /// <param name="filename">形状模型完整路径；由工程目录与工具序号拼出。</param>
        /// <returns>加载成功返回 true；文件无效或 HALCON 读失败返回 false，此时 <see cref="ModelLoaded"/> 为 false。</returns>
        public bool init(string filename)
        {
            try
            {
                ClearModel();
                HOperatorSet.ReadShapeModel(filename, out modelID);
                ModelLoaded = modelID != null && modelID.Length > 0;
                if (ModelLoaded)
                {
                    LoadedModelPath = System.IO.Path.GetFullPath(filename);
                    LoadedModelWriteTimeUtc = System.IO.File.Exists(LoadedModelPath)
                        ? System.IO.File.GetLastWriteTimeUtc(LoadedModelPath)
                        : DateTime.MinValue;
                }
                return ModelLoaded;
            }
            catch (Exception)
            {
                ClearModel();
                return false;
            }
        }

        /// <summary>
        /// 释放当前形状模型句柄并重置加载状态。
        /// </summary>
        private void ClearModel()
        {
            if (modelID != null && modelID.Length > 0)
            {
                try
                {
                    HOperatorSet.ClearShapeModel(modelID);
                }
                catch (Exception)
                {
                }
            }

            modelID = null;
            ModelLoaded = false;
            LoadedModelPath = string.Empty;
            LoadedModelWriteTimeUtc = DateTime.MinValue;
        }

        /// <summary>
        /// 校验模板匹配 ROI 是否可用于 HALCON 矩形生成。
        /// 参数顺序与历史调用约定一致：row1=Row1，row2=Col1，col1=Row2，col2=Col2。
        /// </summary>
        /// <param name="row1">ROI 起始行（px），对应工程 PositionROI.Row1。</param>
        /// <param name="row2">ROI 起始列（px），对应工程 PositionROI.Col1。</param>
        /// <param name="col1">ROI 结束行（px），对应工程 PositionROI.Row2。</param>
        /// <param name="col2">ROI 结束列（px），对应工程 PositionROI.Col2。</param>
        /// <param name="imageHeight">当前图像高度（px）。</param>
        /// <param name="imageWidth">当前图像宽度（px）。</param>
        /// <param name="errorInfo">校验失败时的可读原因，供追溯日志与上层 NG2 说明。</param>
        /// <returns>ROI 合法返回 true；非法返回 false，此时不应调用 FindShapeModel。</returns>
        private static bool TryValidateMatchRoi(int row1, int row2, int col1, int col2, int imageHeight, int imageWidth, out string errorInfo)
        {
            if (imageHeight <= 0 || imageWidth <= 0)
            {
                errorInfo = "图像尺寸无效";
                return false;
            }

            if (row1 > col1 || row2 > col2)
            {
                errorInfo = "ROI 起止坐标颠倒";
                return false;
            }

            if (row1 < 0 || row2 < 0 || col1 >= imageHeight || col2 >= imageWidth)
            {
                errorInfo = "ROI 超出图像范围";
                return false;
            }

            if (row1 == col1 || row2 == col2)
            {
                errorInfo = "ROI 面积为 0";
                return false;
            }

            errorInfo = null;
            return true;
        }

        /// <summary>
        /// 在指定 ROI 内执行形状模板匹配，可选将轮廓与 ROI 绘制到 HALCON 窗口。
        /// </summary>
        /// <param name="image">待匹配图像；单位 px。</param>
        /// <param name="num">最多返回的匹配实例数量。</param>
        /// <param name="row1">搜索 ROI 起始行（px），历史约定对应 PositionROI.Row1。</param>
        /// <param name="row2">搜索 ROI 起始列（px），历史约定对应 PositionROI.Col1。</param>
        /// <param name="col1">搜索 ROI 结束行（px），历史约定对应 PositionROI.Row2。</param>
        /// <param name="col2">搜索 ROI 结束列（px），历史约定对应 PositionROI.Col2。</param>
        /// <param name="MinAngle">允许的最小旋转角（deg）。</param>
        /// <param name="MaxAngle">允许的最大旋转角（deg）。</param>
        /// <param name="redraw">true 时在 HWindow 绘制底图、轮廓与 ROI；false 时仅计算，供 AOI 在线检测使用。</param>
        /// <returns>匹配结果；<see cref="Result.IsSuccess"/> 为 false 时表示模型未加载、ROI 非法或 HALCON 异常。</returns>
        public Result Match(HObject image, int num, int row1, int row2, int col1, int col2, int MinAngle = -20, int MaxAngle = 20, bool redraw = true)
        {
            HObject ho_ROI_0 = null;
            HObject ho_ImageReduced = null;
            HObject ho_ModelContours = null;
            HObject ho_ContoursAffinTrans = null;

            try
            {
                HTuple width, height;
                HOperatorSet.GetImageSize(image, out width, out height);

                if (!TryValidateMatchRoi(row1, row2, col1, col2, (int)height.I, (int)width.I, out string roiError))
                {
                    return new Result() { IsSuccess = false, ErrorInfo = roiError };
                }

                if (!ModelLoaded || modelID == null || modelID.Length == 0)
                {
                    return new Result() { IsSuccess = false, ErrorInfo = "形状模型未加载" };
                }

                HOperatorSet.GenEmptyObj(out ho_ROI_0);
                HOperatorSet.GenEmptyObj(out ho_ImageReduced);
                HOperatorSet.GenEmptyObj(out ho_ModelContours);
                HOperatorSet.GenEmptyObj(out ho_ContoursAffinTrans);

                bool canDraw = redraw && HWindow != null;

                if (canDraw)
                {
                    HWindow.ClearWindow();
                    HWindow.SetPart(0, 0, (int)height.I - 1, (int)width.I - 1);
                    HWindow.DispObj(image);
                }

                HTuple hv_Row = new HTuple(), hv_Column = new HTuple(), hv_Angle = new HTuple(), hv_Score = new HTuple();

                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                    HOperatorSet.GenRectangle1(out ho_ROI_0, row1, row2, col1, col2);
                    HOperatorSet.ReduceDomain(image, ho_ROI_0, out ho_ImageReduced);

                    hv_Row.Dispose(); hv_Column.Dispose(); hv_Angle.Dispose(); hv_Score.Dispose();

                    HOperatorSet.FindShapeModel(ho_ImageReduced, modelID, (new HTuple(MinAngle)).TupleRad()
                   , (new HTuple(MaxAngle)).TupleRad(), 0.5, num, 0.5, "least_squares", 4, 0.9, out hv_Row,
                out hv_Column, out hv_Angle, out hv_Score);
                }

                Result r = new Result()
                {
                    points = new List<Result_Parameter>()
                };

                if (canDraw)
                {
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
                        ho_ContoursAffinTrans.Dispose();
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
                return r;
            }
            catch (Exception ex)
            {
                return new Result() { IsSuccess = false, ErrorInfo = ex.ToString() };
            }
            finally
            {
                ho_ROI_0?.Dispose();
                ho_ImageReduced?.Dispose();
                ho_ModelContours?.Dispose();
                ho_ContoursAffinTrans?.Dispose();
            }
        }

        public ResultData Analysis_Result(Result r)
        {
            if (!r.IsSuccess)
            {
                return null;
            }
            ResultData ResultData = new ResultData();
            if (r.IsSuccess && r.points.Count >= 1)
            {
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
            }

            return ResultData;
        }

        public struct Result
        {
            public List<Result_Parameter> points;
            public bool IsSuccess;
            public string ErrorInfo;
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
