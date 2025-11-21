// ==========================================
// 文件: MainViewModel_FaraVision.cs
// 描述: FaraVision 相关视图模型，负责工程管理、工具增删改、
//       图像/模型读写以及基于 HALCON 的面积计算等核心逻辑。
// ==========================================
using BusbarCompressionSystem.Model.FaraVision.Tool;
using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows;
using BusbarCompressionSystem.Model.FaraVision;
using System.IO;
using Panuon.WPF.UI;
using System.Xml.Serialization;
using GalaSoft.MvvmLight.Command;
using BusbarCompressionSystem.FaraVision;
using System.Runtime.InteropServices; // 用于GDI句柄管理

namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        /// <summary>
        /// 用于释放GDI句柄，防止句柄泄漏
        /// </summary>
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// 初始化界面命令绑定（RelayCommand）。
        /// </summary>
        public void Initrelaycommand()
        {
            DeleteToolCMD = new RelayCommand(DeleteTool);
            AddToolCMD = new RelayCommand(AddTool);
            ClearToolCMD = new RelayCommand(ClearTool);
            CopyToolCMD = new RelayCommand(CopyTool);
            InsertToolCMD = new RelayCommand(InsertTool);

            NEW_PRJCMD = new RelayCommand(NEW_PRJCMD_process);
            SAVE_PRJCMD = new RelayCommand(SAVE_PRJ_process);
        }



        /// <summary>
        /// 初始化 HALCON 显示窗口，使所有工具共享相同的 HWindow。
        /// </summary>
        /// <param name="_HWindow">HALCON 的 HWindow 对象</param>
        public void InitHwindow(HWindow _HWindow)
        {
            DataModel.FaraVisionDataModel.Settingmodel.HWindow = _HWindow;
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                DataModel.FaraVisionDataModel.Processmodel.Tools[i].Reg.Hwindow = _HWindow;
                DataModel.FaraVisionDataModel.Processmodel.Tools[i].ShapeMatch.HWindow = _HWindow;
            }
        }
        /// <summary>
        /// 初始化模板匹配（shm）文件，若缺失或加载失败会记录日志。
        /// </summary>
        public void InitShm()
        {
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                if (DataModel.FaraVisionDataModel.Processmodel.Tools[i].TestMode == TestModes.模板匹配)
                {
                    try
                    {
                        string shmfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{DataModel.FaraVisionDataModel.Processmodel.Tools[i].Index}.shm";
                        if (File.Exists(shmfilename))
                        {
                            DataModel.FaraVisionDataModel.Processmodel.Tools[i].ShapeMatch.init(shmfilename);
                        }
                        else
                        {
                            writeLog($"模型文件不存在:{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{DataModel.FaraVisionDataModel.Processmodel.Tools[i].Index}.shm");
                        }
                    }
                    catch (Exception ex)
                    {
                        writeLog($"模型文件加载失败:{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{DataModel.FaraVisionDataModel.Processmodel.Tools[i].Index}.shm;{ex.Message}");
                        continue;
                    }
                }
            }
        }
        /// <summary>
        /// 工程校验
        /// </summary>
        public void CheckPrj()
        {

            try
            {
                bool checkprj = false;
                DataModel.FaraVisionDataModel.Settingmodel.Prjs.Clear();
                if (!Directory.Exists(DataModel.FaraVisionDataModel.Settingmodel.Prjdir))
                {
                    Directory.CreateDirectory(DataModel.FaraVisionDataModel.Settingmodel.Prjdir);
                }
                string[] dirs = Directory.GetDirectories(DataModel.FaraVisionDataModel.Settingmodel.Prjdir);

                for (int i = 0; i < dirs.Length; i++)
                {
                    DirectoryInfo di = new DirectoryInfo(dirs[i]);
                    DataModel.FaraVisionDataModel.Settingmodel.Prjs.Add(di.Name);
                    if (di.Name.ToUpper() == DataModel.FaraVisionDataModel.Settingmodel.Name.ToUpper())
                    {
                        checkprj = true;
                        //break;
                    }
                }

                if (checkprj)
                {
                    NoticeBox.Show($"工程校验成功:{DataModel.FaraVisionDataModel.Settingmodel.Name}", "成功", MessageBoxIcon.Success, true, 5000);
                }
                else
                {
                    NoticeBox.Show($"工程校验失败:{DataModel.FaraVisionDataModel.Settingmodel.Name}", "失败", MessageBoxIcon.Error, true, 5000);
                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"工程目录加载失败:{ex.Message}", "失败", MessageBoxIcon.Error, true, 5000);

            }
        }

        /// <summary>
        /// 刷新工程选择项
        /// </summary>
        public void Prjs_selectedindex()
        {
            for (int i = 0; i < DataModel.FaraVisionDataModel.Settingmodel.Prjs.Count; i++)
            {
                if (DataModel.FaraVisionDataModel.Settingmodel.Prjs[i] == DataModel.FaraVisionDataModel.Settingmodel.Name)
                {
                    DataModel.FaraVisionDataModel.Settingmodel.prjselected = i;
                    break;
                }
            }
        }


        public void Load_Prj()
        {
            LoadPrjXmls();
            CheckPrj();
            Prjs_selectedindex();
            //ClearToolStatus();
        }
        public void Load_Prj(string PrjName)
        {
            DataModel.FaraVisionDataModel.Settingmodel.Name = PrjName;
            Load_Prj();
        }

        /// <summary>
        /// 从工程目录读取 Tool*.xml 构建工具列表，同时加载对应的示教图像缩略图显示。
        /// </summary>
        public void LoadPrjXmls()
        {

            DataModel.FaraVisionDataModel.Processmodel.Tools.Clear();
            string dir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}";
            if (!Directory.Exists(dir))
            {
                return;
            }
            string[] toolfilenames = Directory.GetFiles(dir);
            int count = 0;
            for (int i = 0; i < toolfilenames.Length; i++)
            {
                FileInfo fi = new FileInfo(toolfilenames[i]);
                if (fi.Extension.ToUpper() == ".XML")
                {
                    count++;
                }
            }
            int index = 0;
            for (int k = 0; k < count; k++)
            {
                try
                {
                    var tool = LoadPrjXml(k + 1);
                    if (tool != null)
                    {
                        tool.Index = (index++) + 1;
                        DataModel.FaraVisionDataModel.Processmodel.Tools.Add(tool);
                        string jpgfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index}.jpg";
                        #region 加载模型图片
                        tool.Image?.Dispose();
                        HOperatorSet.GenEmptyObj(out tool.Image);
                        HOperatorSet.ReadImage(out tool.Image, jpgfilename);
                        #endregion


                        Bitmap bmp;
                        Bitmap src;
                        //HObject Image;
                        //HOperatorSet.GenEmptyObj(out Image);

                        try
                        {
                            //HOperatorSet.ReadImage(out Image, filename);
                            var dst = GetReducedImage(DataModel.FaraVisionDataModel.Settingmodel.ImageSize, DataModel.FaraVisionDataModel.Settingmodel.ImageSize, tool.Image);
                            //Hobject2Bitmap.HobjectToBitmap(tool.Image, out src);
                            Hobject2Bitmap.HobjectToBitmap24(dst, out bmp);

                            // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                            IntPtr hBitmap1 = bmp.GetHbitmap();
                            try
                            {
                                tool.CurrentBitmapSource = null;
                                tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap1, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            }
                            finally
                            {
                                DeleteObject(hBitmap1); // 释放GDI句柄
                            }

                            IntPtr hBitmap2 = bmp.GetHbitmap();
                            try
                            {
                                tool.BitmapSource = null;
                                tool.BitmapSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap2, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            }
                            finally
                            {
                                DeleteObject(hBitmap2); // 释放GDI句柄
                            }

                            bmp?.Dispose();
                            dst?.Dispose();
                            // src?.Dispose();
                        }
                        catch (Exception ex)
                        {; }




                        //using (Bitmap bmp = (Bitmap)Bitmap.FromFile(jpgfilename))
                        //{
                        //    using (Bitmap bmp1 = GetReducedImage(DataModel.Settingmodel.ImageSize, DataModel.Settingmodel.ImageSize, bmp))
                        //    {
                        //        BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(bmp1.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        //        tool.BitmapSource = null;
                        //        tool.CurrentBitmapSource = null;

                        //        tool.BitmapSource = bs;
                        //        tool.CurrentBitmapSource = bs.Clone();
                        //    }
                        //}
                    }
                    GC.Collect();
                }
                catch (Exception ex)
                {

                }
            }

            //for (int i = 0; i < DataModel.Processmodel.Tools.Count; i++)
            //{
            //    DataModel.Processmodel.Tools[i].Index = i + 1;
            //}
        }

        /// <summary>
        /// 读取指定序号的 Tool 配置 XML。
        /// </summary>
        /// <param name="index">工具序号(1-based)</param>
        /// <returns>反序列化的 ToolModel，失败则返回 null</returns>
        private ToolModel LoadPrjXml(int index)
        {
            string dir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}";
            string filename = $"{dir}\\Tool{index}.xml";

            if (File.Exists(filename))
            {
                using (var stream = File.OpenRead(filename))
                {
                    try
                    {
                        var serializer = new XmlSerializer(typeof(ToolModel));
                        var r = serializer.Deserialize(stream) as ToolModel;
                        return (ToolModel)r;
                    }
                    catch {; }
                }
            }
            return null;
        }

        public KColor GetPixelData(int X, int Y)
        {
            try
            {
                int w = DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.PixelWidth;
                int h = DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.PixelHeight;
                int stride = (w * DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.Format.BitsPerPixel + 7) / 8;

                byte[] pixels = new byte[h * stride];
                DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.CopyPixels(pixels, stride, 0);

                int pixelindex = Y * stride + X * (DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource.Format.BitsPerPixel / 8);
                byte r = pixels[pixelindex + 2];
                byte g = pixels[pixelindex + 1];
                byte b = pixels[pixelindex + 0];

                return new KColor() { R = r, G = g, B = b };
            }catch {  return null; }
        }



        public void SavePrjXmls()
        {
            try
            {
                #region 删除旧xml文件
                foreach (string s in Directory.GetFiles($"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}"))
                {
                    if (s.ToUpper().EndsWith(".XML"))
                    {
                        try
                        {
                            File.Delete(s);
                        }
                        catch (Exception ex) { continue; }
                    }
                }
                #endregion
                for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
                {
                    SavePrjXml(DataModel.FaraVisionDataModel.Processmodel.Tools[i], i + 1);
                }
            }
            catch (Exception ex) {; }
        }

        private void SavePrjXml(ToolModel td, int index)
        {
            string filename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index}.xml";
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var stream = File.Open(filename, FileMode.Create))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(ToolModel));
                    serializer.Serialize(stream, td);
                    return;
                }
                catch {; }
            }
        }

        public bool LoadBitmapSource()
        {

            try
            {
                string jpgfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{DataModel.FaraVisionDataModel.Processmodel.selectedindex + 1}.jpg";
                using (Bitmap bmp = (Bitmap)Bitmap.FromFile(jpgfilename))
                {
                    // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = bs;
                    }
                    finally
                    {
                        DeleteObject(hBitmap); // 释放GDI句柄
                    }
                }
                GC.Collect();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }

            GC.Collect();


        }

        #region Command
        public RelayCommand DeleteToolCMD { set; get; } = null;
        public RelayCommand AddToolCMD { set; get; } = null;
        public RelayCommand ClearToolCMD { set; get; } = null;
        public RelayCommand CopyToolCMD { set; get; } = null;
        public RelayCommand InsertToolCMD { set; get; } = null;


        public void DeleteTool()
        {
            int index = DataModel.FaraVisionDataModel.Processmodel.selectedindex;
            DeleteTool(index);

        }
        /// <summary>
        /// 删除指定索引的工具，并同步删除对应 jpg/xml/shm 文件。
        /// 若其后仍有工具，则将后续文件整体前移保持序号连续。
        /// </summary>
        public void DeleteTool(int index)
        {
            try
            {
                if (index < 0)
                {

                    NoticeBox.Show($"请先选择需要删除的工具", "失败", MessageBoxIcon.Error, true, 5000);
                    return;
                }
                if (MessageBoxX.Show("是否确定删除工具？", "提示", System.Windows.MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == System.Windows.MessageBoxResult.Yes)
                {

                    string jpgsrc1 = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index + 1}.jpg";
                    string xmlsrc1 = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index + 1}.xml";
                    string shmsrc1 = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{index + 1}.shm";

                    DeleteFile(jpgsrc1);
                    DeleteFile(xmlsrc1);
                    DeleteFile(shmsrc1);

                    if (index != DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1)
                    {

                        // 将后续工具的 Tool{i+2}.xxx 前移到 Tool{i+1}.xxx
                        for (int i = index; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1; i++)
                        {
                            string jpgsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.jpg";
                            string xmlsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.xml";
                            string shmsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.shm";

                            string jpgdst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.jpg";
                            string xmldst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.xml";
                            string shmdst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.shm";

                            MoveFile(jpgdst, jpgsrc);
                            MoveFile(xmldst, xmlsrc);
                            MoveFile(shmdst, shmsrc);


                        }
                    }
                    if (index >= 0)
                    {
                        DataModel.FaraVisionDataModel.Processmodel.Tools.RemoveAt(index);
                    }
                    autoindex();
                }
            }
            catch (Exception ex)
            {
            }
        }


        public void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }




        public void AddTool()
        {
            int index = DataModel.FaraVisionDataModel.Processmodel.Tools.Count;
            DataModel.FaraVisionDataModel.Processmodel.selectedindex = 0;
            InsertTool(index);

        }
        /// <summary>
        /// 在指定位置插入工具，同时从后向前移动文件避免覆盖。
        /// </summary>
        public void InsertTool(int index, ToolModel tool = null)
        {
            try
            {

                if (DataModel.FaraVisionDataModel.Processmodel.selectedindex < 0)
                {

                    NoticeBox.Show($"请先选择需要插入工具的位置", "失败", MessageBoxIcon.Error, true, 5000);
                    return;
                }

                // 从后向前移动 Tool 文件，避免文件名冲突覆盖
                for (int i = DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1; i >= index; i--)
                {
                    string jpgsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.jpg";
                    string xmlsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.xml";
                    string shmsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.shm";

                    string jpgdst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.jpg";
                    string xmldst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.xml";
                    string shmdst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.shm";

                    MoveFile(jpgsrc, jpgdst);
                    MoveFile(xmlsrc, xmldst);
                    MoveFile(shmsrc, shmdst);


                }

                if (tool == null)
                {
                    tool = new ToolModel();
                }
                DataModel.FaraVisionDataModel.Processmodel.Tools.Insert(index, tool);
                DataModel.FaraVisionDataModel.Processmodel.selectedindex = index;
                autoindex();

            }
            catch (Exception ex) { }
        }

        public void MoveFile(string srcfile, string dstfilename)
        {
            if (File.Exists(srcfile))
            {
                File.Move(srcfile, dstfilename);
            }
        }


        public void ClearTool()
        {
            try
            {
                if (MessageBoxX.Show("是否确定清空所有工具？", "提示", System.Windows.MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == System.Windows.MessageBoxResult.Yes)
                {
                    DataModel.FaraVisionDataModel.Processmodel.Tools.Clear();
                    #region 删除对应目录下面的工具文件
                    string dir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}";
                    var files = Directory.GetFiles(dir);
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            //if (files[i].ToUpper().EndsWith(".XML"))
                            //{
                            //    File.Delete(files[i]);
                            //}

                            File.Delete(files[i]);
                        }
                        catch (Exception ex) { continue; }
                        #endregion
                    }
                }
            }
            catch (Exception ex) {; }
        }
        public void CopyTool()
        {
            try
            {
                if (DataModel.FaraVisionDataModel.Processmodel.selectedindex < 0)
                {

                    NoticeBox.Show($"请先选择需要复制的工具", "失败", MessageBoxIcon.Error, true, 5000);
                    return;
                }

                if (MessageBoxX.Show("是否确定复制选中的工具？", "提示", System.Windows.MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == System.Windows.MessageBoxResult.Yes)
                {

                    CopyTool(DataModel.FaraVisionDataModel.Processmodel.selectedindex + 1, New_Tool_Model(DataModel.FaraVisionDataModel.Processmodel.Tools[DataModel.FaraVisionDataModel.Processmodel.selectedindex]));



                    autoindex();
                }
            }
            catch (Exception ex) { }
        }
        /// <summary>
        /// 在指定位置插入拷贝的工具，并自后向前复制文件避免覆盖。
        /// </summary>
        public void CopyTool(int index, ToolModel tool)
        {
            try
            {

                if (DataModel.FaraVisionDataModel.Processmodel.selectedindex < 0)
                {

                    NoticeBox.Show($"请先选择需要插入工具的位置", "失败", MessageBoxIcon.Error, true, 5000);
                    return;
                }

                // 从后向前复制，避免目标文件被覆盖
                for (int i = DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1; i >= index - 1; i--)
                {
                    string jpgsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.jpg";
                    string xmlsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.xml";
                    string shmsrc = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 1}.shm";

                    string jpgdst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.jpg";
                    string xmldst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.xml";
                    string shmdst = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{i + 2}.shm";

                    CopyFile(jpgsrc, jpgdst);
                    CopyFile(xmlsrc, xmldst);
                    CopyFile(shmsrc, shmdst);


                }

                DataModel.FaraVisionDataModel.Processmodel.Tools.Insert(index, tool);
                DataModel.FaraVisionDataModel.Processmodel.selectedindex = index;
                autoindex();

            }
            catch (Exception ex) { }
        }

        public void CopyFile(string srcfile, string dstfilename)
        {
            if (File.Exists(srcfile))
            {
                File.Copy(srcfile, dstfilename, true);
            }
        }

        public ROI New_ROI(ROI roi)
        {
            ROI r = new ROI();
            r.Row1 = roi.Row1;
            r.Row2 = roi.Row2;
            r.Col1 = roi.Col1;
            r.Col2 = roi.Col2;
            return r;

        }

        public ToolModel New_Tool_Model(ToolModel tool)
        {
            ToolModel t = new ToolModel();
            t.Index = tool.Index;
            t.Command = tool.Command;
            t.Name = tool.Name;
            //t.DecodeBarcode = tool.DecodeBarcode;
            t.BarcodeStr = tool.BarcodeStr;
            t.BarCodeROI = New_ROI(tool.BarCodeROI);
            //t.PositionDetect = tool.PositionDetect;
            t.ModelFileName = tool.ModelFileName;
            t.MinScore = tool.MinScore;
            t.ActualScore = tool.ActualScore;
            t.K = tool.K;
            t.InitX = tool.InitX;
            t.InitY = tool.InitY;
            t.ActualX = tool.ActualX;
            t.ActualY = tool.ActualY;
            t.ActualAngle = tool.ActualAngle;
            t.AllowAngleDelta = tool.AllowAngleDelta;
            t.DeltaX = tool.DeltaX;
            t.DeltaY = tool.DeltaY;
            //t.DirectX = tool.DirectX;
            //t.DirectY = tool.DirectY;
            //t.InvertXY = tool.InvertXY;
            t.SendStatus = tool.SendStatus;
            //t.SendBarcodeData = tool.SendBarcodeData;
            //t.SendPositionData = tool.SendPositionData;
            //t.InvertResult = tool.InvertResult;
            t.Allow_X_Delta = tool.Allow_X_Delta;
            t.Allow_Y_Delta = tool.Allow_Y_Delta;
            //t.StatusColor = tool.StatusColor;
            t.PositionROI = New_ROI(tool.PositionROI);

            t.DimensionROI = New_ROI(tool.DimensionROI);
            //t.DimensionDetect = tool.DimensionDetect;
            t.ActualDimension = tool.ActualDimension;
            t.MinDimension = tool.MinDimension;
            t.MaxDimension = tool.MaxDimension;
            t.GrayMode = tool.GrayMode;
            t.RedChannelEnabled = tool.RedChannelEnabled;
            t.GreenChannelEnabled = tool.GreenChannelEnabled;
            t.BlueChannelEnabled = tool.BlueChannelEnabled;
            t.MinGray = tool.MinGray;
            t.MaxGray = tool.MaxGray;
            t.MinRed = tool.MinRed;
            t.MaxRed = tool.MaxRed;
            t.MinGreen = tool.MinGreen;
            t.MaxGreen = tool.MaxGreen;
            t.MinBlue = tool.MinBlue;
            t.MaxBlue = tool.MaxBlue;
            t.MinAreaFilter = tool.MinAreaFilter;
            t.MaxAreaFilter = tool.MaxAreaFilter;
            t.BitmapSource = null;
            t.CurrentBitmapSource = null;
            t.BitmapSource = tool.BitmapSource.Clone();
            t.CurrentBitmapSource = tool.CurrentBitmapSource.Clone();
            t.CurrentBitmapFileName = tool.CurrentBitmapFileName;
            t.Image = tool.Image.Clone();



            return t;
        }
        public void InsertTool()
        {
            int index = DataModel.FaraVisionDataModel.Processmodel.selectedindex;
            InsertTool(index);
        }

        /// <summary>
        /// 重新计算并设置工具的连续序号，并重命名磁盘文件保持一致。
        /// </summary>
        public string autoindex()
        {
            string error = string.Empty;
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                int initindex = DataModel.FaraVisionDataModel.Processmodel.Tools[i].Index;
                int newindex = i + 1;

                if (initindex != newindex)
                {
                    DataModel.FaraVisionDataModel.Processmodel.Tools[i].Index = i + 1;

                    // 将 xml/shm/jpg 文件名中的序号重命名为新的序号
                    string s1 = ChangeToolIndex(initindex, newindex, "xml");
                    string s2 = ChangeToolIndex(initindex, newindex, "shm");
                    string s3 = ChangeToolIndex(initindex, newindex, "jpg");

                    if (!string.IsNullOrEmpty(s1))
                    {
                        error += $"工具{initindex}修改序号错误:{s1}";
                    }
                    if (!string.IsNullOrEmpty(s2))
                    {
                        error += $"工具{initindex}修改序号错误:{s2}";
                    }
                    if (!string.IsNullOrEmpty(s3))
                    {
                        error += $"工具{initindex}修改序号错误:{s3}";
                    }
                }
            }
            return error;
        }

        /// <summary>
        /// 将 Tool{InitIndex}.type 重命名为 Tool{NewIndex}.type。
        /// </summary>
        public string ChangeToolIndex(int InitIndex, int NewIndex, string type)
        {
            try
            {
                string initfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{InitIndex}.{type}";
                string newfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{NewIndex}.{type}";

                if (File.Exists(initfilename))
                {
                    File.Move(initfilename, newfilename);
                }
                return string.Empty;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        #endregion


        #region 界面menuitem
        public RelayCommand NEW_PRJCMD { set; get; } = null;

        public void NEW_PRJCMD_process()
        {
            try
            {
                if (DataModel.FaraVisionDataModel.Settingmodel.permission)
                {
                    if (MessageBoxX.Show("是否确定新建工程?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == MessageBoxResult.Yes)
                    {
                        BusbarCompressionSystem.Model.newPrj newPrj = new BusbarCompressionSystem.Model.newPrj();
                        if (newPrj.ShowDialog() == true)
                        {
                            string prjname = newPrj.Prj_Name;
                            string prjdir = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{prjname}";
                            if (Directory.Exists(prjdir))
                            {
                                if (MessageBoxX.Show("工程已经存在，是否删除旧工程?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) == MessageBoxResult.Yes)
                                {
                                    Directory.Delete(prjdir, true);
                                }
                                else
                                {
                                    return;
                                }
                            }
                            else
                            {
                                DataModel.FaraVisionDataModel.Settingmodel.Prjs.Add(prjname);
                                DataModel.FaraVisionDataModel.Settingmodel.prjselected = DataModel.FaraVisionDataModel.Settingmodel.Prjs.Count - 1;
                            }

                            #region 新建工程
                            Directory.CreateDirectory(prjdir);
                            DataModel.FaraVisionDataModel.Settingmodel.Name = prjname;
                            DataModel.FaraVisionDataModel.Processmodel.Tools.Clear();
                            //ClearToolStatus();
                            #endregion

                        }
                    }

                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"新建工程失败\r\n{ex.Message}", "错误", MessageBoxIcon.Error, true, 5000);
            }
        }


        public RelayCommand SAVE_PRJCMD { set; get; } = null;
        public void SAVE_PRJ_process()
        {
            //SaveAPPXml();
            SaveProcessmodel();
            SaveSettingModel();
            SavePrjXmls();
            NoticeBox.Show($"工程保存完成", "成功", MessageBoxIcon.Success, true, 5000);

        }

        #endregion



        #region 数据保存加载

        #region 过程数据
        public void Faravision_SaveProcessmodel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
            SaveXmlSafely(filename, DataModel.Processmodel);
        }
        public void Faravision_LoadProcessmodel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\过程数据.xml";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(filename))
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var serializer = new XmlSerializer(typeof(Processmodel));
                        DataModel.FaraVisionDataModel.Processmodel = serializer.Deserialize(stream) as Processmodel;
                    }
                }
                else
                {
                    DataModel.FaraVisionDataModel.Processmodel = new Processmodel();
                }
            }
            catch (Exception ex)
            {
                DataModel.FaraVisionDataModel.Processmodel = new Processmodel();

            }
        }
        #endregion

        #region 配置数据
        public void Faravision_SaveSettingModel()
        {

            string filename = $"{Environment.CurrentDirectory}\\配置\\视觉配置数据.xml";
            SaveXmlSafely(filename, DataModel.FaraVisionDataModel.Settingmodel);
        }
        public void Faravision_LoadSettingModel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\视觉配置数据.xml";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(filename))
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var serializer = new XmlSerializer(typeof(SettingModel));
                        DataModel.FaraVisionDataModel.Settingmodel = serializer.Deserialize(stream) as SettingModel;
                    }
                }
                else
                {
                    DataModel.FaraVisionDataModel.Settingmodel = new SettingModel();
                }
            }
            catch (Exception ex)
            {
                DataModel.FaraVisionDataModel.Settingmodel = new SettingModel();

                MessageBox.Show($"配置数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");

            }
        }
        #endregion

        #region 日志数据
        public void Faravision_SaveRecordModel()
        {
            string filename = $"{Environment.CurrentDirectory}\\配置\\视觉日志数据.xml";
            SaveXmlSafely(filename, DataModel.FaraVisionDataModel.Recordmodel);
        }
        public void Faravision_LoadRecordModel()
        {
            try
            {
                string filename = $"{Environment.CurrentDirectory}\\配置\\视觉日志数据.xml";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(filename))
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var serializer = new XmlSerializer(typeof(RecordModel));
                        DataModel.FaraVisionDataModel.Recordmodel = serializer.Deserialize(stream) as RecordModel;
                    }
                }
                else
                {
                    DataModel.FaraVisionDataModel.Recordmodel = new RecordModel();
                }
            }
            catch (Exception ex)
            {
                DataModel.FaraVisionDataModel.Recordmodel = new RecordModel();
                //MessageBox.Show($"日志数据.xml加载失败,软件已重置配置，请进入配置文件按需求修改,再重新打开软件:\r\n{ex.Message}");

            }
        }
        #endregion
        #region 配方保存加载




        public bool Faravision_LoadBitmapSource()
        {

            try
            {
                string jpgfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{DataModel.FaraVisionDataModel.Processmodel.selectedindex + 1}.jpg";
                using (Bitmap bmp = (Bitmap)Bitmap.FromFile(jpgfilename))
                {
                    // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                        DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = bs;
                    }
                    finally
                    {
                        DeleteObject(hBitmap); // 释放GDI句柄
                    }
                }
                GC.Collect();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }

            GC.Collect();


        }


        #endregion
        #endregion



        #region 面积计算

        /// <summary>
        /// 面积计算（支持灰度/颜色阈值），并可在窗口重绘 ROI 与结果区域。
        /// </summary>
        /// <returns>面积，失败返回 -1，无区域返回 0</returns>
        public int CoculateDimension(HObject image, ToolModel tool, HWindow hwindow, bool redraw = true)
        {
            try
            {

                HTuple Area = new HTuple(), Row = new HTuple(), Column = new HTuple();

                HObject ROI, ReduceImage;

                HObject Region, ConnectedRegions, SelectedRegions, RegionUnion;
                HOperatorSet.GenEmptyObj(out Region);
                HOperatorSet.GenEmptyObj(out ConnectedRegions);
                HOperatorSet.GenEmptyObj(out SelectedRegions);
                HOperatorSet.GenEmptyObj(out RegionUnion);

                HOperatorSet.GenEmptyObj(out ROI);
                HOperatorSet.GenEmptyObj(out ReduceImage);

                HObject ImageR, ImageG, ImageB;
                HObject RegionR, RegionG, RegionB;
                HObject RegionIntersection;

                HOperatorSet.GenEmptyObj(out ImageR);
                HOperatorSet.GenEmptyObj(out ImageG);
                HOperatorSet.GenEmptyObj(out ImageB);

                HOperatorSet.GenEmptyObj(out RegionR);
                HOperatorSet.GenEmptyObj(out RegionG);
                HOperatorSet.GenEmptyObj(out RegionB);

                HOperatorSet.GenEmptyObj(out RegionIntersection);

                try
                {

                    // 1) 生成 ROI 并缩小运算域
                    HOperatorSet.GenRectangle1(out ROI, tool.DimensionROI.Row1, tool.DimensionROI.Col1, tool.DimensionROI.Row2, tool.DimensionROI.Col2);
                    HOperatorSet.ReduceDomain(image, ROI, out ReduceImage);

                    // 2) 阈值分割：灰度 or RGB 三通道交集
                    if (tool.GrayMode)
                    {
                        HOperatorSet.Threshold(ReduceImage, out Region, tool.MinGray, tool.MaxGray);
                    }
                    else
                    {
                        HOperatorSet.Decompose3(ReduceImage, out ImageR, out ImageG, out ImageB);

                        HOperatorSet.Threshold(ImageR, out RegionR, tool.MinRed, tool.MaxRed);
                        HOperatorSet.Threshold(ImageG, out RegionG, tool.MinGreen, tool.MaxGreen);
                        HOperatorSet.Threshold(ImageB, out RegionB, tool.MinBlue, tool.MaxBlue);

                        HOperatorSet.Intersection(RegionR, RegionG, out RegionIntersection);
                        HOperatorSet.Intersection(RegionB, RegionIntersection, out Region);
                    }


                    // 3) 连通域、按面积筛选并合并
                    HOperatorSet.Connection(Region, out ConnectedRegions);
                    HOperatorSet.SelectShape(ConnectedRegions, out SelectedRegions, "area", "and", tool.MinAreaFilter, tool.MaxAreaFilter);
                    HOperatorSet.Union1(SelectedRegions, out RegionUnion);
                    HOperatorSet.AreaCenter(RegionUnion, out Area, out Row, out Column);

                    HTuple width, height;
                    HOperatorSet.GetImageSize(image, out width, out height);
                    //HWindow.HalconWindow.SetPart(0, 0, (int)height - 1, (int)width - 1);

                    if (redraw)
                    {

                        hwindow.ClearWindow();
                        //hwindow.SetPart(0, 0, (int)height - 1, (int)width - 1);
                        hwindow.DispObj(image);
                    }
                    hwindow.SetLineWidth(2);
                    hwindow.SetDraw("margin");
                    hwindow.SetColor("orange");
                    hwindow.DispObj(ROI);
                    hwindow.SetDraw("fill");
                    hwindow.SetColor("red");
                    hwindow.DispObj(SelectedRegions);

                }
                catch (Exception ex) { }

                // 统一释放 HObject 资源，避免句柄泄漏
                ROI?.Dispose();
                ReduceImage?.Dispose();
                Region?.Dispose();
                ConnectedRegions?.Dispose();
                SelectedRegions?.Dispose();
                RegionUnion?.Dispose();
                ImageR?.Dispose();
                ImageG?.Dispose();
                ImageB?.Dispose();
                RegionR?.Dispose();
                RegionG?.Dispose();
                RegionB?.Dispose();
                RegionIntersection?.Dispose();

                //tool.ActualDimension = (int)Area.L;
                if (Area.Length > 0)
                {
                    return (int)Area.L;
                }
                else
                {
                    return 0;
                }
            }
            catch (Exception e)
            {
                //tool.ActualDimension = -1;
                return -1;
            }
        }
        #endregion

        #region 尺寸测量

        /// <summary>
        /// 尺寸测量核心方法，根据测量类型调用相应的子方法
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型</param>
        /// <param name="hwindow">HALCON窗口（用于绘制）</param>
        /// <param name="redraw">是否重绘</param>
        /// <returns>测量值（单位：mm），失败返回-1</returns>
        public double MeasureDimension(HObject image, ToolModel tool, HWindow hwindow, bool redraw = true)
        {
            try
            {
                double result = -1;

                // 验证基本参数
                if (tool.DimensionK <= 0 || (tool.DimensionK == 1000 && tool.CalibrationRealSize == 0))
                {
                    throw new Exception("世界坐标未校准：请先进行校准");
                }

                switch (tool.MeasureType)
                {
                    case DimensionMeasureType.直线到直线:
                        result = MeasureLineToLine(image, tool, hwindow, redraw);
                        break;
                    case DimensionMeasureType.直线到圆心:
                        result = MeasureLineToCircle(image, tool, hwindow, redraw);
                        break;
                    case DimensionMeasureType.圆心到圆心:
                        result = MeasureCircleToCircle(image, tool, hwindow, redraw);
                        break;
                }

                // 转换为实际尺寸（mm）
                // DimensionK的单位是um/pixel，需要转换为mm/pixel
                if (result > 0 && tool.DimensionK > 0)
                {
                    return result * tool.DimensionK / 1000.0; // 转换为mm
                }

                return result;
            }
            catch (Exception e)
            {
                // 重新抛出异常，让UI层显示详细错误信息
                throw new Exception($"测量失败: {e.Message}", e);
            }
        }

        /// <summary>
        /// 直线到直线距离测量
        /// </summary>
        private double MeasureLineToLine(HObject image, ToolModel tool, HWindow hwindow, bool redraw)
        {
            try
            {
                // 验证ROI有效性
                if (tool.MeasureObject1ROI.Row1 == tool.MeasureObject1ROI.Row2 &&
                    tool.MeasureObject1ROI.Col1 == tool.MeasureObject1ROI.Col2)
                {
                    throw new Exception("测量对象1 ROI无效：ROI区域太小");
                }
                if (tool.MeasureObject2ROI.Row1 == tool.MeasureObject2ROI.Row2 &&
                    tool.MeasureObject2ROI.Col1 == tool.MeasureObject2ROI.Col2)
                {
                    throw new Exception("测量对象2 ROI无效：ROI区域太小");
                }

                // 使用Metrology模型检测第一条直线
                double line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd;
                HTuple edge1Rows, edge1Cols;
                if (!DetectEdgeWithMetrology(image, tool, tool.MeasureObject1ROI,
                    out line1RowBegin, out line1ColBegin, out line1RowEnd, out line1ColEnd,
                    out edge1Rows, out edge1Cols))
                {
                    throw new Exception("测量对象1边缘检测失败：请检查ROI位置、Metrology参数设置");
                }

                // 使用Metrology模型检测第二条直线
                double line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd;
                HTuple edge2Rows, edge2Cols;
                if (!DetectEdgeWithMetrology(image, tool, tool.MeasureObject2ROI,
                    out line2RowBegin, out line2ColBegin, out line2RowEnd, out line2ColEnd,
                    out edge2Rows, out edge2Cols))
                {
                    throw new Exception("测量对象2边缘检测失败：请检查ROI位置、Metrology参数设置");
                }

                // 计算两条直线间的精确距离
                double distance = CalculateDistanceBetweenLines(
                    line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,
                    line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd);

                // 可视化绘制
                if (redraw)
                {
                    // 绘制拟合直线（青色）
                    hwindow.SetLineWidth(2);
                    hwindow.SetColor("cyan");
                    hwindow.DispLine(line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd);
                    hwindow.DispLine(line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd);

                    // 绘制真实最短距离连线
                    // 计算两条线段之间的最近点对
                    double closestRow1, closestCol1, closestRow2, closestCol2;
                    CalculateClosestPointsBetweenSegments(
                        line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,
                        line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd,
                        out closestRow1, out closestCol1,
                        out closestRow2, out closestCol2);

                    // 验证：计算出的最近点对距离应与DistanceSs返回值一致
                    double calculatedDistance = CalculateDistance(closestRow1, closestCol1, closestRow2, closestCol2);
                    double tolerance = 0.01; // 允许0.01像素的误差（浮点精度）
                    if (Math.Abs(calculatedDistance - distance) > tolerance)
                    {
                        // 如果不一致，输出调试信息
                        System.Diagnostics.Debug.WriteLine($"警告：最近点对距离({calculatedDistance:F3})与DistanceSs({distance:F3})不一致，差值={Math.Abs(calculatedDistance - distance):F3}");
                    }

                    // 绘制测量距离线（红色虚线）- 连接两条线段的最近点
                    hwindow.SetColor("red");
                    hwindow.SetLineStyle(new HTuple(new int[] { 10, 5 })); // 虚线样式
                    hwindow.DispLine(closestRow1, closestCol1, closestRow2, closestCol2);
                    hwindow.SetLineStyle(new HTuple()); // 恢复实线

                    // 如果启用调试信息，绘制边缘点、卡尺位置和最近点标记
                    if (tool.ShowMetrologyDebugInfo)
                    {
                        // 绘制边缘点（春绿色十字标记）
                        hwindow.SetColor("spring green");
                        hwindow.SetLineWidth(1);
                        for (int i = 0; i < edge1Rows.Length; i++)
                        {
                            double row = edge1Rows[i].D;
                            double col = edge1Cols[i].D;
                            hwindow.DispCross(row, col, 6, 0); // 绘制十字
                        }
                        for (int i = 0; i < edge2Rows.Length; i++)
                        {
                            double row = edge2Rows[i].D;
                            double col = edge2Cols[i].D;
                            hwindow.DispCross(row, col, 6, 0);
                        }

                        // 绘制最近点标记（橙色圆圈）
                        hwindow.SetColor("orange");
                        hwindow.SetLineWidth(2);
                        hwindow.DispCircle(closestRow1, closestCol1, 8);
                        hwindow.DispCircle(closestRow2, closestCol2, 8);

                        // 显示距离验证信息（白色文字）
                        hwindow.SetColor("white");
                        string debugInfo = $"DistanceSs: {distance:F2}px\nCalculated: {calculatedDistance:F2}px";
                        hwindow.DispText(debugInfo, "image", closestRow1 - 30, closestCol1, "white", "box", "false");
                    }
                }

                return distance;
            }
            catch (Exception ex)
            {
                // 记录详细错误信息，便于调试
                System.Diagnostics.Debug.WriteLine($"MeasureLineToLine错误: {ex.Message}");
                throw; // 重新抛出异常，让上层捕获并显示
            }
        }

        /// <summary>
        /// 直线到圆心距离测量
        /// </summary>
        private double MeasureLineToCircle(HObject image, ToolModel tool, HWindow hwindow, bool redraw)
        {
            HTuple measureHandle = null;
            try
            {
                // 检测直线
                HTuple lineRow, lineCol;
                if (!DetectEdgeWithCaliper(image, tool, tool.MeasureObject1ROI, out lineRow, out lineCol))
                {
                    return -1;
                }

                // 检测圆心
                HTuple circleRow, circleCol, circleRadius;
                if (!DetectCircle(image, tool, tool.MeasureObject2ROI, out circleRow, out circleCol, out circleRadius))
                {
                    return -1;
                }

                // 计算点到直线的距离
                // 使用HALCON的distance_pl算子
                HTuple distance;
                HOperatorSet.DistancePl(circleRow, circleCol, lineRow, lineCol, lineRow + 1, lineCol + 1, out distance);

                if (redraw)
                {
                    hwindow.SetLineWidth(2);
                    hwindow.SetColor("green");
                    hwindow.DispLine(lineRow.D, lineCol.D, circleRow.D, circleCol.D);
                    hwindow.SetColor("yellow");
                    hwindow.DispCircle(circleRow.D, circleCol.D, circleRadius.D);
                }

                return distance.D;
            }
            catch (Exception ex)
            {
                return -1;
            }
            finally
            {
                if (measureHandle != null) HOperatorSet.CloseMeasure(measureHandle);
            }
        }

        /// <summary>
        /// 圆心到圆心距离测量
        /// </summary>
        private double MeasureCircleToCircle(HObject image, ToolModel tool, HWindow hwindow, bool redraw)
        {
            try
            {
                // 检测第一个圆心
                HTuple circle1Row, circle1Col, circle1Radius;
                if (!DetectCircle(image, tool, tool.MeasureObject1ROI, out circle1Row, out circle1Col, out circle1Radius))
                {
                    return -1;
                }

                // 检测第二个圆心
                HTuple circle2Row, circle2Col, circle2Radius;
                if (!DetectCircle(image, tool, tool.MeasureObject2ROI, out circle2Row, out circle2Col, out circle2Radius))
                {
                    return -1;
                }

                // 计算两点间距离
                HTuple distance;
                HOperatorSet.DistancePp(circle1Row, circle1Col, circle2Row, circle2Col, out distance);

                if (redraw)
                {
                    hwindow.SetLineWidth(2);
                    hwindow.SetColor("green");
                    hwindow.DispLine(circle1Row.D, circle1Col.D, circle2Row.D, circle2Col.D);
                    hwindow.SetColor("yellow");
                    hwindow.DispCircle(circle1Row.D, circle1Col.D, circle1Radius.D);
                    hwindow.DispCircle(circle2Row.D, circle2Col.D, circle2Radius.D);
                }

                return distance.D;
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        /// <summary>
        /// 使用卡尺工具检测边缘（用于直线检测）
        /// </summary>
        private bool DetectEdgeWithCaliper(HObject image, ToolModel tool, ROI roi, out HTuple row, out HTuple col)
        {
            row = null;
            col = null;
            HTuple measureHandle = null;

            try
            {
                // 计算ROI中心点和角度
                double centerRow = (roi.Row1 + roi.Row2) / 2.0;
                double centerCol = (roi.Col1 + roi.Col2) / 2.0;

                // 如果未设置角度，根据ROI自动计算
                double angle = tool.CaliperAngle;
                if (angle == 0)
                {
                    double deltaRow = roi.Row2 - roi.Row1;
                    double deltaCol = roi.Col2 - roi.Col1;
                    angle = Math.Atan2(deltaRow, deltaCol) * 180.0 / Math.PI;
                }

                // 获取图像尺寸
                HTuple width, height;
                HOperatorSet.GetImageSize(image, out width, out height);

                // 生成卡尺工具
                HOperatorSet.GenMeasureRectangle2(
                    centerRow,
                    centerCol,
                    angle * Math.PI / 180.0,
                    tool.CaliperLength / 2.0,
                    tool.CaliperWidth / 2.0,
                    width,
                    height,
                    tool.SubPixelAccuracy ? "bicubic" : "nearest_neighbor",
                    out measureHandle
                );

                // 检测边缘
                HTuple rowEdge, columnEdge, amplitude, distance;
                HOperatorSet.MeasurePos(
                    image,
                    measureHandle,
                    1.0, // sigma
                    tool.EdgeThreshold,
                    tool.EdgePolarity,
                    tool.EdgeSelection,
                    out rowEdge,
                    out columnEdge,
                    out amplitude,
                    out distance
                );

                // 释放资源
                HOperatorSet.CloseMeasure(measureHandle);
                measureHandle = null;

                // 返回边缘位置（如果检测到边缘，返回第一个）
                if (rowEdge.Length > 0)
                {
                    row = rowEdge[0];
                    col = columnEdge[0];
                    return true;
                }

                // 边缘检测失败，返回false
                return false;
            }
            catch (Exception ex)
            {
                if (measureHandle != null)
                {
                    try { HOperatorSet.CloseMeasure(measureHandle); } catch { }
                }
                return false;
            }
        }

        /// <summary>
        /// 计算两条线段间的最短距离（使用HALCON的DistanceSs）
        /// {{ AURA-X: Modify - 替换为distance_ss算子，计算线段间真实最短距离. Source: HALCON官方文档 distance_ss. }}
        /// </summary>
        /// <param name="line1RowBegin">线段1起点Row</param>
        /// <param name="line1ColBegin">线段1起点Col</param>
        /// <param name="line1RowEnd">线段1终点Row</param>
        /// <param name="line1ColEnd">线段1终点Col</param>
        /// <param name="line2RowBegin">线段2起点Row</param>
        /// <param name="line2ColBegin">线段2起点Col</param>
        /// <param name="line2RowEnd">线段2终点Row</param>
        /// <param name="line2ColEnd">线段2终点Col</param>
        /// <returns>两条线段间的最短距离（像素）</returns>
        private double CalculateDistanceBetweenLines(
            double line1RowBegin, double line1ColBegin, double line1RowEnd, double line1ColEnd,
            double line2RowBegin, double line2ColBegin, double line2RowEnd, double line2ColEnd)
        {
            // 使用HALCON的DistanceSs算子计算两条线段之间的最短距离
            // 该算子会精确计算线段间的真实最短距离，处理所有情况（平行、相交、斜线、端点距离等）
            HTuple distanceMin, distanceMax;
            HOperatorSet.DistanceSs(
                line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,  // 线段1的起点和终点
                line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd,  // 线段2的起点和终点
                out distanceMin,  // 最短距离
                out distanceMax   // 最大距离（未使用）
            );

            return distanceMin.D;  // 返回最短距离
        }

        /// <summary>
        /// 计算两条线段之间最近点对的坐标
        /// {{ AURA-X: Modify - 使用HALCON projection_pl算子替代自定义几何算法，确保与DistanceSs完全一致. Source: HALCON官方文档 projection_pl. }}
        /// </summary>
        /// <param name="line1RowBegin">线段1起点Row</param>
        /// <param name="line1ColBegin">线段1起点Col</param>
        /// <param name="line1RowEnd">线段1终点Row</param>
        /// <param name="line1ColEnd">线段1终点Col</param>
        /// <param name="line2RowBegin">线段2起点Row</param>
        /// <param name="line2ColBegin">线段2起点Col</param>
        /// <param name="line2RowEnd">线段2终点Row</param>
        /// <param name="line2ColEnd">线段2终点Col</param>
        /// <param name="closestRow1">线段1上最近点的Row坐标</param>
        /// <param name="closestCol1">线段1上最近点的Col坐标</param>
        /// <param name="closestRow2">线段2上最近点的Row坐标</param>
        /// <param name="closestCol2">线段2上最近点的Col坐标</param>
        private void CalculateClosestPointsBetweenSegments(
            double line1RowBegin, double line1ColBegin, double line1RowEnd, double line1ColEnd,
            double line2RowBegin, double line2ColBegin, double line2RowEnd, double line2ColEnd,
            out double closestRow1, out double closestCol1,
            out double closestRow2, out double closestCol2)
        {
            // 使用HALCON的projection_pl算子和枚举法找到真实的最近点对
            // 策略：枚举所有可能的候选点对，找到距离最小的一对

            double minDistance = double.MaxValue;
            closestRow1 = line1RowBegin;
            closestCol1 = line1ColBegin;
            closestRow2 = line2RowBegin;
            closestCol2 = line2ColBegin;

            // 候选1: 线段1的起点投影到线段2
            HTuple projRow, projCol;
            HOperatorSet.ProjectionPl(line1RowBegin, line1ColBegin,
                                     line2RowBegin, line2ColBegin,
                                     line2RowEnd, line2ColEnd,
                                     out projRow, out projCol);
            // 裁剪投影点到线段2范围内
            double clampedRow2, clampedCol2;
            ClampPointToSegment(projRow.D, projCol.D,
                               line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd,
                               out clampedRow2, out clampedCol2);
            double dist = CalculateDistance(line1RowBegin, line1ColBegin, clampedRow2, clampedCol2);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = line1RowBegin;
                closestCol1 = line1ColBegin;
                closestRow2 = clampedRow2;
                closestCol2 = clampedCol2;
            }

            // 候选2: 线段1的终点投影到线段2
            HOperatorSet.ProjectionPl(line1RowEnd, line1ColEnd,
                                     line2RowBegin, line2ColBegin,
                                     line2RowEnd, line2ColEnd,
                                     out projRow, out projCol);
            ClampPointToSegment(projRow.D, projCol.D,
                               line2RowBegin, line2ColBegin, line2RowEnd, line2ColEnd,
                               out clampedRow2, out clampedCol2);
            dist = CalculateDistance(line1RowEnd, line1ColEnd, clampedRow2, clampedCol2);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = line1RowEnd;
                closestCol1 = line1ColEnd;
                closestRow2 = clampedRow2;
                closestCol2 = clampedCol2;
            }

            // 候选3: 线段2的起点投影到线段1
            HOperatorSet.ProjectionPl(line2RowBegin, line2ColBegin,
                                     line1RowBegin, line1ColBegin,
                                     line1RowEnd, line1ColEnd,
                                     out projRow, out projCol);
            double clampedRow1, clampedCol1;
            ClampPointToSegment(projRow.D, projCol.D,
                               line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,
                               out clampedRow1, out clampedCol1);
            dist = CalculateDistance(clampedRow1, clampedCol1, line2RowBegin, line2ColBegin);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = clampedRow1;
                closestCol1 = clampedCol1;
                closestRow2 = line2RowBegin;
                closestCol2 = line2ColBegin;
            }

            // 候选4: 线段2的终点投影到线段1
            HOperatorSet.ProjectionPl(line2RowEnd, line2ColEnd,
                                     line1RowBegin, line1ColBegin,
                                     line1RowEnd, line1ColEnd,
                                     out projRow, out projCol);
            ClampPointToSegment(projRow.D, projCol.D,
                               line1RowBegin, line1ColBegin, line1RowEnd, line1ColEnd,
                               out clampedRow1, out clampedCol1);
            dist = CalculateDistance(clampedRow1, clampedCol1, line2RowEnd, line2ColEnd);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestRow1 = clampedRow1;
                closestCol1 = clampedCol1;
                closestRow2 = line2RowEnd;
                closestCol2 = line2ColEnd;
            }
        }

        /// <summary>
        /// 将投影点裁剪到线段范围内
        /// 如果投影点在线段外，返回最近的端点
        /// </summary>
        private void ClampPointToSegment(
            double projRow, double projCol,
            double segRowBegin, double segColBegin, double segRowEnd, double segColEnd,
            out double clampedRow, out double clampedCol)
        {
            // 计算线段的方向向量
            double dirRow = segRowEnd - segRowBegin;
            double dirCol = segColEnd - segColBegin;
            double segmentLengthSquared = dirRow * dirRow + dirCol * dirCol;

            // 处理退化线段（起点和终点重合）
            if (segmentLengthSquared < 1e-10)
            {
                clampedRow = segRowBegin;
                clampedCol = segColBegin;
                return;
            }

            // 计算投影点在线段上的参数t（0表示起点，1表示终点）
            double t = ((projRow - segRowBegin) * dirRow + (projCol - segColBegin) * dirCol) / segmentLengthSquared;

            // 裁剪t到[0, 1]范围
            t = Math.Max(0, Math.Min(1, t));

            // 计算裁剪后的点坐标
            clampedRow = segRowBegin + t * dirRow;
            clampedCol = segColBegin + t * dirCol;
        }

        /// <summary>
        /// 计算两点之间的欧氏距离
        /// </summary>
        private double CalculateDistance(double row1, double col1, double row2, double col2)
        {
            double dRow = row2 - row1;
            double dCol = col2 - col1;
            return Math.Sqrt(dRow * dRow + dCol * dCol);
        }

        /// <summary>
        /// 使用HALCON Metrology模型检测边缘并拟合直线
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型（包含Metrology参数）</param>
        /// <param name="roi">线段ROI（起点和终点定义搜索区域）</param>
        /// <param name="lineRowBegin">输出：拟合直线起点Row坐标</param>
        /// <param name="lineColBegin">输出：拟合直线起点Col坐标</param>
        /// <param name="lineRowEnd">输出：拟合直线终点Row坐标</param>
        /// <param name="lineColEnd">输出：拟合直线终点Col坐标</param>
        /// <param name="edgeRows">输出：检测到的边缘点Row坐标数组</param>
        /// <param name="edgeCols">输出：检测到的边缘点Col坐标数组</param>
        /// <returns>是否检测成功</returns>
        private bool DetectEdgeWithMetrology(
            HObject image, ToolModel tool, ROI roi,
            out double lineRowBegin, out double lineColBegin,
            out double lineRowEnd, out double lineColEnd,
            out HTuple edgeRows, out HTuple edgeCols)
        {
            // 初始化输出参数
            lineRowBegin = lineColBegin = lineRowEnd = lineColEnd = 0;
            edgeRows = new HTuple();
            edgeCols = new HTuple();

            HTuple metrologyHandle = null;
            HTuple width = null, height = null;

            try
            {
                // 1. 获取图像尺寸
                HOperatorSet.GetImageSize(image, out width, out height);

                // 2. 创建Metrology模型
                HOperatorSet.CreateMetrologyModel(out metrologyHandle);
                HOperatorSet.SetMetrologyModelImageSize(metrologyHandle, width, height);

                // 3. 构建线段参数 [Row1, Col1, Row2, Col2]
                HTuple shapeParam = new HTuple();
                shapeParam[0] = roi.Row1;
                shapeParam[1] = roi.Col1;
                shapeParam[2] = roi.Row2;
                shapeParam[3] = roi.Col2;

                // 4. 添加线对象到Metrology模型
                HTuple index;
                HOperatorSet.AddMetrologyObjectGeneric(
                    metrologyHandle,
                    "line",                          // 对象类型：线段
                    shapeParam,                      // 线段参数
                    tool.MetrologyTolerance,         // 搜索范围（Tolerance）
                    tool.MetrologyNumMeasures,       // 卡尺数量
                    1,                               // 卡尺间距（自动分布）
                    tool.MetrologyMeasureThreshold,  // 边缘阈值
                    new HTuple(),                    // GenParamName（空）
                    new HTuple(),                    // GenParamValue（空）
                    out index
                );

                // 5. 设置Metrology对象参数
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_transition", tool.MetrologyMeasureTransition);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "num_measures", tool.MetrologyNumMeasures);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_sigma", tool.MetrologyMeasureSigma);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_threshold", tool.MetrologyMeasureThreshold);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_select", tool.MetrologyMeasureSelect);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "min_score", tool.MetrologyMinScore);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_length1", tool.MetrologyMeasureLength1);
                HOperatorSet.SetMetrologyObjectParam(metrologyHandle, "all", "measure_length2", tool.MetrologyMeasureLength2);

                // 6. 执行测量
                HOperatorSet.ApplyMetrologyModel(image, metrologyHandle);

                // 7. 获取拟合结果（直线参数）
                HTuple parameter;
                HOperatorSet.GetMetrologyObjectResult(
                    metrologyHandle,
                    0,              // 对象索引（第一个对象）
                    "all",          // 实例（全部）
                    "result_type",  // 结果类型
                    "all_param",    // 参数名称
                    out parameter
                );

                // 检查是否获取到有效结果
                if (parameter == null || parameter.Length < 4)
                {
                    return false;
                }

                // 8. 解析直线参数 [Row1, Col1, Row2, Col2]
                lineRowBegin = parameter[0].D;
                lineColBegin = parameter[1].D;
                lineRowEnd = parameter[2].D;
                lineColEnd = parameter[3].D;

                // 9. 获取边缘点坐标（用于可视化）
                HObject contours;
                HOperatorSet.GetMetrologyObjectMeasures(
                    out contours,
                    metrologyHandle,
                    "all",      // 对象索引
                    "all",      // 实例
                    out edgeRows,
                    out edgeCols
                );
                contours?.Dispose();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DetectEdgeWithMetrology错误: {ex.Message}");
                return false;
            }
            finally
            {
                // 释放Metrology模型资源
                if (metrologyHandle != null)
                {
                    try
                    {
                        HOperatorSet.ClearMetrologyObject(metrologyHandle, "all");
                        HOperatorSet.ClearMetrologyModel(metrologyHandle);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 检测圆（用于圆心检测）
        /// </summary>
        private bool DetectCircle(HObject image, ToolModel tool, ROI roi, out HTuple row, out HTuple col, out HTuple radius)
        {
            row = null;
            col = null;
            radius = null;
            HObject reduceImage = null, edges = null, contours = null, selectedContours = null;

            try
            {
                // 生成ROI并缩小运算域
                HObject roiRegion;
                HOperatorSet.GenRectangle1(out roiRegion, roi.Row1, roi.Col1, roi.Row2, roi.Col2);
                HOperatorSet.ReduceDomain(image, roiRegion, out reduceImage);
                roiRegion.Dispose();

                // 边缘检测
                HOperatorSet.EdgesSubPix(reduceImage, out edges, "canny", 1.0, 20, 40);

                // 选择圆形轮廓
                HOperatorSet.SelectShapeXld(edges, out selectedContours, "circularity", "and", 0.7, 1.0);

                if (selectedContours.CountObj() == 0)
                {
                    reduceImage?.Dispose();
                    edges?.Dispose();
                    selectedContours?.Dispose();
                    return false;
                }

                // 拟合圆
                HTuple rowCenter, colCenter, radiusCenter, startPhi, endPhi, pointOrder;
                HOperatorSet.FitCircleContourXld(selectedContours, "algebraic", -1, 0, 0, 3, 2, 
                    out rowCenter, out colCenter, out radiusCenter, out startPhi, out endPhi, out pointOrder);

                if (rowCenter.Length > 0)
                {
                    row = rowCenter[0];
                    col = colCenter[0];
                    radius = radiusCenter[0];

                    reduceImage?.Dispose();
                    edges?.Dispose();
                    selectedContours?.Dispose();
                    return true;
                }

                reduceImage?.Dispose();
                edges?.Dispose();
                selectedContours?.Dispose();
                return false;
            }
            catch (Exception ex)
            {
                reduceImage?.Dispose();
                edges?.Dispose();
                selectedContours?.Dispose();
                return false;
            }
        }

        #endregion

        #region 边缘预览

        /// <summary>
        /// 预览指定ROI区域内的边缘轮廓
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型</param>
        /// <param name="roi">要预览的ROI区域</param>
        /// <param name="hwindow">HALCON窗口</param>
        /// <param name="color">显示颜色</param>
        /// <returns>是否预览成功</returns>
        public bool PreviewEdgesForROI(HObject image, ToolModel tool, ROI roi, HWindow hwindow, string color = "green")
        {
            try
            {
                // 验证ROI有效性
                if (roi.Row1 == roi.Row2 && roi.Col1 == roi.Col2)
                {
                    return false; // ROI区域太小，跳过预览
                }

                // 根据ROI类型选择不同的预览方法
                if (roi.Type == ROIType.Line && tool.TestMode == TestModes.尺寸测量)
                {
                    // 使用Metrology模型预览（用于尺寸测量）
                    return PreviewEdgesWithMetrology(image, tool, roi, hwindow, color);
                }
                else
                {
                    // 使用传统卡尺工具预览（用于其他模式）
                    return PreviewEdgesWithCaliper(image, tool, roi, hwindow, color);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"边缘预览失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用Metrology模型预览边缘（完整调试信息）
        /// </summary>
        private bool PreviewEdgesWithMetrology(HObject image, ToolModel tool, ROI roi, HWindow hwindow, string color)
        {
            try
            {
                // 使用Metrology检测边缘
                double lineRowBegin, lineColBegin, lineRowEnd, lineColEnd;
                HTuple edgeRows, edgeCols;
                if (!DetectEdgeWithMetrology(image, tool, roi,
                    out lineRowBegin, out lineColBegin, out lineRowEnd, out lineColEnd,
                    out edgeRows, out edgeCols))
                {
                    return false;
                }

                // 1. 绘制拟合直线（青色）
                hwindow.SetLineWidth(2);
                hwindow.SetColor("cyan");
                hwindow.DispLine(lineRowBegin, lineColBegin, lineRowEnd, lineColEnd);

                // 2. 绘制边缘点（春绿色十字标记）
                hwindow.SetColor("spring green");
                hwindow.SetLineWidth(1);
                for (int i = 0; i < edgeRows.Length; i++)
                {
                    double row = edgeRows[i].D;
                    double col = edgeCols[i].D;
                    hwindow.DispCross(row, col, 6, 0);
                }

                // 3. 如果启用调试信息，绘制卡尺位置
                if (tool.ShowMetrologyDebugInfo)
                {
                    // 计算线段方向向量
                    double dirRow = lineRowEnd - lineRowBegin;
                    double dirCol = lineColEnd - lineColBegin;
                    double lineLength = Math.Sqrt(dirRow * dirRow + dirCol * dirCol);
                    dirRow /= lineLength;
                    dirCol /= lineLength;

                    // 垂直方向向量
                    double perpRow = -dirCol;
                    double perpCol = dirRow;

                    // 绘制卡尺位置（黄色矩形）
                    hwindow.SetColor("yellow");
                    hwindow.SetLineWidth(1);
                    hwindow.SetDraw("margin");

                    for (int i = 0; i < tool.MetrologyNumMeasures; i++)
                    {
                        // 计算卡尺中心位置
                        double t = (double)i / (tool.MetrologyNumMeasures - 1);
                        double centerRow = lineRowBegin + t * (lineRowEnd - lineRowBegin);
                        double centerCol = lineColBegin + t * (lineColEnd - lineColBegin);

                        // 计算卡尺矩形的四个角点
                        double halfLen1 = tool.MetrologyMeasureLength1;
                        double halfLen2 = tool.MetrologyMeasureLength2;

                        double row1 = centerRow - halfLen1 * dirRow - halfLen2 * perpRow;
                        double col1 = centerCol - halfLen1 * dirCol - halfLen2 * perpCol;
                        double row2 = centerRow + halfLen1 * dirRow - halfLen2 * perpRow;
                        double col2 = centerCol + halfLen1 * dirCol - halfLen2 * perpCol;
                        double row3 = centerRow + halfLen1 * dirRow + halfLen2 * perpRow;
                        double col3 = centerCol + halfLen1 * dirCol + halfLen2 * perpCol;
                        double row4 = centerRow - halfLen1 * dirRow + halfLen2 * perpRow;
                        double col4 = centerCol - halfLen1 * dirCol + halfLen2 * perpCol;

                        // 绘制矩形
                        hwindow.DispLine(row1, col1, row2, col2);
                        hwindow.DispLine(row2, col2, row3, col3);
                        hwindow.DispLine(row3, col3, row4, col4);
                        hwindow.DispLine(row4, col4, row1, col1);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Metrology预览失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用传统卡尺工具预览边缘
        /// </summary>
        private bool PreviewEdgesWithCaliper(HObject image, ToolModel tool, ROI roi, HWindow hwindow, string color)
        {
            HTuple measureHandle = null;
            try
            {
                // 计算ROI中心点和角度（用于卡尺工具）
                double centerRow = (roi.Row1 + roi.Row2) / 2.0;
                double centerCol = (roi.Col1 + roi.Col2) / 2.0;

                // 根据ROI形状自动计算角度
                double angle = 0;
                double deltaRow = roi.Row2 - roi.Row1;
                double deltaCol = roi.Col2 - roi.Col1;
                if (deltaRow != 0 || deltaCol != 0)
                {
                    angle = Math.Atan2(deltaRow, deltaCol) * 180.0 / Math.PI;
                }

                // 获取图像尺寸
                HTuple width, height;
                HOperatorSet.GetImageSize(image, out width, out height);

                // 生成卡尺工具（使用较小的尺寸以提高检测密度）
                HOperatorSet.GenMeasureRectangle2(
                    centerRow,
                    centerCol,
                    angle * Math.PI / 180.0,
                    tool.CaliperLength / 2.0,
                    tool.CaliperWidth / 2.0,
                    width,
                    height,
                    tool.SubPixelAccuracy ? "bicubic" : "nearest_neighbor",
                    out measureHandle
                );

                // 检测边缘
                HTuple rowEdge, columnEdge, amplitude, distance;
                HOperatorSet.MeasurePos(
                    image,
                    measureHandle,
                    1.0, // sigma
                    tool.EdgeThreshold,
                    tool.EdgePolarity,
                    tool.EdgeSelection,
                    out rowEdge,
                    out columnEdge,
                    out amplitude,
                    out distance
                );

                // 在图像上绘制检测到的边缘点
                if (rowEdge.Length > 0)
                {
                    hwindow.SetLineWidth(1);
                    hwindow.SetDraw("margin");
                    hwindow.SetColor(color);

                    // 绘制边缘点为小十字
                    for (int i = 0; i < rowEdge.Length; i++)
                    {
                        double row = rowEdge[i].D;
                        double col = columnEdge[i].D;

                        // 绘制小十字标记
                        hwindow.DispLine((double)(row - 2), (double)col, (double)(row + 2), (double)col); // 垂直线
                        hwindow.DispLine((double)row, (double)(col - 2), (double)row, (double)(col + 2)); // 水平线
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"卡尺预览失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (measureHandle != null)
                {
                    try { HOperatorSet.CloseMeasure(measureHandle); } catch { }
                }
            }
        }

        /// <summary>
        /// 预览所有测量对象的边缘轮廓
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="tool">工具模型</param>
        /// <param name="hwindow">HALCON窗口</param>
        /// <returns>是否预览成功</returns>
        public bool PreviewEdges(HObject image, ToolModel tool, HWindow hwindow)
        {
            try
            {
                if (image == null || hwindow == null)
                {
                    return false;
                }

                // 清空窗口并重新显示图像
                hwindow.ClearWindow();
                hwindow.DispObj(image);

                // 绘制ROI矩形框
                hwindow.SetLineWidth(2);
                hwindow.SetDraw("margin");

                // 绘制测量对象1的ROI（绿色）
                if (tool.MeasureObject1ROI.Row1 != tool.MeasureObject1ROI.Row2 ||
                    tool.MeasureObject1ROI.Col1 != tool.MeasureObject1ROI.Col2)
                {
                    hwindow.SetColor("green");
                    hwindow.DispRectangle1((double)tool.MeasureObject1ROI.Row1, (double)tool.MeasureObject1ROI.Col1,
                                         (double)tool.MeasureObject1ROI.Row2, (double)tool.MeasureObject1ROI.Col2);

                    // 预览测量对象1的边缘
                    PreviewEdgesForROI(image, tool, tool.MeasureObject1ROI, hwindow, "lime");
                }

                // 绘制测量对象2的ROI（黄色）
                if (tool.MeasureObject2ROI.Row1 != tool.MeasureObject2ROI.Row2 ||
                    tool.MeasureObject2ROI.Col1 != tool.MeasureObject2ROI.Col2)
                {
                    hwindow.SetColor("yellow");
                    hwindow.DispRectangle1((double)tool.MeasureObject2ROI.Row1, (double)tool.MeasureObject2ROI.Col1,
                                         (double)tool.MeasureObject2ROI.Row2, (double)tool.MeasureObject2ROI.Col2);

                    // 预览测量对象2的边缘
                    PreviewEdgesForROI(image, tool, tool.MeasureObject2ROI, hwindow, "yellow");
                }

                // 更新WPF显示：从HALCON窗口获取当前显示内容并更新到ShowBitmapSource
                UpdateWpfDisplayFromHWindow(hwindow);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"边缘预览失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从HALCON窗口获取当前显示内容并更新WPF的ShowBitmapSource
        /// </summary>
        /// <param name="hwindow">HALCON窗口</param>
        private void UpdateWpfDisplayFromHWindow(HWindow hwindow)
        {
            try
            {
                // 验证HALCON窗口状态
                if (hwindow == null)
                {
                    System.Diagnostics.Debug.WriteLine("HALCON窗口为空，跳过显示更新");
                    return;
                }

                // 强制刷新HALCON窗口，确保所有绘制操作已完成
                HOperatorSet.SetWindowParam(hwindow, "flush", "true");

                // 短暂延迟确保窗口刷新完成
                System.Threading.Thread.Sleep(50);

                // 从HALCON窗口获取当前显示的图像
                HObject windowImage = null;
                HOperatorSet.GenEmptyObj(out windowImage);
                HOperatorSet.DumpWindowImage(out windowImage, hwindow);

                // 验证获取的图像是否有效
                HTuple width, height;
                HOperatorSet.GetImageSize(windowImage, out width, out height);
                if (width.I <= 0 || height.I <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("获取的窗口图像无效");
                    windowImage?.Dispose();
                    return;
                }

                // 转换为WPF BitmapSource
                Hobject2Bitmap.HobjectToBitmap24(windowImage, out System.Drawing.Bitmap bmp);

                // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                IntPtr hBitmap = bmp.GetHbitmap();
                try
                {
                    BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                    // 更新WPF显示（先置null再赋值，确保绑定更新）
                    DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = null;
                    DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource = bs;

                    // 强制触发UI更新
                    RaisePropertyChanged(() => DataModel.FaraVisionDataModel.Processmodel.ShowBitmapSource);
                }
                finally
                {
                    DeleteObject(hBitmap); // 释放GDI句柄
                }

                bmp?.Dispose();
                windowImage?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新WPF显示失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 预览尺寸测量（用于参数调整时的实时反馈）
        /// {{ AURA-X: Add - 为Metrology参数变更提供实时预览. Approval: 寸止(Q10: Option B). }}
        /// </summary>
        public void PreviewDimensionMeasurement(HObject image, ToolModel tool, HWindow hwindow)
        {
            try
            {
                if (image == null || hwindow == null)
                {
                    return;
                }

                // 调用现有的PreviewEdges方法
                PreviewEdges(image, tool, hwindow);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"尺寸测量预览失败: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// 将 Bitmap 等比缩放至不超过 W×H 的尺寸。
        /// </summary>
        public Bitmap GetReducedImage(double W, double H, Bitmap src)
        {
            try
            {
                double _wscale = W / (double)src.Width;
                double _Hscale = H / (double)src.Height;
                double _scale = Math.Min(_wscale, _Hscale);
                W = src.Width * _scale;
                H = src.Height * _scale;
                Bitmap r = new Bitmap((int)W, (int)H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                Graphics g = Graphics.FromImage(r);
                g.Clear(Color.Transparent);
                // 按比例绘制缩放图像
                g.DrawImage(src, new Rectangle(0, 0, (int)W, (int)H));
                g.Save();
                g.Dispose();
                return r;
            }
            catch (Exception e)
            {
                return null;
            }

        }

        /// <summary>
        /// 使用 HALCON 将 HObject 图像等比缩放至不超过 W×H 的尺寸。
        /// </summary>
        public HObject GetReducedImage(double W, double H, HObject src)
        {
            HObject dst;
            HOperatorSet.GenEmptyObj(out dst);

            try
            {
                HTuple width = new HTuple();
                HTuple height = new HTuple();

                HOperatorSet.GetImageSize(src, out width, out height);
                double _wscale = W / width;
                double _Hscale = H / height;
                double _scale = Math.Min(_wscale, _Hscale);

                W = width * _scale;
                H = height * _scale;
                HOperatorSet.ZoomImageSize(src, out dst, W, H, "constant");
                //Bitmap r = new Bitmap((int)W, (int)H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                //Graphics g = Graphics.FromImage(r);
                //g.Clear(Color.Transparent);
                //g.DrawImage(src, new Rectangle(0, 0, (int)W, (int)H));
                //g.Save();
                //g.Dispose();
                return dst;
            }
            catch (Exception e)
            {
                return null;
            }

        }

    }
}
