using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LuminiTray;

/// <summary>
/// Gerencia o Windows Job Object do Lumini Tray.
/// Agrupa todos os processos filhos (Wallpaper MPV, Supressor de Áudio, Monitor de Métricas, Fans)
/// sob o mesmo grupo no Gerenciador de Tarefas do Windows e assegura encerramento automático
/// em cascata caso o processo principal seja finalizado.
/// </summary>
public static class JobObjectManager
{
    private static IntPtr _jobHandle = IntPtr.Zero;
    private static readonly object _lock = new();

    #region Win32 P/Invoke
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int JobObjectInformationClass,
        IntPtr lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }
    #endregion

    static JobObjectManager()
    {
        InitializeJob();
    }

    private static void InitializeJob()
    {
        lock (_lock)
        {
            if (_jobHandle != IntPtr.Zero) return;

            try
            {
                string jobName = $"LuminiTray_Job_{Environment.ProcessId}";
                _jobHandle = CreateJobObject(IntPtr.Zero, jobName);
                if (_jobHandle == IntPtr.Zero) return;

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    }
                };

                int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                IntPtr infoPtr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, infoPtr, false);
                    SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, infoPtr, (uint)length);
                }
                finally
                {
                    Marshal.FreeHGlobal(infoPtr);
                }

                // Associa o próprio processo principal ao Job
                using var cur = Process.GetCurrentProcess();
                AssignProcessToJobObject(_jobHandle, cur.Handle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JobObjectManager] Falha ao configurar Job Object: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Vincula um processo filho ao Job Object do Lumini Tray para agrupamento e gerenciamento conjunto.
    /// </summary>
    public static bool AssignProcess(Process process)
    {
        if (process == null || process.HasExited) return false;

        lock (_lock)
        {
            if (_jobHandle == IntPtr.Zero)
            {
                InitializeJob();
                if (_jobHandle == IntPtr.Zero) return false;
            }

            try
            {
                return AssignProcessToJobObject(_jobHandle, process.Handle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JobObjectManager] Falha ao vincular PID {process.Id} ao Job: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Encerra e libera o handle do Job.
    /// </summary>
    public static void Dispose()
    {
        lock (_lock)
        {
            if (_jobHandle != IntPtr.Zero)
            {
                try { CloseHandle(_jobHandle); } catch { }
                _jobHandle = IntPtr.Zero;
            }
        }
    }
}
