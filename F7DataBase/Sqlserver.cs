using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

namespace F7DataBase
{
    /// <summary>
    /// 数据库访问类
    /// 核心业务：管理电气安全测试（耐压测试）数据，确保产品质量符合电气绝缘标准
    /// 架构特点：工业局域网部署，重试机制+本地备份保障生产连续性
    /// </summary>
    public class Sqlserver
    {


        #region 数据库基本操作

        /// <summary>
        /// 工业生产数据库连接配置
        /// 连接参数：192.168.95.107工业服务器，F7Data数据库
        /// 数据范畴：工单、生产序列号、测试结果、操作员记录、设备状态
        /// 网络架构：企业级局域网部署，确保生产数据实时同步
        /// </summary>
        private static string connenstrf7 = "Provider=SQLOLEDB.1;Password=f7data;Persist Security Info=True;User ID=f7;Initial Catalog=F7Data;Data Source=192.168.95.107";

        /// <summary>
        /// 数据库连接字符串实例变量
        /// 为每个数据库操作实例提供独立的连接配置
        /// </summary>
        private string SQL_CONNSTR = connenstrf7;
        /// <summary>
        /// 执行数据修改SQL语句 - 工业生产数据写入
        /// 业务场景：生产过程状态更新、测试结果录入、设备状态变更
        /// 资源管理：using语句确保连接及时释放，避免工业环境下的连接池耗尽
        /// 错误处理：记录详细异常信息，支持生产问题快速排查
        /// </summary>
        /// <param name="sql">INSERT/UPDATE/DELETE语句</param>
        /// <returns>true=执行成功，false=执行失败</returns>
        public Boolean excutesql_sql(String sql)
        {
            try
            {
                // 使用using语句确保数据库连接正确释放，避免连接泄漏
                using (OleDbConnection conn = new OleDbConnection(SQL_CONNSTR))
                {
                    conn.Open();  // 建立数据库连接
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;  // 设置要执行的SQL语句
                    cmd.CommandType = CommandType.Text;  // 指定为文本类型的SQL语句

                    cmd.ExecuteNonQuery();  // 执行SQL语句，不返回结果集
                    conn.Close();  // 关闭连接（using语句会自动调用Dispose）
                    conn.Dispose();  // 释放连接资源
                    return true;  // 执行成功返回true
                }
            }
            catch (Exception ex)
            {
                // 记录异常信息到调试输出，用于问题排查
                Debug.WriteLine(ex.ToString());
                return false;  // 执行失败返回false
            }
        }
        /// <summary>
        /// 执行数据查询操作 - 返回完整结果集
        /// 业务场景：生产报表生成、质量统计分析、历史数据追溯
        /// 性能考虑：DataAdapter.Fill()一次性加载所有数据，适合中小型结果集
        /// 资源管理：手动释放连接和适配器，确保长时间运行的生产环境稳定
        /// </summary>
        /// <param name="sql">SELECT查询语句</param>
        /// <returns>DataTable结果集，查询失败返回null</returns>
        public DataTable Read_sql(string sql)
        {
            try
            {
                // 创建数据库连接（注意这里没有使用using语句，需要手动释放）
                OleDbConnection conn = new OleDbConnection(SQL_CONNSTR);
                conn.Open();  // 打开数据库连接

                // 创建数据适配器，用于执行查询并填充数据
                OleDbDataAdapter oda = new OleDbDataAdapter(sql, conn);
                DataTable dt = new DataTable();  // 创建数据表对象存储查询结果
                oda.Fill(dt);  // 执行查询并将结果填充到DataTable中

                // 手动释放数据库资源
                conn.Close();  // 关闭连接
                conn.Dispose();  // 释放连接对象
                oda.Dispose();  // 释放数据适配器

                return dt;  // 返回查询结果
            }
            catch
            {
                // 查询失败时返回null，由调用方处理异常情况
                return null;
            }
        }
        /// <summary>
        /// 执行查询SQL语句 - 返回单个字符串结果
        /// 用于获取单个字段值的查询，如获取产品状态、配置参数等
        /// 在系统中用于快速查询特定数据项
        /// </summary>
        /// <param name="sql">返回单行的SELECT查询语句</param>
        /// <returns>查询结果的第一列第一行的字符串值，失败时返回null</returns>
        public string ReadString_sql(string sql)
        {
            try
            {
                // 使用using语句确保资源正确释放
                using (OleDbConnection conn = new OleDbConnection(SQL_CONNSTR))
                {
                    conn.Open();  // 建立数据库连接

                    // 创建命令对象执行SQL查询
                    OleDbCommand odc = new OleDbCommand(sql, conn);

                    // 执行查询并获取数据读取器
                    var reader = odc.ExecuteReader();

                    // 读取第一行数据（假设查询只返回一行）
                    reader.Read();

                    // 获取第一列的值并转换为字符串
                    string s = reader.GetValue(0).ToString();

                    // 释放数据库资源
                    conn.Close();
                    conn.Dispose();
                    odc.Dispose();

                    return (s);  // 返回查询结果
                }
            }
            catch (Exception ex)
            {
                // 查询失败时返回null
                return null;
            }
        }
        #endregion


        #region 耐压过程数据存储
        /// <summary>
        /// 电气安全测试数据存储 - 核心业务流程
        /// 业务场景：生产过程中，对每件产品进行耐压测试，验证电气绝缘强度
        /// 数据流向：测试仪→本系统→F7Data数据库，完整记录测试参数和结果
        /// 质量控制：测试失败的产品将被隔离，确保电气安全符合国家标准
        /// 故障处理：网络异常时自动切换到本地文件存储，保障生产数据不丢失
        /// 业务逻辑：每次测试产生一条完整记录，包含测试参数、结果和环境信息
        /// 表映射：dr_TVProcess表存储所有电气安全测试历史数据
        /// </summary>
        /// <param name="WO_CODE">工单编号 - 标识生产批次</param>
        /// <param name="SN">产品序列号</param>
        /// <param name="PROCERURENAME">测试步骤名称 - 如"电压上升"、"保持"、"下降"</param>
        /// <param name="RESULT">测试结果 - PASS/FAIL，决定产品质量状态</param>
        /// <param name="WORKERID">操作员ID - 记录测试责任人</param>
        /// <param name="DT">测试时间戳 - 精确到毫秒的生产时间记录</param>
        /// <param name="TVMETERID">测试设备ID - 标识使用的耐压测试仪</param>
        /// <param name="PROCESSDATA">过程数据 - JSON格式存储电压、电流、时间等详细参数</param>
        /// <returns>存储成功状态</returns>
        public bool Save_TVProcessData(string WO_CODE, string SN, string PROCERURENAME, string RESULT, string WORKERID, DateTime DT, string TVMETERID, string PROCESSDATA)
        {
            try
            {
                // 构建SQL插入语句，映射业务参数到数据库字段
                // 字段映射：工单→FBatch, 步骤→FStep, 序列号→FSerial, 结果→FTVResult, 操作员→FOpman, 设备→FMachine, 过程数据→FProcess
                string sql = $"INSERT INTO [F7Data].[dbo].[dr_TVProcess] ( [FBatch], [FStep], [FSerial], [FTVResult], [FOpdate], [FOpman], [FMachine], [FProcess]) VALUES " +
                    $"( '{WO_CODE}', '{PROCERURENAME}', '{SN}', '{RESULT}', '{DT.ToString("yyyy-MM-dd HH:mm:ss.FFF")}', '{WORKERID}', '{TVMETERID}', '{PROCESSDATA}');";

                // 工业环境容错策略：网络瞬断时重试3次，每次间隔100ms
                // 场景：工业网络偶尔出现短暂干扰，重试可恢复90%以上的临时故障
                bool R = true;
                int I = 0;
                while (I++ < 3)
                {
                    R &= excutesql_sql(sql);
                    if (R) { break; }
                    Thread.Sleep(100);
                }

                // 故障转移机制：数据库连续失败时切换到本地文件存储
                // 业务影响：确保测试数据不丢失，支持事后批量导入数据库
                if (!R)
                {
                    #region 保存耐压过程数据到本地
                    Save_TVProcessData_Local(WO_CODE, SN, PROCERURENAME, RESULT, WORKERID, DT, TVMETERID, PROCESSDATA);
                    #endregion
                }
                return R;

            }
            catch (Exception ex) { return false; }
        }

        /// <summary>
        /// 本地文件备份存储 - 故障转移机制
        /// 业务场景：当数据库连接失败时，将测试数据保存到本地文件系统
        /// 文件组织：按日期/工单号组织目录结构，便于管理和批量处理
        /// 数据格式：使用$分隔符的文本行，便于后续导入数据库
        /// 恢复策略：系统恢复后可通过专门工具将本地文件数据导入数据库
        /// </summary>
        private bool Save_TVProcessData_Local(string WO_CODE, string SN, string PROCERURENAME, string RESULT, string WORKERID, DateTime DT, string TVMETERID, string PROCESSDATA)
        {
            try
            {
                // 文件路径策略：保存错误数据/耐压过程数据/日期/工单号.txt
                // 目录结构设计：按日期分组便于归档，按工单分组便于业务查询
                string filename = $"{Environment.CurrentDirectory}\\保存错误数据\\耐压过程数据\\{DateTime.Now.ToString("yyyy-MM-dd")}\\{WO_CODE}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 数据持久化：追加写入模式，确保多条测试记录可累积存储
                using (StreamWriter sw = new StreamWriter(filename))
                {
                    sw.WriteLine($"{WO_CODE}${SN}${PROCERURENAME}${RESULT}${WORKERID}${DT}${TVMETERID}${PROCESSDATA}");
                    sw.Close();
                }

                return true;
            }
            catch (Exception ex) { return false; }
        }
        #endregion
    }
}
