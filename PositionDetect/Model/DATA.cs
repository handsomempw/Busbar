using GalaSoft.MvvmLight;
using HalconDotNet;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;

namespace PositionDetect
{
    public class DATA : ObservableObject
    {
        public DATA()
        {
            Objects.CollectionChanged += Objects_CollectionChanged;
        }

        /// <summary>
        /// 当前模型设置会话独立持有的示教图副本。
        /// 特征坐标、预览模型和已发布轮廓都使用该图的原始 px 坐标；窗口关闭时统一释放。
        /// </summary>
        public HObject image = null;

        /// <summary>
        /// 模型设置窗口内的 HALCON 显示控件，只承载示教交互和预览，不参与在线检测显示。
        /// </summary>
        public HWindowControlWPF HWindow { get; set; }
        //public HSmartWindowControlWPF HWindow { get; set; }

        private bool _ROImode = false;

        /// <summary>
        /// 当前会话是否接受新的特征圈选。
        /// 暂停状态保留已恢复和已绘制区域，并继续允许预览及发布；该值不写入 Tool XML。
        /// </summary>
        public bool ROImode
        {
            set
            {
                _ROImode = value;
                RaisePropertyChanged(() => ROImode);
                RaisePropertyChanged(() => ROIModeStr);
                RaisePropertyChanged(() => CanPreviewModel);
                RaisePropertyChanged(() => CanSaveModel);
            }
            get { return _ROImode; }
        }
        public string ROIModeStr
        {
            get
            {
                if (_ROImode)
                {
                    return "暂停圈选";
                }

                return HasAnyTemplateFeatures ? "继续编辑特征" : "开始圈选特征";
            }
        }


        /// <summary>
        /// 模板特征列表：包含区与排除矩形共用同一列表，便于选中、删除与可视化。
        /// </summary>
        public ObservableCollection<TemplateFeatureItem> Objects { set; get; } = new ObservableCollection<TemplateFeatureItem>();

        /// <summary>
        /// 会话恢复或关闭释放区域时暂停脏状态跟踪，避免资源生命周期动作被识别为操作员修改。
        /// 只由 PositionDetectViewModel 在批量重建和释放 HALCON Region 时使用。
        /// </summary>
        internal bool SuppressTemplateRecipeChangeTracking { get; set; }

        private bool _isTemplateRecipeDirty;

        /// <summary>
        /// 当前模型设置会话是否包含尚未发布的特征增删。
        /// 该状态只控制退出确认和状态提示，不写入工程 XML，也不改变已发布 .shm 或在线判定。
        /// </summary>
        [XmlIgnore]
        public bool IsTemplateRecipeDirty
        {
            get { return _isTemplateRecipeDirty; }
            set
            {
                if (_isTemplateRecipeDirty != value)
                {
                    _isTemplateRecipeDirty = value;
                    RaisePropertyChanged(() => IsTemplateRecipeDirty);
                }
            }
        }

        /// <summary>
        /// 特征列表当前选中项，用于红色强调和删除目标定位；-1 表示当前没有选中区域。
        /// </summary>
        public int selectedindex { set; get; } = -1;
        [XmlIgnore]
        public SolidColorBrush CircleMode { set; get; } = Brushes.White;
        [XmlIgnore]
        public SolidColorBrush RectangleMmode { set; get; } = Brushes.White;
        [XmlIgnore]
        public SolidColorBrush ExcludeRectangleMode { set; get; } = Brushes.White;

        /// <summary>
        /// 最近一次预览生成的诊断摘要（分值、角度、耗时或失败原因），供模型设置状态栏展示。
        /// 只存在于当前示教会话，不写入工程 XML。
        /// </summary>
        [XmlIgnore]
        public string LastPreviewSummary { set; get; } = string.Empty;

        /// <summary>
        /// 预览匹配使用的允许角度偏差 ±δ（deg），与当前工具 AllowAngleDelta 对齐。
        /// 仅影响模型设置窗口内的示教预览，不写入工程 XML。
        /// </summary>
        [XmlIgnore]
        public double PreviewAllowAngleDelta { set; get; } = 20;

        /// <summary>
        /// 预览匹配使用的候选搜索下限，与当前工具 CandidateMinScore 对齐。
        /// </summary>
        [XmlIgnore]
        public double PreviewCandidateMinScore { set; get; } = 0.5;

        /// <summary>
        /// 预览保存门禁使用的合格分值下限，与当前工具 MinScore 对齐。
        /// 示教图匹配分值低于该值时不允许发布模型。
        /// </summary>
        [XmlIgnore]
        public double PreviewMinScore { set; get; } = 0.8;

        /// <summary>
        /// 模型设置预览使用的 PositionROI 坐标，单位为 px。
        /// 打开预览前由当前工具同步，保证预览、设基准与在线匹配在同一搜索区域内形成候选；该值不写入工程 XML。
        /// </summary>
        [XmlIgnore]
        public int PreviewRoiRow1 { set; get; }

        /// <summary>预览 PositionROI 起始列，单位 px；仅存在于当前示教会话。</summary>
        [XmlIgnore]
        public int PreviewRoiCol1 { set; get; }

        /// <summary>预览 PositionROI 结束行，单位 px；仅存在于当前示教会话。</summary>
        [XmlIgnore]
        public int PreviewRoiRow2 { set; get; }

        /// <summary>预览 PositionROI 结束列，单位 px；仅存在于当前示教会话。</summary>
        [XmlIgnore]
        public int PreviewRoiCol2 { set; get; }

        /// <summary>
        /// 是否已有至少一个包含区特征。
        /// 排除区可选；没有包含区时不允许预览与保存。
        /// </summary>
        public bool HasTemplateFeatures
        {
            get { return Objects != null && Objects.Any(o => o != null && !o.IsExclude && o.Region != null); }
        }

        /// <summary>
        /// 特征列表中是否存在任意包含区或排除区，供“重新示教”入口控制启用状态。
        /// </summary>
        public bool HasAnyTemplateFeatures
        {
            get { return Objects != null && Objects.Count > 0; }
        }

        /// <summary>
        /// 模型预览入口状态。操作员完成至少一个包含区特征后才允许生成临时模型。
        /// </summary>
        public bool CanPreviewModel
        {
            get { return HasTemplateFeatures; }
        }

        /// <summary>
        /// 模型文件保存入口状态。保存只面向当前已预览成功的临时模型，特征列表变化后需重新预览。
        /// </summary>
        public bool CanSaveModel
        {
            get { return HasTemplateFeatures && modelID != null; }
        }

        private HTuple _modelID = null;
        /// <summary>
        /// 当前示教会话中的临时形状模型句柄。
        /// 赋值替换或置空时释放旧句柄，避免反复预览与增删排除区时 HALCON 模型资源滞留。
        /// </summary>
        public HTuple modelID
        {
            set
            {
                if (!ReferenceEquals(_modelID, value))
                {
                    ClearShapeModelHandle(_modelID);
                    _modelID = value;
                    RaisePropertyChanged(() => modelID);
                    RaisePropertyChanged(() => CanSaveModel);
                }
            }
            get { return _modelID; }
        }


        /// <summary>
        /// 当前 Tool 正式 .shm 的完整路径。
        /// “保存并发布”始终写入该固定工程路径，避免模型文件与 Tool 序号脱离。
        /// </summary>
        public string modelfilename { set; get; } = string.Empty;

        private void Objects_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            modelID = null;
            LastPreviewSummary = string.Empty;
            if (!SuppressTemplateRecipeChangeTracking)
            {
                IsTemplateRecipeDirty = true;
            }
            RaisePropertyChanged(() => HasTemplateFeatures);
            RaisePropertyChanged(() => HasAnyTemplateFeatures);
            RaisePropertyChanged(() => ROIModeStr);
            RaisePropertyChanged(() => CanPreviewModel);
            RaisePropertyChanged(() => CanSaveModel);
        }

        /// <summary>
        /// 释放示教临时形状模型句柄。特征变更、预览失败或窗口关闭导致模型作废时调用。
        /// </summary>
        /// <param name="shapeModelId">待释放的 HALCON 形状模型句柄。</param>
        private static void ClearShapeModelHandle(HTuple shapeModelId)
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
                // 清理失败只影响已作废的预览句柄，窗口关闭和重新圈选继续完成，正式 .shm 文件保持不变。
            }
            finally
            {
                shapeModelId.Dispose();
            }
        }

    }
}
