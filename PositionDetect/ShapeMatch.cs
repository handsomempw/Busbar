using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace PositionDetect
{
    public class ShapeMatch : ObservableObject
    {
        public HTuple modelID = null;
        //public HWindowControlWPF HWindow = null;
        public BasicData BasicData = new BasicData();
        public HWindow HWindow = null;

        public bool init(string filename)
        {
            try
            {
                HOperatorSet.ReadShapeModel(filename, out modelID);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public Result Match(HObject image, int num, int row1, int row2, int col1, int col2,int MinAngle=-20,int MaxAngle=20, bool redraw = true)
        {


            HObject ho_ROI_0;
            HObject ho_ImageReduced;
            HOperatorSet.GenEmptyObj(out ho_ImageReduced);
            HOperatorSet.GenEmptyObj(out ho_ROI_0);

            HObject ho_ModelContours, ho_ContoursAffinTrans;
            HOperatorSet.GenEmptyObj(out ho_ModelContours);
            HOperatorSet.GenEmptyObj(out ho_ContoursAffinTrans);

            try
            {
                //HWindow.HalconWindow.ClearWindow();
            

                HTuple width, height;
                HOperatorSet.GetImageSize(image, out width, out height);
                //HWindow.HalconWindow.SetPart(0, 0, (int)height - 1, (int)width - 1);
                
                if (redraw)
                {
                    HWindow.ClearWindow();
                    HWindow.SetPart(0, 0, (int)height - 1, (int)width - 1);
                    HWindow.DispObj(image);
                }

                HTuple hv_Row = new HTuple(), hv_Column = new HTuple(), hv_Angle = new HTuple(), hv_Score = new HTuple();

        

                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                    HOperatorSet.GenRectangle1(out ho_ROI_0, row1, row2, col1, col2);
                    HOperatorSet.ReduceDomain(image, ho_ROI_0, out ho_ImageReduced);

                    hv_Row.Dispose(); hv_Column.Dispose(); hv_Angle.Dispose(); hv_Score.Dispose();

                    HOperatorSet.FindShapeModel(ho_ImageReduced, modelID, (new HTuple(MinAngle)).TupleRad()
                   , (new HTuple(MaxAngle)).TupleRad(), 0.5, num, 0.5, "least_squares", 4, 0.9, out hv_Row,
                out hv_Column, out hv_Angle, out hv_Score);
                }

                //ho_ModelContours.Dispose();
                HOperatorSet.GetShapeModelContours(out ho_ModelContours, modelID, 1);

                //HWindow.HalconWindow.SetLineWidth(2);
                //HWindow.HalconWindow.DispObj(image);
                //HWindow.HalconWindow.SetColor("red");
                HWindow.SetLineWidth(2);              
                HWindow.SetColor("red");
                Result r = new Result()
                {
                    points = new List<Result_Parameter>()
                };

                Debug.WriteLine("分值");
                for (int i = 0; i < hv_Row.DArr.Length; i++)
                {
                    Debug.WriteLine($"{hv_Column.DArr[i]},{hv_Row.DArr[i]},{hv_Angle.DArr[i]},{hv_Score.DArr[i]}");

                    HTuple hv_HomMat2D = new HTuple();
                    //将模板映射到目标上
                    hv_HomMat2D.Dispose();
                    HOperatorSet.VectorAngleToRigid(0, 0, 0, hv_Row[i], hv_Column[i], hv_Angle[i], out hv_HomMat2D);
                    ho_ContoursAffinTrans.Dispose();
                    HOperatorSet.AffineTransContourXld(ho_ModelContours, out ho_ContoursAffinTrans, hv_HomMat2D);
                    HWindow.DispObj(ho_ContoursAffinTrans);

                    Result_Parameter rp = new Result_Parameter()
                    {
                        row = hv_Row.DArr[i],
                        column = hv_Column.DArr[i],
                        angle = hv_Angle.DArr[i],
                        score = hv_Score.DArr[i],
                    };
                    r.points.Add(rp);
                }
                HWindow.SetDraw("margin");
                HWindow.SetColor("yellow");
                HWindow.DispObj(ho_ROI_0);
                HWindow.SetColor("green");

                ho_ROI_0?.Dispose();
                ho_ImageReduced?.Dispose();
                ho_ModelContours?.Dispose();
                ho_ContoursAffinTrans?.Dispose();

                r.IsSuccess = true;
                return r;
            }
            catch (Exception ex)
            {
                ho_ROI_0?.Dispose();
                ho_ImageReduced?.Dispose();
                ho_ModelContours?.Dispose();
                ho_ContoursAffinTrans?.Dispose();

                return new Result() { IsSuccess = false, ErrorInfo = ex.ToString() };
            }
        }

        public ResultData Analysis_Result(Result r)
        {
            if (!r.IsSuccess)
            {
                return null;
            }
            ResultData ResultData = new ResultData();
            if (r.IsSuccess && r.points.Count >= 1)
            {
                var rp = r.points[0];

                double deltaX = rp.column - BasicData.matchcenterX_Basic;
                double deltaY = rp.row - BasicData.matchcenterY_Basic;

                double radius = Math.Sqrt(Math.Pow(BasicData.productcenter_X - BasicData.matchcenter_X, 2) +
                    Math.Pow(BasicData.productcenter_Y - BasicData.matchcenter_Y, 2));
                double angle = 0;
                if (radius != 0)
                {
                    angle = Math.Asin((BasicData.productcenter_Y - BasicData.matchcenter_Y) / radius);
                    if (BasicData.matchcenterX_Basic > BasicData.productcenter_X)
                    {
                        angle = Math.PI - angle;
                    }
                }
                double r_X = Math.Cos(-rp.angle + angle) * radius;
                double r_Y = Math.Sin(-rp.angle + angle) * radius;

                double b_X = Math.Cos(angle) * radius;
                double b_Y = Math.Sin(angle) * radius;

                ResultData.X_actual = (deltaX + r_X + BasicData.matchcenterX_Basic) * BasicData.K;
                ResultData.Y_actual = (deltaY + r_Y + BasicData.matchcenterY_Basic) * BasicData.K;

                ResultData.deltaX_actual = (deltaX + r_X - b_X) * BasicData.K;
                ResultData.deltaY_actual = (deltaY + r_Y - b_Y) * BasicData.K;
                ResultData.angle = rp.angle;
                ResultData.score = rp.score;

                ResultData.isSuccess = true;
            }

            return ResultData;
        }

        public struct Result
        {
            public List<Result_Parameter> points;
            public bool IsSuccess;
            public string ErrorInfo;
        }
        public struct Result_Parameter
        {
            public double row, column, angle, score;
        }


    }
    public class BasicData : ObservableObject
    {
        public double K { set; get; } = 1;
        public double matchcenter_X = 0;
        public double matchcenter_Y = 0;
        public double productcenter_X = 0;
        public double productcenter_Y = 0;
        public double matchcenterX_Basic = 0;
        public double matchcenterY_Basic = 0;
        public double dirX = 1;
        public double dirY = 1;
    }

    public class ResultData : ObservableObject
    {
        public double X_actual { set; get; } = 0;
        public double Y_actual { set; get; } = 0;
        public double deltaX_actual { set; get; } = 0;
        public double deltaY_actual { set; get; } = 0;
        public double angle { set; get; } = 0;
        public double score = 0;
        public bool isSuccess { set; get; } = false;
    }

}
