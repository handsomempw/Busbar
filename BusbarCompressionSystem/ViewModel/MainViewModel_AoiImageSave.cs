using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel
    {
        /// <summary>
        /// 当前帧的后台编码、写盘任务。UI 线程在下一帧处理前等待它结束，内存中最多保留一批待保存图片。
        /// </summary>
        private Task _aoiImageSaveTask;

        /// <summary>
        /// 标准窗口退出时关闭 AOI 图像处理入口，随后等待当前存图任务释放图片。
        /// </summary>
        private bool _aoiImageSavingStopped;

        /// <summary>
        /// 串行写入 AOI 存图诊断文件；后台记录直接落盘，UI 等待存图时仍可完成错误记录。
        /// </summary>
        private readonly object _aoiImageSaveLogLock = new object();

        /// <summary>
        /// 一次拍照需要归档的图片。原图副本由本批独立持有，各工具保存已绘制完成的标注图和固定路径。
        /// </summary>
        private sealed class AoiImageSaveBatch : IDisposable
        {
            /// <summary>本次拍照指令，用于关联后台存图耗时。</summary>
            public string Command;

            /// <summary>本帧产品 SN，用于关联连续 A 指令的保存耗时。</summary>
            public string ProductSn;

            /// <summary>本帧自有原图副本；各工具沿用各自的原图路径，整批结束后统一释放。</summary>
            public HObject OriginalImage;

            /// <summary>按工具顺序保存的成品图片；交给后台后由保存任务独占。</summary>
            public readonly List<AoiPreparedImage> Images = new List<AoiPreparedImage>();

            /// <summary>在保存完成或准备中止后释放本帧原图与全部标注图。</summary>
            public void Dispose()
            {
                foreach (var image in Images)
                {
                    image.Dispose();
                }
                OriginalImage?.Dispose();
            }
        }

        /// <summary>
        /// 单个工具的成品标注图。文字绘制失败时保存 HALCON ROI 图，图层准备失败时使用本批原图。
        /// </summary>
        private sealed class AoiPreparedImage : IDisposable
        {
            /// <summary>沿用现有规格、结果分类和工具文件名的原图完整路径。</summary>
            public string OriginalPath;

            /// <summary>沿用现有标注图目录和工具文件名的完整路径。</summary>
            public string AnnotatedPath;

            /// <summary>包含本次 ROI、测量值和判定文字的独立位图。</summary>
            public Bitmap AnnotatedBitmap;

            /// <summary>位图或文字准备失败时使用的 HALCON ROI 图；为空时在标注路径保存原图。</summary>
            public HObject OverlayImage;

            /// <summary>释放当前工具的标注资源，原图生命周期由整批归档管理。</summary>
            public void Dispose()
            {
                AnnotatedBitmap?.Dispose();
                OverlayImage?.Dispose();
            }
        }

        /// <summary>
        /// 在下一帧计算或正常退出前等待上一批存图结束。保存失败由后台记录，结束后的下一帧继续检测。
        /// UI 线程是本任务字段的唯一调度方，后台只访问已准备好的图片和路径。
        /// </summary>
        /// <param name="reason">等待发生的位置，用于区分下一帧等待与软件退出等待。</param>
        private void WaitForAoiImageSave(string reason)
        {
            Task task = _aoiImageSaveTask;
            if (task == null)
            {
                return;
            }

            bool wasPending = !task.IsCompleted;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogAoiImageSave($"后台任务异常：{ex}", true);
            }
            finally
            {
                _aoiImageSaveTask = null;
                if (wasPending)
                {
                    LogAoiImageSave($"{reason}，等待上一批结束={stopwatch.ElapsedMilliseconds}ms");
                }
            }
        }

        /// <summary>
        /// 接管当前帧已准备好的图片，在后台顺序编码和写盘。调用方在任务接管后发送本指令机器人结果。
        /// 调度失败时同步完成本批归档，保持图片的释放责任与现有失败继续口径。
        /// </summary>
        /// <param name="batch">当前帧原图副本、成品标注图和固定路径；所有权交给保存任务。</param>
        private void StartAoiImageSave(AoiImageSaveBatch batch)
        {
            if (batch.Images.Count == 0)
            {
                batch.Dispose();
                return;
            }

            try
            {
                _aoiImageSaveTask = Task.Run(() => SaveAoiImageBatch(batch));
            }
            catch (Exception ex)
            {
                LogAoiImageSave($"后台调度失败，转为同步存图：{ex.Message}", true);
                SaveAoiImageBatch(batch);
            }
        }

        /// <summary>
        /// 保存本批原图及标注图，每个文件独立记录失败，后续文件继续保存。整批结束后释放所有图片。
        /// </summary>
        /// <param name="batch">保存任务独占的当前帧数据，包含全部文件路径。</param>
        private void SaveAoiImageBatch(AoiImageSaveBatch batch)
        {
            var stopwatch = Stopwatch.StartNew();
            int saved = 0;
            int failed = 0;
            try
            {
                foreach (var image in batch.Images)
                {
                    if (TrySaveAoiImageFile(image.OriginalPath,
                        () => HOperatorSet.WriteImage(batch.OriginalImage, "jpg", 0, image.OriginalPath)))
                        saved++;
                    else
                        failed++;

                    if (TrySaveAoiImageFile(image.AnnotatedPath, () =>
                    {
                        if (image.AnnotatedBitmap != null)
                            image.AnnotatedBitmap.Save(image.AnnotatedPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                        else
                            HOperatorSet.WriteImage(image.OverlayImage ?? batch.OriginalImage, "jpg", 0, image.AnnotatedPath);
                    }))
                        saved++;
                    else
                        failed++;
                }
            }
            finally
            {
                batch.Dispose();
                LogAoiImageSave($"SN={batch.ProductSn}，指令={batch.Command}，工具数={batch.Images.Count}，保存成功={saved}，失败={failed}，后台编码写盘={stopwatch.ElapsedMilliseconds}ms");
            }
        }

        /// <summary>为一个归档文件建立目录并保存；异常仅标记该文件失败，产品检测结果沿用视觉判定。</summary>
        /// <param name="path">准备图片时确定的完整路径，供现场按 SN 和工具定位保存失败。</param>
        /// <param name="save">只访问本批图片的编码写盘操作。</param>
        /// <returns>true 表示文件保存完成；false 表示已记录失败并继续处理后续图片。</returns>
        private bool TrySaveAoiImageFile(string path, Action save)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                save();
                return true;
            }
            catch (Exception ex)
            {
                LogAoiImageSave($"保存失败：{path}，{ex.Message}", true);
                return false;
            }
        }

        /// <summary>标准窗口正常退出时停止接收 AOI 处理并收尾当前存图，适用软件既有的退出时限。</summary>
        private void StopAoiImageSaving()
        {
            _aoiImageSavingStopped = true;
            WaitForAoiImageSave("软件退出");
        }

        /// <summary>
        /// 将编码写盘耗时和失败路径写入“日志/AOI存图”。后台日志全程独立于 UI 等待；错误提示异步投递到界面。
        /// </summary>
        /// <param name="message">包含指令、完整图片路径或毫秒耗时的诊断内容。</param>
        /// <param name="isError">文件保存或任务异常时为 true，同时投递操作日志。</param>
        private void LogAoiImageSave(string message, bool isError = false)
        {
            try
            {
                lock (_aoiImageSaveLogLock)
                {
                    DateTime now = DateTime.Now;
                    string directory = Path.Combine(Environment.CurrentDirectory, "日志", "AOI存图");
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, $"{now:yyyyMMdd}.txt"),
                        $"[{now:yyyy-MM-dd HH:mm:ss.fff}][AOI存图] {message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[AOI存图] {message}；诊断落盘失败：{ex.Message}");
            }

            if (isError)
            {
                try
                {
                    var dispatcher = App.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                        dispatcher.BeginInvoke(new Action(() => writeLog($"[AOI存图] {message}", true)));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[AOI存图] 界面提示失败：{ex.Message}");
                }
            }
        }
    }
}
