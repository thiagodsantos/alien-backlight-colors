using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AlienCommandCenter.Services;

internal sealed class WindowsHidTransport : IDisposable
{
    private const ushort VendorId = 0x187C;
    private const ushort ProductId = 0x0550;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagSequentialScan = 0x08000000;
    private const int ErrorNoMoreItems = 259;
    private const int HidpStatusSuccess = 0x00110000;

    private readonly SafeFileHandle _handle;
    private readonly int _outputReportLength;
    private readonly int _inputReportLength;

    private WindowsHidTransport(string devicePath)
    {
        DevicePath = devicePath;
        _handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagSequentialScan,
            IntPtr.Zero);

        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error, "Could not open the Alienware keyboard HID interface.");
        }

        try
        {
            var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            if (!HidD_GetAttributes(_handle, ref attributes))
            {
                throw LastWin32Error("Could not read the keyboard HID attributes.");
            }

            if (attributes.VendorId != VendorId || attributes.ProductId != ProductId)
            {
                throw new InvalidOperationException("The selected HID interface is not an AW-ELC keyboard controller.");
            }

            if (!HidD_GetPreparsedData(_handle, out var preparsedData))
            {
                throw LastWin32Error("Could not read the keyboard HID report descriptor.");
            }

            try
            {
                var capabilities = new HidCapabilities { Reserved = new ushort[17] };
                var status = HidP_GetCaps(preparsedData, ref capabilities);
                if (status != HidpStatusSuccess)
                {
                    throw new InvalidOperationException($"The keyboard HID report descriptor returned status 0x{status:X8}.");
                }

                _outputReportLength = capabilities.OutputReportByteLength;
                _inputReportLength = capabilities.InputReportByteLength;
                if (_outputReportLength < 9 || _inputReportLength < 7)
                {
                    throw new InvalidOperationException(
                        $"The keyboard HID reports are too short for APIv4 (output {_outputReportLength}, input {_inputReportLength} bytes).");
                }
            }
            finally
            {
                HidD_FreePreparsedData(preparsedData);
            }
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    public string DevicePath { get; }

    public static IReadOnlyList<string> FindAwElcDevicePaths()
    {
        HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == new IntPtr(-1))
        {
            throw LastWin32Error("Could not enumerate Windows HID devices.");
        }

        try
        {
            var paths = new List<string>();
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData
                {
                    Size = Marshal.SizeOf<DeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "Could not enumerate Windows HID interfaces.");
                }

                _ = SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    IntPtr.Zero);
                if (requiredSize <= 0)
                {
                    continue;
                }

                var detailData = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(
                        deviceInfoSet,
                        ref interfaceData,
                        detailData,
                        requiredSize,
                        out _,
                        IntPtr.Zero))
                    {
                        continue;
                    }

                    var path = Marshal.PtrToStringUni(IntPtr.Add(detailData, 4));
                    if (path is not null && path.Contains("VID_187C&PID_0550", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailData);
                }
            }

            return paths;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public static WindowsHidTransport Open(string devicePath) => new(devicePath);

    public void SendOutputOnly(params byte[] command)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, this);
        if (command.Length > _outputReportLength - 2)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "The command exceeds the AlienFX APIv4 report size.");
        }

        var report = new byte[_outputReportLength];
        report[0] = 0x00;
        report[1] = 0x03;
        command.CopyTo(report, 2);

        if (!HidD_SetOutputReport(_handle, report, (uint)report.Length))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error,
                $"The AW-ELC controller rejected APIv4 command {Convert.ToHexString(command)} (Win32 error {error}, output report length {_outputReportLength}).");
        }
    }

    public byte[] SendAndRead(params byte[] command)
    {
        SendOutputOnly(command);
        var report = new byte[_inputReportLength];
        report[0] = 0x00;
        if (!HidD_GetInputReport(_handle, report, (uint)report.Length))
        {
            throw LastWin32Error("The AW-ELC controller did not return an input report.");
        }

        return report;
    }

    public void Dispose() => _handle.Dispose();

    private static Win32Exception LastWin32Error(string message) => new(Marshal.GetLastWin32Error(), message);

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HidAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", EntryPoint = "HidP_GetCaps")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidCapabilities capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetInputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInterfaces", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref DeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCapabilities
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }
}
