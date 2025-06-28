
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


        public bool TakePhoto1 { set; get; } = false;

        public float Res { set; get; } = 0;
        public float TVVoltage { set; get; } = 0;

        public float TVMaxVoltage { set; get; } = 0;


        public bool TVResult { set; get; } = false;

        public bool AppearanceInspection { set; get; } = false;
        public DateTime DateTime { set; get; } = DateTime.Now;
        public bool Report { set; get; } = false;


    }
}
