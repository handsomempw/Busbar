using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public HObject image = null;
        public HWindowControlWPF HWindow { get; set; }
        //public HSmartWindowControlWPF HWindow { get; set; }

        private bool _ROImode = false;
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
            get { return _ROImode ? "圈选中：右键完成当前特征" : "1 开始圈选模板特征"; }
        }


        public ObservableCollection<HObject> Objects { set; get; } = new ObservableCollection<HObject>();
        public HObject eraseobject { set; get; } = null;

        public int selectedindex { set; get; } = -1;
        [XmlIgnore]
        public SolidColorBrush CircleMode { set; get; } = Brushes.White;
        [XmlIgnore]
        public SolidColorBrush RectangleMmode { set; get; } = Brushes.White;

        /// <summary>
        /// 模板特征是否已经由 HALCON 图像窗口确认完成。
        /// 该状态只影响模型预览与保存入口，不写入工程 XML，也不改变在线检测搜索 ROI。
        /// </summary>
        public bool HasTemplateFeatures
        {
            get { return Objects != null && Objects.Count > 0; }
        }

        /// <summary>
        /// 模型预览入口状态。操作员完成至少一个矩形或圆形特征后才允许生成临时模型。
        /// </summary>
        public bool CanPreviewModel
        {
            get { return ROImode && HasTemplateFeatures; }
        }

        /// <summary>
        /// 模型文件保存入口状态。保存只面向当前已预览成功的临时模型，特征列表变化后重新预览。
        /// </summary>
        public bool CanSaveModel
        {
            get { return ROImode && HasTemplateFeatures && modelID != null; }
        }

        private HTuple _modelID = null;
        public HTuple modelID
        {
            set
            {
                _modelID = value;
                RaisePropertyChanged(() => modelID);
                RaisePropertyChanged(() => CanSaveModel);
            }
            get { return _modelID; }
        }


        public string modelfilename { set; get; } = string.Empty;

        private void Objects_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            modelID = null;
            RaisePropertyChanged(() => HasTemplateFeatures);
            RaisePropertyChanged(() => CanPreviewModel);
            RaisePropertyChanged(() => CanSaveModel);
        }

    }
}
