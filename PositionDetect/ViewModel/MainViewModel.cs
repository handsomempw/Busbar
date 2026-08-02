using GalaSoft.MvvmLight;
using HalconDotNet;
using Panuon.WPF.UI;
using System.Windows.Media;
using System.Windows;
using System;
using System.Diagnostics;
using Microsoft.Win32;
using System.IO;

namespace PositionDetect.ViewModel
{
    /// <summary>
    /// 模板特征圈选、预览生成与模型导出 ViewModel。
    /// 负责模型设置窗口内的包含区/排除矩形示教；在线 FindShapeModel 仍由主工程 ShapeMatch 加载已保存的 .shm。
    /// </summary>
    public class PositionDetectViewModel : ViewModelBase
    {
        public DATA DATA { get; set; } = new DATA();

        public void showtest()
        {
            HTuple hv_width = null, hv_height = null;

            try
            {
                HOperatorSet.GetImageSize(DATA.image, out hv_width, out hv_height);
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
        /// 绘制包含矩形并加入特征列表。
        /// </summary>
        public void drawrectangle()
        {
            DATA.CircleMode = Brushes.White;
            DATA.ExcludeRectangleMode = Brushes.White;
            DATA.RectangleMmode = Brushes.Orange;
            double row1, col1, row2, col2;
            HObject rect = null;
            HOperatorSet.GenEmptyObj(out rect);
            DATA.HWindow.HalconWindow.DrawRectangle1(out row1, out col1, out row2, out col2);
            HOperatorSet.GenRectangle1(out rect, row1, col1, row2, col2);
            DATA.Objects.Add(new TemplateFeatureItem
            {
                ShapeName = "矩形",
                IsExclude = false,
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
            HOperatorSet.GenEmptyObj(out cir);
            DATA.HWindow.HalconWindow.DrawCircle(out row, out col, out radius);
            HOperatorSet.GenCircle(out cir, row, col, radius);
            DATA.Objects.Add(new TemplateFeatureItem
            {
                ShapeName = "圆形",
                IsExclude = false,
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
            HOperatorSet.GenEmptyObj(out rect);
            DATA.HWindow.HalconWindow.DrawRectangle1(out row1, out col1, out row2, out col2);
            HOperatorSet.GenRectangle1(out rect, row1, col1, row2, col2);
            DATA.Objects.Add(new TemplateFeatureItem
            {
                ShapeName = "矩形",
                IsExclude = true,
                Region = rect
            });
            DATA.ExcludeRectangleMode = Brushes.White;
        }

        /// <summary>
        /// 预览包含区减排除区后的最终建模域，供操作员在生成模型前确认保留边缘范围。
        /// 该操作只刷新 HALCON 窗口，不生成或保存形状模型。
        /// </summary>
        public void showreduce()
        {
            HTuple hv_width = null;
            HTuple hv_height = null;
            HObject ReduceImage = null;
            HObject domain = null;

            try
            {
                DATA.HWindow.HalconWindow.ClearWindow();
                HOperatorSet.GetImageSize(DATA.image, out hv_width, out hv_height);

                if (!TryBuildTemplateDomain(out domain, out string domainError))
                {
                    MessageBoxX.Show(domainError, "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                    return;
                }

                HOperatorSet.GenEmptyObj(out ReduceImage);
                HOperatorSet.ReduceDomain(DATA.image, domain, out ReduceImage);
                DATA.HWindow.HalconWindow.SetPart(0, 0, (int)hv_height - 1, (int)hv_width - 1);
                DATA.HWindow.HalconWindow.DispObj(ReduceImage);
            }
            catch
            {; }
            finally
            {
                hv_width?.Dispose();
                hv_height?.Dispose();
                ReduceImage?.Dispose();
                domain?.Dispose();
            }
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

            HObject ho_ModelContours, ho_ContoursAffinTrans;

            HTuple hv_Row = null, hv_Column = null, hv_Angle = null, hv_Score = null;

            HOperatorSet.GenEmptyObj(out ho_ModelContours);
            HOperatorSet.GenEmptyObj(out ho_ContoursAffinTrans);
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

                HOperatorSet.GenEmptyObj(out ReduceImage);
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

                ho_ModelContours.Dispose();
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
                HTuple hv_HomMat2D = new HTuple();
                hv_HomMat2D.Dispose();
                HOperatorSet.VectorAngleToRigid(0, 0, 0, hv_Row, hv_Column, hv_Angle, out hv_HomMat2D);
                ho_ContoursAffinTrans.Dispose();
                HOperatorSet.AffineTransContourXld(ho_ModelContours, out ho_ContoursAffinTrans,
                    hv_HomMat2D);
                DATA.HWindow.HalconWindow.SetColor("red");
                DATA.HWindow.HalconWindow.DispObj(ho_ContoursAffinTrans);
                hv_HomMat2D.Dispose();

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
            if (shapeModelId != null && shapeModelId.Length > 0)
            {
                try
                {
                    HOperatorSet.ClearShapeModel(shapeModelId);
                }
                catch
                {
                    // 清理失败只影响尚未发布的临时句柄，预览失败状态和正式 .shm 文件保持不变。
                }
            }

            shapeModelId = null;
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
        /// <returns>正式 .shm 完成发布返回 true；特征、预览模型、目标路径或文件校验异常时返回 false。</returns>
        public bool OutputModel()
        {
            if (!DATA.HasTemplateFeatures)
            {
                MessageBoxX.Show("请先完成模板特征圈选，再预览生成模型。", "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (DATA.modelID == null || DATA.modelID.Length == 0)
            {
                MessageBoxX.Show("请先预览再保存模型");
                return false;
            }

            string filename = DATA.modelfilename;
            if (string.IsNullOrWhiteSpace(filename))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "*.shm|*.shm";
                if (saveFileDialog.ShowDialog() != true)
                {
                    return false;
                }

                filename = saveFileDialog.FileName;
            }

            string errorMessage;
            if (!TryWriteShapeModelAtomically(DATA.modelID, filename, out errorMessage))
            {
                MessageBoxX.Show($"模型文件保存失败：{errorMessage}", "提示", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return false;
            }

            DATA.modelfilename = filename;
            NoticeBox.Show($"{filename}", "模型导出成功", MessageBoxIcon.Success, true, 3000);
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
                if (validationModelId != null && validationModelId.Length > 0)
                {
                    try
                    {
                        HOperatorSet.ClearShapeModel(validationModelId);
                    }
                    catch
                    {
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
