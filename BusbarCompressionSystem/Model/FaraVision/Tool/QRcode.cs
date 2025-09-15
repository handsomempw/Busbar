using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusbarCompressionSystem.Model.FaraVision.Tool.QRCode
{
    public class Reg
    {

        private HObject ho_Image;
        private HObject ho_ROI_0;
        private HObject ho_ImageReduced;
        private HObject ho_ImagePart;
        private HObject ho_SymbolXLDs;

        private HTuple hv_Phi = new HTuple();
        private HTuple hv_Area = new HTuple(), hv_Row = new HTuple(), hv_Column = new HTuple(), hv_module_height = new HTuple(), hv_symbol_rows = new HTuple();
        private HTuple hv_PointOrder = new HTuple(), hv_ResultValues = new HTuple();


        private int row1, row2, col1, col2;

        public HWindow Hwindow;


        public List<Barcode> ReadBarcode(HObject _ho_Image, int barcodenum, int _row1, int _col1, int _row2, int _col2, bool showresut = false)
        {
            List<Barcode> barcodes = new List<Barcode>();

            row1 = _row1;
            col1 = _col1;
            row2 = _row2;
            col2 = _col2;

            ho_Image = _ho_Image;




            HOperatorSet.GenEmptyObj(out ho_ROI_0);
            HOperatorSet.GenEmptyObj(out ho_ImageReduced);
            HOperatorSet.GenEmptyObj(out ho_ImagePart);
            HOperatorSet.GenEmptyObj(out ho_SymbolXLDs);
            //HOperatorSet.GenEmptyObj(out ho_ROI_0);           


            try
            {
                //ho_Image = KImage.Image.MatToHobject(mat);
                barcodes = ReadBarcode(barcodenum);
            }
            catch (Exception ex)
            {
                //ho_Image.Dispose();
                //ho_ROI_0.Dispose();
                //ho_ImageReduced.Dispose();
                //ho_ImagePart.Dispose();
                //ho_SymbolXLDs.Dispose();
                //return null;
            }
            //     ho_Image.Dispose();
            ho_ROI_0?.Dispose();
            ho_ImageReduced?.Dispose();
            ho_ImagePart?.Dispose();
            ho_SymbolXLDs?.Dispose();
            return barcodes;

        }

        public List<Barcode> ReadBarcode(int barcodeNum)
        {

            List<Barcode> barcodes = new List<Barcode>();


            // Local control variables 

            HTuple hv_DataCodeHandle = new HTuple(), hv_Length = new HTuple();
            HTuple hv_DecodedDataStrings = new HTuple(), hv_min_gray = new HTuple();
            HTuple hv_max_gray = new HTuple(), hv_Mult = new HTuple();
            HTuple hv_Add = new HTuple(), hv_ResultHandles = new HTuple();
            // Initialize local and output iconic variables 
            HOperatorSet.GenEmptyObj(out ho_SymbolXLDs);
            if (HDevWindowStack.IsOpen())
            {
                HOperatorSet.CloseWindow(HDevWindowStack.Pop());
            }

            //******************识别出二维码
            hv_Length.Dispose();
            hv_Length = 0;
            hv_DecodedDataStrings.Dispose();
            hv_DecodedDataStrings = new HTuple();

            HOperatorSet.CreateDataCode2dModel("QR Code", new HTuple(), new HTuple(), out hv_DataCodeHandle);
            HOperatorSet.SetDataCode2dParam(hv_DataCodeHandle, "persistence", 1);
            HOperatorSet.SetDataCode2dParam(hv_DataCodeHandle, "default_parameters", "enhanced_recognition");


            ho_SymbolXLDs.Dispose(); hv_ResultHandles.Dispose(); hv_DecodedDataStrings.Dispose();


            if (row1 == row2 || col1 == col2)
            {
                HOperatorSet.FindDataCode2d(ho_Image, out ho_SymbolXLDs, hv_DataCodeHandle, "stop_after_result_num",
                   barcodeNum, out hv_ResultHandles, out hv_DecodedDataStrings);
            }
            else
            {
                HOperatorSet.GenRectangle1(out ho_ROI_0, row1, col1, row2, col2);
                HOperatorSet.ReduceDomain(ho_Image, ho_ROI_0, out ho_ImageReduced);

                HOperatorSet.FindDataCode2d(ho_ImageReduced, out ho_SymbolXLDs, hv_DataCodeHandle, "stop_after_result_num",
                    barcodeNum, out hv_ResultHandles, out hv_DecodedDataStrings);
            }




            hv_Phi.Dispose();
            //HOperatorSet.OrientationXld(ho_SymbolXLDs, out hv_Phi);
            hv_Area.Dispose(); hv_Row.Dispose(); hv_Column.Dispose(); hv_PointOrder.Dispose();
            HOperatorSet.AreaCenterXld(ho_SymbolXLDs, out hv_Area, out hv_Row, out hv_Column,
                out hv_PointOrder);

            //HOperatorSet.FitRectangle2ContourXld(ho_SymbolXLDs, "regression", -1, 0, 0, 3, 2,out hv_Row,out  hv_Column,out  hv_Phi, out hv_Length1,out hv_Length2,out hv_PointOrder);

            hv_ResultValues.Dispose();
            HOperatorSet.GetDataCode2dResults(hv_DataCodeHandle, "all_results", "orientation", out hv_ResultValues);
            HOperatorSet.GetDataCode2dResults(hv_DataCodeHandle, "all_results", "symbol_rows", out hv_symbol_rows);
            HOperatorSet.GetDataCode2dResults(hv_DataCodeHandle, "all_results", "module_height", out hv_module_height);
            HOperatorSet.ClearDataCode2dModel(hv_DataCodeHandle);  //must execute


            try
            {
                HTuple ImageW = new HTuple(), ImageH = new HTuple();
                HOperatorSet.GetImageSize(ho_Image, out ImageW, out ImageH);
                Hwindow.ClearWindow();
                //Hwindow.SetPart(0, 0, (int)(ImageH - 1), (int)(ImageW - 1));
                Hwindow.DispObj(ho_Image);
                Hwindow.SetLineWidth(5);
                Hwindow.SetDraw("margin");

                Hwindow.SetColor("green");
                Hwindow.DispObj(ho_SymbolXLDs);
                Hwindow.SetColor("orange");
                Hwindow.DispObj(ho_ROI_0);

            }
            catch { }



            for (int i = 0; i < hv_Row.DArr.Length; i++)
            {
                try
                {
                    int _centerX = (int)hv_Column.DArr[i];
                    int _centerY = (int)hv_Row.DArr[i];
                    int _Width = (int)(hv_symbol_rows.LArr[i] * hv_module_height.DArr[i]);
                    int _Height = (int)(hv_symbol_rows.LArr[i] * hv_module_height.DArr[i]);
                    double _tip_angle = hv_ResultValues.DArr[i];


                    if (_tip_angle < -45 * 3 || _tip_angle > 45 * 3)
                    {
                        _tip_angle = 180;
                    }
                    else if (_tip_angle < -45)
                    {
                        _tip_angle = -90;
                    }
                    else if (_tip_angle > 45)
                    {
                        _tip_angle = 90;
                    }
                    else
                    {
                        _tip_angle = 0;
                    }



                    Barcode barcode = new Barcode()
                    {
                        codestr = hv_DecodedDataStrings[i],
                        Angle = hv_ResultValues.DArr[i],
                        X = _centerX - _Width / 2,
                        Y = _centerY - _Height / 2,
                        Width = _Width,
                        Height = _Height,
                        Tip_double = _tip_angle
                        //Width = (int)hv_Column.DArr[i],
                        //Height = (int)hv_Row.DArr[i]

                    };
                    barcodes.Add(barcode);
                }
                catch (Exception ex) { continue; }
            }


            //*******释放内存
            HOperatorSet.ClearDataCode2dModel(hv_DataCodeHandle);
            //       ho_Image.Dispose();

            ho_SymbolXLDs.Dispose();

            hv_DataCodeHandle.Dispose();
            hv_Length.Dispose();
            hv_DecodedDataStrings.Dispose();
            hv_min_gray.Dispose();
            hv_max_gray.Dispose();
            hv_Mult.Dispose();
            hv_Add.Dispose();
            hv_ResultHandles.Dispose();

            return barcodes;

        }


        public bool RefreshResult(List<Barcode> barcodes)
        {
            try
            {
                foreach (Barcode barcode in barcodes)
                {
                    Hwindow.DispCircle((double)barcode.Y, (double)barcode.X, (double)20);
                }


                return true;
            }
            catch (Exception ex) { return false; }


        }

        public struct Barcode
        {
            public string codestr;
            public int X, Y, Width, Height;
            public double Angle;
            public double Tip_double;

        }


    }
}
