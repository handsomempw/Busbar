using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Xml.Serialization;

namespace PositionDetect
{
    public class DATA : ObservableObject
    {
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
            }
            get { return _ROImode; }
        }
        public string ROIModeStr
        {
            get { return _ROImode ? "停止圈选模板特征" : "1 开始圈选模板特征"; }
        }


        public ObservableCollection<HObject> Objects { set; get; } = new ObservableCollection<HObject>();
        public HObject eraseobject { set; get; } = null;

        public int selectedindex { set; get; } = -1;
        [XmlIgnore]
        public SolidColorBrush CircleMode { set; get; } = Brushes.White;
        [XmlIgnore]
        public SolidColorBrush RectangleMmode { set; get; } = Brushes.White;

        public HTuple modelID { set; get; } = null;


        public string modelfilename { set; get; } = string.Empty;

    }
}
