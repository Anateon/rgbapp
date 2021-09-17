using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace rgbapp
{
    public sealed partial class MainPage : Page
    {
        private DeviceWatcher deviceWatcher;
        private ObservableCollection<BluetoothLEDeviceDisplay> KnownDevices = new ObservableCollection<BluetoothLEDeviceDisplay>();
        private BluetoothLEDevice bluetoothLeDevice = null;
        private GattCharacteristic selectedCharacteristic;
        private DispatcherTimer dispatcherTimer;
        public string SelectedBleDeviceId;
        public string SelectedBleDeviceName = "No device selected";
        public string Command;
        readonly int E_DEVICE_NOT_AVAILABLE = unchecked((int)0x800710df);

        public MainPage()
        {
            this.InitializeComponent();
            DeviceList.ItemsSource = KnownDevices;
            StartBleDeviceWatcher();
            var view = ApplicationView.GetForCurrentView();
            view.SetPreferredMinSize(new Size { Width = 332, Height = 420 });
                dispatcherTimer = new DispatcherTimer()
            {
                Interval = new TimeSpan(0, 0, 0, 0, 34)
            };
            dispatcherTimer.Tick += (object sender, object e) =>
            {
                SendInfo();
                dispatcherTimer.Stop();
            };
        }

        public class BluetoothLEDeviceDisplay
        {
            public BluetoothLEDeviceDisplay(DeviceInformation deviceInfoIn)
            {
                DeviceInformation = deviceInfoIn;
            }
            public DeviceInformation DeviceInformation { get; private set; }
            public string Id => DeviceInformation.Id;
            public string Name => DeviceInformation.Name;
        }


        private void StartBleDeviceWatcher()
        {
            string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Le.IsConnectable" };
            string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";
            deviceWatcher =
                    DeviceInformation.CreateWatcher(
                        aqsAllBluetoothLEDevices,
                        requestedProperties,
                        DeviceInformationKind.AssociationEndpoint);
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Removed += DeviceWatcher_Removed;
            KnownDevices.Clear();
            deviceWatcher.Start();
        }

        private void StopBleDeviceWatcher()
        {
            if (deviceWatcher != null)
            {
                deviceWatcher.Added -= DeviceWatcher_Added;
                deviceWatcher.Removed -= DeviceWatcher_Removed;
                deviceWatcher.Stop();
                deviceWatcher = null;
            }
        }

        private BluetoothLEDeviceDisplay FindBluetoothLEDeviceDisplay(string id)
        {
            foreach (BluetoothLEDeviceDisplay bleDeviceDisplay in KnownDevices)
            {
                if (bleDeviceDisplay.Id == id)
                {
                    return bleDeviceDisplay;
                }
            }
            return null;
        }

        private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                lock (this)
                {
                    if (sender == deviceWatcher)
                    {
                        if (FindBluetoothLEDeviceDisplay(deviceInfo.Id) == null)
                        {
                            if (deviceInfo.Name != string.Empty)
                            {
                                KnownDevices.Add(new BluetoothLEDeviceDisplay(deviceInfo));
                            }
                        }
                    }
                }
            });
        }

        private async void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                lock (this)
                {
                    if (sender == deviceWatcher)
                    {
                        BluetoothLEDeviceDisplay bleDeviceDisplay = FindBluetoothLEDeviceDisplay(deviceInfoUpdate.Id);
                        if (bleDeviceDisplay != null)
                        {
                            KnownDevices.Remove(bleDeviceDisplay);
                        }
                    }
                }
            });
        }

        private async void ConnectButton_Click()
        {
            selectedCharacteristic = null;
            DeviceInfoTextBlock.Text = "";
            DeviceList.IsEnabled = ConnectButton.IsEnabled = false;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            try
            {
                bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(SelectedBleDeviceId);
                if (bluetoothLeDevice == null)
                {
                    DeviceInfoTextBlock.Text = "Failed to connect to device.";
                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                    ModeSwitch.IsEnabled = PickerList.IsEnabled = PowerSwitch.IsEnabled = false;
                    DeviceList.IsEnabled = ConnectButton.IsEnabled = true;
                    return;
                }
            }
            catch (Exception ex) when (ex.HResult == E_DEVICE_NOT_AVAILABLE)
            {
                DeviceInfoTextBlock.Text = "Bluetooth radio is not on.";
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                ModeSwitch.IsEnabled = PickerList.IsEnabled = PowerSwitch.IsEnabled = false;
                DeviceList.IsEnabled = ConnectButton.IsEnabled = true;
                return;
            }
            if (bluetoothLeDevice != null)
            {
                GattDeviceServicesResult result = await bluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                if (result.Status == GattCommunicationStatus.Success)
                {
                    IReadOnlyList<GattDeviceService> services = result.Services;
                    foreach (var serv in services)
                    {
                        if (serv.Uuid.ToString() == "0000fff0-0000-1000-8000-00805f9b34fb")
                        {
                            var service = serv;
                            IReadOnlyList<GattCharacteristic> characteristics = null;
                            try
                            {
                                var accessStatus = await service.RequestAccessAsync();
                                if (accessStatus == DeviceAccessStatus.Allowed)
                                {
                                    var resultIn = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                                    if (result.Status == GattCommunicationStatus.Success)
                                    {
                                        characteristics = resultIn.Characteristics;
                                    }
                                    else
                                    {
                                        characteristics = new List<GattCharacteristic>();
                                    }
                                }
                                else
                                {
                                    characteristics = new List<GattCharacteristic>();
                                }
                            }
                            catch (Exception)
                            {
                                characteristics = new List<GattCharacteristic>();
                            }

                            foreach (GattCharacteristic c in characteristics)
                            {
                                if (c.Uuid.ToString() == "0000fff3-0000-1000-8000-00805f9b34fb")
                                {
                                    selectedCharacteristic = c;
                                }
                            }
                        }
                    }
                }
                else
                {
                    DeviceInfoTextBlock.Text = "Device unreachable";
                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                    ModeSwitch.IsEnabled = PickerList.IsEnabled = PowerSwitch.IsEnabled = false;
                    DeviceList.IsEnabled = ConnectButton.IsEnabled = true;
                    return;
                }
            }
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            if (selectedCharacteristic != null)
            {
                DeviceInfoTextBlock.Text = "Connected";
                ModeSwitch.IsEnabled = PickerList.IsEnabled = PowerSwitch.IsEnabled = true;
                DeviceList.IsEnabled = ConnectButton.IsEnabled = false;
            }
            else
            {
                DeviceInfoTextBlock.Text = "Unsupported device";
                ModeSwitch.IsEnabled = PickerList.IsEnabled = PowerSwitch.IsEnabled = false;
                DeviceList.IsEnabled = ConnectButton.IsEnabled = true;
            }
        }
        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StopBleDeviceWatcher();
            if (DeviceList.SelectedItem is BluetoothLEDeviceDisplay bleDeviceDisplay)
            {
                SelectedBleDeviceId = bleDeviceDisplay.Id;
                SelectedBleDeviceName = bleDeviceDisplay.Name;
                ConnectButton.IsEnabled = true;
            }
        }

        private async Task<bool> WriteBufferToSelectedCharacteristicAsync(IBuffer buffer)
        {
            try
            {
                var result = await selectedCharacteristic.WriteValueWithResultAsync(buffer);
                if (result.Status == GattCommunicationStatus.Success)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static IBuffer ToIBufferFromHexString(string data)
        {
            int NumberChars = data.Length;
            byte[] bytes = new byte[NumberChars / 2];

            for (int i = 0; i < NumberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(data.Substring(i, 2), 16);
            }
            DataWriter writer = new DataWriter();
            writer.WriteBytes(bytes);
            return writer.DetachBuffer();
        }
        
        private async void SendInfo()
        {
            await WriteBufferToSelectedCharacteristicAsync(ToIBufferFromHexString(Command));
        }

        private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            Command = $"7e000503{string.Format("{0:X2}{1:X2}{2:X2}", args.NewColor.R, args.NewColor.G, args.NewColor.B)}00ef";
            if (!dispatcherTimer.IsEnabled)
            {
                dispatcherTimer.Start();
            }
        }

        private void ModeSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (ModeSwitch.IsOn)
            {
                PickerList.Visibility = Visibility.Collapsed;
                Effect.Visibility = Visibility.Visible;
            }
            else
            {
                PickerList.Visibility = Visibility.Visible;
                Effect.Visibility = Visibility.Collapsed;
            }
        }

        private void PowerSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (PowerSwitch.IsOn)
            {
                Command = "7e00040100000000ef";
                PickerList.IsEnabled = ModeSwitch.IsEnabled = true;
            }
            else
            {
                Command = "7e00040000000000ef";
            }
            SendInfo();
        }

        private void SliderBrihtness_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            Command = $"7e0001{string.Format("{0:X2}", Convert.ToInt32(e.NewValue))}00000000ef";
            if (!dispatcherTimer.IsEnabled)
                dispatcherTimer.Start();
        }

        private void SliderSpeed_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            Command = $"7e0002{string.Format("{0:X2}", Convert.ToInt32(e.NewValue))}00000000ef";
            if (!dispatcherTimer.IsEnabled)
                dispatcherTimer.Start();
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string modename = e.AddedItems[0].ToString();
            switch (modename)
            {
                case "Static red":
                    Command = "7e00038003000000ef";
                    break;
                case "Static green":
                    Command = "7e00038203000000ef";
                    break;
                case "Static blue":
                    Command = "7e00038103000000ef";
                    break;
                case "Static yellow":
                    Command = "7e00038403000000ef";
                    break;
                case "Static cyan":
                    Command = "7e00038303000000ef";
                    break;
                case "Static magenta":
                    Command = "7e00038503000000ef";
                    break;
                case "Static white":
                    Command = "7e00038603000000ef";
                    break;
                case "Jump RGB":
                    Command = "7e00038703000000ef";
                    break;
                case "Jump RGBYCMW":
                    Command = "7e00038803000000ef";
                    break;
                case "Gradient RGB":
                    Command = "7e00038903000000ef";
                    break;
                case "Gradient RGBYCMW":
                    Command = "7e00038a03000000ef";
                    break;
                case "Gradient red":
                    Command = "7e00038b03000000ef";
                    break;
                case "Gradient green":
                    Command = "7e00038c03000000ef";
                    break;
                case "Gradient blue":
                    Command = "7e00038d03000000ef";
                    break;
                case "Gradient yellow":
                    Command = "7e00038e03000000ef";
                    break;
                case "Gradient cyan":
                    Command = "7e00038f03000000ef";
                    break;
                case "Gradient mangeta":
                    Command = "7e00039003000000ef";
                    break;
                case "Gradient white":
                    Command = "7e00039103000000ef";
                    break;
                case "Gradient red-green":
                    Command = "7e00039203000000ef";
                    break;
                case "Gradient red-blue":
                    Command = "7e00039303000000ef";
                    break;
                case "Gradient green-blue":
                    Command = "7e00039403000000ef";
                    break;
                case "Blink RGBYCMW":
                    Command = "7e00039503000000ef";
                    break;
                case "Blink red":
                    Command = "7e00039603000000ef";
                    break;
                case "Blink green":
                    Command = "7e00039703000000ef";
                    break;
                case "Blink blue":
                    Command = "7e00039803000000ef";
                    break;
                case "Blink yellow":
                    Command = "7e00039903000000ef";
                    break;
                case "Blink cyan":
                    Command = "7e00039a03000000ef";
                    break;
                case "Blink magenta":
                    Command = "7e00039b03000000ef";
                    break;
                case "Blink white":
                    Command = "7e00039c03000000ef";
                    break;
            }
            SendInfo();
        }
    }
}