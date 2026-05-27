using BusbarCompressionSystem.FaraVision;
using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.FaraVision;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.Record;
using HalconDotNet;
using PositionDetect;
using System;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel
    {
        /// <summary>
        /// 清空当前帧模板定位矫正运行态，在 AOI 工位开始处理同一张图前调用。
        /// </summary>
        private void ResetLocatorCorrectionState()
        {
            DataModel.FaraVisionDataModel.Processmodel.LocatorCorrection?.Reset();
        }

        /// <summary>
        /// 判断工具是否参与整站 OK/NG/NG2 汇总；模板定位为辅助工具，不参与产品判定。
        /// </summary>
        private static bool IsJudgingTool(ToolModel tool)
        {
            return tool != null && tool.TestMode != TestModes.模板定位;
        }

        /// <summary>
        /// 执行模板定位辅助流程：匹配模板并写入本帧变换运行态，不阻断后续工具。
        /// </summary>
        /// <param name="tool">模板定位工具。</param>
        /// <param name="image">当前 AOI 图像。</param>
        /// <param name="traceDetail">输出追溯说明。</param>
        private void ProcessTemplateLocatorTool(ToolModel tool, HObject image, out string traceDetail)
        {
            traceDetail = string.Empty;
            var state = DataModel.FaraVisionDataModel.Processmodel.LocatorCorrection;
            state.Reset();
            state.FollowEnabled = tool.EnableFollowCorrectionForCommand;
            state.LocatorToolIndex = tool.Index;
            state.LocatorToolName = tool.Name ?? string.Empty;
            state.RefRow = tool.ReferenceMatchRow;
            state.RefCol = tool.ReferenceMatchCol;
            state.RefAngleRad = tool.ReferenceMatchAngleDeg * Math.PI / 180.0;

            if (!tool.ShapeMatch.ModelLoaded)
            {
                tool.ToolStatus = ToolStatus.定位未生效;
                traceDetail = "形状模型未加载";
                state.Summary = traceDetail;
                return;
            }

            try
            {
                tool.ShapeMatch.BasicData.matchcenter_X = tool.InitX;
                tool.ShapeMatch.BasicData.matchcenter_Y = tool.InitY;
                tool.ShapeMatch.BasicData.matchcenterX_Basic = tool.InitX;
                tool.ShapeMatch.BasicData.matchcenterY_Basic = tool.InitY;
                tool.ShapeMatch.BasicData.productcenter_X = tool.InitX;
                tool.ShapeMatch.BasicData.productcenter_Y = tool.InitY;

                ShapeMatch.Result matchResult = tool.ShapeMatch.Match(
                    image,
                    1,
                    tool.PositionROI.Row1,
                    tool.PositionROI.Col1,
                    tool.PositionROI.Row2,
                    tool.PositionROI.Col2,
                    (int)-tool.AllowAngleDelta,
                    (int)tool.AllowAngleDelta,
                    false);

                if (!matchResult.IsSuccess)
                {
                    tool.ToolStatus = ToolStatus.定位未生效;
                    tool.ActualScore = 0;
                    traceDetail = string.IsNullOrEmpty(matchResult.ErrorInfo) ? "Match返回失败" : matchResult.ErrorInfo;
                    state.Summary = traceDetail;
                    return;
                }

                if (matchResult.points == null || matchResult.points.Count == 0)
                {
                    tool.ToolStatus = ToolStatus.定位未生效;
                    tool.ActualScore = 0;
                    traceDetail = "未找到匹配目标";
                    state.Summary = traceDetail;
                    return;
                }

                var point = matchResult.points[0];
                tool.ActualScore = point.score;
                tool.ActualAngle = point.angle * 180.0 / Math.PI;
                tool.ActualX = point.column;
                tool.ActualY = point.row;
                state.ActRow = point.row;
                state.ActCol = point.column;
                state.ActAngleRad = point.angle;
                state.MatchScore = point.score;

                if (tool.ActualScore < tool.MinScore)
                {
                    tool.ToolStatus = ToolStatus.定位未生效;
                    traceDetail = "匹配分值低于下限";
                    state.Summary = traceDetail;
                    return;
                }

                if (!tool.ReferencePoseConfigured)
                {
                    tool.ToolStatus = ToolStatus.定位未生效;
                    traceDetail = "参考位姿未配置";
                    state.Summary = traceDetail;
                    return;
                }

                state.TransformActive = true;
                tool.ToolStatus = ToolStatus.OK;
                traceDetail = "定位生效";
                state.Summary = traceDetail;
            }
            catch (Exception ex)
            {
                tool.ToolStatus = ToolStatus.定位未生效;
                tool.ActualScore = 0;
                traceDetail = ex.Message;
                state.Summary = traceDetail;
                writeLog($"模板定位异常: {ex.Message}", false);
            }
        }

        /// <summary>
        /// 判断当前工具是否应使用本帧模板定位变换计算生效 ROI。
        /// </summary>
        private bool ShouldApplyLocatorCorrectionForTool(ToolModel tool)
        {
            if (tool == null)
            {
                return false;
            }

            if (tool.TestMode != TestModes.面积 && tool.TestMode != TestModes.尺寸测量)
            {
                return false;
            }

            var state = DataModel.FaraVisionDataModel.Processmodel.LocatorCorrection;
            return state != null && state.FollowEnabled;
        }

        /// <summary>
        /// 根据本帧定位运行态生成生效 ROI；未启用或未生效时返回 XML 原 ROI 的副本。
        /// </summary>
        /// <param name="sourceRoi">工程 XML 中保存的 ROI。</param>
        /// <param name="correctionApplied">输出是否已应用定位变换。</param>
        /// <param name="detail">输出矫正说明，供追溯日志使用。</param>
        /// <returns>供算法使用的 ROI 副本。</returns>
        private ROI ResolveEffectiveRoi(ROI sourceRoi, out bool correctionApplied, out string detail)
        {
            correctionApplied = false;
            detail = "未启用跟随或未应用变换，使用原ROI";

            ROI effective = CloneRoiForRuntime(sourceRoi);
            if (sourceRoi == null)
            {
                detail = "源ROI为空";
                return effective;
            }

            var state = DataModel.FaraVisionDataModel.Processmodel.LocatorCorrection;
            if (state == null || !state.FollowEnabled)
            {
                detail = "未启用ROI跟随";
                return effective;
            }

            if (!state.TransformActive)
            {
                detail = string.IsNullOrEmpty(state.Summary) ? "定位未生效，使用原ROI" : $"{state.Summary}，使用原ROI";
                return effective;
            }

            try
            {
                HTuple homMat2D;
                HOperatorSet.VectorAngleToRigid(
                    state.RefRow,
                    state.RefCol,
                    state.RefAngleRad,
                    state.ActRow,
                    state.ActCol,
                    state.ActAngleRad,
                    out homMat2D);

                effective = TransformRoiByHomMat(sourceRoi, homMat2D);
                homMat2D.Dispose();
                correctionApplied = true;
                detail = "已应用定位变换";
                return effective;
            }
            catch (Exception ex)
            {
                detail = $"变换失败，使用原ROI: {ex.Message}";
                return effective;
            }
        }

        /// <summary>
        /// 复制 ROI 供运行态变换使用，避免修改工程 XML 绑定对象。
        /// </summary>
        private static ROI CloneRoiForRuntime(ROI roi)
        {
            if (roi == null)
            {
                return new ROI();
            }

            return new ROI
            {
                Type = roi.Type,
                Row1 = roi.Row1,
                Row2 = roi.Row2,
                Col1 = roi.Col1,
                Col2 = roi.Col2,
                CircleCenterRow = roi.CircleCenterRow,
                CircleCenterCol = roi.CircleCenterCol,
                CircleRadius = roi.CircleRadius
            };
        }

        /// <summary>
        /// 对 ROI 施加定位跟随变换；线段按端点保留方向，圆只变换圆心，矩形仅平移中心并保持宽高。
        /// 面积工具使用矩形 ROI 做阈值面积筛选，保持宽高可避免旋转外接框把背景带入面积判定。
        /// </summary>
        private static ROI TransformRoiByHomMat(ROI source, HTuple homMat2D)
        {
            ROI result = CloneRoiForRuntime(source);
            if (source.Type == ROIType.Circle)
            {
                HTuple row, col;
                HOperatorSet.AffineTransPoint2d(homMat2D, source.CircleCenterRow, source.CircleCenterCol, out row, out col);
                result.CircleCenterRow = row.D;
                result.CircleCenterCol = col.D;
                result.CircleRadius = source.CircleRadius;
                return result;
            }

            if (source.Type == ROIType.Line)
            {
                TransformPoint(homMat2D, source.Row1, source.Col1, out double lineRow1, out double lineCol1);
                TransformPoint(homMat2D, source.Row2, source.Col2, out double lineRow2, out double lineCol2);
                result.Row1 = (int)Math.Round(lineRow1);
                result.Col1 = (int)Math.Round(lineCol1);
                result.Row2 = (int)Math.Round(lineRow2);
                result.Col2 = (int)Math.Round(lineCol2);
                return result;
            }

            double sourceCenterRow = (source.Row1 + source.Row2) / 2.0;
            double sourceCenterCol = (source.Col1 + source.Col2) / 2.0;
            TransformPoint(homMat2D, sourceCenterRow, sourceCenterCol, out double centerRow, out double centerCol);
            double rowOffset = centerRow - sourceCenterRow;
            double colOffset = centerCol - sourceCenterCol;

            result.Row1 = (int)Math.Round(source.Row1 + rowOffset);
            result.Row2 = (int)Math.Round(source.Row2 + rowOffset);
            result.Col1 = (int)Math.Round(source.Col1 + colOffset);
            result.Col2 = (int)Math.Round(source.Col2 + colOffset);
            return result;
        }

        private static void TransformPoint(HTuple homMat2D, double row, double col, out double outRow, out double outCol)
        {
            HTuple transRow, transCol;
            HOperatorSet.AffineTransPoint2d(homMat2D, row, col, out transRow, out transCol);
            outRow = transRow.D;
            outCol = transCol.D;
        }

        /// <summary>
        /// 获取 AOI 产品信息，供定位矫正追溯日志使用。
        /// </summary>
        private Productinfo ResolveAoiProductInfo(ToolModel tool)
        {
            Productinfo productInfo = DataModel.Processmodel.TakePhotoTestMode2.Productinfo;
            if (productInfo != null && !string.IsNullOrEmpty(productInfo.SN))
            {
                return productInfo;
            }

            string sn = string.Empty;
            try
            {
                sn = DataModel.FaraVisionDataModel.Processmodel.SNList[tool.ProductPositionNO];
            }
            catch
            {
                sn = DataModel.FaraVisionDataModel.Processmodel.BarcodeStr ?? "UNKNOWN";
            }

            return new Productinfo
            {
                SN = sn,
                WOCODE = string.Empty,
                PartNOID = string.Empty
            };
        }
    }
}
