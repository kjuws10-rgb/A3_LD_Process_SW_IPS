using System.Runtime.InteropServices;

namespace Drilling.Common.Interface;

public class CMelsecNetApi
{
    public const short OpenMode = -1;
    public const string NativeLibraryName = "MDFUNC32.dll";

    public virtual int Open(short channelNo, out int path)
    {
        return CMelsecNetNativeMethods.mdOpen(channelNo, OpenMode, out path);
    }

    public virtual int Close(int path)
    {
        return CMelsecNetNativeMethods.mdClose(path);
    }

    public virtual int SendEx(
        int path,
        int networkNo,
        int stationNo,
        int deviceType,
        int deviceNo,
        ref int size,
        short[] data)
    {
        return CMelsecNetNativeMethods.mdSendEx(
            path,
            networkNo,
            stationNo,
            deviceType,
            deviceNo,
            ref size,
            data);
    }

    public virtual int ReceiveEx(
        int path,
        int networkNo,
        int stationNo,
        int deviceType,
        int deviceNo,
        ref int size,
        short[] data)
    {
        return CMelsecNetNativeMethods.mdReceiveEx(
            path,
            networkNo,
            stationNo,
            deviceType,
            deviceNo,
            ref size,
            data);
    }
}

internal static class CMelsecNetNativeMethods
{
    [DllImport(
        CMelsecNetApi.NativeLibraryName,
        EntryPoint = "mdOpen",
        CallingConvention = CallingConvention.StdCall)]
    internal static extern short mdOpen(
        short channelNo,
        short mode,
        out int path);

    [DllImport(
        CMelsecNetApi.NativeLibraryName,
        EntryPoint = "mdClose",
        CallingConvention = CallingConvention.StdCall)]
    internal static extern short mdClose(int path);

    [DllImport(
        CMelsecNetApi.NativeLibraryName,
        EntryPoint = "mdSendEx",
        CallingConvention = CallingConvention.StdCall)]
    internal static extern int mdSendEx(
        int path,
        int networkNo,
        int stationNo,
        int deviceType,
        int deviceNo,
        ref int size,
        [In] short[] data);

    [DllImport(
        CMelsecNetApi.NativeLibraryName,
        EntryPoint = "mdReceiveEx",
        CallingConvention = CallingConvention.StdCall)]
    internal static extern int mdReceiveEx(
        int path,
        int networkNo,
        int stationNo,
        int deviceType,
        int deviceNo,
        ref int size,
        [Out] short[] data);
}
