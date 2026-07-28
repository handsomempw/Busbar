using MES.Barcode;
using MESAutoLineClient;
using MESAutoLineClient.WorkStationService;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection.Emit;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Threading; //添加Thread命名空间用于获取线程ID

namespace MES_ORACLE_DATABASE
{
    public class MES_ORACLE_DATABASE
    {

        public static string __SN, __WOCODE, __PARTNOID, __NextGroupID, __CurrentGroupID, __QUALITY_STATUS;
        public static string __NEWSN, __NEW_WOCODE;
        public static MESAutoLineClient.Model.WorkGroup[] WorkGroups { get; set; } = null;



        private static string mes_connstr = "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=10.88.16.25)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=FARAMID)));Persist Security Info=True;User ID=SCADA_MES;Password=SCADA#1234;connection timeout=2;";
        private static AutoLineClient client = AutoLineClient.Create("http://mes-equip.efara.cn");
        private static Dictionary<string, string> dicStandardGroup = new Dictionary<string, string>();//工序码、工序名称对应清单
        private static IProductBarcodeResolver _productBarcodeResolver;//= new ProductBarcodeResolverFactory().RemoteRuleSetBased();
        private static object perfLogLocker = new object(); // 性能日志文件锁

        /// <summary>
        /// 添加MES数据库操作性能诊断日志方法
        /// 写入性能诊断日志到独立文件
        /// </summary>
        private static void WritePerfLog(string tag, string message, long? elapsedMs = null, string extraInfo = null)
        {
            try
            {
                int threadId = Thread.CurrentThread.ManagedThreadId;
                string threadName = Thread.CurrentThread.Name ?? $"Thread-{threadId}";

                // 检测是否为异常耗时（超过1秒）
                bool isAbnormalTime = elapsedMs.HasValue && elapsedMs.Value > 1000;
                string perfLevel = isAbnormalTime ? "PERF-异常" : "PERF";

                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}]");
                logBuilder.Append($"[{threadName}]");
                logBuilder.Append($"[MES_DB_{tag}]");
                logBuilder.Append($"[{perfLevel}]");
                logBuilder.Append($"[MES_ORACLE_DATABASE] {message}");

                if (elapsedMs.HasValue)
                {
                    string timeDisplay = isAbnormalTime ?
                        $"耗时={elapsedMs.Value}ms[异常!!!]" :
                        $"耗时={elapsedMs.Value}ms";
                    logBuilder.Append($" | {timeDisplay}");
                }

                if (!string.IsNullOrEmpty(extraInfo))
                {
                    logBuilder.Append($" | {extraInfo}");
                }

                string logContent = logBuilder.ToString();

                string filename = $"{Environment.CurrentDirectory}\\日志\\性能诊断\\{DateTime.Now.ToString("yyyyMMdd")}_performance.log";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                lock (perfLogLocker)
                {
                    using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                    {
                        sw.WriteLine(logContent);
                        sw.Close();
                    }
                }
            }
            catch
            {
                // 性能日志失败不应影响业务
            }
        }
        public struct worker_info
        {
            public string worker_name;
            public string worker_ID;
        }

        public struct BindProductAndRawMaterialResult
        {
            public bool Success;
            public string SN;
            public string ErrorInfo;
        }

        /// <summary>
        /// 母排绑定
        /// </summary>
        /// <param name="StandardCode">标准工序码</param>
        /// <param name="StationCode">工位号</param>
        /// <param name="ProductSN">成品编号</param>
        /// <param name="WO_CODE">批次号</param>
        /// <param name="ComponentSN">母排序列号</param>
        /// <param name="EmployeeNO">操作人员信息</param>
        /// <returns></returns>
        public static BindProductAndRawMaterialResult BindProductAndRawMaterial(string StandardCode, string StationCode, string ProductSN, string WO_CODE, string ComponentSN)

        {
            BindProductAndRawMaterialResult r = new BindProductAndRawMaterialResult() { Success = false };

            try
            {
                worker_info worker_Info = new worker_info();
                if (!get_worker_info(StationCode, out worker_Info))
                {
                    r.ErrorInfo = "未查询到上岗人员信息，请先上岗再进行生产";
                }
                else
                {
                    //AutoLineClient client = client = AutoLineClient.Create("http://mes-equip.efara.cn");
                    var result = client.BindProductAndRawMaterial(StandardCode, StationCode, ProductSN, WO_CODE, ComponentSN, worker_Info.worker_ID);
                    r.Success = result.Success;
                    if (!r.Success) { r.ErrorInfo = result.Message; }
                    else
                    {
                        r.SN = result.Message;
                    }

                }

            }
            catch (Exception ex)
            {
                r.ErrorInfo = ex.ToString();
            }

            return r;
        }


        ///// <summary>
        ///// 获取人员信息
        ///// </summary>
        ///// <param name="station_NO">工位号</param>
        ///// <param name="_worker_Info">人员信息</param>
        ///// <returns></returns>
        //public static bool get_worker_info(string station_NO, out worker_info _worker_Info)
        //{
        //    _worker_Info.worker_ID = string.Empty;
        //    _worker_Info.worker_name = string.Empty;

        //    string sql = string.Format("SELECT EMP_NAME,EMP_NO  FROM SCADA_MES.V_ON_STATION_T WHERE STATION_CODE='{0}' AND OUT_DT is null and rownum=1 ORDER BY ONWORK_DT DESC", station_NO);
        //    try
        //    {
        //        DataTable dt = Read_mes(sql);

        //        if (dt != null)
        //        {
        //            if (dt.Rows.Count > 0)
        //            {
        //                _worker_Info.worker_name = dt.Rows[0]["EMP_NAME"].ToString();
        //                _worker_Info.worker_ID = dt.Rows[0]["EMP_NO"].ToString();
        //                return true;
        //            }
        //        }
        //        return false;
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}




        /// <summary>
        /// 获取人员信息
        /// </summary>
        /// <param name="station_NO">工位号</param>
        /// <param name="_worker_Info">人员信息</param>
        /// <returns></returns>
        public static bool get_worker_info(string station_NO, out worker_info _worker_Info)
        {
            _worker_Info.worker_ID = string.Empty;
            _worker_Info.worker_name = string.Empty;

            try
            {
                var r = client.QueryCurrentOnWorkListByStationCode(station_NO);

                if (r.Success)
                {
                    if (r.Data.Length > 0)
                    {
                        _worker_Info.worker_name = r.Data[0].EmployeeName;
                        _worker_Info.worker_ID = r.Data[0].EmployeeNo;
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;

            //string sql = string.Format("SELECT EMP_NAME,EMP_NO  FROM SCADA_MES.V_ON_STATION_T WHERE STATION_CODE='{0}' AND OUT_DT is null and rownum=1 ORDER BY ONWORK_DT DESC", station_NO);
            //try
            //{
            //    DataTable dt = Read_mes(sql);

            //    if (dt != null)
            //    {
            //        if (dt.Rows.Count > 0)
            //        {
            //            _worker_Info.worker_name = dt.Rows[0]["EMP_NAME"].ToString();
            //            _worker_Info.worker_ID = dt.Rows[0]["EMP_NO"].ToString();
            //            return true;
            //        }
            //    }
            //    return false;
            //}
            //catch
            //{
            //    return false;
            //}
        }









        /// <summary>
        /// 保存测试数据
        /// </summary>
        /// <param name="PRO_Name">工序名称</param>
        /// <param name="PartNO_ID">物料号</param>
        /// <param name="WO_CODE">批号</param>
        /// <param name="SN">产品序号</param>
        /// <param name="group_Name">分组名称</param>
        /// <param name="Titles">测试项目名称</param>
        /// <param name="Datas">测试项目数值</param>
        /// <returns></returns>
        public static bool SaveProcedureData(string PRO_Name, string PartNO_ID, string WO_CODE, string SN, string group_Name, string[] Titles, double[] Datas)
        {
            try
            {
                DateTime dt = DateTime.Now;
                string filename = $"{Environment.CurrentDirectory}\\数据\\详细测试数据\\{dt.ToString("yyyy")}\\{dt.ToString("MM-dd")}\\{WO_CODE}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (Titles == null || Datas == null || Titles.Length != Datas.Length)
                {
                    return false;
                }
                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    string s = $"{PartNO_ID},{WO_CODE},{SN},{group_Name},";
                    for (int i = 0; i < Titles.Length; i++)
                    {
                        s += Datas[i] + ",";
                    }
                    sw.WriteLine(s);
                }

                return _SaveProcedureData(PRO_Name, PartNO_ID, WO_CODE, SN, group_Name, Titles, Datas);
            }
            catch { return false; }
        }
        public static bool _SaveProcedureData(string PRO_Name, string PartNO_ID, string WO_CODE, string SN, string group_Name, string[] Titles, double[] Datas)
        {
            try
            {
                string SCHEMA_Name = $"{group_Name},{WO_CODE}";
                string SCHEMA_ID = GetSCHEMA_ID(PRO_Name, SCHEMA_Name);
                if (string.IsNullOrEmpty(SCHEMA_Name))
                {
                    return false;
                }
                else
                {
                    #region 写入记录

                    DateTime DT = DateTime.Now;

                    if (Titles == null || Titles.Length < 1) { return false; }
                    StringBuilder sb = new StringBuilder();
                    sb.Append($" INSERT ALL");
                    for (int i = 0; i < Titles.Length; i++)
                    {
                        sb.Append($" INTO MEASURE_SN_DATA (SCHEMA_ID,SCHEMA_ITEM_NAME,BATCH_NO,SN,MEASURE_DT,VALUE) VALUES ('{SCHEMA_ID}','{Titles[i]}','{WO_CODE}','{SN}', TO_DATE('{DT.ToString("yyyy-MM-dd HH:mm:ss")}','yyyy-MM-dd HH24:mi:ss'),{Datas[i]})");
                    }
                    sb.Append($" SELECT* FROM dual");
                    return excutesql_mes(sb.ToString());
                    #endregion
                }
            }
            catch { return false; }


        }



        /// <summary>
        /// 保存测试项目清单
        /// </summary>
        /// <param name="WO_CODE">批号</param>
        /// <param name="group_Name">分组名称</param>
        /// <param name="Titles">测试项目名称</param>
        /// <returns></returns>
        public static bool SaveProcedureTitle(string PRO_Name, string WO_CODE, string group_Name, string[] Titles)
        {
            try
            {
                DateTime dt = DateTime.Now;
                string filename = $"{Environment.CurrentDirectory}\\数据\\测试项目清单\\{dt.ToString("yyyy")}\\{dt.ToString("MM-dd")}\\{WO_CODE}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (Titles == null || Titles.Length < 1)
                {
                    return false;
                }
                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    string s = $"{group_Name},";
                    for (int i = 0; i < Titles.Length; i++)
                    {
                        s += Titles[i] + ","; ;
                    }
                    sw.WriteLine(s);
                }

                return _SaveProcedureTitle(PRO_Name, WO_CODE, group_Name, Titles);
            }
            catch { return false; }
        }

        private static bool _SaveProcedureTitle(string PRO_Name, string WO_CODE, string group_Name, string[] Titles)
        {
            #region 创建配方记录
            string SCHEMA_Name = $"{group_Name},{WO_CODE}";
            string SCHEMA_ID = GetSCHEMA_ID(PRO_Name, SCHEMA_Name);
            if (!string.IsNullOrEmpty(SCHEMA_ID))
            {
                return true;
            }
            else if (!CreateSCHEMA_IDRecord(PRO_Name, SCHEMA_Name))
            {
                return false;
            }

            SCHEMA_ID = GetSCHEMA_ID(PRO_Name, SCHEMA_Name);
            if (string.IsNullOrEmpty(SCHEMA_Name))
            {
                return false;
            }
            #endregion

            #region 写入记录

            if (Titles == null || Titles.Length < 1) { return false; }
            StringBuilder sb = new StringBuilder();
            sb.Append($" INSERT ALL");
            for (int i = 0; i < Titles.Length; i++)
            {
                sb.Append($" INTO MEASURE_SCHEMA_ITEM(SCHEMA_ID, SCHEMA_ITEM_NAME, SCHEMA_ITEM_SEQ) VALUES('{SCHEMA_ID}', '{Titles[i]}', {i + 1})");
            }
            sb.Append($" SELECT* FROM dual");

            return excutesql_mes(sb.ToString());


            #endregion


        }

        /// <summary>
        ///  获取SCHEMA_ID
        /// </summary>
        /// <param name="PRO_Name">工序名称</param>
        /// <param name="SCHEMA_Name"></param>
        /// <returns></returns>
        private static string GetSCHEMA_ID(string PRO_Name, string SCHEMA_Name)
        {
            try
            {
                string sql = $"Select SCHEMA_ID from MEASURE_SCHEMA where SCHEMA_NAME='{SCHEMA_Name}' and PRO_Name='{PRO_Name}'";
                return ReadString_mes(sql);

            }
            catch
            {
                return string.Empty;
            }
        }


        private static int CountExtDataNum(string PRO_Name, string SCHEMA_ID)
        {
            try
            {
                string sql = $"SELECT count(*) from  MEASURE_EXT_DATA WHERE SCHEMA_ID='{SCHEMA_ID}'";
                return Convert.ToInt32(ReadString_mes(sql));
            }
            catch
            {
                return 0;
            }
        }


        /// <summary>
        /// 生成SCHEMA_ID记录
        /// </summary>
        /// <param name="SCHEMA_Name"></param>
        /// <returns></returns>
        private static bool CreateSCHEMA_IDRecord(string PRO_Name, string SCHEMA_Name)
        {
            string sql = $"insert into MEASURE_SCHEMA(SCHEMA_NAME,PRO_Name)  select '{SCHEMA_Name}','{PRO_Name}' from dual where not exists (select 1 from MEASURE_SCHEMA where SCHEMA_NAME= '{SCHEMA_Name}' AND PRO_Name='{PRO_Name}')";
            return excutesql_mes(sql);
        }

        /// <summary>
        /// 保存测试项目参数
        /// </summary>
        /// <param name="WO_CODE">批号</param>
        /// <param name="group_Name">分组名称</param>
        /// <param name="Titles">测试项目名称</param>
        /// <param name="Nominal_values">标称值</param>
        /// <param name="Upper_Tol">上偏差</param>
        /// <param name="Lower_Tol">下偏差</param>
        /// <returns></returns>
        public static bool SaveProcedureParameter(string PRO_Name, string WO_CODE, string group_Name, string[] Titles, double[] Nominal_values, double[] Upper_Tol, double[] Lower_Tol)
        {
            try
            {
                DateTime dt = DateTime.Now;
                string filename = $"{Environment.CurrentDirectory}\\数据\\测试项目参数\\{dt.ToString("yyyy")}\\{dt.ToString("MM-dd")}\\{WO_CODE}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (Titles == null || Titles.Length < 1)
                {
                    return false;
                }
                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    string s1 = $"{group_Name},项目,";
                    for (int i = 0; i < Titles.Length; i++)
                    {
                        s1 += Titles[i] + ",";
                    }
                    sw.WriteLine(s1);

                    string s2 = $"{group_Name},标称值,";
                    for (int i = 0; i < Nominal_values.Length; i++)
                    {
                        s2 += Nominal_values[i] + ",";
                    }
                    sw.WriteLine(s2);


                    string s3 = $"{group_Name},上偏差,";
                    for (int i = 0; i < Upper_Tol.Length; i++)
                    {
                        s3 += Upper_Tol[i] + ",";
                    }
                    sw.WriteLine(s3);

                    string s4 = $"{group_Name},下偏差,";
                    for (int i = 0; i < Lower_Tol.Length; i++)
                    {
                        s4 += Lower_Tol[i] + ",";
                    }
                    sw.WriteLine(s4);
                }

                return _SaveProcedureParameter(PRO_Name, WO_CODE, group_Name, Titles, Nominal_values, Upper_Tol, Lower_Tol);

            }
            catch
            {
                return false;
            }
        }

        private static bool _SaveProcedureParameter(string PRO_Name, string WO_CODE, string group_Name, string[] Titles, double[] Nominal_values, double[] Upper_Tol, double[] Lower_Tol)
        {
            try
            {
                string SCHEMA_Name = $"{group_Name},{WO_CODE}";
                string SCHEMA_ID = GetSCHEMA_ID(PRO_Name, SCHEMA_Name);
                if (string.IsNullOrEmpty(SCHEMA_Name))
                {
                    return false;
                }
                else
                {
                    if (CountExtDataNum(PRO_Name, SCHEMA_ID) > 0)
                    {
                        return true;
                    }
                    #region 写入记录

                    if (Titles == null || Titles.Length < 1) { return false; }
                    StringBuilder sb = new StringBuilder();
                    sb.Append($" INSERT ALL");
                    for (int i = 0; i < Titles.Length; i++)
                    {
                        sb.Append($" INTO MEASURE_EXT_DATA (SCHEMA_ID,SCHEMA_ITEM_NAME,BATCH_NO,EXT_NAME,VALUE) VALUES ('{SCHEMA_ID}','{Titles[i]}','{WO_CODE}','标准值', {Nominal_values[i]})");
                        sb.Append($" INTO MEASURE_EXT_DATA (SCHEMA_ID,SCHEMA_ITEM_NAME,BATCH_NO,EXT_NAME,VALUE) VALUES ('{SCHEMA_ID}','{Titles[i]}','{WO_CODE}','上公差', {Upper_Tol[i]})");
                        sb.Append($" INTO MEASURE_EXT_DATA (SCHEMA_ID,SCHEMA_ITEM_NAME,BATCH_NO,EXT_NAME,VALUE) VALUES ('{SCHEMA_ID}','{Titles[i]}','{WO_CODE}','下公差', {Lower_Tol[i]})");
                    }
                    sb.Append($" SELECT* FROM dual");

                    return excutesql_mes(sb.ToString());
                    #endregion
                }
            }
            catch { return false; }
        }


        public static Boolean Save_EquipmentRecord_mes(string STATION_CODE, string WO_CODE, string M_SN, string PROCEDURE_NAME, string EQUIPMENT_NUMBER, string result, string PROCEDURE_NUMBER)
        {
            return Save_EquipmentRecord_mes(STATION_CODE, WO_CODE, M_SN, PROCEDURE_NAME, EQUIPMENT_NUMBER, result, PROCEDURE_NUMBER, out _);
        }

        /// <summary>
        /// 向 MES 报工，并在失败时返回可读原因供上位机弹窗与日志使用。
        /// </summary>
        public static Boolean Save_EquipmentRecord_mes(string STATION_CODE, string WO_CODE, string M_SN, string PROCEDURE_NAME, string EQUIPMENT_NUMBER, string result, string PROCEDURE_NUMBER, out string failMessage)
        {
            failMessage = string.Empty;
            if (dicStandardGroup.Count == 0)
            {
                try
                {
                    var r = client.GetStandardWorkGroupList();
                    if (r.Success)
                    {
                        if (r.Data.Length > 0)
                        {
                            for (int i = 0; i < r.Data.Length; i++)
                            {
                                try
                                {
                                    dicStandardGroup.Add(r.Data[i].Name, r.Data[i].Code);
                                }
                                catch (Exception ex)
                                {
                                    WriteError($"添加工序码失败: {r.Data[i].Name}={r.Data[i].Code}, 异常: {ex.Message}", "GetStandardWorkGroupList");
                                }
                            }
                        }
                    }
                    else
                    {
                        WriteError($"获取工序码字典失败: {r.Message}", "GetStandardWorkGroupList");
                        failMessage = $"获取工序码失败: {r.Message}";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    WriteError($"获取工序码字典异常: {ex.Message}", "GetStandardWorkGroupList");
                    failMessage = $"获取工序码异常: {ex.Message}";
                    return false;
                }
            }
            string employeeNo = "NOT_WORKER";
            try
            {
                WorkStationOnWorkRecord workStationOnWorkRecord = new WorkStationOnWorkRecord
                {
                    StationCode = STATION_CODE,
                    EmployeeNo = string.Empty,
                    OnWorkDate = DateTime.MinValue,
                    EmployeeName = string.Empty,
                };
                var r = client.QueryCurrentOnWorkListByStationCode(STATION_CODE);
                if (r.Success)
                {
                    if (r.Data.Length > 0)
                    {
                        for (int i = 0; i < r.Data.Length; i++)
                        {
                            if (r.Data[i].OnWorkDate > workStationOnWorkRecord.OnWorkDate)
                                workStationOnWorkRecord = r.Data[i];
                        }
                        employeeNo = workStationOnWorkRecord.EmployeeNo;
                        WriteError($"获取员工信息成功: 工位={STATION_CODE}, 员工={employeeNo}", "QueryCurrentOnWorkListByStationCode");
                    }
                    else
                    {
                        WriteError($"工位无上岗员工: 工位={STATION_CODE}, 使用默认员工={employeeNo}", "QueryCurrentOnWorkListByStationCode");
                    }
                }
                else
                {
                    WriteError($"获取员工信息失败: 工位={STATION_CODE}, 错误={r.Message}, 使用默认员工={employeeNo}", "QueryCurrentOnWorkListByStationCode");
                }
            }
            catch (Exception ex)
            {
                WriteError($"获取员工信息异常: 工位={STATION_CODE}, 异常={ex.Message}, 使用默认员工={employeeNo}", "QueryCurrentOnWorkListByStationCode");
            }

            // ReportWorkByProduct 形参顺序为：status, errorType, errorDesc。
            // 注意：默认参数只在“省略参数”时生效，显式传入空字符串会覆盖默认值。
            // 这里按业务语义显式组装：合格 -> status=OK；不合格 -> status=NG 且必须携带不良类型/描述。
            string rawResult = (result ?? string.Empty).Trim();
            bool isOk = string.IsNullOrEmpty(rawResult) || rawResult == "合格";
            string status = isOk ? "OK" : "NG";
            string errorType = isOk ? string.Empty : PROCEDURE_NAME;
            string errorDesc = isOk ? string.Empty : rawResult;

            // 验证工序名称是否存在
            if (!dicStandardGroup.ContainsKey(PROCEDURE_NAME))
            {
                WriteError($"工序名称不存在: {PROCEDURE_NAME}, 可用工序: {string.Join(",", dicStandardGroup.Keys)}", "Save_EquipmentRecord_mes");
                failMessage = $"工序名称不存在: {PROCEDURE_NAME}";
                return false;
            }

            try
            {
                var r = client.ReportWorkByProduct(
                    serialNumber: M_SN,
                    stationCode: STATION_CODE,
                    employeeNo: employeeNo,
                    procedureCode: dicStandardGroup[PROCEDURE_NAME],
                    status: status,
                    errorType: errorType,
                    errorDesc: errorDesc);
                if (r.Success)
                {
                    WriteError($"报工成功: SN={M_SN}, 工位={STATION_CODE}, 员工={employeeNo}, 工序={PROCEDURE_NAME}, 状态={status}, 不良类型={errorType}, 不良描述={errorDesc}", "ReportWorkByProduct");
                    return true;
                }
                else
                {
                    WriteError($"报工失败: SN={M_SN}, 工位={STATION_CODE}, 员工={employeeNo}, 工序={PROCEDURE_NAME}, 状态={status}, 不良类型={errorType}, 不良描述={errorDesc}, 错误信息={r.Message}", "ReportWorkByProduct");
                    failMessage = string.IsNullOrWhiteSpace(r.Message) ? "MES报工返回失败" : r.Message;
                    return false;
                }
            }
            catch (Exception ex)
            {
                WriteError($"报工异常: SN={M_SN}, 工位={STATION_CODE}, 员工={employeeNo}, 工序={PROCEDURE_NAME}, 状态={status}, 不良类型={errorType}, 不良描述={errorDesc}, 异常信息={ex.Message}", "ReportWorkByProduct");
                failMessage = ex.Message;
                return false;
            }

            //if (CheckSN(M_SN, out M_SN, out WO_CODE))
            //{
            //    string sql = string.Format(create_cmd(), STATION_CODE, WO_CODE, M_SN, PROCEDURE_NAME, EQUIPMENT_NUMBER, result, PROCEDURE_NUMBER);
            //    int i = 0;
            //    bool r = false;
            //    while (i++ <= 3 && r == false)
            //    {
            //        r = excutesql_mes(sql);
            //    }
            //    return r;
            //}
            //else
            //{
            //    return false;
            //}
        }






        /// <summary>
        /// 
        /// </summary>
        /// <param name="SN">成品编号</param>
        /// <param name="PROCEDURE_NAME">工序名称</param>
        /// <returns>true：校验合格 false:校验失败</returns>
        public static bool check_productNO(string SN, string PROCEDURE_NAME)
        {
            if (WorkGroups == null || WorkGroups.Count() == 0)
            {
                var r = client.GetStandardWorkGroupList();
                if (!r.Success)
                {
                    return false;
                }
                else
                {
                    WorkGroups = r.Data;
                }
            }

            string __standardcode = WorkGroups.Where(x => x.Name.Equals(PROCEDURE_NAME)).First().Code;

            if (string.IsNullOrEmpty(__SN) || string.IsNullOrEmpty(__WOCODE) || string.IsNullOrEmpty(__PARTNOID) || SN != __SN)
            {
                if (!GetProductInfo(SN))
                {
                    return false;
                }
            }

            if (__standardcode == __CurrentGroupID || (__NextGroupID == __standardcode && __QUALITY_STATUS == "0"))
            {
                return true;

            }
            else
            {
                return false;
            }

            //try
            //{
            //    //string sql = string.Format("SELECT SN FROM SCADA_MES.V_PRODUCT_STATE WHERE SN='{0}' AND next_group_Name='{1}' AND QUALITY_STATUS=0 ",
            //    //    SN, PROCEDURE_NAME);

            //    string sql = $"SELECT SN FROM SCADA_MES.V_PRODUCT_STATE WHERE SN='{SN}'  and((CURRENT_GROUPNAME = '{PROCEDURE_NAME}')or(NEXT_GROUPNAME = '{PROCEDURE_NAME}' AND QUALITY_STATUS = 0))";

            //    string s = ReadString_mes(sql);
            //    if (string.IsNullOrEmpty(s))
            //    {
            //        return false;
            //    }
            //    return true;
            //}
            //catch (Exception e)
            //{
            //    //error_info = e.ToString();
            //    return false;
            //}
        }


        public static bool GetProductInfo(string SN)
        {
            // 添加性能诊断日志
            Stopwatch sw = Stopwatch.StartNew();
            WritePerfLog("GETPRODUCTINFO_START", "GetProductInfo开始(MES API调用)", extraInfo: $"SN={SN}");

            try
            {
                var r = client.GetSerialProductInfo(SN);
                sw.Stop();

                if (r.Success)
                {
                    WritePerfLog("GETPRODUCTINFO_SUCCESS", "GetProductInfo成功", sw.ElapsedMilliseconds,
                        $"WOCODE={r.Data.Lot}, PartNO={r.Data.PartNo}");

                    __SN = r.Data.SerialNumber;
                    __WOCODE = r.Data.Lot;
                    __PARTNOID = r.Data.PartNo;
                    __CurrentGroupID = r.Data.ProcedureCode;
                    __NextGroupID = r.Data.NextProcedureCode;
                    __QUALITY_STATUS = ((int)r.Data.QualityStatus).ToString();
                    // 仅以本次传入SN为准，不再根据 __NEWSN 隐式跳转查询，避免规格被返修链路缓存覆盖。

                    return true;
                }
                else
                {
                    WritePerfLog("GETPRODUCTINFO_FAILED", "GetProductInfo失败", sw.ElapsedMilliseconds,
                        $"Message={r.Message}");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                WritePerfLog("GETPRODUCTINFO_EXCEPTION", "GetProductInfo异常", sw.ElapsedMilliseconds,
                    $"Error={ex.Message}");
            }

            //__SN = string.Empty;
            __WOCODE = string.Empty;
            //__NEWSN = string.Empty;
            __NEW_WOCODE = string.Empty;
            __PARTNOID = string.Empty;
            __CurrentGroupID = string.Empty;
            __NextGroupID = string.Empty;
            __QUALITY_STATUS = string.Empty;
            return false;
        }


        public static bool GetNewProductInfo(string SN)
        {
            try
            {
                var r = client.GetSerialProductInfo(SN);
                if (r.Success)
                {
                    //__NEWSN = r.Data.SerialNumber;
                    __NEW_WOCODE = r.Data.Lot;
                    __PARTNOID = r.Data.PartNo;
                    __CurrentGroupID = r.Data.ProcedureCode;
                    __NextGroupID = r.Data.NextProcedureCode;
                    __QUALITY_STATUS = ((int)r.Data.QualityStatus).ToString();
                    return true;
                }
            }
            catch (Exception ex)
            {
                ;
            }
            //__NEWSN = string.Empty;
            __NEW_WOCODE = string.Empty;
            __PARTNOID = string.Empty;
            __CurrentGroupID = string.Empty;
            __NextGroupID = string.Empty;
            __QUALITY_STATUS = string.Empty;
            return false;
        }


        /// <summary>
        /// 通过批次获取规格
        /// </summary>
        /// <param name="WOCODE"></param>
        /// <returns></returns>
        public static string Get_PARTNOID_BY_WOCODE(string WOCODE)
        {
            try
            {
                var r = client.GetWorkOrder(WOCODE);
                if (r.Success)
                {
                    return r.Data.PartNo;
                }
            }
            catch
            {

            }
            return string.Empty;

        }


        public static List<MESAutoLineClient.Model.EquipmentSettingParameter> Get_Parameters(string MachineID, string WOCODE, string StandardCode)
        {
            try
            {
                var r = client.GetEquipmentSettingParameters(MachineID, WOCODE, StandardCode);
                if (r.Success)
                {
                    return r.Data;
                }
            }
            catch (Exception ex)
            {
                ;
            }
            return null;

        }




        public static string get_WO_CODE(string SN)
        {
            //添加性能诊断日志
            Stopwatch sw = Stopwatch.StartNew();
            WritePerfLog("GET_WOCODE_START", "get_WO_CODE开始", extraInfo: $"SN={SN}");

            try
            {
                if (string.IsNullOrEmpty(__SN) || string.IsNullOrEmpty(__WOCODE) || string.IsNullOrEmpty(__PARTNOID) || SN != __SN)
                {
                    WritePerfLog("GET_WOCODE_CACHE_MISS", "缓存未命中,调用GetProductInfo", extraInfo: $"CachedSN={__SN}, RequestSN={SN}");
                    Stopwatch swGetInfo = Stopwatch.StartNew();
                    var r = GetProductInfo(SN);
                    swGetInfo.Stop();
                    WritePerfLog("GET_WOCODE_GETPRODUCTINFO", "GetProductInfo完成", swGetInfo.ElapsedMilliseconds, $"Success={r}");

                    if (r)
                    {
                        sw.Stop();
                        WritePerfLog("GET_WOCODE_SUCCESS", "get_WO_CODE成功", sw.ElapsedMilliseconds, $"WOCODE={__WOCODE}");
                        return __WOCODE;
                    }
                }
                else
                {
                    sw.Stop();
                    WritePerfLog("GET_WOCODE_CACHE_HIT", "缓存命中", sw.ElapsedMilliseconds, $"WOCODE={__WOCODE}");
                    return __WOCODE;
                }

                ////string sql = $"SELECT  WO_CODE from V_PRODUCT_STATE where sn = '{SN}'";
                //string sql = $"select WO_CODE from WIP_D_PRODUCT_SERIAL@Mesprod WHERE sn='{SN}'";
                //return ReadString_mes(sql);
            }
            catch (Exception ex)
            {
                sw.Stop();
                WritePerfLog("GET_WOCODE_EXCEPTION", "get_WO_CODE异常", sw.ElapsedMilliseconds, $"Error={ex.Message}");
            }

            sw.Stop();
            WritePerfLog("GET_WOCODE_FAILED", "get_WO_CODE失败", sw.ElapsedMilliseconds);
            return null;
        }

        public static string get_PartNO_ID(string SN)
        {
            // 添加性能诊断日志
            Stopwatch sw = Stopwatch.StartNew();
            WritePerfLog("GET_PARTNOID_START", "get_PartNO_ID开始", extraInfo: $"SN={SN}");

            try
            {
                if (string.IsNullOrEmpty(__SN) || string.IsNullOrEmpty(__WOCODE) || string.IsNullOrEmpty(__PARTNOID) || SN != __SN)
                {
                    WritePerfLog("GET_PARTNOID_CACHE_MISS", "缓存未命中,调用GetProductInfo", extraInfo: $"CachedSN={__SN}, RequestSN={SN}");
                    Stopwatch swGetInfo = Stopwatch.StartNew();
                    var r = GetProductInfo(SN);
                    swGetInfo.Stop();
                    WritePerfLog("GET_PARTNOID_GETPRODUCTINFO", "GetProductInfo完成", swGetInfo.ElapsedMilliseconds, $"Success={r}");

                    if (r)
                    {
                        sw.Stop();
                        WritePerfLog("GET_PARTNOID_SUCCESS", "get_PartNO_ID成功", sw.ElapsedMilliseconds, $"PartNOID={__PARTNOID}");
                        return __PARTNOID;
                    }
                }
                else
                {
                    sw.Stop();
                    WritePerfLog("GET_PARTNOID_CACHE_HIT", "缓存命中", sw.ElapsedMilliseconds, $"PartNOID={__PARTNOID}");
                    return __PARTNOID;
                }
                ////string sql = $"SELECT  WO_CODE from V_PRODUCT_STATE where sn = '{SN}'";
                //string sql = $"select WO_CODE from WIP_D_PRODUCT_SERIAL@Mesprod WHERE sn='{SN}'";
                //return ReadString_mes(sql);
            }
            catch (Exception ex)
            {
                sw.Stop();
                WritePerfLog("GET_PARTNOID_EXCEPTION", "get_PartNO_ID异常", sw.ElapsedMilliseconds, $"Error={ex.Message}");
            }

            sw.Stop();
            WritePerfLog("GET_PARTNOID_FAILED", "get_PartNO_ID失败", sw.ElapsedMilliseconds);
            return null;
            //try
            //{
            //    //string sql = $"SELECT PARTNO_ID from V_SN_T WHERE sn='{SN}'";
            //    string sql = $"SELECT PARTNO_ID from WIP_D_PRODUCT_SERIAL@Mesprod WHERE sn='{SN}'";
            //    return ReadString_mes(sql);
            //}
            //catch { return null; }
        }

        /// <summary>
        /// 解析扫描输入并返回可用于后续业务校验的 SN。
        /// </summary>
        /// <param name="CODEstr">扫码枪原始输入，可能包含前缀 BARCODE、大小写差异或不同来源编码。</param>
        /// <returns>
        /// 成功时返回 SN（优先返回最终绑定SN，其次返回上层码 top_sn，最后返回普通 SN）；
        /// 解析失败返回空字符串；发生异常返回 null。
        /// </returns>
        /// <remarks>
        /// 主流程（按优先级）：
        /// 1) 先用 ProductBarcodeResolver 把输入解析为中间码 _msn；
        /// 2) 若 _msn 为空，走 GetSNByMP（MES接口）兜底查询；
        /// 3) 在两个分支中都优先尝试 GetLastBindingProductSN 获取最终绑定SN；
        /// 4) 若最终绑定SN不可得，再使用 GetSNbyMSN / 原始SN 作为回退；
        /// 5) CheckSN 仅执行返修链路查询并刷新 __NEWSN 缓存，不直接决定 DecodeSN 返回值；
        /// 6) 过程中会刷新类级缓存字段（__SN、__WOCODE 等）并输出性能日志。
        /// </remarks>
        public static string DecodeSN(string CODEstr)
        {
            // 添加详细性能诊断日志
            Stopwatch swTotal = Stopwatch.StartNew();
            WritePerfLog("DECODE_START", "DecodeSN开始", extraInfo: $"Input={CODEstr}");

            __SN = string.Empty;
            __WOCODE = string.Empty;
            __NEWSN = string.Empty;
            __NEW_WOCODE = string.Empty;
            __PARTNOID = string.Empty;
            __CurrentGroupID = string.Empty;
            __NextGroupID = string.Empty;
            __QUALITY_STATUS = string.Empty;

            try
            {
                //Match match = Regex.Match(SNstr, "[27]{1}[A-Z]{1}[0-9]{8}");
                //Match match1 = Regex.Match(SNstr, "6[0-9]{9}");
                //if (match.Success)
                //{
                //    return match.Value;
                //}
                //else if (match1.Success)
                //{
                //    return match1.Value;
                //}
                //Stopwatch sw = Stopwatch.StartNew();

                // 步骤1: 初始化ProductBarcodeResolver
                Stopwatch sw1 = Stopwatch.StartNew();
                if (_productBarcodeResolver == null)
                {
                    WritePerfLog("RESOLVER_INIT_START", "ProductBarcodeResolver初始化开始");
                    try
                    {
                        _productBarcodeResolver = new ProductBarcodeResolverFactory().RemoteRuleSetBased();
                        sw1.Stop();
                        WritePerfLog("RESOLVER_INIT_END", "ProductBarcodeResolver初始化完成", sw1.ElapsedMilliseconds);
                    }
                    catch (Exception e)
                    {
                        sw1.Stop();
                        WritePerfLog("RESOLVER_INIT_FAILED", "ProductBarcodeResolver初始化失败", sw1.ElapsedMilliseconds, $"Error={e.Message}");
                    }
                }
                //sw.Stop();
                //Debug.WriteLine(sw.ElapsedMilliseconds);
                // 统一输入格式：去空白、转大写，并移除设备上报中常见的 BARCODE 前缀。
                CODEstr = CODEstr.Trim().ToUpper().Replace("BARCODE", "");
                if (CODEstr.StartsWith("BARCODE"))
                {
                    CODEstr = CODEstr.Replace("BARCODE", "");
                }

                string _sn = string.Empty;
                string SN = string.Empty;
                string WO_CODE = string.Empty;

                string _msn = string.Empty;

                // 步骤2: Resolver解析
                Stopwatch sw2 = Stopwatch.StartNew();
                WritePerfLog("RESOLVER_RESOLVE_START", "Resolver.Resolve开始", extraInfo: $"Input={CODEstr}");
                try
                {
                    _msn = _productBarcodeResolver.Resolve(CODEstr);
                    sw2.Stop();
                    WritePerfLog("RESOLVER_RESOLVE_END", "Resolver.Resolve完成", sw2.ElapsedMilliseconds, $"Result={_msn ?? "NULL"}");
                }
                catch (Exception ex)
                {
                    sw2.Stop();
                    WritePerfLog("RESOLVER_RESOLVE_FAILED", "Resolver.Resolve失败", sw2.ElapsedMilliseconds, $"Error={ex.Message}");
                }

                // 分支A：Resolver 无法识别时，使用 GetSNByMP 作为兜底数据源。
                if (string.IsNullOrEmpty(_msn))
                {
                    // 步骤3: 调MES接口GetSNByMP（母排码转SN）
                    Stopwatch sw3 = Stopwatch.StartNew();
                    WritePerfLog("GRAPHQL_START", "GetSNByMP开始(MES接口查询)", extraInfo: $"MP={CODEstr}");
                    var r = GetSNByMP(CODEstr, out SN, out WO_CODE);
                    sw3.Stop();
                    WritePerfLog("GRAPHQL_END", "GetSNByMP完成", sw3.ElapsedMilliseconds, $"Success={r}, SN={SN ?? "NULL"}");

                    if (r)
                    {
                        // 步骤4: 优先获取“零件最终绑定序列号”
                        Stopwatch swFinal = Stopwatch.StartNew();
                        WritePerfLog("GET_LAST_BINDING_SN_START", "GetLastBindingProductSerialNumberOfPartBarcode开始", extraInfo: $"Barcode={SN}");
                        string finalBindingSn = string.Empty;
                        bool finalBindingOk = GetLastBindingProductSN(SN, out finalBindingSn);
                        swFinal.Stop();
                        WritePerfLog("GET_LAST_BINDING_SN_END", "GetLastBindingProductSerialNumberOfPartBarcode完成", swFinal.ElapsedMilliseconds,
                            $"Success={finalBindingOk}, FinalSN={finalBindingSn ?? "NULL"}");

                        if (finalBindingOk && !string.IsNullOrEmpty(finalBindingSn))
                        {
                            __SN = finalBindingSn;
                            swTotal.Stop();
                            WritePerfLog("DECODE_SUCCESS", "DecodeSN成功(返回最终绑定SN)", swTotal.ElapsedMilliseconds, $"Result={finalBindingSn}");
                            return finalBindingSn;
                        }

                        #region 母排厂获取上一层码
                        // 步骤5: 获取上层码（兜底路径）
                        Stopwatch sw4 = Stopwatch.StartNew();
                        WritePerfLog("GETSNBYMSN_START", "GetSNbyMSN开始", extraInfo: $"SN={SN}");
                        string top_sn = string.Empty;
                        var r1 = GetSNbyMSN(SN, out top_sn);
                        sw4.Stop();
                        WritePerfLog("GETSNBYMSN_END", "GetSNbyMSN完成", sw4.ElapsedMilliseconds, $"Success={r1}, TopSN={top_sn ?? "NULL"}");
                        #endregion


                        // 母排场景优先返回上一层码（top_sn）；没有上一层码则返回当前 SN。
                        if (!string.IsNullOrEmpty(top_sn))
                        {
                            swTotal.Stop();
                            WritePerfLog("DECODE_SUCCESS", "DecodeSN成功(返回TopSN)", swTotal.ElapsedMilliseconds, $"Result={top_sn}");
                            return top_sn;
                        }
                        else
                        {

                            __SN = SN;
                            swTotal.Stop();
                            WritePerfLog("DECODE_SUCCESS", "DecodeSN成功(返回SN)", swTotal.ElapsedMilliseconds, $"Result={SN}");
                            return SN;
                        }
                    }
                    else
                    {
                        swTotal.Stop();
                        WritePerfLog("DECODE_FAILED", "DecodeSN失败(GetSNByMP返回false)", swTotal.ElapsedMilliseconds);
                        return string.Empty;
                    }




                    //GetSNByMP.GetSNByMP GSM = new GetSNByMP.GetSNByMP();
                    //GSM.Main(CODEstr);
                    //if (GSM.issuccess)
                    //{
                    //    SN = GSM.ProductSerial.sn;
                    //    WO_CODE = GSM.ProductSerial.workOrderNo;
                    //    return SN;
                    //}
                    //else
                    //{
                    //    return string.Empty;
                    //}
                }
                // 分支B：Resolver 成功时，优先用 _msn 回查真实 SN，再执行 CheckSN 做链路校验/归一化。
                else
                {
                    // 步骤5: 通过MSN获取SN
                    Stopwatch sw5 = Stopwatch.StartNew();
                    WritePerfLog("GETSNBYMSN2_START", "GetSNbyMSN开始(通过MSN)", extraInfo: $"MSN={_msn}");
                    var r1 = GetSNbyMSN(_msn, out _sn);
                    sw5.Stop();
                    WritePerfLog("GETSNBYMSN2_END", "GetSNbyMSN完成", sw5.ElapsedMilliseconds, $"Success={r1}, SN={_sn ?? "NULL"}");

                    // 回查不到映射时，保底把 _msn 当作 SN 使用，避免流程中断。
                    if (string.IsNullOrEmpty(_sn))
                    {
                        _sn = _msn;
                    }

                    //var r = CheckSN(_sn, out SN, out WO_CODE);
                    //if (r)
                    //{
                    //    return SN;
                    //}

                    // 步骤6: CheckSN
                    Stopwatch sw6 = Stopwatch.StartNew();
                    WritePerfLog("CHECKSN_START", "CheckSN开始", extraInfo: $"SN={_sn}");
                    CheckSN(_sn);
                    sw6.Stop();
                    WritePerfLog("CHECKSN_END", "CheckSN完成", sw6.ElapsedMilliseconds);

                    // 步骤7: 优先使用“零件最终绑定序列号”
                    Stopwatch sw7 = Stopwatch.StartNew();
                    WritePerfLog("GET_LAST_BINDING_SN_START", "GetLastBindingProductSerialNumberOfPartBarcode开始", extraInfo: $"Barcode={_sn}");
                    string finalBindingSn = string.Empty;
                    bool finalBindingOk = GetLastBindingProductSN(_sn, out finalBindingSn);
                    sw7.Stop();
                    WritePerfLog("GET_LAST_BINDING_SN_END", "GetLastBindingProductSerialNumberOfPartBarcode完成", sw7.ElapsedMilliseconds,
                        $"Success={finalBindingOk}, FinalSN={finalBindingSn ?? "NULL"}");
                    if (finalBindingOk && !string.IsNullOrEmpty(finalBindingSn))
                    {
                        _sn = finalBindingSn;
                    }

                    // 将结果写入类级缓存，供后续流程复用。
                    __SN = _sn;
                    swTotal.Stop();
                    WritePerfLog("DECODE_SUCCESS", "DecodeSN成功(通过Resolver)", swTotal.ElapsedMilliseconds, $"Result={_sn}");

                }
                return _sn;
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                WritePerfLog("DECODE_EXCEPTION", "DecodeSN异常", swTotal.ElapsedMilliseconds, $"Error={ex.Message}");
            }
            return null;
        }


        /// <summary>
        /// 通过中间码(MSN)查询对应SN（常用于上一层/中间层码映射）。
        /// </summary>
        /// <param name="MSN">中间码（Resolver产出或流程内中间SN）</param>
        /// <param name="SN">查询得到的SN</param>
        /// <returns>true: 查询成功；false: 查询失败</returns>
        public static bool GetSNbyMSN(string MSN, out string SN)
        {
            SN = string.Empty;
            try
            {
                var r = client.GetProductSerialNumberByCapacitorCore(MSN);
                if (r.Success)
                {
                    SN = r.Message.Replace("OK;", "");
                    return true;
                }
            }
            catch (Exception ex)
            { }
            return false;
        }

        /// <summary>
        /// 通过零件条码获取最终绑定成品序列号。
        /// </summary>
        /// <param name="barcode">零件条码或中间序列号</param>
        /// <param name="finalSN">最终绑定成品序列号</param>
        /// <returns>true: 获取成功且 finalSN 非空；false: 获取失败</returns>
        public static bool GetLastBindingProductSN(string barcode, out string finalSN)
        {
            finalSN = string.Empty;
            try
            {   
                var r = client.GetLastBindingProductSerialNumberOfPartBarcode(barcode);
                if (r != null && r.Success)
                {
                    finalSN = (r.Data ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(finalSN) && !string.IsNullOrEmpty(r.Message))
                    {
                        finalSN = r.Message.Replace("OK;", "").Trim();
                    }
                    return !string.IsNullOrEmpty(finalSN);
                }
            }
            catch (Exception ex)
            { }
            return false;
        }

        /// <summary>
        /// 保存母排单次过程数据到 MES。双Y ACW/DCW 复测会逐次调用本入口，压力值按 PLC uint32 数据宽度原样上传。
        /// </summary>
        /// <param name="STATIONCODE">MES 工站代码。</param>
        /// <param name="EQUIPMENTID">执行测试的设备编号。</param>
        /// <param name="PARTNOID">产品规格编码。</param>
        /// <param name="WOCODE">产品工单号。</param>
        /// <param name="SN">本次过程数据关联的产品序列号。</param>
        /// <param name="TAKEPHOTO1">前置拍照结果；双Y纯电测行使用 false。</param>
        /// <param name="RES">接触电阻测量值，单位沿用工艺配置。</param>
        /// <param name="TVMAXVOLTAGE">耐压过程最大电压，单位 V。</param>
        /// <param name="TVMAXCURRENT">耐压过程最大电流，单位 mA。</param>
        /// <param name="TVMETERID">执行本次耐压测试的仪器编号。</param>
        /// <param name="TVINFO">带 ACW/DCW/IR 模式前缀的仪器结果信息。</param>
        /// <param name="TVRESULT">本次电测判定。</param>
        /// <param name="PRESSURE_MAX">PLC uint32 压力最大值，单位沿用现场标定。</param>
        /// <param name="PRESSURE_AVERAGE">PLC uint32 压力平均值，单位沿用现场标定。</param>
        /// <param name="PRESSURE_MIN">PLC uint32 压力最小值，单位沿用现场标定。</param>
        /// <param name="PRESSURE_RESULT">本次流程压力判定。</param>
        /// <param name="TAKEPHOTO2">AOI 外观结果；双Y纯电测行使用 false。</param>
        /// <param name="RESULT">产品本轮流程综合结果。</param>
        /// <returns>MES 插入成功时返回 true。</returns>
        public static bool SaveBusBarData(string STATIONCODE, string EQUIPMENTID, string PARTNOID, string WOCODE, string SN,
           bool TAKEPHOTO1, float RES, float TVMAXVOLTAGE, float TVMAXCURRENT, string TVMETERID, string TVINFO, bool TVRESULT,
           UInt32 PRESSURE_MAX, UInt32 PRESSURE_AVERAGE, UInt32 PRESSURE_MIN, bool PRESSURE_RESULT,
           bool TAKEPHOTO2, string RESULT)
        {
            try
            {
                string sql = $"INSERT INTO SCADA_MES.\"BusbarCompressionData\" (STATIONCODE, EQUIPMENTID, PARTNOID, WOCODE, SN, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVMAXCURRENT, TVMETERID, TVINFO, TVRESULT, PRESSURE_MAX, PRESSURE_AVERAGE, PRESSURE_MIN, PRESSURE_RESULT, TAKEPHOTO2,  RESULT) VALUES " +
                    $"('{STATIONCODE}', '{EQUIPMENTID}', '{PARTNOID}', '{WOCODE}','{SN}', " +
                    $"{(TAKEPHOTO1?1:0)}, {RES},{TVMAXVOLTAGE}, {TVMAXCURRENT},'{TVMETERID}','{TVINFO}',{(TVRESULT?1:0)}," +
                    $"{PRESSURE_MAX}, {PRESSURE_AVERAGE},{PRESSURE_MIN}, {(PRESSURE_RESULT ? 1 : 0)}," +
                    $"{(TAKEPHOTO2 ? 1 : 0)}, '{RESULT}')";

                bool result = excutesql_mes(sql);
                if (result)
                {
                    WriteError($"母排数据保存成功: SN={SN}, 工位={STATIONCODE}, 结果={RESULT}", "SaveBusBarData");
                }
                else
                {
                    WriteError($"母排数据保存失败: SN={SN}, 工位={STATIONCODE}, 结果={RESULT}, SQL={sql}", "SaveBusBarData");
                }
                return result;
            }
            catch (Exception ex)
            {
                WriteError($"母排数据保存异常: SN={SN}, 工位={STATIONCODE}, 结果={RESULT}, 异常={ex.Message}", "SaveBusBarData");
                return false;
            }
        }





        //public static bool GetSNbyMSN(string MSN, out string SN)
        //{
        //    try
        //    {
        //        string sql = $"select SN from V_SN_MSN_T where MSN='{MSN}'";
        //        SN = ReadString_mes(sql);
        //        return true;
        //    }
        //    catch (Exception ex)
        //    {
        //        SN = string.Empty;
        //        return false;
        //    }
        //}


        /// <summary>
        /// 通过母排码(MP)查询SN（Resolver失败时的兜底路径）。
        /// </summary>
        /// <param name="MP">母排码</param>
        /// <param name="SN">查询到的SN</param>
        /// <param name="WOCODE">预留输出参数，当前实现未赋值</param>
        /// <returns>true: 查询成功；false: 查询失败</returns>
        public static bool GetSNByMP(string MP, out string SN, out string WOCODE)
        {
            SN = string.Empty;
            WOCODE = string.Empty;
            try
            {

                var r = client.GetProductSerialNumberByBusbar(MP);
                if (r.Success)
                {
                    SN = r.Data.ToString();
                    return true;
                }
            }
            catch (Exception ex)
            { }


            return false;
        }



        /// <summary>
        /// 查询返修后的最新SN并写入 __NEWSN 缓存。
        /// 注意：当前返回值未参与 DecodeSN 分支决策，仅用于补充链路信息。
        /// </summary>
        /// <param name="oldSN">待查询SN</param>
        /// <returns>当前实现固定返回 false（保留历史签名）。</returns>
        public static bool CheckSN(string oldSN)
        {

            try
            {
                //string sql = $"SELECT  WO_CODE,SN,OLD_SN from V_SN_F7_T WHERE sn='{oldSN}' OR OLD_SN='{oldSN}' ORDER BY OLD_SN  ASC ";

                var r = client.GetProductSerialNumberAfterLastRework(oldSN);

                if (r.Success)
                {
                    __NEWSN = r.Data;

                }



                //string sql = $"select WO_CODE,SN,OLD_SN from WIP_D_PRODUCT_SERIAL@Mesprod WHERE sn='{oldSN}' OR OLD_SN='{oldSN}' ORDER BY OLD_SN  ASC ";

                //DataTable dt = Read_mes(sql);


                //if (dt.Rows.Count == 1)
                //{
                //    SN = dt.Rows[0]["SN"].ToString();
                //    wo_code = dt.Rows[0]["WO_CODE"].ToString();
                //    return true;
                //}
                //else if (dt.Rows.Count >= 2)
                //{
                //    for (int i = 0; i < dt.Rows.Count; i++)
                //    {
                //        if (!string.IsNullOrEmpty(dt.Rows[i]["OLD_SN"].ToString()))
                //        {
                //            SN = dt.Rows[i]["SN"].ToString();
                //            wo_code = dt.Rows[i]["WO_CODE"].ToString();
                //            return true;
                //        }
                //    }
                //}
            }
            catch (Exception ex)
            {
                ;
            }

            //SN = string.Empty;
            //wo_code = string.Empty;
            return false;

        }


        //public static bool CheckSN(string oldSN, out string SN, out string wo_code)
        //{

        //    try
        //    {
        //        //string sql = $"SELECT  WO_CODE,SN,OLD_SN from V_SN_F7_T WHERE sn='{oldSN}' OR OLD_SN='{oldSN}' ORDER BY OLD_SN  ASC ";

        //        string sql = $"select WO_CODE,SN,OLD_SN from WIP_D_PRODUCT_SERIAL@Mesprod WHERE sn='{oldSN}' OR OLD_SN='{oldSN}' ORDER BY OLD_SN  ASC ";

        //        DataTable dt = Read_mes(sql);


        //        if (dt.Rows.Count == 1)
        //        {
        //            SN = dt.Rows[0]["SN"].ToString();
        //            wo_code = dt.Rows[0]["WO_CODE"].ToString();
        //            return true;
        //        }
        //        else if (dt.Rows.Count >= 2)
        //        {
        //            for (int i = 0; i < dt.Rows.Count; i++)
        //            {
        //                if (!string.IsNullOrEmpty(dt.Rows[i]["OLD_SN"].ToString()))
        //                {
        //                    SN = dt.Rows[i]["SN"].ToString();
        //                    wo_code = dt.Rows[i]["WO_CODE"].ToString();
        //                    return true;
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        ;
        //    }

        //    SN = string.Empty;
        //    wo_code = string.Empty;
        //    return false;

        //}


        //private static string create_cmd()
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.Append(" INSERT INTO SCADA_MES.EQUIPMENT_OUTPUT_RECORD (WO_CODE, ");
        //    sb.Append(" M_SN, ");
        //    sb.Append(" PROCEDURE_NAME,PROCEDURE_NUMBER, ");
        //    sb.Append(" EQUIPMENT_NUMBER, ");
        //    sb.Append(" PARA_NAME, ");
        //    sb.Append(" ADD_BY)  ");
        //    sb.Append(" WITH EMP AS ( ");
        //    sb.Append(" SELECT ");
        //    sb.Append(" EMP_NO ");
        //    sb.Append(" FROM ");
        //    sb.Append(" SCADA_MES.V_ON_STATION_T ");
        //    sb.Append(" WHERE ");
        //    sb.Append(" STATION_CODE = '{0}' ");
        //    sb.Append(" AND OUT_DT IS NULL ");
        //    sb.Append(" AND ROWNUM = 1 ");
        //    sb.Append(" ORDER BY ");
        //    sb.Append(" ONWORK_DT DESC ) ");
        //    sb.Append(" SELECT ");
        //    sb.Append(" '{1}' AS WO_CODE , ");
        //    sb.Append(" '{2}' AS M_SN, ");
        //    sb.Append(" '{3}' AS PROCEDURE_NAME,'{6}' AS PROCEDURE_NUMBER, ");
        //    sb.Append(" '{4}' AS EQUIPMENT_NUMBER, ");
        //    sb.Append(" '{5}' AS PARA_NAME, ");
        //    sb.Append(" NVL(EMP_NO, 'NOT_WORKER') AS ADD_BY ");
        //    sb.Append(" FROM ");
        //    sb.Append(" dual ");
        //    sb.Append(" LEFT JOIN EMP ON ");
        //    sb.Append(" EMP_NO IS NOT NULL ");
        //    return sb.ToString();
        //    // }
        //    // }

        //}

        private static bool WriteError(string message, string SQL)
        {
            string filename = $"{Environment.CurrentDirectory}\\数据库\\错误日志\\{DateTime.Now.ToString("yyyy-MM-dd")}.txt";
            string dir = Path.GetDirectoryName(filename);
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using (StreamWriter sw = new StreamWriter(filename, true))
                {
                    sw.WriteLine($"{message}\r\n{SQL}\r\n\r\n\r\n");
                    sw.Close();
                }
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }



        private static DataTable Read_mes(string sql)
        {
            try
            {
                DataTable dt = new DataTable();
                using (OracleConnection conn = new OracleConnection(mes_connstr))
                {
                    conn.Open();
                    OracleDataAdapter oda = new OracleDataAdapter(sql, conn);
                    oda.Fill(dt);
                    conn.Close();
                    conn.Dispose();
                    oda.Dispose();
                }
                return dt;
            }
            catch (Exception ex)
            {
                WriteError(ex.ToString(), sql);
                return null;
            }
        }
        private static string ReadString_mes(String sql)
        {
            try
            {
                string s = null;
                using (OracleConnection conn = new OracleConnection(mes_connstr))
                {
                    conn.Open();
                    OracleCommand odc = new OracleCommand(sql, conn);
                    OracleDataReader reader = odc.ExecuteReader();
                    reader.Read();
                    if (reader.HasRows)
                    {
                        s = reader.GetValue(0).ToString();
                    }

                    conn.Close();
                    conn.Dispose();
                    odc.Dispose();
                }
                return (s);
            }
            catch (Exception ex)
            {
                Debug.Write(ex.ToString());
                WriteError(ex.ToString(), sql);
                return null;
            }
        }
        private static Boolean excutesql_mes(String sql)
        {
            try
            {
                using (OracleConnection conn = new OracleConnection(mes_connstr))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandType = CommandType.Text;
                    cmd.ExecuteNonQuery();
                    conn.Close();
                    conn.Dispose();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                WriteError(ex.ToString(), sql);
                return false;
            }
        }


    }
}
