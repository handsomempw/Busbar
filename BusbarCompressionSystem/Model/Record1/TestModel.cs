using BusbarCompressionSystem.Model.Record;
using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace BusbarCompressionSystem.Model.Record1
{
    public class TakePhotoTestModel:ObservableObject
    {
        public Productinfo Productinfo { get; set; } = new Productinfo();
        public Status Status { set; get; } = Status.等待中;

        public string error=string.Empty;
    }

    public class TVTestTestModel:ObservableObject
    {
        public Productinfo Productinfo { get; set; } = new Productinfo();
        public string Status { set; get; } = string.Empty;
        public float Res { set; get; } = 0;

        public float Time { set; get; } = 0;
        public float Current { set; get; } = 0;
        public float Voltage { set; get; } = 0;

        public float TVMaxVoltage { set; get; } = 0;
        public float TVMaxCurrent { set; get; } = 0;

        //public float Pressure_Max { set; get; }
        //public float Pressure_Average { set; get; }
        //public bool Pressure_Result { set; get; }
        public string TVMeterID { set; get; }
        public string TVInfo { set; get; }

    }



    public enum Status
    {
        等待中,
        合格,
        不合格
    }


}
