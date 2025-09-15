using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Camera.Hikvision;
using System.Xml.Serialization;
using GalaSoft.MvvmLight;
using MvCamCtrl.NET;
using System.Windows.Forms;
using GalaSoft.MvvmLight.Command;
using System.IO;
using System.Runtime.InteropServices.ComTypes;

namespace Camera
{
    public class MyEventArgs : EventArgs
    {
        public HObject Image { set; get; }
        public int Height { set; get; }
        public int Width { set; get; }
    }
    public class ErrorEventArgs : EventArgs
    {
        public string CameraID { set; get; }
        public string Error { set; get; }
    }


    public class CamImageFormatConvert
    {
        #region 声明图像变量
        HObject ho_Image;
        #endregion

        #region 图像数据转换方法
        public HObject ImageDataConvet(MyCamera m_pMyCamera, MyCamera.MV_FRAME_OUT_INFO_EX pFrameInfo, IntPtr pImageBuffer, IntPtr pData)
        {

            //判断图片是否为彩色
            if (IsColorPixelFormat(pFrameInfo.enPixelType))
            {

                int bytewidth1 = (pFrameInfo.nWidth * 3 + 3) / 4 * 4;
                int bytewidthg1 = (pFrameInfo.nWidth + 3) / 4 * 4;
                byte[] m_pImageData = new byte[pFrameInfo.nWidth * pFrameInfo.nHeight * 3];
                byte[] m_pImageDataR = new byte[pFrameInfo.nWidth * pFrameInfo.nHeight];
                byte[] m_pImageDataG = new byte[pFrameInfo.nWidth * pFrameInfo.nHeight];
                byte[] m_pImageDataB = new byte[pFrameInfo.nWidth * pFrameInfo.nHeight];
                //byte[] m_pImageDatagray = new byte[pFrameInfo.nWidth * pFrameInfo.nHeight];
                //IntPtr pImageBuffer = Marshal.AllocHGlobal((int)g_nPayloadSize * 3);
                int nRet = ConvertTocam(m_pMyCamera, pData, pFrameInfo.nHeight, pFrameInfo.nWidth, pFrameInfo.enPixelType, pImageBuffer);
                try { Marshal.Copy(pImageBuffer, m_pImageData, 0, pFrameInfo.nWidth * pFrameInfo.nHeight * 3); }
                catch { }

                unsafe
                {
                    for (int j = 0; j < pFrameInfo.nHeight; j++)
                    {
                        for (int i = 0; i < pFrameInfo.nWidth; i++)
                        {
                            m_pImageDataR[j * bytewidthg1 + i] = m_pImageData[j * bytewidth1 + i * 3 + 0];
                            m_pImageDataG[j * bytewidthg1 + i] = m_pImageData[j * bytewidth1 + i * 3 + 1];
                            m_pImageDataB[j * bytewidthg1 + i] = m_pImageData[j * bytewidth1 + i * 3 + 2];
                            // m_pImageDatagray[j * bytewidthg1 + i] = (byte)(0.11 * m_pImageData1[j * bytewidth1 + i * 3 + 0] + 0.59 * m_pImageData1[j * bytewidth1 + i * 3 + 1] + 0.30 * m_pImageData1[j * bytewidth1 + i * 3 + 2]);
                        }
                    }
                    fixed (byte* pR = m_pImageDataR, pG = m_pImageDataG, pB = m_pImageDataB)
                    //fixed (byte* p = m_pImageDatagray)
                    {
                        HOperatorSet.GenImage3(out ho_Image, "byte", pFrameInfo.nWidth, pFrameInfo.nHeight, new IntPtr(pR), new IntPtr(pG), new IntPtr(pB));
                        // HOperatorSet.GenImage1(out ho_Image, "byte", pFrameInfo.nWidth, pFrameInfo.nHeight, new IntPtr(p));
                    }
                }
            }
            //相机为黑白相机
            else if (IsMonoPixelFormat(pFrameInfo.enPixelType))
            {
                int bytewidth = (pFrameInfo.nWidth * 3 + 3) / 4 * 4;
                int bytewidthg = (pFrameInfo.nWidth + 3) / 4 * 4;
                byte[] m_pImageDatagray = new byte[pFrameInfo.nWidth * pFrameInfo.nHeight];
                byte[] m_pImageData = new byte[pFrameInfo.nWidth * pFrameInfo.nHeight * 3];
                //IntPtr pImageBuffer = Marshal.AllocHGlobal((int)g_nPayloadSize * 3);
                int nRet = ConvertTocam(m_pMyCamera, pData, pFrameInfo.nHeight, pFrameInfo.nWidth, pFrameInfo.enPixelType, pImageBuffer);
                try { Marshal.Copy(pImageBuffer, m_pImageData, 0, pFrameInfo.nWidth * pFrameInfo.nHeight * 3); }
                catch { }
                unsafe
                {
                    for (int j = 0; j < pFrameInfo.nHeight; j++)
                    {
                        for (int i = 0; i < pFrameInfo.nWidth; i++)
                        {

                            m_pImageDatagray[j * bytewidthg + i] = (byte)(0.11 * m_pImageData[j * bytewidth + i * 3 + 0] + 0.59 * m_pImageData[j * bytewidth + i * 3 + 1] + 0.30 * m_pImageData[j * bytewidth + i * 3 + 2]);
                        }
                    }
                    fixed (byte* p = m_pImageDatagray)
                    {
                        HOperatorSet.GenImage1(out ho_Image, "byte", pFrameInfo.nWidth, pFrameInfo.nHeight, new IntPtr(p));
                    }
                }
            }
            return ho_Image;
        }
        #endregion

        #region 判断是否为黑白相机
        public bool IsMonoPixelFormat(MyCamera.MvGvspPixelType enType)
        {
            switch (enType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12_Packed:
                    return true;
                default:
                    return false;
            }
        }
        #endregion

        #region 判断是否为彩色相机
        public bool IsColorPixelFormat(MyCamera.MvGvspPixelType enType)
        {
            switch (enType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_RGBA8_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BGRA8_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_YUYV_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12_Packed:
                    return true;
                default:
                    return false;
            }
        }
        #endregion

        #region 图像数据转内存数据
        //图像数据转内存数据
        public Int32 ConvertTocam(object obj, IntPtr pSrc, ushort nHeight, ushort nWidth, MyCamera.MvGvspPixelType nPixelType, IntPtr pDst)
        {
            if (IntPtr.Zero == pSrc || IntPtr.Zero == pDst)
            {
                return MyCamera.MV_E_PARAMETER;
            }

            int nRet = MyCamera.MV_OK;
            MyCamera device = obj as MyCamera;
            MyCamera.MV_PIXEL_CONVERT_PARAM stPixelConvertParam = new MyCamera.MV_PIXEL_CONVERT_PARAM();

            stPixelConvertParam.pSrcData = pSrc;//源数据
            if (IntPtr.Zero == stPixelConvertParam.pSrcData)
            {
                return -1;
            }

            stPixelConvertParam.nWidth = nWidth;//图像宽度
            stPixelConvertParam.nHeight = nHeight;//图像高度
            stPixelConvertParam.enSrcPixelType = nPixelType;//源数据的格式
            stPixelConvertParam.nSrcDataLen = (uint)(nWidth * nHeight * ((((uint)nPixelType) >> 16) & 0x00ff) >> 3);

            stPixelConvertParam.nDstBufferSize = (uint)(nWidth * nHeight * ((((uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed) >> 16) & 0x00ff) >> 3);
            stPixelConvertParam.pDstBuffer = pDst;//转换后的数据
            stPixelConvertParam.enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed;
            stPixelConvertParam.nDstBufferSize = (uint)nWidth * nHeight * 3;

            nRet = device.MV_CC_ConvertPixelType_NET(ref stPixelConvertParam);//格式转换
            if (MyCamera.MV_OK != nRet)
            {
                return -1;
            }

            return MyCamera.MV_OK;
        }
        #endregion
    }


    public class Hikvision : ObservableObject
    {

        public Hikvision()
        {
            //init_Command = new RelayCommand(init);
            DeviceListAcq_Command = new RelayCommand(DeviceListAcq);
            bnOpen_Click_Command = new RelayCommand(bnOpen_Click);
            SetCtrlWhenOpen_Command = new RelayCommand(SetCtrlWhenOpen);
            bnClose_Click_Command = new RelayCommand(bnClose_Click);
            SetCtrlWhenClose_Command = new RelayCommand(SetCtrlWhenClose);
            bnContinuesMode_CheckedChanged_Command = new RelayCommand(bnContinuesMode_CheckedChanged);
            bnTriggerMode_CheckedChanged_Command = new RelayCommand(bnTriggerMode_CheckedChanged);
            SetCtrlWhenStartGrab_Command = new RelayCommand(SetCtrlWhenStartGrab);
            bnStartGrab_Click_Command = new RelayCommand(SetCtrlWhenStartGrab);
            cbSoftTrigger_CheckedChanged_Command = new RelayCommand(cbSoftTrigger_CheckedChanged);
            bnTriggerExec_Click_Command = new RelayCommand(bnTriggerExec_Click);
            SetCtrlWhenStopGrab_Command = new RelayCommand(SetCtrlWhenStopGrab);
            bnStopGrab_Click_Command = new RelayCommand(bnStopGrab_Click);
            bnSaveBmp_Click_Command = new RelayCommand(bnSaveBmp_Click);
            bnSavePng_Click_Command = new RelayCommand(bnSavePng_Click);
            bnSaveTiff_Click_Command = new RelayCommand(bnSaveTiff_Click);
            bnSaveJpg_Click_Command = new RelayCommand(bnSaveJpg_Click);
            bnSetParam_Click_Command = new RelayCommand(bnSetParam_Click);
            bnGetParam_Click_Command = new RelayCommand(bnGetParam_Click);
        }



        [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        MyCamera.MV_CC_DEVICE_INFO_LIST m_stDeviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
        public MyCamera m_MyCamera = new MyCamera();
        MyCamera.cbOutputExdelegate cbImage;
        bool m_bGrabbing = false;
        //Thread m_hReceiveThread = null;
        MyCamera.MV_FRAME_OUT_INFO_EX m_stFrameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();

        // ch:用于从驱动获取图像的缓存 | en:Buffer for getting image from driver
        UInt32 m_nBufSizeForDriver = 0;
        IntPtr m_BufForDriver = IntPtr.Zero;
        public static Object BufForDriverLock = new Object();


        public ObservableCollection<string> cbDeviceList { set; get; } = new ObservableCollection<string>();
        public int SelectedIndex { set; get; } = -1;


        #region 显示
        public bool bnOpenEnabled { set; get; } = false;
        public bool bnCloseEnabled { set; get; } = false;
        public bool bnStartGrabEnabled { set; get; } = false;
        public bool bnStopGrabEnabled { set; get; } = false;
        public bool bnContinuesModeEnabled { set; get; } = false;
        public bool bnTriggerModeEnabled { set; get; } = false;
        public bool cbSoftTriggerEnabled { set; get; } = false;
        public bool bnTriggerExecEnabled { set; get; } = false;
        public bool bnSaveBmpEnabled { set; get; } = false;
        public bool bnSaveJpgEnabled { set; get; } = false;
        public bool bnSaveTiffEnabled { set; get; } = false;
        public bool bnSavePngEnabled { set; get; } = false;
        public bool tbExposureEnabled { set; get; } = false;
        public bool tbGainEnabled { set; get; } = false;
        public bool tbFrameRateEnabled { set; get; } = false;
        public bool bnGetParamEnabled { set; get; } = false;
        public bool bnSetParamEnabled { set; get; } = false;
        public bool bnContinuesModeChecked { set; get; } = false;
        public bool bnTriggerModeChecked { set; get; } = false;
        public bool cbSoftTriggerChecked { set; get; } = false;
        #endregion

        #region 变量
        //public string tbExposureText { set; get; }
        //public string tbGainText { set; get; }
        //public string tbFrameRateText { set; get; }

        [XmlElement("曝光时间")]
        public float Exposure { set; get; }
        //public float Gain { set; get; }
        //public float FrameRate { set; get; }
        [XmlIgnore]
        //public HWindowControlWPF ShowWindow = null;
        public HSmartWindowControlWPF ShowWindow = null;
        #endregion

        [XmlElement("相机序列号")]
        public string CameraID { set; get; } = "00E69008823";


        public HObject ho_Image = new HObject();
        ////定义图像的宽高
        //HTuple w1, h1;
        //用来计算取图时间
        Stopwatch sw1 = new Stopwatch();
        TimeSpan ts1;

        CamImageFormatConvert CamImageConvert = new CamImageFormatConvert();


        // ch:显示错误信息 | en:Show error message
        public void ShowErrorMsg(string csMessage, int nErrorNum)
        {
            string errorMsg;
            if (nErrorNum == 0)
            {
                errorMsg = csMessage;
            }
            else
            {
                errorMsg = csMessage + ": Error =" + String.Format("{0:X}", nErrorNum);
            }

            switch (nErrorNum)
            {
                case MyCamera.MV_E_HANDLE: errorMsg += " Error or invalid handle "; break;
                case MyCamera.MV_E_SUPPORT: errorMsg += " Not supported function "; break;
                case MyCamera.MV_E_BUFOVER: errorMsg += " Cache is full "; break;
                case MyCamera.MV_E_CALLORDER: errorMsg += " Function calling order error "; break;
                case MyCamera.MV_E_PARAMETER: errorMsg += " Incorrect parameter "; break;
                case MyCamera.MV_E_RESOURCE: errorMsg += " Applying resource failed "; break;
                case MyCamera.MV_E_NODATA: errorMsg += " No data "; break;
                case MyCamera.MV_E_PRECONDITION: errorMsg += " Precondition error, or running environment changed "; break;
                case MyCamera.MV_E_VERSION: errorMsg += " Version mismatches "; break;
                case MyCamera.MV_E_NOENOUGH_BUF: errorMsg += " Insufficient memory "; break;
                case MyCamera.MV_E_UNKNOW: errorMsg += " Unknown error "; break;
                case MyCamera.MV_E_GC_GENERIC: errorMsg += " General error "; break;
                case MyCamera.MV_E_GC_ACCESS: errorMsg += " Node accessing condition error "; break;
                case MyCamera.MV_E_ACCESS_DENIED: errorMsg += " No permission "; break;
                case MyCamera.MV_E_BUSY: errorMsg += " Device is busy, or network disconnected "; break;
                case MyCamera.MV_E_NETER: errorMsg += " Network error "; break;
            }

            //MessageBox.Show(errorMsg, "PROMPT");
            try
            {

                ErrorReceived.Invoke(this, new ErrorEventArgs()
                {
                    CameraID=CameraID,
                    Error = errorMsg
                });
            }catch (Exception ex) {; }
        }

        public Boolean IsMonoData(MyCamera.MvGvspPixelType enGvspPixelType)
        {
            switch (enGvspPixelType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12_Packed:
                    return true;

                default:
                    return false;
            }
        }

        /************************************************************************
         *  @fn     IsColorData()
         *  @brief  判断是否是彩色数据
         *  @param  enGvspPixelType         [IN]           像素格式
         *  @return 成功，返回0；错误，返回-1 
         ************************************************************************/
        public Boolean IsColorData(MyCamera.MvGvspPixelType enGvspPixelType)
        {
            switch (enGvspPixelType)
            {
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG8:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG10_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGR12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerRG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerGB12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_BayerBG12_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_Packed:
                case MyCamera.MvGvspPixelType.PixelType_Gvsp_YUV422_YUYV_Packed:
                    return true;

                default:
                    return false;
            }
        }


        public void init(string _CameraID)
        {
            CameraID = _CameraID;
            DeviceListAcq();
            cbImage = new MyCamera.cbOutputExdelegate(ImageCallBack);

        }

        public void DeviceListAcq()
        {
            // ch:创建设备列表 | en:Create Device List
            System.GC.Collect();
            cbDeviceList.Clear();
            m_stDeviceList.nDeviceNum = 0;
            int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref m_stDeviceList);
            if (0 != nRet)
            {
                ShowErrorMsg("枚举相机失败!", 0);
                return;
            }

            // ch:在窗体列表中显示设备名 | en:Display device name in the form list
            for (int i = 0; i < m_stDeviceList.nDeviceNum; i++)
            {
                MyCamera.MV_CC_DEVICE_INFO device = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(m_stDeviceList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(device.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO));

                    if (gigeInfo.chUserDefinedName != "")
                    {
                        cbDeviceList.Add("GEV: " + gigeInfo.chUserDefinedName + " (" + gigeInfo.chSerialNumber + ")");
                    }
                    else
                    {
                        cbDeviceList.Add("GEV: " + gigeInfo.chManufacturerName + " " + gigeInfo.chModelName + " (" + gigeInfo.chSerialNumber + ")");
                    }

                    if (gigeInfo.chSerialNumber.ToUpper() == CameraID.ToUpper())
                    {
                        SelectedIndex = i;
                    }


                }
                else if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(device.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    if (usbInfo.chUserDefinedName != "")
                    {
                        cbDeviceList.Add("U3V: " + usbInfo.chUserDefinedName + " (" + usbInfo.chSerialNumber + ")");
                    }
                    else
                    {
                        cbDeviceList.Add("U3V: " + usbInfo.chManufacturerName + " " + usbInfo.chModelName + " (" + usbInfo.chSerialNumber + ")");
                    }

                    if (usbInfo.chSerialNumber.ToUpper() == CameraID.ToUpper())
                    {
                        SelectedIndex = i;
                    }
                }
            }


            bnOpenEnabled = SelectedIndex >= 0;
            // ch:选择第一项 | en:Select the first item
            //if (m_stDeviceList.nDeviceNum != 0)
            //{
            //    SelectedIndex = 0;
            //}

        }


        public void SetCtrlWhenOpen()
        {
            bnOpenEnabled = false;

            bnCloseEnabled = true;
            bnStartGrabEnabled = true;
            bnStopGrabEnabled = false;
            bnContinuesModeEnabled = true;
            bnContinuesModeChecked = false;
            bnTriggerModeEnabled = true;
            bnTriggerModeChecked = true;
            cbSoftTriggerEnabled = true;
            cbSoftTriggerChecked = true;
            bnTriggerExecEnabled = false;

            tbExposureEnabled = true;
            tbGainEnabled = true;
            tbFrameRateEnabled = true;
            bnGetParamEnabled = true;
            bnSetParamEnabled = true;
        }

        public void bnOpen_Click()
        {
            if (m_stDeviceList.nDeviceNum == 0 || SelectedIndex == -1)
            {
                ShowErrorMsg("请选择需要打开的相机设备", 0);
                return;
            }

            // ch:获取选择的设备信息 | en:Get selected device information
            MyCamera.MV_CC_DEVICE_INFO device =
                (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(m_stDeviceList.pDeviceInfo[SelectedIndex],
                                                              typeof(MyCamera.MV_CC_DEVICE_INFO));

            // ch:打开设备 | en:Open device
            if (null == m_MyCamera)
            {
                m_MyCamera = new MyCamera();
                if (null == m_MyCamera)
                {
                    return;
                }
            }

            int nRet = m_MyCamera.MV_CC_CreateDevice_NET(ref device);
            if (MyCamera.MV_OK != nRet)
            {
                return;
            }

            nRet = m_MyCamera.MV_CC_OpenDevice_NET();
            if (MyCamera.MV_OK != nRet)
            {
                m_MyCamera.MV_CC_DestroyDevice_NET();
                ShowErrorMsg("相机打开失败!", nRet);
                return;
            }

            // ch:探测网络最佳包大小(只对GigE相机有效) | en:Detection network optimal package size(It only works for the GigE camera)
            if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                int nPacketSize = m_MyCamera.MV_CC_GetOptimalPacketSize_NET();
                if (nPacketSize > 0)
                {
                    nRet = m_MyCamera.MV_CC_SetIntValue_NET("GevSCPSPacketSize", (uint)nPacketSize);
                    if (nRet != MyCamera.MV_OK)
                    {
                        ShowErrorMsg("设置数据包大小失败!", nRet);
                    }
                }
                else
                {
                    ShowErrorMsg("获取数据包大小失败!", nPacketSize);
                }
            }

            // ch:设置采集连续模式 | en:Set Continues Aquisition Mode
            int nret0 = m_MyCamera.MV_CC_SetEnumValue_NET("AcquisitionMode", (uint)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS);
            //  int nret1 = m_MyCamera.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
            int nret2 = m_MyCamera.MV_CC_RegisterImageCallBackEx_NET(cbImage, (IntPtr)0);

            bnGetParam_Click();// ch:获取参数 | en:Get parameters

            // ch:控件操作 | en:Control operation
            SetCtrlWhenOpen();
        }

        public void SetCtrlWhenClose()
        {
            bnOpenEnabled = true;

            bnCloseEnabled = false;
            bnStartGrabEnabled = false;
            bnStopGrabEnabled = false;
            bnContinuesModeEnabled = false;
            bnTriggerModeEnabled = false;
            cbSoftTriggerEnabled = false;
            bnTriggerExecEnabled = false;

            bnSaveBmpEnabled = false;
            bnSaveJpgEnabled = false;
            bnSaveTiffEnabled = false;
            bnSavePngEnabled = false;
            tbExposureEnabled = false;
            tbGainEnabled = false;
            tbFrameRateEnabled = false;
            bnGetParamEnabled = false;
            bnSetParamEnabled = false;
        }

        public void bnClose_Click()
        {
            // ch:取流标志位清零 | en:Reset flow flag bit
            if (m_bGrabbing == true)
            {
                m_bGrabbing = false;
                //m_hReceiveThread.Join();
            }

            if (m_BufForDriver != IntPtr.Zero)
            {
                Marshal.Release(m_BufForDriver);
            }

            // ch:关闭设备 | en:Close Device
            m_MyCamera.MV_CC_CloseDevice_NET();
            m_MyCamera.MV_CC_DestroyDevice_NET();

            // ch:控件操作 | en:Control Operation
            SetCtrlWhenClose();
            cbImage = new MyCamera.cbOutputExdelegate(ImageCallBack);
        }

        public void bnContinuesMode_CheckedChanged()
        {
            if (bnContinuesModeChecked)
            {
                m_MyCamera.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
                cbSoftTriggerEnabled = false;
                bnTriggerExecEnabled = false;
            }
        }

        public void bnTriggerMode_CheckedChanged()
        {
            // ch:打开触发模式 | en:Open Trigger Mode
            if (bnTriggerModeChecked)
            {
                int nret = m_MyCamera.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_ON);

                // ch:触发源选择:0 - Line0; | en:Trigger source select:0 - Line0;
                //           1 - Line1;
                //           2 - Line2;
                //           3 - Line3;
                //           4 - Counter;
                //           7 - Software;
                if (cbSoftTriggerChecked)
                {
                    m_MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE);
                    if (m_bGrabbing)
                    {
                        bnTriggerExecEnabled = true;
                    }
                }
                else
                {
                    m_MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_LINE0);
                }
                cbSoftTriggerEnabled = true;
            }
        }

        public void SetCtrlWhenStartGrab()
        {
            bnStartGrabEnabled = false;
            bnStopGrabEnabled = true;

            if (bnTriggerModeChecked && cbSoftTriggerChecked)
            {
                bnTriggerExecEnabled = true;
            }

            bnSaveBmpEnabled = true;
            bnSaveJpgEnabled = true;
            bnSaveTiffEnabled = true;
            bnSavePngEnabled = true;
        }

        #region 取流回调函数 
        // ch:取流回调函数 | en:Aquisition Callback Function
        public void ImageCallBack(IntPtr pData, ref MyCamera.MV_FRAME_OUT_INFO_EX pFrameInfo, IntPtr pUser)
        {
            try
            {
                int nIndex = (int)pUser;
                // ch:抓取的帧数 | en:Aquired Frame Number
                ho_Image?.Dispose();

                if (bnContinuesModeChecked)  //相机一实时状态显示
                {
                    ho_Image = CamImageConvert.ImageDataConvet(m_MyCamera, pFrameInfo, m_BufForDriver, pData);
                    //HOperatorSet.GetImageSize(ho_Image, out w1, out h1);
                    //HOperatorSet.SetPart(ShowWindow.HalconWindow, 0, 0, h1 - 1, w1 - 1);
                    //HOperatorSet.DispObj(ho_Image, ShowWindow.HalconWindow);
                }
                else if (bnTriggerModeChecked) //相机一触发模式用来运行图像处理
                {
                    sw1.Reset();
                    sw1.Start();
                    ho_Image = CamImageConvert.ImageDataConvet(m_MyCamera, pFrameInfo, m_BufForDriver, pData);
                    //HOperatorSet.GetImageSize(ho_Image, out w1, out h1);
                    //HOperatorSet.SetPart(ShowWindow.HalconWindow, 0, 0, h1 - 1, w1 - 1);
                    //HOperatorSet.DispObj(ho_Image, ShowWindow.HalconWindow);
                    sw1.Stop();
                    ts1 = sw1.Elapsed;
                    string s1 = ts1.TotalMilliseconds.ToString("f2");
                    m_stFrameInfo = pFrameInfo;
                    //触发图片接收事件
                    ImageReceived.Invoke(this, new MyEventArgs()
                    {
                        Image = ho_Image,
                        Width = pFrameInfo.nWidth,
                        Height = pFrameInfo.nHeight
                    });


                }
            }
            catch (Exception ex)
            {

            }
        }
        #endregion




        public void bnStartGrab_Click()
        {


            //m_hReceiveThread = new Thread(ReceiveThreadProcess);
            //m_hReceiveThread.Start();

            m_stFrameInfo.nFrameLen = 0;//取流之前先清除帧长度
            m_stFrameInfo.enPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_Undefined;
            // ch:开始采集 | en:Start Grabbing
            int nRet = m_MyCamera.MV_CC_StartGrabbing_NET();
            if (MyCamera.MV_OK != nRet)
            {
                m_bGrabbing = false;
                //m_hReceiveThread.Join();
                ShowErrorMsg("启动图像抓取失败!", nRet);
                return;
            }


            // ch:控件操作 | en:Control Operation
            SetCtrlWhenStartGrab();

            // ch:标志位置位true | en:Set position bit true
            m_bGrabbing = true;

            // ch:获取包大小 || en: Get Payload Size
            MyCamera.MVCC_INTVALUE stParam = new MyCamera.MVCC_INTVALUE();
            nRet = m_MyCamera.MV_CC_GetIntValue_NET("PayloadSize", ref stParam);
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("相机二获取数据包大小失败", nRet);
                return;
            }
            m_nBufSizeForDriver = stParam.nCurValue;
            //在内存中开辟一个空间用来存图
            m_BufForDriver = Marshal.AllocHGlobal((int)m_nBufSizeForDriver * 3);

        }

        public void cbSoftTrigger_CheckedChanged()
        {
            if (cbSoftTriggerChecked)
            {
                // ch:触发源设为软触发 | en:Set trigger source as Software
                m_MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE);
                if (m_bGrabbing)
                {
                    bnTriggerExecEnabled = true;
                }
            }
            else
            {
                m_MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_LINE0);
                bnTriggerExecEnabled = false;
            }
        }

        public void bnTriggerExec_Click()
        {
            int nret = m_MyCamera.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_ON);

            m_MyCamera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE);

            // ch:触发命令 | en:Trigger command
            int nRet = m_MyCamera.MV_CC_SetCommandValue_NET("TriggerSoftware");
            if (MyCamera.MV_OK != nRet)
            {
                ShowErrorMsg("设置软触发失败!", nRet);
            }
        }

        public void SetCtrlWhenStopGrab()
        {
            bnStartGrabEnabled = true;
            bnStopGrabEnabled = false;

            bnTriggerExecEnabled = false;


            bnSaveBmpEnabled = false;
            bnSaveJpgEnabled = false;
            bnSaveTiffEnabled = false;
            bnSavePngEnabled = false;
        }

        public void bnStopGrab_Click()
        {
            // ch:标志位设为false | en:Set flag bit false
            m_bGrabbing = false;
            //m_hReceiveThread.Join();

            // ch:停止采集 | en:Stop Grabbing
            int nRet = m_MyCamera.MV_CC_StopGrabbing_NET();
            if (nRet != MyCamera.MV_OK)
            {
                ShowErrorMsg("停止图像抓取失败!", nRet);
            }

            // ch:控件操作 | en:Control Operation
            SetCtrlWhenStopGrab();
        }

        public void bnSaveBmp_Click()
        {
            if (false == m_bGrabbing)
            {
                ShowErrorMsg("未启动图像抓取", 0);
                return;
            }

            if (RemoveCustomPixelFormats(m_stFrameInfo.enPixelType))
            {
                ShowErrorMsg("不支持的图像格式!", 0);
                return;
            }

            MyCamera.MV_SAVE_IMG_TO_FILE_PARAM stSaveFileParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM();

            string filename = $"{Environment.CurrentDirectory}\\图片\\{DateTime.Now.ToString("yyyyMMdd_HHmmssFFF")}.bmp";
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            { Directory.CreateDirectory(dir); }
            
            lock (BufForDriverLock)
            {
                if (m_stFrameInfo.nFrameLen == 0)
                {
                    ShowErrorMsg("保存bmp图片失败!", 0);
                    return;
                }
                stSaveFileParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Bmp;
                stSaveFileParam.enPixelType = m_stFrameInfo.enPixelType;
                stSaveFileParam.pData = m_BufForDriver;
                stSaveFileParam.nDataLen = m_stFrameInfo.nFrameLen;
                stSaveFileParam.nHeight = m_stFrameInfo.nHeight;
                stSaveFileParam.nWidth = m_stFrameInfo.nWidth;
                stSaveFileParam.iMethodValue = 2;
             
                stSaveFileParam.pImagePath = filename;
                int nRet = m_MyCamera.MV_CC_SaveImageToFile_NET(ref stSaveFileParam);
                if (MyCamera.MV_OK != nRet)
                {
                    ShowErrorMsg("保存bmp图片失败!", nRet);
                    return;
                }
            }

            ShowErrorMsg("保存成功!", 0);
            Process.Start(dir);


        }

        public void bnSaveJpg_Click()
        {
            if (false == m_bGrabbing)
            {
                ShowErrorMsg("未启动图像抓取", 0);
                return;
            }

            if (RemoveCustomPixelFormats(m_stFrameInfo.enPixelType))
            {
                ShowErrorMsg("不支持的图像格式!", 0);
                return;
            }

            MyCamera.MV_SAVE_IMG_TO_FILE_PARAM stSaveFileParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM();

            string filename = $"{Environment.CurrentDirectory}\\图片\\{DateTime.Now.ToString("yyyyMMdd_HHmmssFFF")}.jpg";
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            { Directory.CreateDirectory(dir); }
            
            lock (BufForDriverLock)
            {
                if (m_stFrameInfo.nFrameLen == 0)
                {
                    ShowErrorMsg("保存jpg图片失败!", 0);
                    return;
                }
                stSaveFileParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Jpeg;
                stSaveFileParam.enPixelType = m_stFrameInfo.enPixelType;
                stSaveFileParam.pData = m_BufForDriver;
                stSaveFileParam.nDataLen = m_stFrameInfo.nFrameLen;
                stSaveFileParam.nHeight = m_stFrameInfo.nHeight;
                stSaveFileParam.nWidth = m_stFrameInfo.nWidth;
                stSaveFileParam.nQuality = 80;
                stSaveFileParam.iMethodValue = 2;
             
                stSaveFileParam.pImagePath = filename;
                int nRet = m_MyCamera.MV_CC_SaveImageToFile_NET(ref stSaveFileParam);
                if (MyCamera.MV_OK != nRet)
                {
                    ShowErrorMsg("保存jpg图片失败!", nRet);
                    return;
                }
            }

            ShowErrorMsg("保存成功!", 0);
            Process.Start(dir);


        }

        public void bnSavePng_Click()
        {
            if (false == m_bGrabbing)
            {
                ShowErrorMsg("未启动图像抓取", 0);
                return;
            }

            if (RemoveCustomPixelFormats(m_stFrameInfo.enPixelType))
            {
                ShowErrorMsg("不支持的图像格式!", 0);
                return;
            }

            MyCamera.MV_SAVE_IMG_TO_FILE_PARAM stSaveFileParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM();

            string filename = $"{Environment.CurrentDirectory}\\图片\\{DateTime.Now.ToString("yyyyMMdd_HHmmssFFF")}.png";
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            { Directory.CreateDirectory(dir); }
            
            lock (BufForDriverLock)
            {
                if (m_stFrameInfo.nFrameLen == 0)
                {
                    ShowErrorMsg("保存png图片失败!", 0);
                    return;
                }
                stSaveFileParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Png;
                stSaveFileParam.enPixelType = m_stFrameInfo.enPixelType;
                stSaveFileParam.pData = m_BufForDriver;
                stSaveFileParam.nDataLen = m_stFrameInfo.nFrameLen;
                stSaveFileParam.nHeight = m_stFrameInfo.nHeight;
                stSaveFileParam.nWidth = m_stFrameInfo.nWidth;
                stSaveFileParam.nQuality = 8;
                stSaveFileParam.iMethodValue = 2;
             
                stSaveFileParam.pImagePath = filename;
                int nRet = m_MyCamera.MV_CC_SaveImageToFile_NET(ref stSaveFileParam);
                if (MyCamera.MV_OK != nRet)
                {
                    ShowErrorMsg("保存png图片失败!", nRet);
                    return;
                }
            }

            ShowErrorMsg("保存成功!", 0);
            Process.Start(dir);


        }

        public void bnSaveTiff_Click()
        {
            if (false == m_bGrabbing)
            {
                ShowErrorMsg("未启动图像抓取", 0);
                return;
            }

            if (RemoveCustomPixelFormats(m_stFrameInfo.enPixelType))
            {
                ShowErrorMsg("不支持的图像格式!", 0);
                return;
            }

            MyCamera.MV_SAVE_IMG_TO_FILE_PARAM stSaveFileParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM();
            string filename = $"{Environment.CurrentDirectory}\\图片\\{DateTime.Now.ToString("yyyyMMdd_HHmmssFFF")}.tiff";
            string dir = Path.GetDirectoryName(filename);
            if (!Directory.Exists(dir))
            { Directory.CreateDirectory(dir); }
            
            lock (BufForDriverLock)
            {
                if (m_stFrameInfo.nFrameLen == 0)
                {
                    ShowErrorMsg("保存tiff图片失败!", 0);
                    return;
                }
                stSaveFileParam.enImageType = MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Tif;
                stSaveFileParam.enPixelType = m_stFrameInfo.enPixelType;
                stSaveFileParam.pData = m_BufForDriver;
                stSaveFileParam.nDataLen = m_stFrameInfo.nFrameLen;
                stSaveFileParam.nHeight = m_stFrameInfo.nHeight;
                stSaveFileParam.nWidth = m_stFrameInfo.nWidth;
                stSaveFileParam.iMethodValue = 2;
             
                stSaveFileParam.pImagePath = filename;
                int nRet = m_MyCamera.MV_CC_SaveImageToFile_NET(ref stSaveFileParam);
                if (MyCamera.MV_OK != nRet)
                {
                    ShowErrorMsg("保存tiff图片失败!", nRet);
                    return;
                }
            }
            ShowErrorMsg("保存成功!", 0);
            Process.Start(dir);
        }

        public void bnGetParam_Click()
        {
            MyCamera.MVCC_FLOATVALUE stParam = new MyCamera.MVCC_FLOATVALUE();
            int nRet = m_MyCamera.MV_CC_GetFloatValue_NET("ExposureTime", ref stParam);
            if (MyCamera.MV_OK == nRet)
            {
                Exposure = stParam.fCurValue;
                //tbExposureText = stParam.fCurValue.ToString("F1");
            }

            //nRet = m_MyCamera.MV_CC_GetFloatValue_NET("Gain", ref stParam);
            //if (MyCamera.MV_OK == nRet)
            //{
            //    Gain = stParam.fCurValue;
            //    //tbGainText = stParam.fCurValue.ToString("F1");
            //}

            //nRet = m_MyCamera.MV_CC_GetFloatValue_NET("ResultingFrameRate", ref stParam);
            //if (MyCamera.MV_OK == nRet)
            //{
            //    FrameRate = stParam.fCurValue;
            //    //tbFrameRateText = stParam.fCurValue.ToString("F1");
            //}
        }

        public void bnSetParam_Click()
        {
            //try
            //{
            //    float.Parse(tbExposureText);
            //    float.Parse(tbGainText);
            //    float.Parse(tbFrameRateText);
            //}
            //catch
            //{
            //    ShowErrorMsg("请输入正确的数据格式 !", 0);
            //    return;
            //}

            m_MyCamera.MV_CC_SetEnumValue_NET("ExposureAuto", 0);
            int nRet = m_MyCamera.MV_CC_SetFloatValue_NET("ExposureTime", Exposure);
            if (nRet != MyCamera.MV_OK)
            {
                ShowErrorMsg("设置曝光时间失败!", nRet);
            }

            //m_MyCamera.MV_CC_SetEnumValue_NET("GainAuto", 0);
            //nRet = m_MyCamera.MV_CC_SetFloatValue_NET("Gain", Gain);
            //if (nRet != MyCamera.MV_OK)
            //{
            //    ShowErrorMsg("设置增益失败!", nRet);
            //}

            //nRet = m_MyCamera.MV_CC_SetFloatValue_NET("AcquisitionFrameRate", FrameRate);
            //if (nRet != MyCamera.MV_OK)
            //{
            //    ShowErrorMsg("设置帧率失败!", nRet);
            //}
        }




        // ch:去除自定义的像素格式 | en:Remove custom pixel formats
        public bool RemoveCustomPixelFormats(MyCamera.MvGvspPixelType enPixelFormat)
        {
            UInt32 nResult = ((UInt32)enPixelFormat) & (unchecked((UInt32)0x80000000));
            if (0x80000000 == nResult)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public event EventHandler ImageReceived;
        public event EventHandler ErrorReceived;

        #region Command
        [XmlIgnore]
        public RelayCommand init_Command { set; get; }
        [XmlIgnore]
        public RelayCommand DeviceListAcq_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnOpen_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand SetCtrlWhenOpen_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnClose_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand SetCtrlWhenClose_Command { set; get; }

        [XmlIgnore]
        public RelayCommand bnContinuesMode_CheckedChanged_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnTriggerMode_CheckedChanged_Command { set; get; }
        [XmlIgnore]
        public RelayCommand SetCtrlWhenStartGrab_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnStartGrab_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand cbSoftTrigger_CheckedChanged_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnTriggerExec_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand SetCtrlWhenStopGrab_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnStopGrab_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnSaveBmp_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnSavePng_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnSaveTiff_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnSaveJpg_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnSetParam_Click_Command { set; get; }
        [XmlIgnore]
        public RelayCommand bnGetParam_Click_Command { set; get; }
        #endregion


    }
}
