/*
 * 数据模型总容器
 *
 * MVVM架构中的Model层核心，统一管理所有业务数据：
 * - Processmodel：生产流程参数和测试数据
 * - Recordmodel：生产记录和历史数据
 * - Settingmodel：系统配置和硬件参数
 * - FaraVisionDataModel：视觉检测相关数据
 *
 * 作用：将分散的数据模型组织在一起，通过XML序列化实现数据持久化
 */

using BusbarCompressionSystem.FaraVision;
using BusbarCompressionSystem.Model.Setting1;
using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model
{
    public class DataModel : ObservableObject
    {
        #region 数据模型
        [XmlElement("过程参数模型")]
        public Processmodel Processmodel { get; set; } = new Processmodel();

        [XmlElement("日志模型")]
        public RecordModel Recordmodel { get; set; } = new RecordModel();

        [XmlElement("配置模型")]
        public SettingModel Settingmodel { get; set; } = new SettingModel();

        private FaraVisionDataModel _faraVisionDataModel;

        /// <summary>
        /// 标准产线的 FaraVision 运行模型。双Y专用窗口不会访问该属性，视觉模型和相机对象只在标准产线实际使用时创建。
        /// </summary>
        public FaraVisionDataModel FaraVisionDataModel
        {
            get { return _faraVisionDataModel ?? (_faraVisionDataModel = new FaraVisionDataModel()); }
            set { _faraVisionDataModel = value; }
        }

        /// <summary>
        /// 双Y机台专用配置，独立保存到“配置\双Y电测配置.xml”。
        /// 普通扫码、标准视觉和机器人配置继续由 <see cref="Settingmodel"/> 管理。
        /// </summary>
        [XmlIgnore]
        public DualYElectricalTestConfiguration DualYConfiguration { get; set; } = new DualYElectricalTestConfiguration();


        #endregion



    }
}
