using BusbarCompressionSystem.Model.FaraVision.Tool.QRCode;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.FaraVision;
using BusbarCompressionSystem.Model.Record;
using Camera;
using GalaSoft.MvvmLight;
using HalconDotNet;
using Panuon.WPF.UI;
using PositionDetect;
using SQLITEDATABASE;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows;
using System;


namespace BusbarCompressionSystem.ViewModel
{
    public partial class MainViewModel : ViewModelBase
    {
        // 相机相关方法拆分到独立文件，便于维护
        // 运行态日志节流：避免相机回调高频导致日志刷屏（尤其在无产品/空跑时）
        private readonly object _cameraReceiveLogLock = new object();
        private DateTime[] _lastCameraReceiveLogUtc = new DateTime[6]; // 1..5使用

        private void LogCameraReceiveThrottled(int cameraIndex, bool onlyWhenExpectingShot)
        {
            // cameraIndex: 1..5
            if (cameraIndex < 1 || cameraIndex > 5) { return; }

            // 对拍照留底(1~3)：仅在“本次流程确实在等照片”(finished=false)时记录一次
            if (onlyWhenExpectingShot)
            {
                try
                {
                    bool finished;
                    if (cameraIndex == 1)
                        finished = DataModel.Settingmodel.camedata1.CameraModel.finished;
                    else if (cameraIndex == 2)
                        finished = DataModel.Settingmodel.camedata2.CameraModel.finished;
                    else if (cameraIndex == 3)
                        finished = DataModel.Settingmodel.camedata3.CameraModel.finished;
                    else
                        finished = true;
                    if (finished)
                    {
                        return;
                    }
                }
                catch
                {
                    // 任何异常都不影响拍照回调主流程
                }
            }

            // 对AOI/其他(4~5) 或者异常触发：做时间节流，默认 1s 最多一次
            DateTime nowUtc = DateTime.UtcNow;
            lock (_cameraReceiveLogLock)
            {
                if (_lastCameraReceiveLogUtc[cameraIndex] != default
                    && (nowUtc - _lastCameraReceiveLogUtc[cameraIndex]).TotalMilliseconds < 1000)
                {
                    return;
                }
                _lastCameraReceiveLogUtc[cameraIndex] = nowUtc;
            }

            writeLog($"相机->视觉:接收照片(C{cameraIndex})", false);
        }

        #region 相机初始化
        public void InitCamera()
        {
            DataModel.Settingmodel.camedata1.init1($"{Environment.CurrentDirectory}\\配置\\相机配置1.xml");
            DataModel.Settingmodel.camedata2.init1($"{Environment.CurrentDirectory}\\配置\\相机配置2.xml");
            DataModel.Settingmodel.camedata3.init1($"{Environment.CurrentDirectory}\\配置\\相机配置3.xml");
            DataModel.Settingmodel.camedata4.init1($"{Environment.CurrentDirectory}\\配置\\相机配置4.xml");
            DataModel.Settingmodel.camedata5.init1($"{Environment.CurrentDirectory}\\配置\\相机配置5.xml");

            DataModel.Settingmodel.camedata1.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata2.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata3.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata4.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;
            DataModel.Settingmodel.camedata5.CameraModel.camera.ErrorReceived += OnCameraErrorReceive;

            DataModel.Settingmodel.camedata1.init2();
            DataModel.Settingmodel.camedata2.init2();
            DataModel.Settingmodel.camedata3.init2();
            DataModel.Settingmodel.camedata4.init2();
            DataModel.Settingmodel.camedata5.init2();

            start();
        }

        public void CloseCamera()
        {
            DataModel.Settingmodel.camedata1.closing($"{Environment.CurrentDirectory}\\配置\\相机配置1.xml");
            DataModel.Settingmodel.camedata2.closing($"{Environment.CurrentDirectory}\\配置\\相机配置2.xml");
            DataModel.Settingmodel.camedata3.closing($"{Environment.CurrentDirectory}\\配置\\相机配置3.xml");
            DataModel.Settingmodel.camedata4.closing($"{Environment.CurrentDirectory}\\配置\\相机配置4.xml");
            DataModel.Settingmodel.camedata5.closing($"{Environment.CurrentDirectory}\\配置\\相机配置5.xml");
        }

        #endregion

        #region 拍照留底
        /// <summary>
        /// 初始化拍照留底和AOI检测的相机事件订阅。
        /// </summary>
        /// <remarks>
        /// 订阅5个相机的图像接收事件：
        /// - 相机1-3：拍照留底工位使用，接收到图像后保存并设置完成标志
        /// - 相机4：AOI外观检测工位使用，接收到图像后进行视觉识别
        /// - 相机5：预留相机（如有需要）
        /// 
        /// 调用时机：
        /// - 系统初始化时调用一次，建立相机与事件处理函数的绑定关系
        /// </remarks>
        public void start()
        {
            DataModel.Settingmodel.camedata1.CameraModel.camera.ImageReceived += OnCamera1Receive;
            DataModel.Settingmodel.camedata2.CameraModel.camera.ImageReceived += OnCamera2Receive;
            DataModel.Settingmodel.camedata3.CameraModel.camera.ImageReceived += OnCamera3Receive;
            DataModel.Settingmodel.camedata4.CameraModel.camera.ImageReceived += OnCamera4Receive;
            DataModel.Settingmodel.camedata5.CameraModel.camera.ImageReceived += OnCamera5Receive;
        }

        private void OnCameraErrorReceive(object sender, EventArgs e)
        {
            try
            {
                Camera.ErrorEventArgs errorEventArgs = e as Camera.ErrorEventArgs;
                NoticeBox.Show($"{errorEventArgs.Error}", $"相机错误-{errorEventArgs.CameraID}", MessageBoxIcon.Error, true, 10000);
            }
            catch (Exception ex) { }
        }

        private void OnCamera1Receive(object sender, EventArgs e)
        {

            try
            {
                LogCameraReceiveThrottled(1, onlyWhenExpectingShot: true);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnReceiveProcess(DataModel.Settingmodel.HWindow1, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 1);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, "拍照留底", 1, "OK");
                    DataModel.Settingmodel.camedata1.CameraModel.finished = true;

                    GC.Collect();
                }));

            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }
        private void OnCamera2Receive(object sender, EventArgs e)
        {

            try
            {
                LogCameraReceiveThrottled(2, onlyWhenExpectingShot: true);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnReceiveProcess(DataModel.Settingmodel.HWindow2, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 2);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, "拍照留底", 2, "OK");
                    DataModel.Settingmodel.camedata2.CameraModel.finished = true;

                    GC.Collect();
                }));

            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }

        /// <summary>
        /// 相机3图像接收事件处理函数（拍照留底工位）。
        /// </summary>
        /// <remarks>
        /// 处理流程：
        /// 1. 接收相机3传来的图像数据（MyEventArgs包含图像、宽度、高度）
        /// 2. 在UI线程中显示图像到HWindow3窗口
        /// 3. 保存图像到本地文件系统（文件名包含SN、工位、相机编号）
        /// 4. 设置相机3完成标志位（finished = true）
        /// 5. 触发垃圾回收释放内存
        /// 
        /// 调用时机：
        /// - 相机3执行软触发拍照后，图像采集完成时自动触发
        /// - 在 TakePhoto1Process() 中会轮询检查此完成标志位
        /// 
        /// 注意：
        /// - 此方法在相机回调线程中执行，需要通过Dispatcher切换到UI线程操作界面
        /// </remarks>
        private void OnCamera3Receive(object sender, EventArgs e)
        {
            try
            {
                LogCameraReceiveThrottled(3, onlyWhenExpectingShot: true);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnReceiveProcess(DataModel.Settingmodel.HWindow3, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 3);
                    SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, "拍照留底", 3, "OK");
                    DataModel.Settingmodel.camedata3.CameraModel.finished = true;
                    GC.Collect();
                }));
            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }


        private void OnCamera4Receive(object sender, EventArgs e)
        {

            try
            {
                LogCameraReceiveThrottled(4, onlyWhenExpectingShot: false);

                MyEventArgs myEventArgs = e as MyEventArgs;

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    //#region 仅保存照片
                    //OnReceiveProcess(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 4);
                    //SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, "外观检测", 1, "OK");
                    //SendMsgRobot("OK");
                    //#endregion


                    #region AOI识别
                    //OnReceiveProcessAOI(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 4);
                    OnReceiveProcessAOI(myEventArgs.Image, myEventArgs.Height, myEventArgs.Width);

                    #endregion

                    GC.Collect();
                }));

            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }
        private void OnCamera5Receive(object sender, EventArgs e)
        {

            try
            {
                LogCameraReceiveThrottled(5, onlyWhenExpectingShot: false);
                MyEventArgs myEventArgs = e as MyEventArgs;
                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    //#region 仅保存照片
                    //OnReceiveProcess(DataModel.Settingmodel.HWindow4, myEventArgs.Image, myEventArgs.Height, myEventArgs.Width, 5);
                    //SaveImage(myEventArgs.Image, DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, "外观检测", 1, "OK");

                    //if (DataModel.Processmodel.CMD == "A5")
                    //{
                    //    DateTime dt = DateTime.Now;
                    //    updatetakephoto2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, true, dt);
                    //    sqlite.UpdateTakePhoto2(
                    //        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                    //        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                    //        DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                    //        true);
                    //}
                    //SendMsgRobot("OK");
                    //#endregion

                    #region AOI识别
                    OnReceiveProcessAOI(myEventArgs.Image, myEventArgs.Height, myEventArgs.Width);
                    #endregion

                    GC.Collect();
                }));
            }
            catch (Exception ex)
            {
                // writeError($"[{DataModel.Processmodel.RCMD}]识别错误:{ex.ToString()}");
            }
        }


        /// <summary>
        /// 处理相机接收到的图像并显示到指定窗口（拍照留底工位）。
        /// </summary>
        /// <param name="hwindow">Halcon显示窗口对象</param>
        /// <param name="Image">相机采集的图像对象（HObject）</param>
        /// <param name="H">图像高度（像素）</param>
        /// <param name="W">图像宽度（像素）</param>
        /// <param name="Index">相机编号（1-5）</param>
        /// <remarks>
        /// 处理流程：
        /// 1. 检查图像通道数（CountChannels）
        /// 2. 如果是单通道灰度图，转换为三通道RGB图（Compose3）
        /// 3. 清空显示窗口（ClearWindow）
        /// 4. 在窗口中显示图像（DispObj）
        /// 
        /// 用途：
        /// - 拍照留底工位的三个相机（1-3）接收图像后调用
        /// - 仅用于图像显示，不进行视觉识别
        /// 
        /// 注意：
        /// - 单通道图像会被转换为三通道后释放原图像，避免内存泄漏
        /// </remarks>
        public void OnReceiveProcess(HWindow hwindow, HObject Image, int H, int W, int Index)
        {
            #region 图片接收
            //Image
            HOperatorSet.CountChannels(Image, out var channels);
            if (channels == 1)
            {
                HOperatorSet.Compose3(Image, Image, Image, out var multiChannelImage);
                Image.Dispose();
                Image = multiChannelImage;
            }
            hwindow.ClearWindow();
            hwindow.DispObj(Image);
            #endregion
        }

        /// <summary>
        /// AOI外观检测工位的图像接收和视觉识别处理（相机4）。
        /// </summary>
        /// <param name="Image">相机采集的图像对象（HObject）</param>
        /// <param name="H">图像高度（像素）</param>
        /// <param name="W">图像宽度（像素）</param>
        /// <remarks>
        /// 处理流程：
        /// 1. 图像预处理：单通道转三通道，显示到AOI窗口
        /// 2. 遍历所有配置的视觉工具（Tools），执行对应的检测算法：
        ///    - 二维码识别：读取二维码并可选上报给扫码汇报软件
        ///    - 模板匹配：检测产品位置、角度偏移是否在允许范围内
        ///    - 面积检测：计算区域面积是否在阈值范围内
        ///    - 尺寸测量：测量产品尺寸是否符合规格
        /// 3. 根据检测结果保存图像到OK/NG目录
        /// 4. 综合判断所有工具的结果：
        ///    - 全部OK → 状态=OK
        ///    - 有NG2（缺件/测量失败）→ 状态=NG2
        ///    - 有NG（不合格）→ 状态=NG
        /// 5. 更新数据库中的外观检测结果（UpdateTakePhoto2）
        /// 6. 可选发送结果给机器人（SendMsgRobot）
        /// 
        /// 调用时机：
        /// - 相机4（AOI工位）接收到图像后自动触发
        /// - 在 OnCamera4Receive() 事件处理函数中调用
        /// 
        /// 注意：
        /// - 此方法包含复杂的视觉算法，执行时间较长（通常几百毫秒）
        /// - 需要在UI线程中执行，以便更新界面显示
        /// - 内存管理：及时释放Bitmap和HObject，避免GDI句柄泄漏
        /// </remarks>
        public void OnReceiveProcessAOI(HObject Image, int H, int W)
        {
            try
            {
                #region 图片接收
                //Image
                HOperatorSet.CountChannels(Image, out var channels);
                if (channels == 1)
                {
                    HOperatorSet.Compose3(Image, Image, Image, out var multiChannelImage);
                    Image.Dispose();
                    Image = multiChannelImage;
                }

                HWindow hwindow = DataModel.FaraVisionDataModel.Settingmodel.HWindow;
                hwindow.ClearWindow();
                //hwindow.SetPart(0, 0, H - 1, W - 1);
                hwindow.DispObj(Image);
                for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
                {
                    if (DataModel.FaraVisionDataModel.Processmodel.Tools[i].Command == DataModel.FaraVisionDataModel.Processmodel.RCMD)
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();

                        if (i == 0)
                        {
                            DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.识别中;
                        }
                        DataModel.FaraVisionDataModel.Processmodel.ToolIndex = i + 1;
                        ToolModel tool = DataModel.FaraVisionDataModel.Processmodel.Tools[i];
                        string templateMatchTraceDetail = string.Empty;
                        Bitmap bmp;
                        try
                        {
                            var dst = GetReducedImage(DataModel.FaraVisionDataModel.Settingmodel.ImageSize, DataModel.FaraVisionDataModel.Settingmodel.ImageSize, Image);
                            Hobject2Bitmap.HobjectToBitmap24(dst, out bmp);
                            // 修复GDI句柄泄漏：GetHbitmap()创建的句柄需要手动释放
                            IntPtr hBitmap = bmp.GetHbitmap();
                            try
                            {
                                tool.CurrentBitmapSource = null;
                                tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            }
                            finally
                            {
                                DeleteObject(hBitmap); // 释放GDI句柄
                            }
                            bmp?.Dispose();
                            dst?.Dispose();
                        }
                        catch (Exception e)
                        {; }



                        //Bitmap bmp;
                        //try
                        //{
                        //    Hobject2Bitmap.HobjectToBitmap24(Image, out bmp);

                        //    using (Bitmap bmp1 = GetReducedImage(DataModel.Settingmodel.ImageSize, DataModel.Settingmodel.ImageSize, bmp))
                        //    {
                        //        tool.CurrentBitmapSource = null;
                        //        tool.CurrentBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bmp1.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        //    }
                        //    bmp.Dispose();
                        //}
                        //catch (Exception ex)
                        //{

                        //}


                        ClearTool(tool);
                        tool.ToolStatus = ToolStatus.识别中;
                        try
                        {
                            if (tool.TestMode == TestModes.二维码)
                            {
                                #region 读取二维码
                                List<Reg.Barcode> barcodelist = null;

                                try
                                {
                                    barcodelist = tool.Reg.ReadBarcode(Image, 1, tool.BarCodeROI.Row1, tool.BarCodeROI.Col1, tool.BarCodeROI.Row2, tool.BarCodeROI.Col2, true);
                                    if (barcodelist.Count > 0)
                                    {
                                        tool.BarcodeStr = barcodelist[0].codestr;
                                        //  NoticeBox.Show(barcodelist[0].codestr, "二维码读取成功", MessageBoxIcon.Success, true, 5000);
                                        //if (tool.SendBarcodeData)
                                        //{
                                        //    SendMsgSoft(tool.BarcodeStr);
                                        //}
                                        writeLog($"二维码读取成功:{barcodelist[0].codestr}");

                                        if (tool.MPMode)
                                        {
                                            DataModel.FaraVisionDataModel.Processmodel.Barcodes += barcodelist[0].codestr + ";";
                                        }
                                        if (tool.SendBarcode)
                                        {
                                            tool.ToolStatus = ToolStatus.等待中;

                                            if (DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.Connect(DataModel.FaraVisionDataModel.Settingmodel.BarcodeReporter.RemoteIP, DataModel.FaraVisionDataModel.Settingmodel.BarcodeReporter.RemotePort))
                                            {

                                                DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.SendMsg($"{DataModel.FaraVisionDataModel.Processmodel.Scannerstr};{DataModel.FaraVisionDataModel.Processmodel.Barcodes}{tool.FinishedCode}\r\n");
                                                string s = DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.ReceiveMsg(10000);
                                                s = s.Replace("\r", "").Replace("\n", "");
                                                if (!string.IsNullOrEmpty(s))
                                                {
                                                    App.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                    {
                                                        DataModel.FaraVisionDataModel.Processmodel.SNList.Add(s);
                                                    }));
                                                    tool.ToolStatus = ToolStatus.OK;
                                                    writeLog($"扫码汇报软件返回成品编号:{s}");
                                                    DataModel.FaraVisionDataModel.Processmodel.Barcodes = string.Empty;


                                                }
                                                else
                                                {
                                                    tool.ToolStatus = ToolStatus.NG2;
                                                    writeLog($"扫码汇报软件返回为空");

                                                }

                                                DataModel.FaraVisionDataModel.Settingmodel.TcpClientH.DisConnect();
                                            }
                                            else
                                            {
                                                tool.ToolStatus = ToolStatus.NG2;
                                                writeLog($"连接扫码汇报软件NG2");
                                            }


                                        }
                                        else
                                        {
                                            tool.ToolStatus = ToolStatus.OK;
                                        }



                                        //#region 发送二维码给上位机设备
                                        //DataModel.Settingmodel.TcpServerSoft.SendMessage(tool.BarcodeStr);
                                        //Thread.Sleep(tool.Delaytimes);
                                        //string s = DataModel.Settingmodel.TcpServerSoft.GetMsg();

                                        //writeLog($"二维码校验返回错误:{s}");

                                        //if (string.IsNullOrEmpty(s) || s != "OK")
                                        //{

                                        //}
                                        //else
                                        //{

                                        //}

                                        //if (!string.IsNullOrEmpty(tool.FinishedCode))
                                        //{
                                        //    DataModel.Settingmodel.TcpServerSoft.SendMessage(tool.FinishedCode);
                                        //    Thread.Sleep(tool.Delaytimes);
                                        //    string sn = DataModel.Settingmodel.TcpServerSoft.GetMsg();
                                        //    if (!string.IsNullOrEmpty(sn))
                                        //    {
                                        //        App.Current.Dispatcher.BeginInvoke(new Action(() =>
                                        //        {
                                        //            DataModel.Processmodel.SNList.Add(sn);
                                        //        }));
                                        //    }
                                        //}

                                        //#endregion


                                    }
                                    else
                                    {
                                        tool.BarcodeStr = string.Empty;
                                        writeLog($"二维码读取失败:{barcodelist[0].codestr}");
                                        tool.ToolStatus = ToolStatus.NG;

                                    }
                                }
                                catch
                                {
                                    ;
                                }

                                #endregion
                            }
                            else if (tool.TestMode == TestModes.模板匹配)
                            {
                                #region 读取位置
                                /// NG  未安装好  NG2:缺少配件  OK:合格              

                                try
                                {
                                    if (!tool.ShapeMatch.ModelLoaded)
                                    {
                                        tool.ToolStatus = ToolStatus.NG2;
                                        templateMatchTraceDetail = "形状模型未加载，已跳过 FindShapeModel";
                                    }
                                    else
                                    {
                                        ShapeMatch.Result shapmatchresult = new ShapeMatch.Result();
                                        tool.ShapeMatch.BasicData.matchcenter_X = tool.InitX;
                                        tool.ShapeMatch.BasicData.matchcenter_Y = tool.InitY;
                                        tool.ShapeMatch.BasicData.matchcenterX_Basic = tool.InitX;
                                        tool.ShapeMatch.BasicData.matchcenterY_Basic = tool.InitY;
                                        tool.ShapeMatch.BasicData.productcenter_X = tool.InitX;
                                        tool.ShapeMatch.BasicData.productcenter_Y = tool.InitY;
                                        shapmatchresult = tool.ShapeMatch.Match(Image, 1, tool.PositionROI.Row1, tool.PositionROI.Col1, tool.PositionROI.Row2, tool.PositionROI.Col2, (int)-tool.AllowAngleDelta, (int)tool.AllowAngleDelta, true);

                                        if (!shapmatchresult.IsSuccess)
                                        {
                                            tool.ToolStatus = ToolStatus.NG2;
                                            templateMatchTraceDetail = string.IsNullOrEmpty(shapmatchresult.ErrorInfo)
                                                ? "Match返回失败"
                                                : shapmatchresult.ErrorInfo;
                                        }
                                        else
                                        {
                                            var Result_Data = tool.ShapeMatch.Analysis_Result(shapmatchresult);
                                            if (Result_Data != null)
                                            {
                                                tool.ActualX = Result_Data.X_actual;
                                                tool.ActualY = Result_Data.Y_actual;
                                                tool.ActualAngle = (Result_Data.angle / Math.PI * 180.0);
                                                tool.ActualScore = Result_Data.score;
                                                tool.DeltaX = Result_Data.deltaX_actual * tool.K / 1000;
                                                tool.DeltaY = Result_Data.deltaY_actual * tool.K / 1000;


                                                if (tool.ActualScore >= tool.MinScore)
                                                {
                                                    if (Math.Abs(tool.DeltaX) < tool.Allow_X_Delta &&
                                                        Math.Abs(tool.DeltaY) < tool.Allow_Y_Delta &&
                                                        Math.Abs(tool.ActualAngle) < tool.AllowAngleDelta
                                                        )
                                                    {
                                                        tool.ToolStatus = ToolStatus.OK;
                                                        templateMatchTraceDetail = "偏差在允许范围内";
                                                    }
                                                    else
                                                    {
                                                        tool.ToolStatus = ToolStatus.NG;
                                                        templateMatchTraceDetail = "位置或角度超差";
                                                    }

                                                }
                                                else
                                                {
                                                    tool.ToolStatus = ToolStatus.NG2;
                                                    templateMatchTraceDetail = "匹配分值低于下限";
                                                }

                                            }
                                            else
                                            {
                                                tool.ToolStatus = ToolStatus.NG2;
                                                templateMatchTraceDetail = "Analysis_Result返回空";
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    tool.ToolStatus = ToolStatus.NG2;
                                    templateMatchTraceDetail = ex.Message;
                                    writeLog($"模板匹配异常: {ex.Message}", false);
                                }


                                #endregion
                            }
                            else if (tool.TestMode == TestModes.面积)
                            {
                                #region 读取面积

                                int Areaint = CoculateDimension(Image, tool, hwindow, false);
                                tool.ActualDimension = Areaint;

                                if (Areaint >= tool.MinDimension && Areaint <= tool.MaxDimension)
                                {
                                    tool.ToolStatus = ToolStatus.OK;
                                }
                                else
                                {
                                    tool.ToolStatus = ToolStatus.NG;
                                }

                                GC.Collect();
                                #endregion
                            }
                            else if (tool.TestMode == TestModes.尺寸测量)
                            {
                                #region 尺寸测量

                                try
                                {
                                    ClearDimensionRemeasureTrace(tool);

                                    // AOI 流程在接收图像时已经清空窗口并显示当前图像；
                                    // 尺寸测量这里只叠加完整测量层，避免找边预览覆盖测距结果。
                                    double measureValue = MeasureDimension(Image, tool, hwindow, true);
                                    tool.ActualMeasureValue = measureValue;

                                    if (measureValue >= 0 && measureValue >= tool.MinMeasureValue && measureValue <= tool.MaxMeasureValue)
                                    {
                                        tool.ToolStatus = ToolStatus.OK;
                                    }
                                    else if (measureValue < 0)
                                    {
                                        tool.ToolStatus = ToolStatus.NG2; // 测量失败
                                    }
                                    else
                                    {
                                        tool.ToolStatus = ToolStatus.NG; // 测量值超出范围
                                    }
                                }
                                catch (Exception ex)
                                {
                                    tool.ToolStatus = ToolStatus.NG2;
                                    // 明确失败态测量值，避免保留上一次/初始值导致误判
                                    tool.ActualMeasureValue = -1;
                                    tool.LastMeasurePixelValue = -1;
                                    writeLog($"尺寸测量失败: {ex.Message}", false);

                                    if (IsDimensionEdgeDetectFailure(ex))
                                    {
                                        TryRemeasureDimensionFromSavedNgImage(Image, tool, hwindow, ex);
                                    }
                                }

                                GC.Collect();
                                #endregion
                            }
                            else if (tool.TestMode == TestModes.直线检测)
                            {
                                #region 直线检测

                                try
                                {
                                    var lineResult = DetectLinePresence(Image, tool, hwindow, true);
                                    if (lineResult.DetectFailed)
                                    {
                                        tool.ToolStatus = ToolStatus.NG2;
                                        tool.LastLineDetectFailReason = lineResult.FailReason;
                                        writeLog($"直线检测失败[{tool.Name}]: {lineResult.FailReason}", false);
                                    }
                                    else if (lineResult.JudgementOk)
                                    {
                                        tool.ToolStatus = ToolStatus.OK;
                                        writeLog(
                                            $"直线检测OK[{tool.Name}]: 角度偏差={lineResult.AngleDeviation:F2}°, 命中率={lineResult.EdgeHitRatio:P0}, 分数={lineResult.FitScore:F2}",
                                            false);
                                    }
                                    else
                                    {
                                        tool.ToolStatus = ToolStatus.NG;
                                        tool.LastLineDetectFailReason = lineResult.FailReason;
                                        writeLog(
                                            $"直线检测NG[{tool.Name}]: {lineResult.FailReason}, 角度偏差={lineResult.AngleDeviation:F2}°, 命中率={lineResult.EdgeHitRatio:P0}",
                                            false);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    tool.ToolStatus = ToolStatus.NG2;
                                    tool.LastLineDetectFailReason = ex.Message;
                                    writeLog($"直线检测异常[{tool.Name}]: {ex.Message}", false);
                                }

                                GC.Collect();
                                #endregion
                            }
                        }
                        catch {; }

                        writeLog($"视觉->视觉:计算完成", false);
                        string s1 = $"{DataModel.FaraVisionDataModel.Processmodel.Tools[i].Name}:识别耗时:{stopwatch.ElapsedMilliseconds}ms";
                        Save_record(s1);
                        stopwatch.Restart();

                        #region 保存结果到数据库和更新状态
                        if (i == DataModel.FaraVisionDataModel.Processmodel.Tools.Count - 1)
                        {
                            int status = -1;
                            int c = DataModel.FaraVisionDataModel.Processmodel.Tools.Count();


                            var ok = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                      where (ToolModel.ToolStatus == ToolStatus.OK)
                                      select ToolModel);
                            var NG1 = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                       where (ToolModel.ToolStatus == ToolStatus.NG)
                                       select ToolModel);
                            var NG2 = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                       where (ToolModel.ToolStatus == ToolStatus.NG2)
                                       select ToolModel);
                            var wait = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                        where (ToolModel.ToolStatus == ToolStatus.等待中 || ToolModel.ToolStatus == ToolStatus.识别中)
                                        select ToolModel);

                            if (ok.Count() == c)
                            {
                                status = 0;
                            }
                            else if (NG2.Count() > 0)
                            {
                                status = 2;
                            }
                            else
                            {
                                status = 1;
                            }

                            if (status == 0)
                            {
                                DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.OK;
                                //PLC_write((UInt16)1);
                            }
                            else if (status == 2)
                            {
                                DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.NG2;
                                //PLC_write((UInt16)3);
                            }
                            else
                            {
                                DataModel.FaraVisionDataModel.Processmodel.Status = ToolStatus.NG;
                                //PLC_write((UInt16)2);
                            }

                            #region 保存拍照记录到本地
                            DateTime dt = DateTime.Now;
                            updatetakephoto2(DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN, status == 0, dt);
                            bool updateAoiDbOk = sqlite.UpdateTakePhoto2(
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID,
                                DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN,
                                status == 0);
                            if (!updateAoiDbOk)
                            {
                                // 写库失败必须在UI日志可见，否则会出现“UI/DB不一致、CHECK2判定异常”难排查
                                writeLog($"[AOI] 写入数据库TAKEPHOTO2失败：WOCODE={DataModel.Processmodel.TakePhotoTestMode2.Productinfo.WOCODE}, PartNOID={DataModel.Processmodel.TakePhotoTestMode2.Productinfo.PartNOID}, SN={DataModel.Processmodel.TakePhotoTestMode2.Productinfo.SN}, 结果={(status == 0 ? "OK" : "NG")}", true);
                            }
                            #endregion
                        }
                        #endregion

                        #region 保存图片
                        try
                        {
                            if (tool.ToolStatus == ToolStatus.OK)
                            {
                                if (DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.SaveOK)
                                {

                                    string savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\OK\\{DataModel.FaraVisionDataModel.Processmodel.BarcodeStr}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    try
                                    {
                                        savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\OK\\{DataModel.FaraVisionDataModel.Processmodel.SNList[DataModel.FaraVisionDataModel.Processmodel.Tools[i].ProductPositionNO]}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    }
                                    catch {; }

                                    string dir = Path.GetDirectoryName(savefilename);
                                    if (!Directory.Exists(dir))
                                    { Directory.CreateDirectory(dir); }
                                    HOperatorSet.WriteImage(Image, "jpg", 0, savefilename);
                                    // 运行态追溯：记录该工具本次落盘图片路径（便于对齐“日志记录 ↔ 图片文件”）
                                    tool.LastResultImagePath = savefilename;
                                }
                            }
                            else
                            {
                                if (DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.SaveNG)
                                {
                                    string savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\NG\\{DataModel.FaraVisionDataModel.Processmodel.BarcodeStr}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    try
                                    {
                                        savefilename = $"{DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\外观检测\\{DateTime.Now.ToString("yyyyMMdd")}\\NG\\{DataModel.FaraVisionDataModel.Processmodel.SNList[DataModel.FaraVisionDataModel.Processmodel.Tools[i].ProductPositionNO]}-{tool.Index.ToString("00")}-{tool.Name}-{tool.ToolStatus}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
                                    }
                                    catch {; }
                                    string dir = Path.GetDirectoryName(savefilename);
                                    if (!Directory.Exists(dir))
                                    { Directory.CreateDirectory(dir); }
                                    HOperatorSet.WriteImage(Image, "jpg", 0, savefilename);
                                    // 运行态追溯：记录该工具本次落盘图片路径（便于对齐“日志记录 ↔ 图片文件”）
                                    tool.LastResultImagePath = savefilename;

                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            writeLog($"视觉->保存照片:保存失败：{ex.ToString()}", false);
                        }

                        #endregion

                        #region 保存尺寸测量日志
                        // 仅当测试模式为尺寸测量时，记录测量结果到日志文件
                        if (tool.TestMode == TestModes.尺寸测量)
                        {
                            try
                            {
                                // 直接使用AOI工位的产品信息（扫码时已填充，包含完整的SN、WOCODE、PartNOID）
                                Productinfo productInfo = DataModel.Processmodel.TakePhotoTestMode2.Productinfo;
                                
                                // 安全检查：如果产品信息为空或SN为空，使用备用方案
                                if (productInfo == null || string.IsNullOrEmpty(productInfo.SN))
                                {
                                    // 备用方案：从SNList获取SN
                                    string sn = "";
                                    try
                                    {
                                        sn = DataModel.FaraVisionDataModel.Processmodel.SNList[tool.ProductPositionNO];
                                    }
                                    catch
                                    {
                                        sn = DataModel.FaraVisionDataModel.Processmodel.BarcodeStr ?? "UNKNOWN";
                                    }
                                    
                                    productInfo = new Productinfo
                                    {
                                        SN = sn,
                                        WOCODE = "",
                                        PartNOID = ""
                                    };
                                }

                                // 写入测量日志 + 诊断日志（同一把锁、同一时间戳，便于对齐排查）
                                DateTime measureTime = DateTime.Now;
                                WriteMeasurementLogs(tool, productInfo, measureTime);
                            }
                            catch (Exception ex)
                            {
                                // 日志写入失败不影响主流程，仅记录错误
                                writeLog($"保存尺寸测量日志异常: {ex.Message}", false);
                            }
                        }
                        #endregion

                        #region 保存模板匹配追溯日志
                        if (tool.TestMode == TestModes.模板匹配)
                        {
                            try
                            {
                                string shmfilename = $"{DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{DataModel.FaraVisionDataModel.Settingmodel.Name}\\Tool{tool.Index}.shm";
                                Productinfo productInfo = DataModel.Processmodel.TakePhotoTestMode2.Productinfo;

                                if (productInfo == null || string.IsNullOrEmpty(productInfo.SN))
                                {
                                    string sn = string.Empty;
                                    try
                                    {
                                        sn = DataModel.FaraVisionDataModel.Processmodel.SNList[tool.ProductPositionNO];
                                    }
                                    catch
                                    {
                                        sn = DataModel.FaraVisionDataModel.Processmodel.BarcodeStr ?? "UNKNOWN";
                                    }

                                    productInfo = new Productinfo
                                    {
                                        SN = sn,
                                        WOCODE = string.Empty,
                                        PartNOID = string.Empty
                                    };
                                }

                                DateTime traceTime = DateTime.Now;
                                WriteTemplateMatchTraceLog(
                                    "在线匹配",
                                    tool,
                                    shmfilename,
                                    templateMatchTraceDetail,
                                    productInfo,
                                    traceTime);
                            }
                            catch (Exception ex)
                            {
                                writeLog($"保存模板匹配追溯日志异常: {ex.Message}", false);
                            }
                        }
                        #endregion

                        if (tool.SendStatus)
                        {

                            int status = -1;

                            var r = (from ToolModel in DataModel.FaraVisionDataModel.Processmodel.Tools
                                     where ToolModel.Command == DataModel.FaraVisionDataModel.Processmodel.RCMD
                                     select ToolModel);
                            var wait = (from ToolModel in r
                                        where (ToolModel.ToolStatus == ToolStatus.等待中 || ToolModel.ToolStatus == ToolStatus.识别中)
                                        select ToolModel);

                            if (wait.Count() == 0)
                            {
                                var ok = (from ToolModel in r
                                          where (ToolModel.ToolStatus == ToolStatus.OK)
                                          select ToolModel);

                                if (ok.Count() == r.Count())
                                {
                                    status = 0;
                                }
                                else
                                {
                                    var NG2 = (from ToolModel in r
                                               where (ToolModel.ToolStatus == ToolStatus.NG2)
                                               select ToolModel);
                                    if (NG2.Count() > 0)
                                    {
                                        status = 2;
                                    }
                                    else
                                    {
                                        status = 1;
                                    }

                                }
                            }

                            switch (status)
                            {

                                case 0:
                                    {
                                        SendMsgRobot(tool.OKCMD.Trim());
                                        break;
                                    }
                                case 1:
                                    {
                                        SendMsgRobot(tool.NG1CMD.Trim());
                                        break;
                                    }
                                case 2:
                                    {
                                        SendMsgRobot(tool.NG2CMD.Trim());
                                        break;
                                    }
                                default:
                                    {
                                        break;
                                    }
                            }
                        }

                        writeLog($"视觉->视觉:保存完成", false);

                        string s2 = $"{DataModel.FaraVisionDataModel.Processmodel.Tools[i].Name}:保存图片发送结果耗时:{stopwatch.ElapsedMilliseconds}ms";
                        Save_record(s2);
                    }
                }
                #endregion

            }
            catch (Exception ex) {; }

        }

        /// <summary>
        /// 判断尺寸测量失败是否属于 Metrology 找边失败。
        /// 该判断只用于 AOI 在线图失败后的落盘图复测，校准缺失、ROI 无效和尺寸超限保持原判定路径。
        /// </summary>
        /// <param name="ex">尺寸测量流程抛出的异常，通常包含 MeasureDimension 包装后的业务提示。</param>
        /// <returns>true 表示失败原因来自测量对象边缘检测，可进入落盘图复测；false 表示保持原失败结果。</returns>
        private bool IsDimensionEdgeDetectFailure(Exception ex)
        {
            string message = ex?.ToString() ?? string.Empty;
            return message.Contains("边缘检测失败");
        }

        /// <summary>
        /// 清理尺寸测量复测的运行态诊断字段。
        /// AOI 每个工具进入尺寸测量前调用，保证日志只描述当前拍照周期的在线图和落盘图结果。
        /// </summary>
        /// <param name="tool">当前 AOI 尺寸测量工具；为空时直接返回。</param>
        private void ClearDimensionRemeasureTrace(ToolModel tool)
        {
            if (tool == null)
            {
                return;
            }

            tool.DimensionRemeasureAttempted = false;
            tool.DimensionRemeasureSucceeded = false;
            tool.DimensionRemeasureImagePath = null;
            tool.DimensionRemeasureOriginalError = string.Empty;
            tool.DimensionRemeasureError = string.Empty;
            tool.DimensionRemeasureMeasureValue = -1;
            tool.DimensionRemeasurePixelValue = -1;
            tool.DimensionRemeasureMessage = string.Empty;
        }

        /// <summary>
        /// 在线相机内存图尺寸测量找边失败后，保存 NG 图片并读取该图片复测一次。
        /// 复测使用同一个工具参数和尺寸上下限；复测成功时最终状态按复测尺寸判定，复测失败时保持 NG2。
        /// 诊断字段记录首次失败原因、复测图片、复测测量值和复测失败原因，供现场对齐“在线图”和“落盘图”差异。
        /// </summary>
        /// <param name="sourceImage">相机回调得到的在线 HALCON 图像，作为复测 NG 图片的保存来源。</param>
        /// <param name="tool">当前尺寸测量工具，包含 ROI、校准系数、Metrology 参数和尺寸上下限。</param>
        /// <param name="hwindow">AOI 显示窗口；复测只计算尺寸，传入窗口用于保持 MeasureDimension 调用签名一致。</param>
        /// <param name="originalException">在线图首次尺寸测量失败原因，写入诊断日志用于追溯。</param>
        private void TryRemeasureDimensionFromSavedNgImage(HObject sourceImage, ToolModel tool, HWindow hwindow, Exception originalException)
        {
            if (sourceImage == null || tool == null)
            {
                return;
            }

            tool.DimensionRemeasureAttempted = true;
            tool.DimensionRemeasureSucceeded = false;
            tool.DimensionRemeasureOriginalError = originalException?.Message ?? string.Empty;
            tool.DimensionRemeasureError = string.Empty;
            tool.DimensionRemeasureMeasureValue = -1;
            tool.DimensionRemeasurePixelValue = -1;
            tool.DimensionRemeasureMessage = "在线图找边失败，准备保存NG图复测";

            string remeasureImagePath = string.Empty;
            HObject remeasureImage = null;

            try
            {
                remeasureImagePath = BuildDimensionRemeasureImagePath(tool);
                string dir = Path.GetDirectoryName(remeasureImagePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                HOperatorSet.WriteImage(sourceImage, "jpg", 0, remeasureImagePath);
                tool.DimensionRemeasureImagePath = remeasureImagePath;

                HOperatorSet.ReadImage(out remeasureImage, remeasureImagePath);
                double remeasureValue = MeasureDimension(remeasureImage, tool, hwindow, false);
                if (remeasureValue < 0)
                {
                    throw new Exception("落盘图复测返回失败值-1");
                }

                tool.DimensionRemeasureSucceeded = true;
                tool.DimensionRemeasureMeasureValue = remeasureValue;
                tool.DimensionRemeasurePixelValue = tool.LastMeasurePixelValue;
                tool.ActualMeasureValue = remeasureValue;

                if (remeasureValue >= tool.MinMeasureValue && remeasureValue <= tool.MaxMeasureValue)
                {
                    tool.ToolStatus = ToolStatus.OK;
                }
                else
                {
                    tool.ToolStatus = ToolStatus.NG;
                }

                tool.DimensionRemeasureMessage = "在线图找边失败，落盘图复测成功，最终按复测尺寸判定";
                writeLog($"尺寸测量复测成功[{tool.Name}]: 在线图找边失败，落盘图={Path.GetFileName(remeasureImagePath)}，复测值={remeasureValue:F3}mm，最终状态={tool.ToolStatus}", false);
            }
            catch (Exception retryEx)
            {
                tool.ToolStatus = ToolStatus.NG2;
                tool.ActualMeasureValue = -1;
                tool.LastMeasurePixelValue = -1;
                tool.DimensionRemeasureSucceeded = false;
                tool.DimensionRemeasureMeasureValue = -1;
                tool.DimensionRemeasurePixelValue = -1;
                tool.DimensionRemeasureError = retryEx.Message;
                tool.DimensionRemeasureMessage = "在线图找边失败，落盘图复测失败，工具结果保持NG2";
                writeLog($"尺寸测量复测失败[{tool.Name}]: 在线图找边失败，落盘图={Path.GetFileName(remeasureImagePath)}，原因={retryEx.Message}", false);
            }
            finally
            {
                remeasureImage?.Dispose();
            }
        }

        /// <summary>
        /// 生成尺寸测量找边失败后的落盘复测图片路径。
        /// 图片放入 AOI 当天 NG 目录，文件名带 Remeasure 标记，便于和最终 OK/NG/NG2 图片区分。
        /// </summary>
        /// <param name="tool">当前尺寸测量工具，提供产品位置、工具序号和工具名。</param>
        /// <returns>用于写入和读回复测的 JPG 图片完整路径。</returns>
        private string BuildDimensionRemeasureImagePath(ToolModel tool)
        {
            string imageSaveDir = DataModel.FaraVisionDataModel.Settingmodel.ImageSaveSetting.ImageSaveDir;
            if (string.IsNullOrWhiteSpace(imageSaveDir))
            {
                imageSaveDir = Environment.CurrentDirectory;
            }

            string sn = DataModel.FaraVisionDataModel.Processmodel.BarcodeStr ?? "UNKNOWN";
            try
            {
                sn = DataModel.FaraVisionDataModel.Processmodel.SNList[tool.ProductPositionNO];
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(sn))
                {
                    sn = "UNKNOWN";
                }
            }

            return $"{imageSaveDir}\\外观检测\\{DateTime.Now:yyyyMMdd}\\NG\\{sn}-{tool.Index:00}-{tool.Name}-NG2-Remeasure-{DateTime.Now:yyyyMMddHHmmssFFF}.jpg";
        }

        public void ClearTools()
        {
            for (int i = 0; i < DataModel.FaraVisionDataModel.Processmodel.Tools.Count; i++)
            {
                ClearTool(DataModel.FaraVisionDataModel.Processmodel.Tools[i]);
            }
        }

        public void ClearTool(ToolModel tool)
        {
            tool.BarcodeStr = string.Empty;
            tool.DeltaX = 0;
            tool.DeltaY = 0;
            tool.ActualScore = 0;
            tool.ActualX = 0;
            tool.ActualY = 0;
            tool.ActualAngle = 0;
            tool.ActualDimension = 0;
            // 清理运行态字段，避免界面/日志误用上一周期残留数据
            tool.ActualMeasureValue = 0;
            tool.LastResultImagePath = null;
            tool.LastMeasurePixelValue = -1;
            ClearDimensionRemeasureTrace(tool);
            tool.ActualLineAngle = 0;
            tool.ActualAngleDeviation = 0;
            tool.LastEdgeHitRatio = 0;
            tool.LastLineDetectScore = 0;
            tool.LastLineDetectFailReason = string.Empty;
            tool.ToolStatus = ToolStatus.等待中;

        }

        #endregion

    }
}
