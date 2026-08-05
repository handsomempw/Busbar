using GalaSoft.MvvmLight;
using HalconDotNet;
using Panuon.WPF.UI;
using System.Windows.Media;
using System.Windows;
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace PositionDetect.ViewModel
{
    /// <summary>
    /// 模板特征圈选、预览生成与模型导出 ViewModel。
    /// 负责模型设置窗口内的包含区/排除矩形示教；在线 FindShapeModel 仍由主工程 ShapeMatch 加载已保存的 .shm。
    /// </summary>
    public class PositionDetectViewModel : ViewModelBase
    {
        public DATA DATA { get; set; } = new DATA();

        /// <summary>
        /// 显示当前示教图并恢复原图坐标范围，作为区域编辑、列表选中和已发布模型轮廓的共同底图。
        /// 该方法只刷新模型设置窗口，不修改特征配方、临时模型或工程文件。
        /// </summary>
        public void showtest()
        {
            HTuple hv_width = null, hv_height = null;

            try
            {
                HOperatorSet.GetImageSize(DATA.image, out hv_width, out hv_height);
                DATA.HWindow.HalconWindow.ClearWindow();
                DATA.HWindow.HalconWindow.SetPart(0, 0, (int)hv_height - 1, (int)hv_width - 1);
                DATA.HWindow.HalconWindow.DispObj(DATA.image);

            }
            catch
            {; }
            finally
            {
                hv_width?.Dispose();
                hv_height?.Dispose();
            }
        }

        /// <summary>
        /// 从 Tool XML 中的几何配方重建本次模型设置会话。
        /// 每个定义生成独立 HALCON Region；无效项跳过并返回诊断，已发布 .shm 和 ToolModel 原始集合保持当前状态。
        /// </summary>
        /// <param name="definitions">当前 Tool 保存的模板特征定义；允许为空，旧工程按空配方进入。</param>
        /// <param name="warningMessage">存在无效定义时返回跳过数量和首个原因，供模型状态栏提示。</param>
        /// <returns>完成恢复的包含区与排除区总数。</returns>
        public int RestoreTemplateFeatures(IEnumerable<TemplateFeatureDefinition> definitions, out string warningMessage)
        {
            warningMessage = string.Empty;
            int skippedCount = 0;
            string firstError = null;

            DATA.SuppressTemplateRecipeChangeTracking = true;
            try
            {
                ClearTemplateFeatureRegions();
                foreach (TemplateFeatureDefinition definition in definitions ?? Enumerable.Empty<TemplateFeatureDefinition>())
                {
                    if (TryCreateTemplateFeatureItem(definition, out TemplateFeatureItem item, out string errorMessage))
                    {
                        DATA.Objects.Add(item);
                    }
                    else
                    {
                        skippedCount++;
                        if (string.IsNullOrWhiteSpace(firstError))
                        {
                            firstError = errorMessage;
                        }
                    }
                }

                if (skippedCount > 0)
                {
                    warningMessage = $"有 {skippedCount} 个示教区域未恢复：{firstError}";
                }

                return DATA.Objects.Count;
            }
            finally
            {
                DATA.SuppressTemplateRecipeChangeTracking = false;
                DATA.IsTemplateRecipeDirty = false;
                DATA.ROImode = false;
            }
        }

        /// <summary>
        /// 导出当前会话中的模板特征几何配方，供模型发布前写入对应 Tool XML。
        /// 返回深拷贝，后续窗口增删区域不会直接修改工具已发布配方。
        /// </summary>
        /// <returns>按界面列表顺序排列的独立几何定义集合。</returns>
        public List<TemplateFeatureDefinition> ExportTemplateFeatureDefinitions()
        {
            return DATA.Objects
                .Where(item => item?.Definition != null && item.Region != null)
                .Select(item => item.Definition.Clone())
                .ToList();
        }

        /// <summary>
        /// 清空当前会话的包含区与排除区，进入重新示教状态。
        /// 正式 .shm 和 ToolModel 已发布配方继续生效，直到操作员完成预览并再次发布模型。
        /// </summary>
        public void StartNewTemplateTeaching()
        {
            ClearTemplateFeatureRegions();
            DATA.IsTemplateRecipeDirty = true;
            DATA.ROImode = true;
            showtest();
        }

        /// <summary>
        /// 结束模型设置会话并释放临时区域、图像和模型句柄。
        /// Tool XML 中的几何配方、工程 .shm 和在线 ShapeMatch 句柄由主工程继续持有。
        /// </summary>
        public void DisposeTemplateFeatureSession()
        {
            DATA.SuppressTemplateRecipeChangeTracking = true;
            try
            {
                DATA.ROImode = false;
                DATA.modelID = null;
                ClearTemplateFeatureRegions();
                DATA.image?.Dispose();
                DATA.image = null;
                DATA.HWindow = null;
            }
            finally
            {
                DATA.SuppressTemplateRecipeChangeTracking = false;
                DATA.IsTemplateRecipeDirty = false;
            }
        }

        /// <summary>
        /// 释放当前列表中的 HALCON Region 并清空选择状态。
        /// 调用方通过 SuppressTemplateRecipeChangeTracking 区分会话恢复/释放与操作员重新示教。
        /// </summary>
        private void ClearTemplateFeatureRegions()
        {
            foreach (TemplateFeatureItem item in DATA.Objects.ToList())
            {
                try
                {
                    item?.Region?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"模板示教区域释放失败：{ex.Message}");
                }
            }

            DATA.Objects.Clear();
            DATA.selectedindex = -1;
        }

        /// <summary>
        /// 将一个持久化几何定义转换为可显示、可参与建模的 HALCON Region。
        /// 矩形和圆形坐标均按原图 px 校验，异常定义留在 Tool XML 中供诊断，本次会话跳过该项。
        /// </summary>
        /// <param name="definition">来自当前 Tool XML 的特征定义。</param>
        /// <param name="item">转换成功后的会话区域项，Region 生命周期归模型窗口所有。</param>
        /// <param name="errorMessage">几何参数无效或 HALCON 创建失败时的可读原因。</param>
        /// <returns>true 表示区域可用于显示和建模；false 表示本次会话跳过该定义。</returns>
        private static bool TryCreateTemplateFeatureItem(
            TemplateFeatureDefinition definition,
            out TemplateFeatureItem item,
            out string errorMessage)
        {
            item = null;
            errorMessage = string.Empty;
            if (definition == null)
            {
                errorMessage = "特征定义为空";
                return false;
            }

            HObject region = null;
            try
            {
                TemplateFeatureDefinition clonedDefinition = definition.Clone();
                if (clonedDefinition.ShapeType == TemplateFeatureShapeType.Rectangle)
                {
                    if (!AreFinite(clonedDefinition.Row1, clonedDefinition.Col1, clonedDefinition.Row2, clonedDefinition.Col2)
                        || clonedDefinition.Row1 == clonedDefinition.Row2
                        || clonedDefinition.Col1 == clonedDefinition.Col2)
                    {
                        errorMessage = "矩形坐标无效";
                        return false;
                    }

                    HOperatorSet.GenRectangle1(
                        out region,
                        clonedDefinition.Row1,
                        clonedDefinition.Col1,
                        clonedDefinition.Row2,
                        clonedDefinition.Col2);
                }
                else if (clonedDefinition.ShapeType == TemplateFeatureShapeType.Circle)
                {
                    if (!AreFinite(clonedDefinition.CenterRow, clonedDefinition.CenterCol, clonedDefinition.Radius)
                        || clonedDefinition.Radius <= 0)
                    {
                        errorMessage = "圆形坐标或半径无效";
                        return false;
                    }

                    HOperatorSet.GenCircle(
                        out region,
                        clonedDefinition.CenterRow,
                        clonedDefinition.CenterCol,
                        clonedDefinition.Radius);
                }
                else
                {
                    errorMessage = $"区域类型 {clonedDefinition.ShapeType} 暂无恢复入口";
                    return false;
                }

                item = new TemplateFeatureItem
                {
                    Definition = clonedDefinition,
                    Region = region
                };
                region = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                region?.Dispose();
            }
        }

        /// <summary>
        /// 校验模板几何坐标是否为 HALCON 可接受的有限数值。
        /// </summary>
        /// <param name="values">待校验的原图坐标或半径，单位 px。</param>
        /// <returns>全部数值均为有限数时返回 true。</returns>
        private static bool AreFinite(params double[] values)
        {
            return values != null && values.All(value => !double.IsNaN(value) && !double.IsInfinity(value));
        }

        /// <summary>
        /// 刷新图像上的包含区与排除区可视化。
        /// 包含区半透明绿色，排除区半透明品红，选中项用红色强调，便于现场确认“挖洞”范围。
        /// </summary>
        public void showregion()
        {
            HObject includeRoi = null;
            HObject excludeRoi = null;
            try
            {
                HOperatorSet.GenEmptyObj(out includeRoi);
                HOperatorSet.GenEmptyObj(out excludeRoi);

                foreach (var item in DATA.Objects)
                {
                    if (item?.Region == null)
                    {
                        continue;
                    }

                    if (item.IsExclude)
                    {
                        UnionRegion(ref excludeRoi, item.Region);
                    }
                    else
                    {
                        UnionRegion(ref includeRoi, item.Region);
                    }
                }

                DATA.HWindow.HalconWindow.SetRgba(0, 255, 0, 90);
                DATA.HWindow.HalconWindow.DispObj(includeRoi);

                DATA.HWindow.HalconWindow.SetRgba(255, 0, 180, 110);
                DATA.HWindow.HalconWindow.DispObj(excludeRoi);

                if (DATA.Objects.Count > 0 && DATA.selectedindex >= 0 && DATA.selectedindex < DATA.Objects.Count)
                {
                    var selected = DATA.Objects[DATA.selectedindex];
                    if (selected?.Region != null)
                    {
                        DATA.HWindow.HalconWindow.SetRgba(255, 0, 0, 120);
                        DATA.HWindow.HalconWindow.DispObj(selected.Region);
                    }
                }

            }
            catch { ; }
            finally
            {
                includeRoi?.Dispose();
                excludeRoi?.Dispose();
            }
        }

        /// <summary>
        /// 在示教图上显示已保存模板模型的实际匹配轮廓，并叠加当前可编辑区域。
        /// 该入口使用模型设置预览参数搜索一次，只提供进入窗口后的可视确认；在线模型句柄、基准点和生产判定保持由主工程管理。
        /// </summary>
        /// <param name="modelFilename">当前 Tool 的正式 .shm 完整路径。</param>
        /// <param name="summary">轮廓显示结果、分值或失败原因，供模型状态栏展示。</param>
        /// <returns>true 表示已发布模型在示教图中找到候选并绘制红色轮廓。</returns>
        public bool DisplayPublishedModelContours(string modelFilename, out string summary)
        {
            summary = string.Empty;
            showtest();
            showregion();

            if (string.IsNullOrWhiteSpace(modelFilename) || !File.Exists(modelFilename))
            {
                summary = "当前工具尚未发布模型文件";
                return false;
            }

            HTuple modelId = null;
            HTuple rows = null;
            HTuple columns = null;
            HTuple angles = null;
            HTuple scores = null;
            HTuple homMat2D = null;
            HObject modelContours = null;
            HObject transformedContours = null;
            try
            {
                HOperatorSet.ReadShapeModel(modelFilename, out modelId);
                ShapeMatch.GetFindShapeModelAngles(
                    DATA.PreviewAllowAngleDelta,
                    out double angleStartDeg,
                    out double angleExtentDeg);

                if (!ShapeMatch.TryFindShapeModelInRoi(
                    DATA.image,
                    modelId,
                    1,
                    DATA.PreviewRoiRow1,
                    DATA.PreviewRoiCol1,
                    DATA.PreviewRoiRow2,
                    DATA.PreviewRoiCol2,
                    angleStartDeg,
                    angleExtentDeg,
                    DATA.PreviewCandidateMinScore,
                    out rows,
                    out columns,
                    out angles,
                    out scores,
                    out string searchError))
                {
                    summary = $"已发布模型轮廓显示失败：{searchError}";
                    return false;
                }

                if (rows == null || rows.Length == 0)
                {
                    summary = "已发布模型在当前示教图和 PositionROI 内未找到候选";
                    return false;
                }

                HOperatorSet.GetShapeModelContours(out modelContours, modelId, 1);
                HOperatorSet.VectorAngleToRigid(0, 0, 0, rows[0], columns[0], angles[0], out homMat2D);
                HOperatorSet.AffineTransContourXld(modelContours, out transformedContours, homMat2D);
                DATA.HWindow.HalconWindow.SetDraw("margin");
                DATA.HWindow.HalconWindow.SetLineWidth(2);
                DATA.HWindow.HalconWindow.SetColor("red");
                DATA.HWindow.HalconWindow.DispObj(transformedContours);

                double score = scores[0].D;
                summary = score >= DATA.PreviewMinScore
                    ? $"已显示发布模型轮廓，分值={score:F3}"
                    : $"已显示发布模型轮廓，分值={score:F3}，低于合格下限 {DATA.PreviewMinScore:F3}";
                return true;
            }
            catch (Exception ex)
            {
                summary = $"已发布模型轮廓显示异常：{ex.Message}";
                return false;
            }
            finally
            {
                rows?.Dispose();
                columns?.Dispose();
                angles?.Dispose();
                scores?.Dispose();
                homMat2D?.Dispose();
                modelContours?.Dispose();
                transformedContours?.Dispose();
                ClearLocalShapeModel(ref modelId);
            }
        }

        /// <summary>
        /// 绘制包含矩形并加入特征列表。
        /// </summary>
        public void drawrectangle()
        {
            DATA.CircleMode = Brushes.White;
            DATA.ExcludeRectangleMode = Brushes.White;
            DATA.RectangleMmode = Brushes.Orange;
            double row1, col1, row2, col2;
            HObject rect = null;
            DATA.HWindow.HalconWindow.DrawRectangle1(out row1, out col1, out row2, out col2);
            HOperatorSet.GenRectangle1(out rect, row1, col1, row2, col2);
            DATA.Objects.Add(new TemplateFeatureItem
            {
                Definition = new TemplateFeatureDefinition
                {
                    ShapeType = TemplateFeatureShapeType.Rectangle,
                    IsExclude = false,
                    Row1 = row1,
                    Col1 = col1,
                    Row2 = row2,
                    Col2 = col2
                },
                Region = rect
            });
            DATA.RectangleMmode = Brushes.White;
        }

        /// <summary>
        /// 绘制包含圆形并加入特征列表。
        /// </summary>
        public void drawcircle()
        {
            DATA.RectangleMmode = Brushes.White;
            DATA.ExcludeRectangleMode = Brushes.White;
            DATA.CircleMode = Brushes.OrangeRed;
            double row, col, radius;
            HObject cir = null;
            DATA.HWindow.HalconWindow.DrawCircle(out row, out col, out radius);
            HOperatorSet.GenCircle(out cir, row, col, radius);
            DATA.Objects.Add(new TemplateFeatureItem
            {
                Definition = new TemplateFeatureDefinition
                {
                    ShapeType = TemplateFeatureShapeType.Circle,
                    IsExclude = false,
                    CenterRow = row,
                    CenterCol = col,
                    Radius = radius
                },
                Region = cir
            });
            DATA.CircleMode = Brushes.White;
        }

        /// <summary>
        /// 绘制排除矩形：建模时从包含区减去该区域，用于屏蔽反光、字符、阴影等干扰边缘。
        /// 排除区可选；至少需要一个包含区才能预览。
        /// </summary>
        public void drawExcludeRectangle()
        {
            DATA.CircleMode = Brushes.White;
            DATA.RectangleMmode = Brushes.White;
            DATA.ExcludeRectangleMode = Brushes.MediumVioletRed;
            double row1, col1, row2, col2;
            HObject rect = null;
            DATA.HWindow.HalconWindow.DrawRectangle1(out row1, out col1, out row2, out col2);
            HOperatorSet.GenRectangle1(out rect, row1, col1, row2, col2);
            DATA.Objects.Add(new TemplateFeatureItem
            {
                Definition = new TemplateFeatureDefinition
                {
                    ShapeType = TemplateFeatureShapeType.Rectangle,
                    IsExclude = true,
                    Row1 = row1,
                    Col1 = col1,
                    Row2 = row2,
                    Col2 = col2
                },
                Region = rect
            });
            DATA.ExcludeRectangleMode = Brushes.White;
        }

        /// <summary>
        /// 按包含区减排除区生成临时形状模型，并使用生产 PositionROI 与固定 HALCON 搜索参数验证一次。
        /// 建模角域使用全周覆盖在线能力；预览、设基准与在线共用搜索区域、角域和候选分，预览达到合格分后才允许保存发布。
        /// </summary>
        /// <returns>预览成功且达到合格门禁返回 true；无包含区、域为空、无候选、分值不足或 HALCON 失败返回 false。</returns>
        public bool showdetect()
        {
            if (!DATA.HasTemplateFeatures)
            {
                DATA.LastPreviewSummary = "请先画至少一个包含区特征";
                MessageBoxX.Show("请先画矩形或圆形包含特征，并在图像窗口点鼠标右键完成当前特征。需要屏蔽干扰时可再画排除矩形。", "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return false;
            }

            DATA.modelID = null;
            DATA.LastPreviewSummary = string.Empty;
            HTuple hv_width = null, hv_height = null;
            HObject ReduceImage = null;
            HObject domain = null;

            HObject ho_ModelContours = null;
            HObject ho_ContoursAffinTrans = null;

            HTuple hv_Row = null, hv_Column = null, hv_Angle = null, hv_Score = null;
            HTuple hv_HomMat2D = null;

            Stopwatch stopwatch = Stopwatch.StartNew();
            HTuple modelID = null;
            try
            {
                DATA.HWindow.HalconWindow.ClearWindow();
                HOperatorSet.GetImageSize(DATA.image, out hv_width, out hv_height);

                if (!TryBuildTemplateDomain(out domain, out string domainError))
                {
                    DATA.LastPreviewSummary = domainError;
                    MessageBoxX.Show(domainError, "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                    return false;
                }

                double previewMinScore = DATA.PreviewMinScore;
                double previewCandidate = DATA.PreviewCandidateMinScore;
                if (double.IsNaN(previewMinScore) || double.IsInfinity(previewMinScore) ||
                    previewMinScore < 0.1 || previewMinScore > 1.0 ||
                    double.IsNaN(previewCandidate) || double.IsInfinity(previewCandidate) ||
                    previewCandidate < 0.1 || previewCandidate > previewMinScore)
                {
                    DATA.LastPreviewSummary = "预览分值参数无效，请返回工具设置检查候选分和合格分";
                    MessageBoxX.Show(DATA.LastPreviewSummary, "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                    return false;
                }

                ShapeMatch.GetFindShapeModelAngles(DATA.PreviewAllowAngleDelta, out double angleStartDeg, out double angleExtentDeg);

                HOperatorSet.ReduceDomain(DATA.image, domain, out ReduceImage);
                DATA.HWindow.HalconWindow.SetPart(0, 0, (int)hv_height - 1, (int)hv_width - 1);
                DATA.HWindow.HalconWindow.DispObj(ReduceImage);
                // 临时模型覆盖全周角域，已发布模型可直接服务工程允许的任意角度偏差。
                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                    HOperatorSet.CreateShapeModel(
                        ReduceImage,
                        "auto",
                        (new HTuple(-180)).TupleRad(),
                        (new HTuple(360)).TupleRad(),
                        "auto",
                        "auto",
                        "use_polarity",
                        "auto",
                        "auto",
                        out modelID);
                }
                if (!ShapeMatch.TryFindShapeModelInRoi(
                    DATA.image,
                    modelID,
                    1,
                    DATA.PreviewRoiRow1,
                    DATA.PreviewRoiCol1,
                    DATA.PreviewRoiRow2,
                    DATA.PreviewRoiCol2,
                    angleStartDeg,
                    angleExtentDeg,
                    previewCandidate,
                    out hv_Row,
                    out hv_Column,
                    out hv_Angle,
                    out hv_Score,
                    out string searchError))
                {
                    DATA.LastPreviewSummary = $"预览搜索失败：{searchError}";
                    ClearLocalShapeModel(ref modelID);
                    DATA.HWindow.HalconWindow.SetDraw("margin");
                    DrawDomainOutlines(domain);
                    MessageBoxX.Show(DATA.LastPreviewSummary, "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                    return false;
                }

                HOperatorSet.GetShapeModelContours(out ho_ModelContours, modelID, 1);

                DATA.HWindow.HalconWindow.SetLineWidth(2);
                DATA.HWindow.HalconWindow.DispObj(DATA.image);

                int matchCount = hv_Row != null && hv_Row.Length > 0 ? hv_Row.Length : 0;
                if (matchCount == 0)
                {
                    DATA.LastPreviewSummary = $"示教图未找到候选（角域{angleStartDeg:F1}°起宽度{angleExtentDeg:F1}°，候选下限{previewCandidate:F2}），耗时={stopwatch.ElapsedMilliseconds}ms；模型未开放保存";
                    ClearLocalShapeModel(ref modelID);
                    DATA.HWindow.HalconWindow.SetDraw("margin");
                    DrawDomainOutlines(domain);
                    MessageBoxX.Show(DATA.LastPreviewSummary, "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                    return false;
                }

                double score = hv_Score[0].D;
                double angleDeg = hv_Angle[0].D * 180.0 / Math.PI;
                HOperatorSet.VectorAngleToRigid(0, 0, 0, hv_Row, hv_Column, hv_Angle, out hv_HomMat2D);
                HOperatorSet.AffineTransContourXld(ho_ModelContours, out ho_ContoursAffinTrans,
                    hv_HomMat2D);
                DATA.HWindow.HalconWindow.SetColor("red");
                DATA.HWindow.HalconWindow.DispObj(ho_ContoursAffinTrans);
                DATA.HWindow.HalconWindow.SetDraw("margin");
                DATA.HWindow.HalconWindow.SetColor("green");
                DrawDomainOutlines(domain);

                if (score < previewMinScore)
                {
                    DATA.LastPreviewSummary = $"预览分值低于合格下限({score:F3} < {previewMinScore:F3})，角度={angleDeg:F2}°，耗时={stopwatch.ElapsedMilliseconds}ms；模型未开放保存";
                    ClearLocalShapeModel(ref modelID);
                    MessageBoxX.Show(DATA.LastPreviewSummary, "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                    return false;
                }

                DATA.LastPreviewSummary = $"预览成功：分值={score:F3}，角度={angleDeg:F2}°，耗时={stopwatch.ElapsedMilliseconds}ms（红=模型边缘）";
                DATA.modelID = modelID;
                modelID = null;
                return true;
            }
            catch (Exception ex)
            {
                ClearLocalShapeModel(ref modelID);
                DATA.modelID = null;
                DATA.LastPreviewSummary = $"预览生成失败：{ex.Message}";
                return false;
            }
            finally
            {
                hv_Row?.Dispose();
                hv_Column?.Dispose();
                hv_Angle?.Dispose();
                hv_Score?.Dispose();
                hv_HomMat2D?.Dispose();
                hv_width?.Dispose();
                hv_height?.Dispose();
                domain?.Dispose();
                ReduceImage?.Dispose();
                ho_ModelContours?.Dispose();
                ho_ContoursAffinTrans?.Dispose();
                ClearLocalShapeModel(ref modelID);
            }
        }

        /// <summary>
        /// 释放尚未挂到 DATA.modelID 的临时形状模型，避免预览失败路径泄漏句柄。
        /// </summary>
        /// <param name="shapeModelId">待释放的临时模型句柄；释放后置空。</param>
        private static void ClearLocalShapeModel(ref HTuple shapeModelId)
        {
            if (shapeModelId == null)
            {
                return;
            }

            try
            {
                if (shapeModelId.Length > 0)
                {
                    HOperatorSet.ClearShapeModel(shapeModelId);
                }
            }
            catch
            {
                // 清理失败只影响尚未发布的临时句柄，预览失败状态和正式 .shm 文件保持不变。
            }
            finally
            {
                shapeModelId.Dispose();
                shapeModelId = null;
            }
        }

        /// <summary>
        /// 合并包含区并减去排除矩形，得到 CreateShapeModel 使用的模板域。
        /// </summary>
        /// <param name="domain">输出模板域；调用方负责 Dispose。</param>
        /// <param name="errorMessage">域为空或缺少包含区时的可读原因。</param>
        /// <returns>域有效返回 true。</returns>
        private bool TryBuildTemplateDomain(out HObject domain, out string errorMessage)
        {
            domain = null;
            errorMessage = string.Empty;

            HObject includeRoi = null;
            HObject excludeRoi = null;
            try
            {
                HOperatorSet.GenEmptyObj(out includeRoi);
                HOperatorSet.GenEmptyObj(out excludeRoi);

                bool hasInclude = false;
                bool hasExclude = false;
                foreach (var item in DATA.Objects)
                {
                    if (item?.Region == null)
                    {
                        continue;
                    }

                    if (item.IsExclude)
                    {
                        UnionRegion(ref excludeRoi, item.Region);
                        hasExclude = true;
                    }
                    else
                    {
                        UnionRegion(ref includeRoi, item.Region);
                        hasInclude = true;
                    }
                }

                if (!hasInclude)
                {
                    errorMessage = "请先画至少一个包含区（矩形或圆形）";
                    return false;
                }

                if (hasExclude)
                {
                    HOperatorSet.Difference(includeRoi, excludeRoi, out domain);
                }
                else
                {
                    HOperatorSet.CopyObj(includeRoi, out domain, 1, -1);
                }

                HTuple area = null;
                HTuple row = null;
                HTuple col = null;
                try
                {
                    HOperatorSet.AreaCenter(domain, out area, out row, out col);
                    if (area == null || area.Length == 0 || area.D <= 0)
                    {
                        errorMessage = "包含区被排除矩形挖空后面积为 0，请缩小排除区或扩大包含区";
                        domain?.Dispose();
                        domain = null;
                        return false;
                    }
                }
                finally
                {
                    area?.Dispose();
                    row?.Dispose();
                    col?.Dispose();
                }

                return true;
            }
            catch (Exception ex)
            {
                domain?.Dispose();
                domain = null;
                errorMessage = $"模板域计算失败：{ex.Message}";
                return false;
            }
            finally
            {
                includeRoi?.Dispose();
                excludeRoi?.Dispose();
            }
        }

        /// <summary>
        /// 将一个示教区域并入聚合区域，并释放被替换的 HALCON 中间对象。
        /// 该入口只管理聚合结果，列表中的原始包含区和排除区仍由示教会话持有，便于选中、删除和重新预览。
        /// </summary>
        /// <param name="aggregate">当前聚合区域；调用后替换为新的并集对象。</param>
        /// <param name="region">待并入的原始示教区域；该方法不接管其生命周期。</param>
        private static void UnionRegion(ref HObject aggregate, HObject region)
        {
            HObject merged = null;
            try
            {
                HOperatorSet.Union2(aggregate, region, out merged);
                aggregate?.Dispose();
                aggregate = merged;
                merged = null;
            }
            finally
            {
                merged?.Dispose();
            }
        }

        /// <summary>
        /// 在预览结果上描出包含区与排除区边界，不填充，避免挡住红色模型边缘。
        /// </summary>
        /// <param name="domain">最终建模域，黄色描边。</param>
        private void DrawDomainOutlines(HObject domain)
        {
            try
            {
                DATA.HWindow.HalconWindow.SetDraw("margin");
                DATA.HWindow.HalconWindow.SetLineWidth(2);

                foreach (var item in DATA.Objects)
                {
                    if (item?.Region == null)
                    {
                        continue;
                    }

                    DATA.HWindow.HalconWindow.SetColor(item.IsExclude ? "magenta" : "cyan");
                    DATA.HWindow.HalconWindow.DispObj(item.Region);
                }

                if (domain != null)
                {
                    DATA.HWindow.HalconWindow.SetColor("yellow");
                    DATA.HWindow.HalconWindow.DispObj(domain);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 经操作员确认后删除当前选中的示教区域并释放对应 HALCON Region。
        /// 特征集合变化会使临时模型失效并标记会话待发布，正式 .shm 和 Tool XML 保持当前版本。
        /// </summary>
        public void deletelistitem()
        {
            if (DATA.selectedindex < 0 || DATA.selectedindex >= DATA.Objects.Count)
            {
                MessageBoxX.Show("请先选择需要删除的模板特征", "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return;
            }

            var selected = DATA.Objects[DATA.selectedindex];
            string tip = selected != null && selected.IsExclude
                ? "是否确定删除选中的排除矩形？"
                : "是否确定删除选中的包含特征？";

            if (MessageBoxX.Show(tip, "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == MessageBoxResult.Yes)
            {
                try
                {
                    selected?.Region?.Dispose();
                    DATA.Objects.RemoveAt(DATA.selectedindex);
                }
                catch {; }
            }
        }

        /// <summary>
        /// 将已预览的 HALCON 形状模型发布到工程目录。
        /// 模型先写入同目录临时文件并读回校验，再替换正式 .shm；工程切换、软件异常退出或存储介质写入异常发生在保存过程时，原有正式模型保持可用。
        /// </summary>
        /// <param name="errorMessage">特征、预览模型、目标路径或文件校验异常时返回可供发布事务展示的原因。</param>
        /// <returns>正式 .shm 完成发布返回 true。</returns>
        public bool OutputModel(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!DATA.HasTemplateFeatures)
            {
                errorMessage = "请先完成模板特征圈选，再预览生成模型";
                return false;
            }

            if (DATA.modelID == null || DATA.modelID.Length == 0)
            {
                errorMessage = "当前没有预览成功的临时模型";
                return false;
            }

            string filename = DATA.modelfilename;
            if (string.IsNullOrWhiteSpace(filename))
            {
                errorMessage = "当前工具的模型文件路径为空";
                return false;
            }

            if (!TryWriteShapeModelAtomically(DATA.modelID, filename, out errorMessage))
            {
                return false;
            }

            DATA.modelfilename = filename;
            return true;
        }

        /// <summary>
        /// 将形状模型安全发布到指定文件。
        /// 临时模型通过 HALCON 读回验证后才覆盖正式文件，避免半写入文件在工程重启或切换后被当作有效模板读取。
        /// </summary>
        /// <param name="modelId">预览生成成功的 HALCON 形状模型句柄。</param>
        /// <param name="filename">目标 .shm 完整路径，通常对应当前工程的 Tool 序号。</param>
        /// <param name="errorMessage">保存或验证失败时返回的可追溯原因。</param>
        /// <returns>正式模型完成替换或首次落盘时返回 true。</returns>
        private static bool TryWriteShapeModelAtomically(HTuple modelId, string filename, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (modelId == null || modelId.Length == 0)
            {
                errorMessage = "当前没有可保存的预览模型";
                return false;
            }

            if (string.IsNullOrWhiteSpace(filename))
            {
                errorMessage = "模型文件路径为空";
                return false;
            }

            string targetFile = System.IO.Path.GetFullPath(filename);
            string tempFile = targetFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string directory = System.IO.Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                HOperatorSet.WriteShapeModel(modelId, tempFile);
                if (!TryValidateShapeModelFile(tempFile, out errorMessage))
                {
                    return false;
                }

                if (File.Exists(targetFile))
                {
                    ReplaceExistingShapeModel(tempFile, targetFile);
                }
                else
                {
                    File.Move(tempFile, targetFile);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                TryDeleteFile(tempFile);
            }
        }

        /// <summary>
        /// 验证临时 .shm 是否能由 HALCON 重新读取。
        /// 该校验只使用临时句柄，完成后立即释放，不影响模型设置窗口内用于预览和保存的原始句柄。
        /// </summary>
        /// <param name="filename">待验证的临时模型文件完整路径。</param>
        /// <param name="errorMessage">读取失败时的 HALCON 或文件系统原因。</param>
        /// <returns>文件可被 HALCON 读取且模型句柄有效时返回 true。</returns>
        private static bool TryValidateShapeModelFile(string filename, out string errorMessage)
        {
            errorMessage = string.Empty;
            HTuple validationModelId = null;
            try
            {
                HOperatorSet.ReadShapeModel(filename, out validationModelId);
                if (validationModelId == null || validationModelId.Length == 0)
                {
                    errorMessage = "HALCON 未返回有效模型句柄";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                if (validationModelId != null)
                {
                    try
                    {
                        if (validationModelId.Length > 0)
                        {
                            HOperatorSet.ClearShapeModel(validationModelId);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        validationModelId.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 使用文件系统替换语义发布已校验的模型文件。
        /// 替换过程产生的系统级临时备份只服务本次事务；工程级人工恢复由 AOI 工程快照负责。
        /// </summary>
        /// <param name="sourceFile">已完成 HALCON 读回校验的临时模型文件。</param>
        /// <param name="destinationFile">当前工程内正式生效的 .shm 文件。</param>
        private static void ReplaceExistingShapeModel(string sourceFile, string destinationFile)
        {
            string replaceBackupFile = destinationFile + ".replacebak";
            TryDeleteFile(replaceBackupFile);
            File.Replace(sourceFile, destinationFile, replaceBackupFile);
            TryDeleteFile(replaceBackupFile);
        }

        /// <summary>
        /// 清理形状模型保存过程的临时文件。
        /// 清理失败保留文件供现场诊断，正式模型文件和已发布模型句柄保持当前状态。
        /// </summary>
        /// <param name="filename">待清理的临时文件完整路径。</param>
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
