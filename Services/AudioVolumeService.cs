using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

public sealed class AudioVolumeService : IDisposable
{
    private const int ClsctxAll = 0x17;

    private readonly Dispatcher _ui;
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly VolumeCallback _volumeCallback;
    private readonly DeviceCallback _deviceCallback;
    private IAudioEndpointVolume? _endpoint;
    private Guid _context = Guid.NewGuid();

    public event Action<double, bool>? Changed;

    public AudioVolumeService(Dispatcher ui)
    {
        _ui = ui;
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _volumeCallback = new VolumeCallback(this);
        _deviceCallback = new DeviceCallback(this);
        Bind();
        _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
    }

    public void Nudge(int direction)
    {
        if (_endpoint == null || _endpoint.GetMasterVolumeLevelScalar(out float level) != 0) return;
        float next = Math.Clamp(MathF.Round((level + direction * 0.02f) * 50f) / 50f, 0f, 1f);
        if (direction > 0 && _endpoint.GetMute(out bool muted) == 0 && muted) _endpoint.SetMute(false, ref _context);
        _endpoint.SetMasterVolumeLevelScalar(next, ref _context);
    }

    private void Bind()
    {
        Unbind();
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0 || device == null)
                return;
            try
            {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object? instance) == 0 && instance is IAudioEndpointVolume endpoint)
                {
                    _endpoint = endpoint;
                    _endpoint.RegisterControlChangeNotify(_volumeCallback);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception ex)
        {
            Log.Error("audio.bind", ex);
            _endpoint = null;
        }
    }

    private void Unbind()
    {
        if (_endpoint == null) return;
        try { _endpoint.UnregisterControlChangeNotify(_volumeCallback); } catch { }
        Marshal.ReleaseComObject(_endpoint);
        _endpoint = null;
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback); } catch { }
        Unbind();
        Marshal.ReleaseComObject(_enumerator);
    }

    private sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly AudioVolumeService _owner;
        public VolumeCallback(AudioVolumeService owner) => _owner = owner;

        public int OnNotify(IntPtr notifyData)
        {
            if (notifyData == IntPtr.Zero) return 0;
            var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData);
            double level = data.MasterVolume;
            bool muted = data.Muted != 0;
            _owner._ui.BeginInvoke(() => _owner.Changed?.Invoke(level, muted));
            return 0;
        }
    }

    private sealed class DeviceCallback : IMMNotificationClient
    {
        private readonly AudioVolumeService _owner;
        public DeviceCallback(AudioVolumeService owner) => _owner = owner;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia) _owner._ui.BeginInvoke(_owner.Bind);
            return 0;
        }

        public int OnDeviceStateChanged(string deviceId, int newState) => 0;
        public int OnDeviceAdded(string deviceId) => 0;
        public int OnDeviceRemoved(string deviceId) => 0;
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
    }
}
