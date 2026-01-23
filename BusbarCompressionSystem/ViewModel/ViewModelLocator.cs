/*
 * MVVM架构的ViewModel定位器
 *
 * WPF MVVM模式的核心组件，负责创建和管理所有ViewModel实例：
 * 1. 在App.xaml中注册为全局资源
 * 2. 提供统一的ViewModel访问接口
 * 3. 支持依赖注入和单例模式管理
 * 4. 连接View层和ViewModel层的桥梁
 *
 * 使用方式：在XAML中通过DataContext绑定到具体的ViewModel
 */

using CommonServiceLocator;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight;


namespace BusbarCompressionSystem.ViewModel
{
    /// <summary>
    /// This class contains static references to all the view models in the
    /// application and provides an entry point for the bindings.
    /// </summary>
    public class ViewModelLocator
    {
        /// <summary>
        /// Initializes a new instance of the ViewModelLocator class.
        /// </summary>
        public ViewModelLocator()
        {
            ServiceLocator.SetLocatorProvider(() => SimpleIoc.Default);

            ////if (ViewModelBase.IsInDesignModeStatic)
            ////{
            ////    // Create design time view services and models
            ////    SimpleIoc.Default.Register<IDataService, DesignDataService>();
            ////}
            ////else
            ////{
            ////    // Create run time view services and models
            ////    SimpleIoc.Default.Register<IDataService, DataService>();
            ////}

            SimpleIoc.Default.Register<MainViewModel>();
            SimpleIoc.Default.Register<PositionDetect.ViewModel.PositionDetectViewModel>();

        }

        public MainViewModel Main
        {
            get
            {
                return ServiceLocator.Current.GetInstance<MainViewModel>();
            }
        }
        public PositionDetect.ViewModel.PositionDetectViewModel PositionDetectViewModel
        {
            get
            {
                return ServiceLocator.Current.GetInstance<PositionDetect.ViewModel.PositionDetectViewModel>();
            }
        }
        public static void Cleanup()
        {
            // TODO Clear the ViewModels
        }
    }
}
