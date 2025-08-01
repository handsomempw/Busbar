
using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusbarCompressionSystem.Model.Record
{
    public class ProductInfoRecord : ObservableObject
    {

        public Productinfo Productinfo { set; get; } = new Productinfo();

        public string StationCode { set; get; }
        public string EQUIPMENTID { set; get; }

        public bool TakePhoto1 { set; get; } = false;

        public float Res { set; get; } = 0;
        public float TVVoltage { set; get; } = 0;

        public float TVMaxVoltage { set; get; } = 0;
        public float TVMaxCurrent { set; get; } = 0;


        public bool TVResult { set; get; } = false;

        public bool AppearanceInspection { set; get; } = false;
        public DateTime DateTime { set; get; } = DateTime.Now;

        public float Pressure_Max { set; get; }
        public float Pressure_Average { set; get; }
        public bool Pressure_Result { set; get; }
        public string TVMeterID { set; get; }
        public string TVInfo { set; get; }


        public bool Report { set; get; } = false;


    }
}
