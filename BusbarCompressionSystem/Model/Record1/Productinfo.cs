using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusbarCompressionSystem.Model.Record
{
    public class Productinfo:ObservableObject
    {
        public string SN { get; set; } = string.Empty;
        public string WOCODE { set; get; } = string.Empty;
        public string PartNOID { set; get; } = string.Empty;
    }
}
