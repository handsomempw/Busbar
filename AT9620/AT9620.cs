using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Serialization;
using GalaSoft.MvvmLight;

namespace AT9620
{
    public class AT9620 : ObservableObject
    {
        public event EventHandler DataReceived;
        public TVParameter TVParameter { get; set; } = new TVParameter();
        public int delaytime { set; get; } = 1000;
        public int intervaltime { set; get; } = 30;

        public bool stop { set; get; } = false;

        [XmlIgnore]
        public string strrecord { set; get; } = string.Empty;
        public bool TestResult { set; get; } = false;

        private TcpClient tcp = new TcpClient();

        public string IP { set; get; } = "192.168.0.211";
        public int Port { set; get; } = 2000;

        [XmlIgnore]
        public bool isconnected = false;

        public bool connect(string _IPaddress, int _Port, int _receivetimeout = 5000)
        {
            try
            {
                IP = _IPaddress;
                Port = _Port;

                if ((tcp.Client == null) || (!tcp.Connected))
                {
                    tcp = new TcpClient();
                    tcp.ReceiveTimeout = 10000;
                    tcp.ConnectAsync(IP, Port).Wait(200);
                }
                isconnected = tcp.Connected;
            }
            catch (Exception ex)
            {
                isconnected = false;
            }

            return isconnected;
        }

        public bool disconnect()
        {
            try
            {
                if (isconnected)
                {
                    tcp?.Close();
                    isconnected = false;
                }
            }
            catch
            {

            }

            return !isconnected;
        }

        public Result Start()
        {
            var c = connect(IP, Port);
            if (!c)
            {
                return new Result() { Error = "连接失败" };
            }
            var r = _Start();
            disconnect();
            return r;
        }

        public Result Download()
        {
            var c = connect(IP, Port);
            if (!c)
            {
                return new Result() { Error = "连接失败" };
            }
            var r = _Download();
            disconnect();
            return r;
        }
        public Result _Download()
        {

            Result r = new Result();

            var r1 = Send("FUNC:SOUR:STEP:NEW\n");
            Thread.Sleep(intervaltime);

            string wp = $"WP 1,{TVParameter.TestMode},{TVParameter.Voltage},{TVParameter.TestTime},{TVParameter.RiseTime},{TVParameter.FallTime},{TVParameter.High},{TVParameter.Low},{TVParameter.Arc},{(TVParameter.Freq == 50 ? 0 : 1)}\n";
            var r2 = Send(wp);
            if (!r2.Success)
            {
                r.Error = r2.Error;
                return r;
            }
            Thread.Sleep(intervaltime);

            var r3 = Get_String("rp? 1\n");
            if (!r3.Success)
            {
                r.Error = r3.Error;
                return r;
            }
            var r4 = TVParameter.compare(r3.Value);
            if (!r4.Success)
            {
                r.Error = $"测试工艺参数不一致，请重新下发工艺参数\r\n{r4.Error}";
                return r;
            }

            r.Success = true;


            return r;


        }

        /// <summary>
        /// 执行耐压测试的核心方法
        /// 这是电测系统的核心执行逻辑，负责启动测试、实时监控测试过程、判断测试结果
        /// </summary>
        /// <returns>Result对象，包含测试结果和详细的错误信息或过程数据</returns>
        public Result _Start()
        {
            // 初始化返回结果对象
            Result r = new Result();

            // 步骤1：验证测试步骤数量
            // AT9620设备支持多个测试步骤，这里确保只有一个步骤（简化模式）
            var r1 = Get_StepNum();
            if (!r1.Success)
            {
                r.Error = r1.Error;
                return r;
            }
            if (r1.Value != 1)
            {
                r.Error = $"测试工艺步骤数量{r1.Value}，不等于1";
                return r;
            }

            // 步骤2：参数一致性验证
            // 从设备回读当前设置的参数，与本地TVParameter对象进行对比
            // 确保设备实际运行的参数与期望参数一致
            var r2 = Get_String("rp? 1\n");
            if (!r2.Success)
            {
                r.Error = r2.Error;
                return r;
            }
            var r3 = TVParameter.compare(r2.Value);
            if (!r3.Success)
            {
                r.Error = $"测试工艺参数不一致，请重新下发工艺参数\r\n{r3.Error}";
                return r;
            }

            // 步骤3：启动耐压测试
            // 向AT9620设备发送启动测试命令
            var r4 = Send("FUNCtion:STARt\n");
            Thread.Sleep(delaytime); // 等待设备响应

            // 初始化测试监控变量
            ResultTVProcess resultTVProcess = new ResultTVProcess(); // 测试过程数据接收对象
            DateTime dt = DateTime.Now; // 记录测试开始时间
            // 计算理论测试总时间 = 上升时间 + 保持时间 + 下降时间
            float totaltime = TVParameter.TestTime + TVParameter.RiseTime + TVParameter.FallTime;

            #region 清空数据
            // 清空历史测试记录，为新的测试过程做准备
            strrecord = string.Empty;
            #endregion

            // 初始化停止标志
            stop = false;

            // 步骤4：进入测试监控主循环
            // 实时监控测试过程，直到测试完成或超时
            while (true)
            {
                try
                {
                    // 检查是否有外部停止信号
                    if (stop)
                    {
                        var stopr = Send("FUNCtion:STOP\n"); // 发送停止测试命令
                    }

                    // 获取实时测试过程数据
                    resultTVProcess = GetProcess();
                    if (resultTVProcess.Success)
                    {
                        // 触发数据接收事件，通知上层应用更新界面显示
                        DataReceived.Invoke(this, new AT9620EventArgs()
                        {
                            ResultTVProcess = resultTVProcess
                        });

                        // 核心状态判断逻辑
                        // AT9620设备在测试的不同阶段会返回不同的状态字符串：
                        // "Ramp Up" - 电压上升阶段
                        // "Dwell" - 电压保持阶段
                        // "Ramp Down" - 电压下降阶段
                        // "PASS" - 测试通过
                        // 其他状态 - 测试失败（如"上升不良"、"电流超限"等）
                        if (resultTVProcess.Value.status != "Dwell" &&
                            resultTVProcess.Value.status != "Ramp Up" &&
                            resultTVProcess.Value.status != "Ramp Down")
                        {
                            // 收到非过程状态，表示测试已结束
                            if (resultTVProcess.Value.status == "PASS")
                            {
                                r.Success = true; // 测试通过
                            }
                            else
                            {
                                // 测试失败，记录具体的失败原因
                                r.Error = resultTVProcess.Value.status;
                            }
                            break; // 退出监控循环
                        }
                    }

                    // 超时检测机制
                    // 如果测试时间超过理论总时间+2秒（缓冲时间），认为测试异常
                    if ((DateTime.Now - dt).TotalSeconds > totaltime + 2)
                    {
                        r.Error = "测试超时未完成";
                        break;
                    }
                }
                catch
                {
                    // 捕获异常但不中断测试，继续监控
                    // 这是为了防止网络波动等临时问题导致测试中断
                    continue;
                }
            }

            // 保存完整的测试过程原始数据记录
            // 无论测试成功还是失败，都保存过程数据用于后续分析
            //if(!r.Success)
            //{
            r.Recordstr = strrecord;
            //}

            // 返回测试结果
            return r;
        }
        public Result Get_String(string cmd)
        {
            Result r = new Result();
            try
            {
                if (isconnected)
                {
                    #region 读取数值
                    //string steprquestcmd = "FUNC:SOUR:STEP?\n";
                    //tcp.Client.Send(Encoding.ASCII.GetBytes($"{steprquestcmd}"));
                    tcp.Client.Send(Encoding.ASCII.GetBytes($"{cmd}"));

                    Thread.Sleep(100);
                    byte[] receive = new byte[1024];
                    tcp.Client.Receive(receive);

                    string result_str = System.Text.Encoding.ASCII.GetString(receive).Replace("\0", "").Replace("\r", "").Replace("\n", "");
                    if (string.IsNullOrEmpty(result_str))
                    {
                        r.Error = "接收数据为空";
                    }
                    else
                    {
                        r.Value = result_str;
                        r.Success = true;
                    }
                    #endregion

                }
            }
            catch (Exception ex)
            {
                r.Error = ex.ToString();
            }
            return r;


        }

        public Resultint Get_StepNum()
        {
            Resultint r = new Resultint();

            var re = Get_String("FUNC:SOUR:STEP?\n");
            if (re.Success)
            {
                try
                {
                    r.Value = Convert.ToInt32(re.Value.Split('-')[1].Replace("TOTAL", "").Trim());
                    r.Success = true;
                }
                catch (Exception EX)
                {

                    r.Error = $"接收内容:{re.Value}\r\n {EX.ToString()}";
                }

            }
            else
            {
                r.Error = re.Error;
                r.Success = re.Success;

            }



            return r;


        }
        public Result Send(string cmd)
        {
            Result r = new Result();
            try
            {
                if (isconnected)
                {
                    #region 读取数值
                    //string steprquestcmd = "FUNC:SOUR:STEP?\n";
                    //tcp.Client.Send(Encoding.ASCII.GetBytes($"{steprquestcmd}"));
                    tcp.Client.Send(Encoding.ASCII.GetBytes($"{cmd}"));

                    //Thread.Sleep(100);
                    //byte[] receive = new byte[1024];
                    //tcp.Client.Receive(receive);

                    //string result_str = System.Text.Encoding.ASCII.GetString(receive).Replace("\0", "").Replace("\r", "").Replace("\n", "");
                    //if (string.IsNullOrEmpty(result_str))
                    //{
                    //    r.Error = "接收数据为空";
                    //}
                    //else
                    //{
                    //    r.Value = result_str;
                    //    r.Success = true;
                    //}
                    r.Success = true;
                    #endregion


                }
            }
            catch (Exception ex)
            {
                r.Error = ex.ToString();
            }
            return r;
        }

        public ResultTVProcess GetProcess()
        {
            ResultTVProcess ResultTVProcess = new ResultTVProcess();
            var re = Get_String("Fetch?\n");
            if (re.Success)
            {
                strrecord += re.Value + "/";
                string[] ss = re.Value.Split(',');
                if (ss.Length == 6)
                {
                    float f1, f2, f3;
                    bool r1 = float.TryParse(ss[3], out f1);
                    bool r2 = float.TryParse(ss[4], out f2);
                    bool r3 = float.TryParse(ss[5], out f3);
                    if (r1 & r2 & r3)
                    {
                        ResultTVProcess.Value.status = ss[2];
                        ResultTVProcess.Value.Voltage = f1;
                        ResultTVProcess.Value.Current = f2;
                        ResultTVProcess.Value.Time = f3;
                        ResultTVProcess.Success = true;
                    }
                    else
                    {
                        ResultTVProcess.Error = "字符串格式错误";
                    }
                }

            }
            else
            {
                ResultTVProcess.Error = re.Error;

            }
            return ResultTVProcess;
        }


    }



    public class Resultint : ObservableObject
    {
        public bool Success { get; set; } = false;
        public int Value { get; set; } = -1;
        public string Error { get; set; }

    }


    public class Result : ObservableObject
    {
        public bool Success { get; set; } = false;
        public string Value { set; get; } = string.Empty;
        public string Error { get; set; }
        public string Recordstr { set; get; }

    }

    public class ResultTVProcess : ObservableObject
    {
        public bool Success { get; set; } = false;
        public TVProcess Value { set; get; } = new TVProcess();
        public string Error { get; set; }
    }


    public class TVParameter : ObservableObject
    {

        //public string Parastring
        //{
        //    get
        //    {
        //        return $"{TestMode},{Voltage},{TestTime},{RiseTime},{FallTime},{High},{Low},{Arc},{(Freq == 50 ? 0 : 1)}";

        //    }
        //}

        public Result compare(string cmd)
        {
            Result result = new Result();
            try
            {
                string[] ss = cmd.Split(',');

                float _voltage, _testtime, _risetime, _falltime, _high, _low, _arc, _freq;
                var r1 = float.TryParse(ss[1], out _voltage);
                var r2 = float.TryParse(ss[2], out _testtime);
                var r3 = float.TryParse(ss[3], out _risetime);
                var r4 = float.TryParse(ss[4], out _falltime);
                var r5 = float.TryParse(ss[5], out _high);
                var r6 = float.TryParse(ss[6], out _low);
                var r7 = float.TryParse(ss[7], out _arc);
                var r8 = float.TryParse(ss[8], out _freq);
                _freq = (_freq == 0 ? 50 : 60);

                if (!r1)
                {
                    result.Error += $"\r\n回读电压数据格式错误";
                }
                if (!r2)
                {
                    result.Error += $"\r\n回读测试时间数据格式错误";
                }
                if (!r3)
                {
                    result.Error += $"\r\n回读上升时间数据格式错误";
                }
                if (!r4)
                {
                    result.Error += $"\r\n回读下降时间数据格式错误";
                }
                if (!r5)
                {
                    result.Error += $"\r\n回读电流上限数据格式错误";
                }
                if (!r6)
                {
                    result.Error += $"\r\n回读电流下限数据格式错误";
                }
                if (!r7)
                {
                    result.Error += $"\r\n回读电弧侦测数据格式错误";
                }
                if (!r8)
                {
                    result.Error += $"\r\n回读频率数据格式错误";
                }

                if (TestMode.ToString() != ss[0])
                {
                    result.Error += $"\r\n测试模式不匹配";
                }
                if (Voltage != _voltage)
                {
                    result.Error += $"\r\n回读电压与测试工艺不符合";
                }

                if (TestTime != _testtime)
                {
                    result.Error += $"\r\n回读测试时间与测试工艺不符合";
                }
                if (RiseTime != _risetime)
                {
                    result.Error += $"\r\n回读上升时间与测试工艺不符合";
                }
                if (FallTime != _falltime)
                {
                    result.Error += $"\r\n回读下降时间与测试工艺不符合";
                }
                if (High != _high)
                {
                    result.Error += $"\r\n回读电流上限与测试工艺不符合";
                }
                if (Low != _low)
                {
                    result.Error += $"\r\n回读电流下限与测试工艺不符合";
                }
                if (Arc != _arc)
                {
                    result.Error += $"\r\n回读电弧与测试工艺不符合";
                }
                if (Freq != _freq)
                {
                    result.Error += $"\r\n回读电压与测试工艺不符合";
                }
                if (string.IsNullOrEmpty(result.Error))
                {
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Error += "\r\n" + ex.Message;
            }
            return result;
        }


        public TestMode TestMode { get; set; } = TestMode.ACW;

        public float Voltage { set; get; } = -1;

        public float TestTime { set; get; } = -1;
        public float RiseTime { set; get; } = -1;
        public float FallTime { set; get; } = -1;
        public float High { set; get; } = -1;
        public float Low { set; get; } = 0;
        public float Arc { set; get; } = 0;
        public float Freq { set; get; } = 50;


    }
    public class TVProcess
    {

        public string status { set; get; } = string.Empty;

        public float Voltage { set; get; } = 0;
        public float Current { set; get; } = 0;
        public float Time { set; get; } = 0;
    }

    public enum TestMode
    {
        ACW,
        DCW,
        IR
    }
    public class AT9620EventArgs : EventArgs
    {
        public ResultTVProcess ResultTVProcess { set; get; } = new ResultTVProcess();
    }
}
