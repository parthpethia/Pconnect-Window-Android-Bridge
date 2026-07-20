using System.Diagnostics;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Pconnect.Agent.Services;

internal struct SystemMetricsSnapshot
{
    public int CpuPercent { get; set; }
    public int RamPercent { get; set; }
    public ulong TotalRamMb { get; set; }
    public ulong UsedRamMb { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ProcessCount { get; set; }
}

internal sealed class SystemMetricsService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ComTypes.FILETIME lpIdleTime, out ComTypes.FILETIME lpKernelTime, out ComTypes.FILETIME lpUserTime);

    private ulong _prevIdleTicks;
    private ulong _prevTotalTicks;
    private int _lastCpuPercent;
    private DateTime _lastCpuCheckTime = DateTime.MinValue;

    public SystemMetricsSnapshot GetSnapshot()
    {
        var snapshot = new SystemMetricsSnapshot();

        // 1. Calculate RAM
        try
        {
            var memEx = new MEMORYSTATUSEX();
            memEx.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            if (GlobalMemoryStatusEx(ref memEx))
            {
                snapshot.RamPercent = (int)memEx.dwMemoryLoad;
                snapshot.TotalRamMb = memEx.ullTotalPhys / (1024 * 1024);
                snapshot.UsedRamMb = (memEx.ullTotalPhys - memEx.ullAvailPhys) / (1024 * 1024);
            }
        }
        catch
        {
            snapshot.RamPercent = 0;
        }

        // 2. Calculate CPU % (throttle query to at least 500ms intervals)
        var now = DateTime.UtcNow;
        if ((now - _lastCpuCheckTime).TotalMilliseconds >= 500)
        {
            _lastCpuCheckTime = now;
            if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                ulong idleTicks = FileTimeToTicks(idleTime);
                ulong totalTicks = FileTimeToTicks(kernelTime) + FileTimeToTicks(userTime);

                if (_prevTotalTicks > 0)
                {
                    ulong totalDelta = totalTicks - _prevTotalTicks;
                    ulong idleDelta = idleTicks - _prevIdleTicks;

                    if (totalDelta > 0)
                    {
                        double cpu = (1.0 - ((double)idleDelta / totalDelta)) * 100.0;
                        _lastCpuPercent = Math.Clamp((int)Math.Round(cpu), 0, 100);
                    }
                }

                _prevIdleTicks = idleTicks;
                _prevTotalTicks = totalTicks;
            }
        }
        snapshot.CpuPercent = _lastCpuPercent;

        // 3. System Uptime & Process Count
        try
        {
            snapshot.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            snapshot.ProcessCount = Process.GetProcesses().Length;
        }
        catch
        {
            snapshot.ProcessCount = 0;
        }

        return snapshot;
    }

    private static ulong FileTimeToTicks(ComTypes.FILETIME fileTime)
    {
        return ((ulong)fileTime.dwHighDateTime << 32) + (uint)fileTime.dwLowDateTime;
    }
}
