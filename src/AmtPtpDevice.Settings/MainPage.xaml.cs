using AmtPtpDevice.Settings.Comm;
using AmtPtpDevice.Settings.DataObjects;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace AmtPtpDevice.Settings
{
    public sealed partial class MainPage : Page
    {
        private PtpUserModeConfReport m_report;
        private bool m_isInitialDataPresented = false;
        private UsbHidDeviceAccessSubscription m_inputDevice;
        private UsbHidDeviceAccessSubscription m_battery;

        public MainPage()
        {
            this.InitializeComponent();
            ApplicationView.GetForCurrentView().SetPreferredMinSize(new Size(480, 500));
            ApplicationView.PreferredLaunchViewSize = new Size(480, 500);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;

            this.Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            SetupWatcher();

            if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.ApplicationView"))
            {
                var titleBar = ApplicationView.GetForCurrentView().TitleBar;
                if (titleBar != null)
                {
                    titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor 
                        = ((SolidColorBrush) Resources["AppBarBackground"]).Color;
                    titleBar.ButtonForegroundColor = ((SolidColorBrush) Resources["ApplicationForegroundThemeBrush"]).Color;
                    titleBar.BackgroundColor = titleBar.InactiveBackgroundColor 
                        = ((SolidColorBrush) Resources["AppBarBackground"]).Color;
                    titleBar.ForegroundColor = ((SolidColorBrush) Resources["ApplicationForegroundThemeBrush"]).Color;
                }
            }
        }

        private void SetupWatcher()
        {
            // Match by usage page/usage only (no fixed VID/PID) so both USB (VID 0x05AC) and
            // Bluetooth (VID 0x004C) connected Magic Trackpad 2/3 are picked up.
            m_inputDevice = new UsbHidDeviceAccessSubscription(HidDevice.GetDeviceSelector(0xff00, 0x0001));
            m_battery = new UsbHidDeviceAccessSubscription(HidDevice.GetDeviceSelector(0xff00, 0x0014));

            m_inputDevice.TargetDeviceAvailable += OnInputDeviceAvailable;
            m_inputDevice.TargetDeviceLost += OnInputDeviceLost;
            m_battery.TargetDeviceAvailable += OnBatteryAvailable;
        }

        private async void OnBatteryAvailable(object sender, EventArgs e)
        {
            var bReport = await m_battery.Device.GetInputReportAsync(0x90);
            var ptr = Marshal.AllocHGlobal((int) bReport.Data.Length);
            Marshal.Copy(bReport.Data.ToArray(), 0, ptr, (int) bReport.Data.Length);

            var battReport = Marshal.PtrToStructure<Mt2BatteryStatusReport>(ptr);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                m_battStatus.Text = $"배터리 {battReport.ChargeStatus}% 충전됨";
            });

            Marshal.FreeHGlobal(ptr);
        }

        private async void OnInputDeviceLost(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Set visibility
                m_disconnctedView.Visibility = Visibility.Visible;
                m_deviceControl.Visibility = Visibility.Collapsed;
                // Set state
                m_isInitialDataPresented = false;
            });
        }

        private async void OnInputDeviceAvailable(object sender, EventArgs e)
        {
            // The sensitivity feature report (0x09) is only implemented by the USB
            // user-mode driver; the Bluetooth HID filter driver doesn't support it.
            // Treat a failure here as "sensitivity controls unavailable" instead of
            // crashing the app.
            try
            {
                var sReport = await m_inputDevice.Device.GetFeatureReportAsync(0x09);
                var ptr = Marshal.AllocHGlobal((int)sReport.Data.Length);
                Marshal.Copy(sReport.Data.ToArray(), 0, ptr, (int)sReport.Data.Length);

                m_report = Marshal.PtrToStructure<PtpUserModeConfReport>(ptr);
                Marshal.FreeHGlobal(ptr);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Set value
                    m_sensitivitySlider.Value = m_report.PressureQualificationLevel;
                    m_confidenceSlider.Value = m_report.SingleContactSizeQualificationLevel;
                    m_muConfidenceSlider.Value = m_report.MultipleContactSizeQualificationLevel;

                    m_sensitivitySlider.IsEnabled = true;
                    m_confidenceSlider.IsEnabled = true;
                    m_muConfidenceSlider.IsEnabled = true;

                    // Set state
                    m_isInitialDataPresented = true;
                });
            }
            catch (Exception)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    m_sensitivitySlider.IsEnabled = false;
                    m_confidenceSlider.IsEnabled = false;
                    m_muConfidenceSlider.IsEnabled = false;

                    m_isInitialDataPresented = false;
                });
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Set visibility
                m_disconnctedView.Visibility = Visibility.Collapsed;
                m_deviceControl.Visibility = Visibility.Visible;
            });
        }

        private async void ApplySettings()
        {
            if (m_inputDevice.Device == null || !m_isInitialDataPresented) return;

            m_report.PressureQualificationLevel = (byte) m_sensitivitySlider.Value;
            m_report.SingleContactSizeQualificationLevel = (byte) m_confidenceSlider.Value;
            m_report.MultipleContactSizeQualificationLevel = (byte) m_muConfidenceSlider.Value;

            try
            {
                var featureReport = m_inputDevice.Device.CreateFeatureReport(0x09);
                using (var datawriter = new DataWriter())
                {
                    datawriter.WriteByte(0x09);
                    datawriter.WriteByte(m_report.PressureQualificationLevel);
                    datawriter.WriteByte(m_report.SingleContactSizeQualificationLevel);
                    datawriter.WriteByte(m_report.MultipleContactSizeQualificationLevel);
                    featureReport.Data = datawriter.DetachBuffer();
                    await m_inputDevice.Device.SendFeatureReportAsync(featureReport);
                }
            }
            catch (Exception)
            {
                // Device may have been disconnected mid-write, or the report isn't
                // supported by the current transport. Ignore rather than crash.
            }
        }

        private async void OnGestureButtonClicked(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("ms-settings:devices-touchpad"));
        }

        private void OnSliderValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            ApplySettings();
        }

        private void OnConfidenceSliderValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            ApplySettings();
        }

        private void OnMuConfidenceSliderValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            ApplySettings();
        }
    }
}
