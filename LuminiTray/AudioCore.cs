using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LuminiTray;

public enum AudioFeedbackType
{
    DeviceSwitched,
    MicMuted,
    MicUnmuted,
    PlaybackMuted,
    PlaybackUnmuted,
    PttOn,
    PttOff
}

public sealed class AudioDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AudioInterop.EDataFlow DataFlow { get; set; }
    public AudioInterop.EDeviceState State { get; set; }
    public bool IsDefaultConsole { get; set; }
    public bool IsDefaultCommunications { get; set; }
    public float VolumeScalar { get; set; }
    public int VolumePercent => Math.Clamp((int)Math.Round(VolumeScalar * 100f), 0, 100);
    public bool IsMuted { get; set; }
    public bool IsNotMuted => !IsMuted;
    public string MuteStatusText => IsMuted ? "MUTADO" : "ATIVO";
    public string MuteButtonText => IsMuted ? "DESMUTAR" : "MUTAR";
    public bool IsSelectedInCycle { get; set; }
    public int OutputNumber { get; set; } = 0;
    public string OutputBadge => OutputNumber > 0 ? $"#{OutputNumber}" : "";

    public bool IsDisabled { get; set; } = false;
    public bool IsEnabled => !IsDisabled;
    public string DeviceStatusText => IsDisabled ? "DESATIVADO" : "ATIVO";
    public string ToggleEnableButtonText => IsDisabled ? "ATIVAR DISPOSITIVO" : "DESATIVAR";
    public double CardOpacity => IsDisabled ? 0.6 : 1.0;

    public string DisplayRole
    {
        get
        {
            if (IsDefaultConsole && IsDefaultCommunications) return "Padrão & Comunicação";
            if (IsDefaultConsole) return "Padrão do Sistema";
            if (IsDefaultCommunications) return "Comunicação Padrão";
            return "";
        }
    }
}

public sealed class AudioSessionInfo
{
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public float Volume { get; set; }
    public int VolumePercent => Math.Clamp((int)Math.Round(Volume * 100f), 0, 100);
    public bool IsMuted { get; set; }
    public float PeakValue { get; set; }
    public int State { get; set; }
}

public static class AudioCore
{
    private const int CLSCTX_ALL = 23;
    private static readonly Guid IID_IAudioEndpointVolume = new(AudioInterop.IID_IAudioEndpointVolume);
    private static readonly Guid IID_IAudioSessionManager2 = new(AudioInterop.IID_IAudioSessionManager2);

    /// <summary>
    /// Estado de mudo desejado para o microfone (equivalente a this.state + force_current_state=1 em MicrophoneController.ahk).
    /// </summary>
    public static bool? DesiredMicMuteState { get; set; }

    /// <summary>
    /// Encerra processos externos conflitantes (como AHK_MicMute standalone) que possuem callback
    /// force_current_state=1 em "All Microphones" e revertem o SetMute(true) em poucos milissegundos.
    /// </summary>
    public static void StopConflictingExternalAudioTools()
    {
        try
        {
            int currentPid = Environment.ProcessId;
            foreach (var procName in new[] { "MicMute", "AHK_MicMute" })
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(procName))
                {
                    try
                    {
                        if (p.Id != currentPid)
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }


        }
        catch { }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (string Name, long Timestamp)> _processNameCache = new();
    /// <summary>
    /// Obtém o nome do processo de forma segura e em cache por ~10 segundos,
    /// eliminando alocações repetidas de instâncias Process na manipulação de volume rápido.
    /// </summary>
    public static string GetSafeProcessName(uint pid)
    {
        if (pid == 0) return "";
        long now = Environment.TickCount64;
        if (_processNameCache.TryGetValue(pid, out var cached) && (now - cached.Timestamp) < 10000)
        {
            return cached.Name;
        }

        string name = "";
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch { }

        _processNameCache[pid] = (name, now);
        return name;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

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

    /// <summary>
    /// Retorna o conjunto com o PID alvo e todos os seus processos filhos e descendentes (árvore de processos).
    /// Baseado no mod do Windhawk (taskbar-volume-control-per-app): navegadores Chromium (Chrome, Edge, Brave),
    /// Discord, Spotify e jogos executam o subsistema de áudio em subprocessos utilitários ou renderers.
    /// </summary>
    public static HashSet<uint> GetProcessTree(uint rootPid)
    {
        var pids = new HashSet<uint> { rootPid };
        if (rootPid == 0) return pids;

        var procList = new List<(uint pid, uint parentPid)>(128);
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap != IntPtr.Zero && snap != (IntPtr)(-1))
        {
            try
            {
                var pe = new PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (Process32First(snap, ref pe))
                {
                    do
                    {
                        procList.Add((pe.th32ProcessID, pe.th32ParentProcessID));
                    } while (Process32Next(snap, ref pe));
                }
            }
            catch { }
            finally
            {
                CloseHandle(snap);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < procList.Count; i++)
            {
                var p = procList[i];
                if (pids.Contains(p.parentPid) && !pids.Contains(p.pid))
                {
                    pids.Add(p.pid);
                    changed = true;
                }
            }
        }

        return pids;
    }

    /// <summary>
    /// Enumera todos os dispositivos de áudio ativos para o fluxo informado (Render ou Capture).
    /// Executado no AudioComThread (STA) seguindo a arquitetura do SoundSwitch.
    /// </summary>
    public static List<AudioDeviceInfo> GetDevices(
        AudioInterop.EDataFlow flow,
        IEnumerable<string>? selectedCycleIds = null,
        IEnumerable<string>? disabledDeviceIds = null)
    {
        return AudioComThread.Invoke(() => GetDevicesCore(flow, selectedCycleIds, disabledDeviceIds));
    }

    private static List<AudioDeviceInfo> GetDevicesCore(
        AudioInterop.EDataFlow flow,
        IEnumerable<string>? selectedCycleIds,
        IEnumerable<string>? disabledDeviceIds)
    {
        var list = new List<AudioDeviceInfo>();
        var selectedSet = selectedCycleIds != null ? new HashSet<string>(selectedCycleIds, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabledSet = disabledDeviceIds != null ? new HashSet<string>(disabledDeviceIds, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AudioInterop.IMMDeviceEnumerator? enumerator = null;
        AudioInterop.IMMDeviceCollection? collection = null;

        try
        {
            enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();

            string? defaultConsoleId = null;
            string? defaultCommsId = null;

            if (enumerator.GetDefaultAudioEndpoint(flow, AudioInterop.ERole.Console, out var defConsole) == 0 && defConsole != null)
            {
                if (defConsole.GetId(out var dcId) == 0) defaultConsoleId = dcId;
                Marshal.ReleaseComObject(defConsole);
            }

            if (enumerator.GetDefaultAudioEndpoint(flow, AudioInterop.ERole.Communications, out var defComms) == 0 && defComms != null)
            {
                if (defComms.GetId(out var dmId) == 0) defaultCommsId = dmId;
                Marshal.ReleaseComObject(defComms);
            }

            var stateMask = AudioInterop.EDeviceState.Active | AudioInterop.EDeviceState.Disabled;
            if (enumerator.EnumAudioEndpoints(flow, stateMask, out collection) == 0 && collection != null)
            {
                if (collection.GetCount(out uint count) == 0)
                {
                    for (uint i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out var device) != 0 || device == null) continue;

                        try
                        {
                            if (device.GetId(out var devId) != 0) continue;
                            device.GetState(out var devState);

                            bool isDisabled = (devState == AudioInterop.EDeviceState.Disabled) || disabledSet.Contains(devId);

                            string friendlyName = "Dispositivo de Áudio";
                            if (device.OpenPropertyStore(0 /* STGM_READ */, out var propStore) == 0 && propStore != null)
                            {
                                try
                                {
                                    var key = AudioInterop.PKEY_Device_FriendlyName;
                                    if (propStore.GetValue(ref key, out var pv) == 0)
                                    {
                                        if (pv.vt == 31 /* VT_LPWSTR */ && pv.data1 != IntPtr.Zero)
                                        {
                                            friendlyName = Marshal.PtrToStringUni(pv.data1) ?? friendlyName;
                                        }
                                        AudioInterop.PropVariant.PropVariantClear(ref pv);
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(propStore);
                                }
                            }

                            float vol = 1.0f;
                            bool muted = false;
                            Guid iid = IID_IAudioEndpointVolume;
                            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                            {
                                try
                                {
                                    var endpointVolume = (AudioInterop.IAudioEndpointVolume)volObj;
                                    endpointVolume.GetMasterVolumeLevelScalar(out vol);
                                    endpointVolume.GetMute(out muted);
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(volObj);
                                }
                            }

                            bool isDefConsole = string.Equals(devId, defaultConsoleId, StringComparison.OrdinalIgnoreCase);
                            bool isDefComms = string.Equals(devId, defaultCommsId, StringComparison.OrdinalIgnoreCase);

                            list.Add(new AudioDeviceInfo
                            {
                                Id = devId,
                                Name = friendlyName,
                                DataFlow = flow,
                                State = devState,
                                IsDefaultConsole = !isDisabled && isDefConsole,
                                IsDefaultCommunications = !isDisabled && isDefComms,
                                VolumeScalar = vol,
                                IsMuted = muted,
                                IsDisabled = isDisabled,
                                IsSelectedInCycle = !isDisabled && selectedSet.Contains(devId)
                            });
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(device);
                        }
                    }
                }
            }

            if (flow == AudioInterop.EDataFlow.Render)
            {
                int num = 1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].IsEnabled)
                    {
                        list[i].OutputNumber = num++;
                    }
                    else
                    {
                        list[i].OutputNumber = 0;
                    }
                }
            }
        }
        catch
        {
            // Tratamento gracioso para ambientes sem dispositivos ou CoreAudio inacessível
        }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }

        return list;
    }

    /// <summary>
    /// Altera o endpoint de áudio padrão para o dispositivo especificado.
    /// Suporta preservar o nível de volume e alternar comunicações.
    /// </summary>
    public static bool SetDefaultDevice(string deviceId, bool changeCommunications = true, bool keepVolume = false)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        float? previousVolume = null;
        if (keepVolume)
        {
            previousVolume = GetDefaultVolumeScalar(AudioInterop.EDataFlow.Render);
        }

        return AudioComThread.Invoke(() =>
        {
            object? policyClientObj = null;
            try
            {
                policyClientObj = new AudioInterop.PolicyConfigClientComObject();
                if (policyClientObj is AudioInterop.IPolicyConfigX configX)
                {
                    configX.SetDefaultEndpoint(deviceId, AudioInterop.ERole.Console);
                    configX.SetDefaultEndpoint(deviceId, AudioInterop.ERole.Multimedia);
                    if (changeCommunications)
                    {
                        configX.SetDefaultEndpoint(deviceId, AudioInterop.ERole.Communications);
                    }
                }
                else if (policyClientObj is AudioInterop.IPolicyConfig config)
                {
                    config.SetDefaultEndpoint(deviceId, AudioInterop.ERole.Console);
                    config.SetDefaultEndpoint(deviceId, AudioInterop.ERole.Multimedia);
                    if (changeCommunications)
                    {
                        config.SetDefaultEndpoint(deviceId, AudioInterop.ERole.Communications);
                    }
                }
                else
                {
                    return false;
                }

                if (keepVolume && previousVolume.HasValue)
                {
                    SetDeviceVolumeScalarCore(deviceId, previousVolume.Value);
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (policyClientObj != null && Marshal.IsComObject(policyClientObj))
                {
                    Marshal.ReleaseComObject(policyClientObj);
                }
            }
        });
    }

    /// <summary>
    /// Ativa ou desativa a visibilidade/estado do endpoint de áudio no Windows usando PolicyConfig.
    /// </summary>
    public static bool SetDeviceVisibility(string deviceId, bool visible)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        return AudioComThread.Invoke(() =>
        {
            object? policyClientObj = null;
            try
            {
                policyClientObj = new AudioInterop.PolicyConfigClientComObject();
                if (policyClientObj is AudioInterop.IPolicyConfigX configX)
                {
                    int hr = configX.SetEndpointVisibility(deviceId, visible);
                    return hr == 0;
                }
                else if (policyClientObj is AudioInterop.IPolicyConfig config)
                {
                    int hr = config.SetEndpointVisibility(deviceId, visible);
                    return hr == 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (policyClientObj != null && Marshal.IsComObject(policyClientObj))
                {
                    Marshal.ReleaseComObject(policyClientObj);
                }
            }
        });
    }

    /// <summary>
    /// Alterna ciclicamente para o próximo dispositivo habilitado na lista de IDs.
    /// Dispositivos desativados são estritamente excluídos da rotação.
    /// </summary>
    public static AudioDeviceInfo? CycleNextDevice(
        AudioInterop.EDataFlow flow,
        List<string>? cycleIds,
        List<string>? disabledIds,
        bool changeCommunications,
        bool keepVolume)
    {
        var allDevices = GetDevices(flow, cycleIds, disabledIds).Where(d => d.IsEnabled).ToList();
        if (allDevices.Count == 0) return null;

        // Se a lista de ciclo estiver vazia ou nenhum estiver presente, considera todos os habilitados
        var candidateDevices = allDevices.Where(d => cycleIds != null && cycleIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (candidateDevices.Count == 0)
        {
            candidateDevices = allDevices;
        }

        var currentDefault = candidateDevices.FirstOrDefault(d => d.IsDefaultConsole) ?? candidateDevices.First();
        int currentIndex = candidateDevices.FindIndex(d => string.Equals(d.Id, currentDefault.Id, StringComparison.OrdinalIgnoreCase));
        int nextIndex = (currentIndex + 1) % candidateDevices.Count;
        var nextDevice = candidateDevices[nextIndex];

        if (SetDefaultDevice(nextDevice.Id, changeCommunications, keepVolume))
        {
            return nextDevice;
        }

        return null;
    }

    public static AudioDeviceInfo? CycleNextDevice(AudioInterop.EDataFlow flow, List<string> cycleIds, bool changeCommunications, bool keepVolume)
    {
        return CycleNextDevice(flow, cycleIds, null, changeCommunications, keepVolume);
    }

    /// <summary>
    /// Obtém o volume escalar (0.0 a 1.0) do dispositivo padrão especificado (Render ou Capture).
    /// </summary>
    public static float GetDefaultVolumeScalar(AudioInterop.EDataFlow flow)
    {
        return AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? defaultDev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(flow, AudioInterop.ERole.Console, out defaultDev) == 0 && defaultDev != null)
                {
                    Guid iid = IID_IAudioEndpointVolume;
                    if (defaultDev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                    {
                        try
                        {
                            var endpointVolume = (AudioInterop.IAudioEndpointVolume)volObj;
                            endpointVolume.GetMasterVolumeLevelScalar(out float level);
                            return level;
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(volObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (defaultDev != null) Marshal.ReleaseComObject(defaultDev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return 1.0f;
        });
    }

    /// <summary>
    /// Obtém o pico de volume instantâneo (0.0 a 1.0) do dispositivo informado (ou padrão se deviceId for nulo/vazio).
    /// </summary>
    public static float GetDevicePeak(string? deviceId, AudioInterop.EDataFlow flow)
    {
        return AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (!string.IsNullOrEmpty(deviceId))
                {
                    if (enumerator.GetDevice(deviceId, out dev) != 0 || dev == null)
                        dev = null;
                }

                if (dev == null)
                {
                    enumerator.GetDefaultAudioEndpoint(flow, AudioInterop.ERole.Console, out dev);
                }

                if (dev != null)
                {
                    Guid iid = new Guid(AudioInterop.IID_IAudioMeterInformation);
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var meterObj) == 0 && meterObj is AudioInterop.IAudioMeterInformation meter)
                    {
                        try
                        {
                            if (meter.GetPeakValue(out float peak) == 0)
                                return Math.Clamp(peak, 0f, 1f);
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(meter);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return 0f;
        });
    }

    /// <summary>
    /// Obtém o volume escalar atual (0.0 a 1.0) do dispositivo especificado.
    /// </summary>
    public static float GetDeviceVolumeScalar(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return 1.0f;
        return AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDevice(deviceId, out dev) == 0 && dev != null)
                {
                    Guid iid = IID_IAudioEndpointVolume;
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                    {
                        try
                        {
                            var endpointVolume = (AudioInterop.IAudioEndpointVolume)volObj;
                            if (endpointVolume.GetMasterVolumeLevelScalar(out float level) == 0)
                            {
                                return Math.Clamp(level, 0f, 1f);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(volObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return 1.0f;
        });
    }

    /// <summary>
    /// Define o volume escalar de um dispositivo pelo seu ID.
    /// </summary>
    public static bool SetDeviceVolumeScalar(string deviceId, float scalar)
    {
        return AudioComThread.Invoke(() => SetDeviceVolumeScalarCore(deviceId, scalar));
    }

    private static bool SetDeviceVolumeScalarCore(string deviceId, float scalar)
    {
        AudioInterop.IMMDeviceEnumerator? enumerator = null;
        AudioInterop.IMMDevice? dev = null;
        try
        {
            enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
            if (enumerator.GetDevice(deviceId, out dev) == 0 && dev != null)
            {
                Guid iid = IID_IAudioEndpointVolume;
                if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                {
                    try
                    {
                        var endpointVolume = (AudioInterop.IAudioEndpointVolume)volObj;
                        float clamped = Math.Clamp(scalar, 0f, 1f);
                        Guid eventContext = Guid.Empty;
                        if (endpointVolume.GetChannelCount(out uint chCount) == 0 && chCount == 2)
                        {
                            endpointVolume.SetChannelVolumeLevelScalar(0, clamped, ref eventContext);
                            endpointVolume.SetChannelVolumeLevelScalar(1, clamped, ref eventContext);
                        }
                        endpointVolume.SetMasterVolumeLevelScalar(clamped, ref eventContext);
                        return true;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(volObj);
                    }
                }
            }


        }
        catch { }
        finally
        {
            if (dev != null) Marshal.ReleaseComObject(dev);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
        return false;
    }

    private static AudioInterop.IAudioEndpointVolume? _cachedMasterVolumeObj;
    private static long _cachedMasterVolumeTimestamp = 0;

    /// <summary>
    /// Ajusta o volume mestre da saída de reprodução padrão em um delta (ex: +0.02f ou -0.02f).
    /// Executa com cache de endpoint COM ativo no AudioComThread, reduzindo a latência para ~5 microsegundos.
    /// Retorna a nova porcentagem de 0 a 100%, ou -1 em caso de erro.
    /// </summary>
    public static int AdjustDefaultRenderVolume(float delta)
    {
        return AudioComThread.Invoke(() =>
        {
            long now = Environment.TickCount64;

            // 1. Tenta utilizar o endpoint volume em cache se tiver menos de 3 segundos
            if (_cachedMasterVolumeObj != null && (now - _cachedMasterVolumeTimestamp) < 3000)
            {
                try
                {
                    if (_cachedMasterVolumeObj.GetMasterVolumeLevelScalar(out float currentVol) == 0)
                    {
                        float newVol = Math.Clamp(currentVol + delta, 0f, 1f);
                        Guid eventContext = Guid.Empty;
                        _cachedMasterVolumeObj.SetMasterVolumeLevelScalar(newVol, ref eventContext);
                        _cachedMasterVolumeTimestamp = now;
                        return Math.Clamp((int)Math.Round(newVol * 100f), 0, 100);
                    }
                }
                catch
                {
                    try { Marshal.ReleaseComObject(_cachedMasterVolumeObj); } catch { }
                    _cachedMasterVolumeObj = null;
                }
            }

            // 2. Se o cache expirou ou falhou, reativa o endpoint
            if (_cachedMasterVolumeObj != null)
            {
                try { Marshal.ReleaseComObject(_cachedMasterVolumeObj); } catch { }
                _cachedMasterVolumeObj = null;
            }

            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Multimedia, out dev) == 0 && dev != null)
                {
                    Guid iid = IID_IAudioEndpointVolume;
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                    {
                        var endpointVolume = (AudioInterop.IAudioEndpointVolume)volObj;
                        if (endpointVolume.GetMasterVolumeLevelScalar(out float currentVol) == 0)
                        {
                            float newVol = Math.Clamp(currentVol + delta, 0f, 1f);
                            Guid eventContext = Guid.Empty;
                            endpointVolume.SetMasterVolumeLevelScalar(newVol, ref eventContext);

                            _cachedMasterVolumeObj = endpointVolume;
                            _cachedMasterVolumeTimestamp = now;
                            return Math.Clamp((int)Math.Round(newVol * 100f), 0, 100);
                        }
                        else
                        {
                            Marshal.ReleaseComObject(volObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return -1;
        });
    }

    private static AudioInterop.ISimpleAudioVolume? _cachedSessionVolumeObj;
    private static uint _cachedSessionPid = 0;
    private static string _cachedSessionName = "";
    private static long _cachedSessionTimestamp = 0;

    /// <summary>
    /// Ajusta o volume de uma aplicação específica (sessão WASAPI) por PID ou nome de processo.
    /// Retorna a nova porcentagem de 0 a 100%, ou -1 se não encontrar uma sessão ativa.
    /// </summary>
    public static int AdjustProcessVolume(uint targetPid, string fallbackName, float delta, out string resolvedName)
    {
        string appName = fallbackName;
        if (targetPid > 0)
        {
            string safeName = GetSafeProcessName(targetPid);
            if (!string.IsNullOrWhiteSpace(safeName)) appName = safeName;
        }

        string alias = TaskbarVolumeHook.MapAppNameToProcessAlias(appName);
        string finalName = !string.IsNullOrWhiteSpace(alias) ? alias : appName;
        HashSet<uint>? treePids = targetPid > 0 ? GetProcessTree(targetPid) : null;

        int result = AudioComThread.Invoke(() =>
        {
            long now = Environment.TickCount64;

            // 1. Tenta usar a sessão em cache se for o mesmo app e tiver menos de 2.5s
            if (_cachedSessionVolumeObj != null && (now - _cachedSessionTimestamp) < 2500)
            {
                bool matchCached = (targetPid > 0 && (_cachedSessionPid == targetPid || (treePids != null && treePids.Contains(_cachedSessionPid)))) ||
                                   (!string.IsNullOrEmpty(finalName) && finalName.Equals(_cachedSessionName, StringComparison.OrdinalIgnoreCase));
                if (matchCached)
                {
                    try
                    {
                        if (_cachedSessionVolumeObj.GetMasterVolume(out float curVol) == 0)
                        {
                            float newVol = Math.Clamp(curVol + delta, 0f, 1f);
                            Guid eventContext = Guid.Empty;
                            _cachedSessionVolumeObj.SetMasterVolume(newVol, ref eventContext);
                            if (newVol < 0.005f) _cachedSessionVolumeObj.SetMute(true, ref eventContext);
                            else _cachedSessionVolumeObj.SetMute(false, ref eventContext);
                            _cachedSessionTimestamp = now;
                            return Math.Clamp((int)Math.Round(newVol * 100f), 0, 100);
                        }
                    }
                    catch
                    {
                        try { Marshal.ReleaseComObject(_cachedSessionVolumeObj); } catch { }
                        _cachedSessionVolumeObj = null;
                    }
                }
            }

            if (_cachedSessionVolumeObj != null)
            {
                try { Marshal.ReleaseComObject(_cachedSessionVolumeObj); } catch { }
                _cachedSessionVolumeObj = null;
            }

            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Multimedia, out dev) == 0 && dev != null)
                {
                    Guid iid = IID_IAudioSessionManager2;
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0 && mgrObj != null)
                    {
                        try
                        {
                            var mgr = (AudioInterop.IAudioSessionManager2)mgrObj;
                            if (mgr.GetSessionEnumerator(out var sessionEnum) == 0 && sessionEnum != null)
                            {
                                try
                                {
                                    sessionEnum.GetCount(out int count);
                                    // 1ª tentativa: PID exato ou árvore de processos (Chromium utility/audio service/renderers/game engines)
                                    if (treePids != null && treePids.Count > 0)
                                    {
                                        for (int i = 0; i < count; i++)
                                        {
                                            if (sessionEnum.GetSession(i, out object sessionObj) == 0 && sessionObj != null)
                                            {
                                                try
                                                {
                                                    if (sessionObj is AudioInterop.IAudioSessionControl2 ctl2)
                                                    {
                                                        if (ctl2.GetProcessId(out uint sessionPid) == 0 && sessionPid > 0 && treePids.Contains(sessionPid))
                                                        {
                                                            if (sessionObj is AudioInterop.ISimpleAudioVolume simpleVol)
                                                            {
                                                                if (simpleVol.GetMasterVolume(out float curVol) == 0)
                                                                {
                                                                    float newVol = Math.Clamp(curVol + delta, 0f, 1f);
                                                                    Guid eventContext = Guid.Empty;
                                                                    simpleVol.SetMasterVolume(newVol, ref eventContext);
                                                                    if (newVol < 0.005f) simpleVol.SetMute(true, ref eventContext);
                                                                    else simpleVol.SetMute(false, ref eventContext);

                                                                    _cachedSessionVolumeObj = simpleVol;
                                                                    _cachedSessionPid = sessionPid;
                                                                    _cachedSessionName = finalName;
                                                                    _cachedSessionTimestamp = now;
                                                                    return Math.Clamp((int)Math.Round(newVol * 100f), 0, 100);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                catch { }
                                                finally
                                                {
                                                    if (sessionObj != _cachedSessionVolumeObj)
                                                    {
                                                        Marshal.ReleaseComObject(sessionObj);
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    // 2ª tentativa: correspondência por alias, executável ou DisplayName
                                    if (!string.IsNullOrWhiteSpace(finalName))
                                    {
                                        for (int i = 0; i < count; i++)
                                        {
                                            if (sessionEnum.GetSession(i, out object sessionObj) == 0 && sessionObj != null)
                                            {
                                                try
                                                {
                                                    if (sessionObj is AudioInterop.IAudioSessionControl2 ctl2)
                                                    {
                                                        if (ctl2.GetProcessId(out uint sPid) == 0 && sPid > 0)
                                                        {
                                                            string sName = GetSafeProcessName(sPid);
                                                            ctl2.GetDisplayName(out string disp);

                                                            bool isMatch = false;
                                                            if (!string.IsNullOrEmpty(sName))
                                                            {
                                                                isMatch = sName.Equals(finalName, StringComparison.OrdinalIgnoreCase) ||
                                                                          (!string.IsNullOrEmpty(alias) && sName.Equals(alias, StringComparison.OrdinalIgnoreCase)) ||
                                                                          finalName.Contains(sName, StringComparison.OrdinalIgnoreCase) ||
                                                                          sName.Contains(finalName, StringComparison.OrdinalIgnoreCase);
                                                            }
                                                            if (!isMatch && !string.IsNullOrEmpty(disp))
                                                            {
                                                                isMatch = disp.Contains(finalName, StringComparison.OrdinalIgnoreCase) ||
                                                                          finalName.Contains(disp, StringComparison.OrdinalIgnoreCase);
                                                            }

                                                            if (isMatch && sessionObj is AudioInterop.ISimpleAudioVolume simpleVol)
                                                            {
                                                                if (simpleVol.GetMasterVolume(out float curVol) == 0)
                                                                {
                                                                    float newVol = Math.Clamp(curVol + delta, 0f, 1f);
                                                                    Guid eventContext = Guid.Empty;
                                                                    simpleVol.SetMasterVolume(newVol, ref eventContext);
                                                                    if (newVol < 0.005f) simpleVol.SetMute(true, ref eventContext);
                                                                    else simpleVol.SetMute(false, ref eventContext);

                                                                    _cachedSessionVolumeObj = simpleVol;
                                                                    _cachedSessionPid = sPid;
                                                                    _cachedSessionName = sName;
                                                                    _cachedSessionTimestamp = now;
                                                                    return Math.Clamp((int)Math.Round(newVol * 100f), 0, 100);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                catch { }
                                                finally
                                                {
                                                    if (sessionObj != _cachedSessionVolumeObj)
                                                    {
                                                        Marshal.ReleaseComObject(sessionObj);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(sessionEnum);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(mgrObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return -1;
        });

        resolvedName = finalName;
        return result;
    }

    /// <summary>
    /// Retorna a lista de sessões de áudio ativas no dispositivo de reprodução padrão.
    /// Utilizado pelo mod de controle de volume da barra de tarefas para associar atalhos aos apps em execução.
    /// </summary>
    public static List<AudioSessionInfo> GetActiveAudioSessions()
    {
        return AudioComThread.Invoke(() =>
        {
            var list = new List<AudioSessionInfo>();
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Multimedia, out dev) == 0 && dev != null)
                {
                    Guid iid = IID_IAudioSessionManager2;
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0 && mgrObj != null)
                    {
                        try
                        {
                            var mgr = (AudioInterop.IAudioSessionManager2)mgrObj;
                            if (mgr.GetSessionEnumerator(out var sessionEnum) == 0 && sessionEnum != null)
                            {
                                try
                                {
                                    sessionEnum.GetCount(out int count);
                                    for (int i = 0; i < count; i++)
                                    {
                                        if (sessionEnum.GetSession(i, out object sessionObj) == 0 && sessionObj != null)
                                        {
                                            try
                                            {
                                                if (sessionObj is AudioInterop.IAudioSessionControl2 ctl2)
                                                {
                                                    if (ctl2.IsSystemSoundsSession() == 0) continue; // S_OK (0) significa que É som do sistema, portanto pula

                                                    if (ctl2.GetProcessId(out uint pid) == 0 && pid > 0)
                                                    {
                                                        string pName = GetSafeProcessName(pid);

                                                        ctl2.GetDisplayName(out string disp);
                                                        float vol = 1.0f;
                                                        bool muted = false;
                                                        if (sessionObj is AudioInterop.ISimpleAudioVolume simpleVol)
                                                        {
                                                            simpleVol.GetMasterVolume(out vol);
                                                            simpleVol.GetMute(out muted);
                                                        }

                                                        list.Add(new AudioSessionInfo
                                                        {
                                                            ProcessId = pid,
                                                            ProcessName = pName,
                                                            DisplayName = disp ?? "",
                                                            Volume = vol,
                                                            IsMuted = muted
                                                        });
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                Marshal.ReleaseComObject(sessionObj);
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(sessionEnum);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(mgrObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return list;
        });
    }

    /// <summary>
    /// Alterna o estado de mudo de uma aplicação específica (sessão WASAPI).
    /// </summary>
    public static bool ToggleProcessMute(uint targetPid, string fallbackName, out string resolvedName, out bool newMutedState)
    {
        string appName = fallbackName;
        if (targetPid > 0)
        {
            string safeName = GetSafeProcessName(targetPid);
            if (!string.IsNullOrWhiteSpace(safeName)) appName = safeName;
        }

        string alias = TaskbarVolumeHook.MapAppNameToProcessAlias(appName);
        string finalName = !string.IsNullOrWhiteSpace(alias) ? alias : appName;
        HashSet<uint>? treePids = targetPid > 0 ? GetProcessTree(targetPid) : null;
        bool resultMute = false;
        bool found = false;

        found = AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Multimedia, out dev) == 0 && dev != null)
                {
                    Guid iid = IID_IAudioSessionManager2;
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0 && mgrObj != null)
                    {
                        try
                        {
                            var mgr = (AudioInterop.IAudioSessionManager2)mgrObj;
                            if (mgr.GetSessionEnumerator(out var sessionEnum) == 0 && sessionEnum != null)
                            {
                                try
                                {
                                    sessionEnum.GetCount(out int count);
                                    for (int i = 0; i < count; i++)
                                    {
                                        if (sessionEnum.GetSession(i, out object sessionObj) == 0 && sessionObj != null)
                                        {
                                            try
                                            {
                                                if (sessionObj is AudioInterop.IAudioSessionControl2 ctl2)
                                                {
                                                    ctl2.GetProcessId(out uint pid);
                                                    bool match = (targetPid > 0 && pid == targetPid) || (treePids != null && treePids.Contains(pid));
                                                    if (!match && !string.IsNullOrWhiteSpace(finalName))
                                                    {
                                                        string sName = GetSafeProcessName(pid);
                                                        ctl2.GetDisplayName(out string disp);

                                                        if (!string.IsNullOrEmpty(sName))
                                                        {
                                                            match = sName.Equals(finalName, StringComparison.OrdinalIgnoreCase) ||
                                                                    (!string.IsNullOrEmpty(alias) && sName.Equals(alias, StringComparison.OrdinalIgnoreCase)) ||
                                                                    finalName.Contains(sName, StringComparison.OrdinalIgnoreCase) ||
                                                                    sName.Contains(finalName, StringComparison.OrdinalIgnoreCase);
                                                        }
                                                        if (!match && !string.IsNullOrEmpty(disp))
                                                        {
                                                            match = disp.Contains(finalName, StringComparison.OrdinalIgnoreCase) ||
                                                             finalName.Contains(disp, StringComparison.OrdinalIgnoreCase);
                                                        }

                                                        if (match)
                                                        {
                                                            finalName = !string.IsNullOrEmpty(sName) ? sName : finalName;
                                                        }
                                                    }

                                                    if (match && sessionObj is AudioInterop.ISimpleAudioVolume simpleVol)
                                                    {
                                                        simpleVol.GetMute(out bool isMuted);
                                                        bool nextMute = !isMuted;
                                                        Guid ctx = Guid.Empty;
                                                        simpleVol.SetMute(nextMute, ref ctx);
                                                        resultMute = nextMute;
                                                        return true;
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                Marshal.ReleaseComObject(sessionObj);
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(sessionEnum);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(mgrObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return false;
        });

        resolvedName = finalName;
        newMutedState = resultMute;
        return found;
    }

    public static bool IsExplorerProcess(uint pid)
    {
        try
        {
            string name = GetSafeProcessName(pid);
            return name.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static uint _lastActivePlayerPid = 0;
    private static string _lastActivePlayerName = "";
    private static long _lastActivePlayerTimestamp = 0;

    private sealed class PlayerCandidate
    {
        public uint ProcessId;
        public string ProcessName = "";
        public string FriendlyName = "";
        public float Peak;
        public int State;
        public int Score;
        public AudioInterop.ISimpleAudioVolume? SimpleVolume;
    }

    private static int ScoreCandidate(string pName, string disp, uint pid, float peak, int state, long now, out string friendly)
    {
        friendly = FormatPlayerFriendlyName(pName, disp, pid);
        int score = 0;

        // 1. Emissão de som em tempo real tem prioridade MÁXIMA
        if (peak > 0.001f) score += 10000;
        else if (peak > 0.00001f) score += 5000;

        // 2. Estado da sessão WASAPI
        if (state == 0) score += 600;      // Active
        else if (state == 1) score += 100; // Inactive / Pausado

        string lower = pName.ToLowerInvariant();
        string dispLower = (disp ?? "").ToLowerInvariant();

        bool isDedicatedMedia = lower.Contains("spotify") ||
                               lower.Contains("vlc") ||
                               lower.Contains("foobar") ||
                               lower.Contains("aimp") ||
                               lower.Contains("tidal") ||
                               lower.Contains("deezer") ||
                               lower.Contains("itunes") ||
                               lower.Contains("applemusic") ||
                               lower.Contains("potplayer") ||
                               lower.Contains("wmplayer") ||
                               lower.Contains("music.ui") ||
                               dispLower.Contains("spotify") ||
                               dispLower.Contains("music") ||
                               dispLower.Contains("player");

        bool isBrowser = lower.Contains("chrome") ||
                         lower.Contains("msedge") ||
                         lower.Contains("edge") ||
                         lower.Contains("firefox") ||
                         lower.Contains("brave") ||
                         lower.Contains("opera") ||
                         lower.Contains("vivaldi") ||
                         dispLower.Contains("youtube");

        bool isVoice = lower.Contains("discord") ||
                       lower.Contains("telegram") ||
                       lower.Contains("whatsapp");

        bool isBackgroundDaemon = lower is "nvcontainer" or "windhawk" or "steam" or "steamwebhelper" or
                                  "epicgameslauncher" or "origin" or "ea" or "galaxyclient" or
                                  "razer" or "icue" or "steelseries" or "overwolf" or "audiodg";

        if (isDedicatedMedia)
        {
            score += 3500;
        }
        else if (isBrowser)
        {
            score += 1800;
            try
            {
                var procs = Process.GetProcessesByName(pName);
                foreach (var pr in procs)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(pr.MainWindowTitle))
                        {
                            string mt = pr.MainWindowTitle;
                            if (mt.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
                                mt.Contains("Twitch", StringComparison.OrdinalIgnoreCase) ||
                                mt.Contains("SoundCloud", StringComparison.OrdinalIgnoreCase) ||
                                mt.Contains("Netflix", StringComparison.OrdinalIgnoreCase) ||
                                mt.Contains("Deezer", StringComparison.OrdinalIgnoreCase) ||
                                mt.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                            {
                                score += 3000;
                                break;
                            }
                        }
                    }
                    catch { }
                    finally { pr.Dispose(); }
                }
            }
            catch { }
        }
        else if (isVoice)
        {
            score += (peak > 0.00001f ? 1200 : 50);
        }
        else if (isBackgroundDaemon)
        {
            score -= 4000;
        }
        else
        {
            score += 150;
        }

        if (pid == _lastActivePlayerPid && (now - _lastActivePlayerTimestamp) < 6000)
        {
            score += 1200;
        }

        return score;
    }

    public static bool IsProcessExcluded(string processName, string? displayName, IEnumerable<string>? excludedList)
    {
        if (excludedList == null) return false;

        string cleanProc = processName.Trim();
        if (cleanProc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleanProc = cleanProc[..^4];

        foreach (var item in excludedList)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            string cleanItem = item.Trim();
            if (cleanItem.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanItem = cleanItem[..^4];

            if (string.Equals(cleanProc, cleanItem, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(processName.Trim(), item.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(displayName) && displayName.Contains(cleanItem, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ajusta o volume do player de mídia ou áudio atualmente ativo (Spotify, YouTube Music, vídeo no navegador, etc.).
    /// Retorna a nova porcentagem de volume (0 a 100), ou -1 se nenhum player estiver em execução.
    /// </summary>
    public static int AdjustActiveMediaPlayerVolume(float delta, out string resolvedPlayerName, IEnumerable<string>? excludedApps = null)
    {
        string finalName = "";
        int result = AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Multimedia, out dev) == 0 && dev != null)
                {
                    Guid iid = IID_IAudioSessionManager2;
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0 && mgrObj != null)
                    {
                        try
                        {
                            var mgr = (AudioInterop.IAudioSessionManager2)mgrObj;
                            if (mgr.GetSessionEnumerator(out var sessionEnum) == 0 && sessionEnum != null)
                            {
                                try
                                {
                                    sessionEnum.GetCount(out int count);
                                    var candidates = new List<PlayerCandidate>();
                                    var allSessions = new List<object>();
                                    long now = Environment.TickCount64;

                                    for (int i = 0; i < count; i++)
                                    {
                                        if (sessionEnum.GetSession(i, out object sessionObj) == 0 && sessionObj != null)
                                        {
                                            allSessions.Add(sessionObj);
                                            try
                                            {
                                                if (sessionObj is AudioInterop.IAudioSessionControl2 ctl2)
                                                {
                                                    if (ctl2.IsSystemSoundsSession() == 0) continue; // S_OK (0) significa que É som do sistema, portanto pula

                                                    if (ctl2.GetProcessId(out uint pid) == 0 && pid > 0)
                                                    {
                                                        if (IsExplorerProcess(pid)) continue;

                                                        ctl2.GetState(out int state);
                                                        if (state == 2) continue; // Expired

                                                        string pName = GetSafeProcessName(pid);
                                                        ctl2.GetDisplayName(out string disp);

                                                        if (IsProcessExcluded(pName, disp, excludedApps)) continue;

                                                        float peak = 0f;
                                                        if (sessionObj is AudioInterop.IAudioMeterInformation meter)
                                                        {
                                                            meter.GetPeakValue(out peak);
                                                        }

                                                        if (sessionObj is not AudioInterop.ISimpleAudioVolume sav) continue;

                                                        int score = ScoreCandidate(pName, disp, pid, peak, state, now, out string friendly);

                                                        candidates.Add(new PlayerCandidate
                                                        {
                                                            ProcessId = pid,
                                                            ProcessName = pName,
                                                            FriendlyName = friendly,
                                                            Peak = peak,
                                                            State = state,
                                                            Score = score,
                                                            SimpleVolume = sav
                                                        });
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                    }

                                    if (candidates.Count > 0)
                                    {
                                        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
                                        var best = candidates[0];

                                        if (best.Score > 0 || best.Peak > 0.00001f)
                                        {
                                            if (best.SimpleVolume != null && best.SimpleVolume.GetMasterVolume(out float curVol) == 0)
                                        {
                                            float newVol = Math.Clamp(curVol + delta, 0f, 1f);
                                            Guid eventContext = Guid.Empty;
                                            best.SimpleVolume.SetMasterVolume(newVol, ref eventContext);
                                            if (newVol < 0.005f) best.SimpleVolume.SetMute(true, ref eventContext);
                                            else best.SimpleVolume.SetMute(false, ref eventContext);

                                            _lastActivePlayerPid = best.ProcessId;
                                            _lastActivePlayerName = best.FriendlyName;
                                            _lastActivePlayerTimestamp = now;

                                            // Ajusta também subprocessos/irmãos da mesma árvore de processos (ex: processos utilitários de áudio do Chrome/Edge)
                                            var tree = GetProcessTree(best.ProcessId);
                                            if (tree != null && tree.Count > 1)
                                            {
                                                for (int j = 1; j < candidates.Count; j++)
                                                {
                                                    var c = candidates[j];
                                                    if (tree.Contains(c.ProcessId) && c.SimpleVolume != null && c.SimpleVolume != best.SimpleVolume)
                                                    {
                                                        try
                                                        {
                                                            c.SimpleVolume.SetMasterVolume(newVol, ref eventContext);
                                                            if (newVol < 0.005f) c.SimpleVolume.SetMute(true, ref eventContext);
                                                            else c.SimpleVolume.SetMute(false, ref eventContext);
                                                        }
                                                        catch { }
                                                    }
                                                }
                                            }

                                            finalName = best.FriendlyName;
                                            return Math.Clamp((int)Math.Round(newVol * 100f), 0, 100);
                                        }
                                        }
                                    }

                                    // Libera todos os objetos COM
                                    foreach (var obj in allSessions)
                                    {
                                        try { Marshal.ReleaseComObject(obj); } catch { }
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(sessionEnum);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(mgrObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return -1;
        });

        resolvedPlayerName = finalName;
        return result;
    }

    /// <summary>
    /// Alterna o mudo do player de mídia ou áudio atualmente ativo.
    /// </summary>
    public static bool ToggleActiveMediaPlayerMute(out string resolvedPlayerName, out bool isMuted, IEnumerable<string>? excludedApps = null)
    {
        string finalName = "";
        bool resultMuted = false;
        bool success = AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? dev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Multimedia, out dev) == 0 && dev != null)
                {
                    Guid iid = IID_IAudioSessionManager2;
                    if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0 && mgrObj != null)
                    {
                        try
                        {
                            var mgr = (AudioInterop.IAudioSessionManager2)mgrObj;
                            if (mgr.GetSessionEnumerator(out var sessionEnum) == 0 && sessionEnum != null)
                            {
                                try
                                {
                                    sessionEnum.GetCount(out int count);
                                    var candidates = new List<PlayerCandidate>();
                                    var allSessions = new List<object>();
                                    long now = Environment.TickCount64;

                                    for (int i = 0; i < count; i++)
                                    {
                                        if (sessionEnum.GetSession(i, out object sessionObj) == 0 && sessionObj != null)
                                        {
                                            allSessions.Add(sessionObj);
                                            try
                                            {
                                                if (sessionObj is AudioInterop.IAudioSessionControl2 ctl2)
                                                {
                                                    if (ctl2.IsSystemSoundsSession() == 0) continue; // S_OK (0) significa que É som do sistema, portanto pula

                                                    if (ctl2.GetProcessId(out uint pid) == 0 && pid > 0)
                                                    {
                                                        if (IsExplorerProcess(pid)) continue;

                                                        ctl2.GetState(out int state);
                                                        if (state == 2) continue;

                                                        string pName = GetSafeProcessName(pid);
                                                        ctl2.GetDisplayName(out string disp);

                                                        if (IsProcessExcluded(pName, disp, excludedApps)) continue;

                                                        float peak = 0f;
                                                        if (sessionObj is AudioInterop.IAudioMeterInformation meter)
                                                        {
                                                            meter.GetPeakValue(out peak);
                                                        }

                                                        if (sessionObj is not AudioInterop.ISimpleAudioVolume sav) continue;

                                                        int score = ScoreCandidate(pName, disp, pid, peak, state, now, out string friendly);

                                                        candidates.Add(new PlayerCandidate
                                                        {
                                                            ProcessId = pid,
                                                            ProcessName = pName,
                                                            FriendlyName = friendly,
                                                            Peak = peak,
                                                            State = state,
                                                            Score = score,
                                                            SimpleVolume = sav
                                                        });
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                    }

                                    if (candidates.Count > 0)
                                    {
                                        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
                                        var best = candidates[0];

                                        if (best.Score > 0 || best.Peak > 0.00001f)
                                        {
                                            if (best.SimpleVolume != null && best.SimpleVolume.GetMute(out bool currentlyMuted) == 0)
                                        {
                                            bool nextMute = !currentlyMuted;
                                            Guid eventContext = Guid.Empty;
                                            best.SimpleVolume.SetMute(nextMute, ref eventContext);

                                            _lastActivePlayerPid = best.ProcessId;
                                            _lastActivePlayerName = best.FriendlyName;
                                            _lastActivePlayerTimestamp = now;

                                            var tree = GetProcessTree(best.ProcessId);
                                            if (tree != null && tree.Count > 1)
                                            {
                                                for (int j = 1; j < candidates.Count; j++)
                                                {
                                                    var c = candidates[j];
                                                    if (tree.Contains(c.ProcessId) && c.SimpleVolume != null && c.SimpleVolume != best.SimpleVolume)
                                                    {
                                                        try { c.SimpleVolume.SetMute(nextMute, ref eventContext); } catch { }
                                                    }
                                                }
                                            }

                                            finalName = best.FriendlyName;
                                            resultMuted = nextMute;
                                            return true;
                                        }
                                        }
                                    }

                                    foreach (var obj in allSessions)
                                    {
                                        try { Marshal.ReleaseComObject(obj); } catch { }
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(sessionEnum);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(mgrObj);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return false;
        });

        resolvedPlayerName = finalName;
        isMuted = resultMuted;
        return success;
    }

    public static string FormatPlayerFriendlyName(string processName, string displayName, uint pid = 0)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("@") && !displayName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return displayName.Trim();
        }

        if (string.IsNullOrWhiteSpace(processName)) return "Player de Mídia";
        string lower = processName.ToLowerInvariant();

        string baseName = processName;
        if (lower.Contains("spotify")) baseName = "Spotify";
        else if (lower.Contains("msedge") || lower.Contains("edge")) baseName = "Microsoft Edge";
        else if (lower.Contains("chrome")) baseName = "Google Chrome";
        else if (lower.Contains("firefox")) baseName = "Mozilla Firefox";
        else if (lower.Contains("brave")) baseName = "Brave Browser";
        else if (lower.Contains("opera")) baseName = "Opera Browser";
        else if (lower.Contains("vivaldi")) baseName = "Vivaldi";
        else if (lower.Contains("vlc")) baseName = "VLC Media Player";
        else if (lower.Contains("foobar")) baseName = "foobar2000";
        else if (lower.Contains("tidal")) baseName = "TIDAL";
        else if (lower.Contains("deezer")) baseName = "Deezer";
        else if (lower.Contains("itunes")) baseName = "iTunes";
        else if (lower.Contains("applemusic")) baseName = "Apple Music";
        else if (lower.Contains("aimp")) baseName = "AIMP";
        else if (lower.Contains("potplayer")) baseName = "PotPlayer";
        else if (lower.Contains("wmplayer") || lower.Contains("music.ui")) baseName = "Windows Media Player";
        else if (lower.Contains("discord")) baseName = "Discord";
        else if (lower.Contains("telegram")) baseName = "Telegram";
        else if (lower.Contains("whatsapp")) baseName = "WhatsApp";

        if (pid > 0)
        {
            try
            {
                string title = "";
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    title = p.MainWindowTitle;
                }
                catch { }

                if (string.IsNullOrWhiteSpace(title))
                {
                    var sameNameProcs = Process.GetProcessesByName(processName);
                    foreach (var sp in sameNameProcs)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(sp.MainWindowTitle))
                            {
                                title = sp.MainWindowTitle;
                                break;
                            }
                        }
                        catch { }
                        finally
                        {
                            sp.Dispose();
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    if (title.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)) return $"YouTube Music ({baseName})";
                    if (title.Contains("YouTube", StringComparison.OrdinalIgnoreCase)) return $"YouTube ({baseName})";
                    if (title.Contains("Twitch", StringComparison.OrdinalIgnoreCase)) return $"Twitch ({baseName})";
                    if (title.Contains("SoundCloud", StringComparison.OrdinalIgnoreCase)) return $"SoundCloud ({baseName})";
                    if (title.Contains("Netflix", StringComparison.OrdinalIgnoreCase)) return $"Netflix ({baseName})";
                }
            }
            catch { }
        }

        return baseName;
    }

    /// <summary>
    /// Define o volume escalar do dispositivo padrão atual.
    /// </summary>
    public static bool SetDefaultVolumeScalar(AudioInterop.EDataFlow flow, float scalar)
    {
        return AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDevice? defaultDev = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(flow, AudioInterop.ERole.Console, out defaultDev) == 0 && defaultDev != null)
                {
                    if (defaultDev.GetId(out var devId) == 0)
                    {
                        return SetDeviceVolumeScalarCore(devId, scalar);
                    }
                }
            }
            catch { }
            finally
            {
                if (defaultDev != null) Marshal.ReleaseComObject(defaultDev);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return false;
        });
    }

    /// <summary>
    /// Obtém o estado de mudo do dispositivo padrão (verifica Comunicações e Console, igual a SoundSwitch e AHK_MicMute).
    /// </summary>
    public static bool GetDefaultMute(AudioInterop.EDataFlow flow)
    {
        return AudioComThread.Invoke(() =>
        {
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                // Verifica primeiro Console e Communications (os dois papéis principais do Windows)
                var roles = new[] { AudioInterop.ERole.Console, AudioInterop.ERole.Communications };
                foreach (var role in roles)
                {
                    if (enumerator.GetDefaultAudioEndpoint(flow, role, out var dev) == 0 && dev != null)
                    {
                        try
                        {
                            Guid iid = IID_IAudioEndpointVolume;
                            if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                            {
                                try
                                {
                                    var endpointVolume = (AudioInterop.IAudioEndpointVolume)volObj;
                                    if (endpointVolume.GetMute(out bool isMuted) == 0)
                                    {
                                        return isMuted;
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(volObj);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return false;
        });
    }

    /// <summary>
    /// Define o estado de mudo do dispositivo padrão em todos os papéis (Console, Comunicações e Multimídia).
    /// </summary>
    public static bool SetDefaultMute(AudioInterop.EDataFlow flow, bool isMuted)
    {
        if (flow == AudioInterop.EDataFlow.Capture)
        {
            StopConflictingExternalAudioTools();
            DesiredMicMuteState = isMuted;
        }

        return AudioComThread.Invoke(() =>
        {
            bool success = false;
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                var roles = new[] { AudioInterop.ERole.Console, AudioInterop.ERole.Communications, AudioInterop.ERole.Multimedia };
                var affectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var role in roles)
                {
                    if (enumerator.GetDefaultAudioEndpoint(flow, role, out var dev) == 0 && dev != null)
                    {
                        try
                        {
                            if (dev.GetId(out var devId) == 0 && !affectedIds.Contains(devId))
                            {
                                affectedIds.Add(devId);
                                success |= SetDeviceMuteCore(devId, isMuted, flow == AudioInterop.EDataFlow.Capture);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return success;
        });
    }

    /// <summary>
    /// Inverte o estado de mudo do dispositivo padrão. Se for captura (microfone), sincroniza todos os microfones ("All Microphones" do AHK_MicMute).
    /// </summary>
    public static bool ToggleDefaultMute(AudioInterop.EDataFlow flow)
    {
        if (flow == AudioInterop.EDataFlow.Capture)
        {
            StopConflictingExternalAudioTools();
        }

        bool current = GetDefaultMute(flow);
        bool next = !current;
        SetDefaultMute(flow, next);
        if (flow == AudioInterop.EDataFlow.Capture)
        {
            DesiredMicMuteState = next;
            SetAllCaptureMute(next);
        }
        return next;
    }

    /// <summary>
    /// Muta ou desmuta todos os microfones/captura ativos do sistema simultaneamente ("All Microphones" de AHK_MicMute).
    /// </summary>
    public static void SetAllCaptureMute(bool isMuted)
    {
        StopConflictingExternalAudioTools();
        DesiredMicMuteState = isMuted;
        var devices = GetDevices(AudioInterop.EDataFlow.Capture);
        AudioComThread.Invoke(() =>
        {
            foreach (var dev in devices)
            {
                SetDeviceMuteCore(dev.Id, isMuted, isCapture: true);
            }
        });
    }

    /// <summary>
    /// Reaplica o estado de mudo desejado nos microfones caso algum jogo/aplicativo tente alterá-lo (force_current_state de AHK_MicMute).
    /// </summary>
    public static bool EnforceDesiredMicMuteState()
    {
        if (!DesiredMicMuteState.HasValue) return false;
        bool targetMute = DesiredMicMuteState.Value;
        bool currentMute = GetDefaultMute(AudioInterop.EDataFlow.Capture);
        if (currentMute == targetMute) return false;

        StopConflictingExternalAudioTools();
        SetDefaultMute(AudioInterop.EDataFlow.Capture, targetMute);
        SetAllCaptureMute(targetMute);
        return true;
    }

    /// <summary>
    /// Define mudo em um dispositivo específico por ID.
    /// </summary>
    public static bool SetDeviceMute(string deviceId, bool isMuted)
    {
        return AudioComThread.Invoke(() => SetDeviceMuteCore(deviceId, isMuted, isCapture: true));
    }

    private static bool SetDeviceMuteCore(string deviceId, bool isMuted, bool isCapture)
    {
        AudioInterop.IMMDeviceEnumerator? enumerator = null;
        AudioInterop.IMMDevice? dev = null;
        try
        {
            enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
            if (enumerator.GetDevice(deviceId, out dev) == 0 && dev != null)
            {
                Guid iid = IID_IAudioEndpointVolume;
                if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                {
                    try
                    {
                        var endpointVolume = (AudioInterop.IAudioEndpointVolume)volObj;
                        Guid eventContext = Guid.Empty;
                        int hr = endpointVolume.SetMute(isMuted, ref eventContext);

                        // Também sincroniza sessões de captura ativas (ISimpleAudioVolume) se for microfone
                        if (isCapture)
                        {
                            MuteDeviceSessionsCore(dev, isMuted);
                        }

                        return hr >= 0;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(volObj);
                    }
                }
            }


        }
        catch { }
        finally
        {
            if (dev != null) Marshal.ReleaseComObject(dev);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
        return false;
    }

    private static void MuteDeviceSessionsCore(AudioInterop.IMMDevice dev, bool isMuted)
    {
        try
        {
            Guid iidMgr = IID_IAudioSessionManager2;
            if (dev.Activate(ref iidMgr, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) == 0 && mgrObj != null)
            {
                try
                {
                    var mgr = (AudioInterop.IAudioSessionManager2)mgrObj;
                    if (mgr.GetSessionEnumerator(out var sessEnum) == 0 && sessEnum != null)
                    {
                        try
                        {
                            if (sessEnum.GetCount(out int count) == 0)
                            {
                                Guid ctx = Guid.Empty;
                                for (int i = 0; i < count; i++)
                                {
                                    if (sessEnum.GetSession(i, out var sessObj) == 0 && sessObj != null)
                                    {
                                        try
                                        {
                                            if (sessObj is AudioInterop.ISimpleAudioVolume simpleVol)
                                            {
                                                simpleVol.SetMute(isMuted, ref ctx);
                                            }
                                        }
                                        finally
                                        {
                                            Marshal.ReleaseComObject(sessObj);
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(sessEnum);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(mgrObj);
                }
            }


        }
        catch { }
    }

    /// <summary>
    /// Volume do feedback sonoro de 0 a 100%. Padrão: 50%.
    /// </summary>
    public static int FeedbackVolume { get; set; } = 50;

    /// <summary>
    /// Toca feedback de áudio em memória (WAV sintetizado) sem dependências externas.
    /// </summary>
    public static void PlayFeedback(AudioFeedbackType type, int? volumePercent = null)
    {
        int vol = volumePercent ?? FeedbackVolume;
        if (vol <= 0) return;

        Task.Run(() =>
        {
            try
            {
                byte[]? wavBytes = type switch
                {
                    AudioFeedbackType.DeviceSwitched => GenerateChimeWav(new[] { (600, 60), (950, 90) }, vol),
                    AudioFeedbackType.MicMuted => GenerateChimeWav(new[] { (620, 50), (380, 80) }, vol),
                    AudioFeedbackType.MicUnmuted => GenerateChimeWav(new[] { (400, 50), (780, 80) }, vol),
                    AudioFeedbackType.PlaybackMuted => GenerateChimeWav(new[] { (500, 60), (300, 80) }, vol),
                    AudioFeedbackType.PlaybackUnmuted => GenerateChimeWav(new[] { (350, 60), (600, 80) }, vol),
                    AudioFeedbackType.PttOn => GenerateChimeWav(new[] { (720, 30) }, vol),
                    AudioFeedbackType.PttOff => GenerateChimeWav(new[] { (420, 30) }, vol),
                    _ => null
                };

                if (wavBytes != null)
                {
                    using var ms = new MemoryStream(wavBytes);
                    using var player = new SoundPlayer(ms);
                    player.PlaySync();
                }
            }
            catch
            {
                // Silencioso se áudio não estiver disponível
            }
        });
    }

    /// <summary>
    /// Gera um arquivo WAV mono PCM de 16 bits na memória contendo tons suaves com envelope anti-clique.
    /// </summary>
    private static byte[] GenerateChimeWav((int frequency, int durationMs)[] tones, int volumePercent = 50)
    {
        const int sampleRate = 44100;
        int totalSamples = tones.Sum(t => (int)(sampleRate * (t.durationMs / 1000.0)));
        byte[] pcmData = new byte[totalSamples * 2];

        double maxAmp = Math.Clamp((volumePercent / 100.0) * 16000.0, 0, 32000.0);

        int sampleIndex = 0;
        foreach (var (freq, durationMs) in tones)
        {
            int toneSamples = (int)(sampleRate * (durationMs / 1000.0));
            int fadeSamples = Math.Min(sampleRate / 100, toneSamples / 4); // 10ms fade in/out

            for (int i = 0; i < toneSamples && sampleIndex < totalSamples; i++, sampleIndex++)
            {
                double envelope = 1.0;
                if (i < fadeSamples)
                {
                    envelope = (double)i / fadeSamples;
                }
                else if (i > toneSamples - fadeSamples)
                {
                    envelope = (double)(toneSamples - i) / fadeSamples;
                }

                double time = (double)i / sampleRate;
                short sample = (short)(Math.Sin(2.0 * Math.PI * freq * time) * envelope * maxAmp);

                pcmData[sampleIndex * 2] = (byte)(sample & 0xFF);
                pcmData[sampleIndex * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF Header
        bw.Write("RIFF"u8);
        bw.Write(36 + pcmData.Length);
        bw.Write("WAVE"u8);

        // Subchunk 1 "fmt "
        bw.Write("fmt "u8);
        bw.Write(16);          // Subchunk1Size (16 for PCM)
        bw.Write((short)1);    // AudioFormat (1 = PCM)
        bw.Write((short)1);    // NumChannels (1 = Mono)
        bw.Write(sampleRate);  // SampleRate
        bw.Write(sampleRate * 2); // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
        bw.Write((short)2);    // BlockAlign (NumChannels * BitsPerSample/8)
        bw.Write((short)16);   // BitsPerSample (16 bits)

        // Subchunk 2 "data"
        bw.Write("data"u8);
        bw.Write(pcmData.Length);
        bw.Write(pcmData);

        return ms.ToArray();
    }
}
