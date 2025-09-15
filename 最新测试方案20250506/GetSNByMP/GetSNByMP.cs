using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GetSNByMP
{
    public class GetSNByMP
    {

        public string result_str { set; get; } = string.Empty;
        public string err_message { set; get; } = string.Empty;

        public bool issuccess { set; get; } = false;

        public ProductSerial ProductSerial { set; get; } = new ProductSerial();


        private readonly GraphQlClient _graphQlClient = new GraphQlClient("http://api.mes-equip.efara.cn/graphql");



        //public async Task Main(string MP)
        public void Main(string MP)
        {
            result_str = string.Empty;
            err_message = string.Empty;
            issuccess = false;

            const string query = @"
                    query ($mpsn: String!) {
                      mpBind(mpsn: $mpsn) {
                        productSerial {
                          sn
                          partNo
                          workOrderNo
                          customerSeqNo
                          productionDate
                          productionYearLast2
                          productionDayOfYear
                        }
                      }
                    }";
            try
            {
                var result = _graphQlClient.Request<JObject>(query, new Variables { ["mpsn"] = MP });
                ProductSerial = result?["mpBind"]?["productSerial"]?.ToObject<ProductSerial>();
                issuccess = ProductSerial != null;
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                err_message = ex.Message;
                issuccess = false;
            }
        }


    }

    public class ProductSerial
    {
        public string sn { get; set; }
        public string partNo { get; set; }
        public string workOrderNo { get; set; }
        public string customerSeqNo { get; set; }
        public DateTime productionDate { get; set; }
        public string productionYearLast2 { get; set; }
        public string productionDayOfYear { get; set; }
    }
}
