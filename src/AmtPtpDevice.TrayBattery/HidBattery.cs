using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AmtPtpDevice.TrayBattery
{
    /// <summary>
    /// Reads the Magic Trackpad's battery status (REPORTID 0x90) directly through the
    /// classic Win32 HID API, so this tray app needs no packaging/capabilities unlike
    /// the WinRT HID APIs used by the UWP Settings app.
    /// </summary>
    internal static class HidBattery
    {
        private const int VID_APPLE_USB = 0x05AC;
        private const int VID_APPLE_BLUETOOTH = 0x004C;
        private const int PID_MAGIC_TRACKPAD = 0x0265;
        private const byte REPORTID_BATTERY = 0x90;

        // Battery report layout mirrors AmtPtpDevice.Settings.DataObjects.Mt2BatteryStatusReport:
        // byte 0 = report id, byte 1 = battery flags, byte 2 = charge percentage.
        private const int BATTERY_REPORT_LENGTH = 3;

        public static bool TryReadChargePercent(out int percent)
        {
            percent = -1;

            foreach (var devicePath in EnumerateHidDevicePaths())
            {
                using var handle = CreateFile(
                    devicePath,
                    FileAccess.GENERIC_READ | FileAccess.GENERIC_WRITE,
                    FileShare.FILE_SHARE_READ | FileShare.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    CreationDisposition.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid) continue;

                if (!HidD_GetAttributes(handle, out var attributes)) continue;
                if (attributes.VendorID != VID_APPLE_USB && attributes.VendorID != VID_APPLE_BLUETOOTH) continue;
                if (attributes.ProductID != PID_MAGIC_TRACKPAD) continue;

                var buffer = new byte[BATTERY_REPORT_LENGTH];
                buffer[0] = REPORTID_BATTERY;

                if (HidD_GetInputReport(handle, buffer, buffer.Length))
                {
                    percent = buffer[2];
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumerateHidDevicePaths()
        {
            var hidGuid = Guid.Empty;
            HidD_GetHidGuid(ref hidGuid);

            var deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1)) yield break;

            try
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = Marshal.SizeOf(interfaceData);

                for (uint index = 0; SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData); index++)
                {
                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                    if (requiredSize == 0) continue;

                    var detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        // cbSize depends on pointer width; the struct is packed to match.
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);

                        if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                        {
                            var pathPtr = detailDataBuffer + 4;
                            var path = Marshal.PtrToStringAuto(pathPtr);
                            if (!string.IsNullOrEmpty(path)) yield return path;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        #region P/Invoke

        private const int DIGCF_PRESENT = 0x02;
        private const int DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [Flags]
        private enum FileAccess : uint
        {
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000
        }

        [Flags]
        private enum FileShare : uint
        {
            FILE_SHARE_READ = 0x1,
            FILE_SHARE_WRITE = 0x2
        }

        private enum CreationDisposition : uint
        {
            OPEN_EXISTING = 3
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(ref Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, out HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetInputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string fileName, FileAccess desiredAccess, FileShare shareMode, IntPtr securityAttributes, CreationDisposition creationDisposition, int flagsAndAttributes, IntPtr templateFile);

        #endregion
    }
}
