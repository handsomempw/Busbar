/*
 * 手动工艺参数配置类
 *
 * 用于从JSON配置文件加载手动工艺参数：
 * 1. TVParameter：耐压测试参数配置
 * 2. PressureParameter：压力测试参数配置
 * 3. 配置元信息：配置名称、创建时间、描述等
 *
 * 作用：支持手动参数下发功能，无需通过MES系统获取参数
 */

using GalaSoft.MvvmLight;
using System;

namespace BusbarCompressionSystem.Model
{
    /// <summary>
    /// 手动工艺参数配置类
    /// </summary>
    public class ManualParameterConfig : ObservableObject
    {
        /// <summary>
        /// 配置名称
        /// </summary>
        public string ConfigName { get; set; } = "默认配置";
        
        /// <summary>
        /// 创建时间
        /// </summary>
        public string CreateTime { get; set; } = "";
        
        /// <summary>
        /// 配置描述
        /// </summary>
        public string Description { get; set; } = "";
        
        /// <summary>
        /// 耐压测试参数配置
        /// </summary>
        public TVParameterConfig TVParameter { get; set; } = new TVParameterConfig();
        
        /// <summary>
        /// 压力测试参数配置
        /// </summary>
        public PressureParameterConfig PressureParameter { get; set; } = new PressureParameterConfig();
    }

    /// <summary>
    /// 耐压测试参数配置
    /// </summary>
    public class TVParameterConfig
    {
        /// <summary>
        /// 测试模式：0=ACW(交流耐压), 1=DCW(直流耐压), 2=IR(绝缘电阻)
        /// </summary>
        public int TestMode { get; set; } = 0;
        
        /// <summary>
        /// 测试电压(V)
        /// </summary>
        public float Voltage { get; set; } = 2700;
        
        /// <summary>
        /// 测试时间(s)
        /// </summary>
        public float TestTime { get; set; } = 20;
        
        /// <summary>
        /// 上升时间(s)
        /// </summary>
        public float RiseTime { get; set; } = 2;
        
        /// <summary>
        /// 下降时间(s)
        /// </summary>
        public float FallTime { get; set; } = 2;
        
        /// <summary>
        /// 电流上限(mA) - 对应测试电流/设备电流上限
        /// </summary>
        public float High { get; set; } = 3;
        
        /// <summary>
        /// 电流下限(mA) - 对应充电电流下限
        /// </summary>
        public float Low { get; set; } = 0.1f;
        
        /// <summary>
        /// 电弧等级
        /// </summary>
        public float Arc { get; set; } = 0;
        
        /// <summary>
        /// 测试频率(Hz)
        /// </summary>
        public float Freq { get; set; } = 50;
    }

    /// <summary>
    /// 压力测试参数配置
    /// </summary>
    public class PressureParameterConfig
    {
        /// <summary>
        /// 目标压力(KPa)
        /// </summary>
        public ushort Pressure { get; set; } = 1000;
        
        /// <summary>
        /// 压力上限(KPa)
        /// </summary>
        public float Max_Pressure { get; set; } = 1050;
        
        /// <summary>
        /// 压力下限(KPa)
        /// </summary>
        public float Min_Pressure { get; set; } = 950;
    }
}

