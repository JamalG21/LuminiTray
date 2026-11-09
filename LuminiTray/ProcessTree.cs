using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LuminiTray;

/// <summary>
/// Coleta de alto desempenho da árvore de processos descendentes via Toolhelp32Snapshot.
/// Permite somar com precisão o consumo de memória (WorkingSet) do Lumini Tray
/// e todos os seus subprocessos (mpv, supressor, controladores, etc.).
/// </summary>
public static class ProcessTree
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static long _lastCalculatedBytes = 0;
    private static long _lastTimestampTicks = 0;
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Retorna a soma do WorkingSet (RAM física em uso) do processo raiz e de todos os seus descendentes.
    /// Resultado em cache de 750 ms para garantir consumo de CPU virtualmente nulo na overlay.
    /// </summary>
    public static long GetTreeWorkingSet(int rootPid)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_cacheLock)
        {
            // Cache de 750ms (~ Stopwatch.Frequency * 0.75)
            if (_lastCalculatedBytes > 0 && (now - _lastTimestampTicks) < (Stopwatch.Frequency * 3 / 4))
            {
                return _lastCalculatedBytes;
            }
        }

        long total = 0;
        try
        {
            var pids = GetDescendantPids(rootPid);
            pids.Add(rootPid);

            foreach (int pid in pids)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        total += proc.WorkingSet64;
                    }
                }
                catch
                {
                    // Processo encerrou ou acesso negado transitório
                }
            }
        }
        catch
        {
            total = Environment.WorkingSet;
        }

        lock (_cacheLock)
        {
            _lastCalculatedBytes = total;
            _lastTimestampTicks = now;
        }

        return total;
    }

    /// <summary>
    /// Enumera recursivamente todos os PIDs descendentes de um processo pai usando Toolhelp32Snapshot.
    /// Executa em menos de 1 milissegundo no Windows moderno.
    /// </summary>
    public static HashSet<int> GetDescendantPids(int rootPid)
    {
        var descendants = new HashSet<int>();
        var parentToChildren = new Dictionary<int, List<int>>();

        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return descendants;
        }

        try
        {
            var pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref pe))
            {
                do
                {
                    int parent = (int)pe.th32ParentProcessID;
                    int child = (int)pe.th32ProcessID;

                    if (!parentToChildren.TryGetValue(parent, out var list))
                    {
                        list = new List<int>();
                        parentToChildren[parent] = list;
                    }
                    list.Add(child);
                }
                while (Process32Next(snapshot, ref pe));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var queue = new Queue<int>();
        queue.Enqueue(rootPid);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (parentToChildren.TryGetValue(current, out var children))
            {
                foreach (int child in children)
                {
                    if (descendants.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return descendants;
    }
}
