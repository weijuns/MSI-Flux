// This file is part of MSIFlux (Yet Another MSI Dragon Center Clone).
// Licensed under GPL-3.0-or-later.
//
// ServiceIpcProxy: GUI 侧的命名管道客户端封装. 把 "IPC 消息异步往返" 转换成
// GUI 代码更习惯的同步调用 (ApplyConf(), GetTemp(fan), ...).
//
// 设计要点:
// - 内部对 NamedPipeClient<ServiceCommand, ServiceResponse> 做薄封装.
// - 每条请求持有一个 TaskCompletionSource, 通过"命令类别"匹配回来的响应.
// - 单客户端单请求用 SemaphoreSlim 序列化同命令调用, 避免响应串号.
// - 所有公共方法都可以安全地在 UI 线程调用, 带超时保护, 不会卡死 UI.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using MSIFlux.IPC;

namespace MSIFlux.GUI.Helpers;

internal sealed class ServiceIpcProxy : IDisposable
{
    /// <summary>和 FanControlService 里 IPCServer 使用的 pipe 名保持一致.</summary>
    public const string PipeName = "MSIFlux-Server";

    /// <summary>单次同步请求的默认超时时间.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(2000);

    private readonly NamedPipeClient<ServiceResponse, ServiceCommand> _pipe;

    // 一类命令同时只允许一个 in-flight 请求 (避免响应串号).
    private readonly ConcurrentDictionary<Command, SemaphoreSlim> _cmdLocks = new();

    // 按命令类别登记的"等待响应"句柄.
    private readonly ConcurrentDictionary<Command, PendingRequest> _pending = new();

    private volatile bool _connected;
    private bool _disposed;

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    /// <summary>
    /// 服务端推送 (或响应) 到达时触发. 订阅方可以根据 Response.Response 做转发.
    /// 例如把 Response.Temp / Response.FanRPM 缓存到 FanControlRunner 的属性里.
    /// </summary>
    public event EventHandler<ServiceResponse>? ServerMessage;

    public bool IsConnected => _connected;

    public ServiceIpcProxy()
    {
        _pipe = new NamedPipeClient<ServiceResponse, ServiceCommand>(PipeName)
        {
            AutoReconnect = true,
            AutoReconnectDelay = 1000,
        };
        _pipe.Connected += (_, _) =>
        {
            _connected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        };
        _pipe.Disconnected += (_, _) =>
        {
            _connected = false;
            // 把所有正在等待的调用唤醒 (以失败收尾), 否则 UI 会一直等下去
            foreach (var kv in _pending)
            {
                kv.Value.TrySetCanceled();
            }
            _pending.Clear();
            Disconnected?.Invoke(this, EventArgs.Empty);
        };
        _pipe.ServerMessage += (_, e) => HandleServerMessage(e.Message);
        _pipe.Error += (_, e) =>
        {
            Debug.WriteLine($"[ServiceIpcProxy] 管道错误: {e.Exception?.Message}");
        };
    }

    /// <summary>启动连接 (异步). 用 WaitForConnection() 等待完成.</summary>
    public void Start() => _pipe.Start();

    /// <summary>等待连接建立; 返回是否在超时内完成连接.</summary>
    public bool WaitForConnection(TimeSpan timeout) => _pipe.WaitForConnection(timeout);

    public void Stop()
    {
        try { _pipe.Stop(); } catch { }
    }

    // ====================================================================
    // 同步便捷方法
    // ====================================================================

    /// <summary>让服务重新加载 CurrentConfig.xml 并写入 EC.</summary>
    public bool ApplyConf(TimeSpan? timeout = null)
        => SendAndWaitForAck(Command.ApplyConf, timeout);

    public bool SetFullBlast(int enable, TimeSpan? timeout = null)
        => SendAndWaitForAck(Command.SetFullBlast, timeout, enable);

    public bool SetPerfMode(int mode, TimeSpan? timeout = null)
        => SendAndWaitForAck(Command.SetPerfMode, timeout, mode);

    public bool SetFanProf(int prof, TimeSpan? timeout = null)
        => SendAndWaitForAck(Command.SetFanProf, timeout, prof);

    public bool SetWinFnSwap(int enable, TimeSpan? timeout = null)
        => SendAndWaitForAck(Command.SetWinFnSwap, timeout, enable);

    public bool SetGpuMode(int mode, TimeSpan? timeout = null)
        => SendAndWaitForAck(Command.SetGpuMode, timeout ?? TimeSpan.FromSeconds(15), mode);

    /// <summary>Gets the current GPU MUX mode. Returns 0=Hybrid, 1=Discrete, -1=error.</summary>
    public int GetGpuMode(TimeSpan? timeout = null)
    {
        var resp = SendAndWait(Command.GetGpuMode, Response.GpuModeResult, timeout);
        if (resp == null || resp.Value == null || resp.Value.Length < 1) return -1;
        return UnboxInt(resp.Value[0]);
    }

    /// <summary>读取指定风扇对应传感器的温度 (°C). 失败返回 -1.</summary>
    public int GetTemp(int fanIndex, TimeSpan? timeout = null)
    {
        var resp = SendAndWait(Command.GetTemp, Response.Temp, timeout, fanIndex);
        if (resp == null || resp.Value == null || resp.Value.Length < 2) return -1;
        return UnboxInt(resp.Value[1]);
    }

    /// <summary>读取指定风扇当前 RPM. 失败返回 -1.</summary>
    public int GetFanRPM(int fanIndex, TimeSpan? timeout = null)
    {
        var resp = SendAndWait(Command.GetFanRPM, Response.FanRPM, timeout, fanIndex);
        if (resp == null || resp.Value == null || resp.Value.Length < 2) return -1;
        return UnboxInt(resp.Value[1]);
    }

    /// <summary>读取指定风扇目标速度 (%) . 失败返回 -1.</summary>
    public int GetFanSpeed(int fanIndex, TimeSpan? timeout = null)
    {
        var resp = SendAndWait(Command.GetFanSpeed, Response.FanSpeed, timeout, fanIndex);
        if (resp == null || resp.Value == null || resp.Value.Length < 2) return -1;
        return UnboxInt(resp.Value[1]);
    }

    public bool ReadECByte(byte reg, out byte value, TimeSpan? timeout = null)
    {
        value = 0;
        var resp = SendAndWait(Command.ReadECByte, Response.ReadResult, timeout, reg);
        if (resp == null || resp.Value == null || resp.Value.Length < 2) return false;
        try
        {
            value = Convert.ToByte(resp.Value[1]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool WriteECByte(byte reg, byte val, TimeSpan? timeout = null)
        => SendAndWaitForAck(Command.WriteECByte, timeout, reg, val);

    // ====================================================================
    // 内部机制
    // ====================================================================

    /// <summary>
    /// 发送命令并等待 Success/Error 回包.
    /// </summary>
    private bool SendAndWaitForAck(Command cmd, TimeSpan? timeout, params object[] args)
    {
        if (!_connected) return false;

        var sem = _cmdLocks.GetOrAdd(cmd, _ => new SemaphoreSlim(1, 1));
        if (!sem.Wait(timeout ?? DefaultTimeout)) return false;

        try
        {
            var pending = new PendingRequest(expectedResponses: new[] { Response.Success, Response.Error });
            _pending[cmd] = pending;

            try
            {
                _pipe.PushMessage(new ServiceCommand(cmd, args));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceIpcProxy] 发送 {cmd} 失败: {ex.Message}");
                _pending.TryRemove(cmd, out _);
                return false;
            }

            if (!pending.Wait(timeout ?? DefaultTimeout))
            {
                _pending.TryRemove(cmd, out _);
                return false;
            }

            return pending.Response?.Response == Response.Success;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// 发送命令并等待一个特定类别的响应 (比如 GetTemp -> Response.Temp).
    /// </summary>
    private ServiceResponse? SendAndWait(Command cmd, Response expected, TimeSpan? timeout, params object[] args)
    {
        if (!_connected) return null;

        var sem = _cmdLocks.GetOrAdd(cmd, _ => new SemaphoreSlim(1, 1));
        if (!sem.Wait(timeout ?? DefaultTimeout)) return null;

        try
        {
            var pending = new PendingRequest(expectedResponses: new[] { expected, Response.Error });
            _pending[cmd] = pending;

            try
            {
                _pipe.PushMessage(new ServiceCommand(cmd, args));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServiceIpcProxy] 发送 {cmd} 失败: {ex.Message}");
                _pending.TryRemove(cmd, out _);
                return null;
            }

            if (!pending.Wait(timeout ?? DefaultTimeout))
            {
                _pending.TryRemove(cmd, out _);
                return null;
            }

            return pending.Response?.Response == expected ? pending.Response : null;
        }
        finally
        {
            sem.Release();
        }
    }

    private void HandleServerMessage(ServiceResponse resp)
    {
        if (resp == null) return;

        // 1. 先把消息广播给订阅者 (比如 FanControlRunner 用来抓 Temp/FanRPM 做 TempUpdated 事件)
        try { ServerMessage?.Invoke(this, resp); } catch { /* 订阅者异常不影响 IPC */ }

        // 2. 再尝试匹配正在等待的请求
        // 由 Value 里的第一个元素标识命令 (Success/Error) 或根据响应类别做反查
        Command? matchedCmd = MapResponseToCommand(resp);
        if (matchedCmd.HasValue && _pending.TryRemove(matchedCmd.Value, out var pending))
        {
            pending.TrySetResult(resp);
        }
    }

    /// <summary>
    /// 把收到的响应反查到对应命令. Success/Error 的 Value[0] 携带了原命令 int.
    /// 其他响应 (Temp/FanRPM/...) 按类别唯一映射.
    /// </summary>
    private static Command? MapResponseToCommand(ServiceResponse resp)
    {
        switch (resp.Response)
        {
            case Response.Success:
            case Response.Error:
                if (resp.Value != null && resp.Value.Length >= 1)
                {
                    try { return (Command)UnboxInt(resp.Value[0]); }
                    catch { return null; }
                }
                return null;
            case Response.Temp:       return Command.GetTemp;
            case Response.FanRPM:     return Command.GetFanRPM;
            case Response.FanSpeed:   return Command.GetFanSpeed;
            case Response.ReadResult: return Command.ReadECByte;
            case Response.ServiceVer: return Command.GetServiceVer;
            case Response.FirmVer:    return Command.GetFirmVer;
            case Response.KeyLightBright: return Command.GetKeyLightBright;
            case Response.GpuModeResult:   return Command.GetGpuMode;
            default: return null;
        }
    }

    private static int UnboxInt(object o)
    {
        // MessagePack 反序列化回来的 int 可能是 int/long/byte/short, 统一收口
        return o switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            sbyte sb => sb,
            uint ui => (int)ui,
            ulong ul => (int)ul,
            ushort us => us,
            _ => Convert.ToInt32(o),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _pipe.Stop(); } catch { }
        try { _pipe.Dispose(); } catch { }
        foreach (var s in _cmdLocks.Values) { try { s.Dispose(); } catch { } }
        _cmdLocks.Clear();
    }

    // --------------------------------------------------------------------
    private sealed class PendingRequest
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private readonly Response[] _expected;
        public ServiceResponse? Response { get; private set; }

        public PendingRequest(Response[] expectedResponses)
        {
            _expected = expectedResponses;
        }

        public bool Wait(TimeSpan timeout) => _gate.Wait(timeout);

        public void TrySetResult(ServiceResponse resp)
        {
            if (Response != null) return;
            // 只接受预期的响应类别
            foreach (var e in _expected)
            {
                if (resp.Response == e)
                {
                    Response = resp;
                    _gate.Set();
                    return;
                }
            }
            // 非预期类别忽略 (留给其他订阅者处理)
        }

        public void TrySetCanceled()
        {
            if (Response == null) _gate.Set();
        }
    }
}
