using System;
using System.Reflection;
using UnityEngine;

[AddComponentMenu("REDbox/Arduino Bridge")]
public class ArduinoBridge : MonoBehaviour
{
    public static ArduinoBridge Instance { get; private set; }

    [Header("Hardware")]
    public HardwareSettings settings;

    public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public static event Action<ConnectionState> OnConnectionStateChanged;
    public static event Action<string> OnRawDataReceived;
    public static event Action<bool> OnDeviceReadyChanged;

    public string LastConnectionError { get; private set; } = "-";
    public string ActivePort { get; private set; } = "-";
    public bool IsDeviceReady => State == ConnectionState.Connected;

    private object _serialPort;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    [ContextMenu("Connect")]
    public void Connect()
    {
        if (settings == null)
        {
            LastConnectionError = "HardwareSettings missing";
            Debug.LogError("[ArduinoBridge] HardwareSettings missing.");
            return;
        }

        string[] ports = GetAvailablePorts();
        if (ports.Length == 0)
        {
            LastConnectionError = "No serial ports found";
            SetState(ConnectionState.Disconnected);
            return;
        }

        string target = ResolvePort(ports);
        if (!OpenPort(target, settings.baudRate))
        {
            SetState(ConnectionState.Disconnected);
            return;
        }

        ActivePort = target;
        SetState(ConnectionState.Connected);
        OnDeviceReadyChanged?.Invoke(true);
    }

    [ContextMenu("Disconnect")]
    public void Disconnect()
    {
        ClosePort();
        ActivePort = "-";
        SetState(ConnectionState.Disconnected);
        OnDeviceReadyChanged?.Invoke(false);
    }

    public string[] GetAvailablePorts()
    {
        Type serialType = Type.GetType("System.IO.Ports.SerialPort, System.IO.Ports")
                          ?? Type.GetType("System.IO.Ports.SerialPort");
        if (serialType == null) return Array.Empty<string>();

        try
        {
            MethodInfo method = serialType.GetMethod("GetPortNames", BindingFlags.Public | BindingFlags.Static);
            return (string[])(method?.Invoke(null, null) ?? Array.Empty<string>());
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void ActivateScanner() { }
    public void DeactivateScanner() { }
    public void SimulateScan(string cardId)
    {
        OnRawDataReceived?.Invoke($"SIM:{cardId}");
    }

    private string ResolvePort(string[] ports)
    {
        if (settings != null && !string.IsNullOrWhiteSpace(settings.serialPort))
        {
            for (int i = 0; i < ports.Length; i++)
            {
                if (string.Equals(ports[i], settings.serialPort, StringComparison.OrdinalIgnoreCase))
                    return ports[i];
            }
        }
        return ports[0];
    }

    private bool OpenPort(string portName, int baudRate)
    {
        Type serialType = Type.GetType("System.IO.Ports.SerialPort, System.IO.Ports")
                          ?? Type.GetType("System.IO.Ports.SerialPort");
        if (serialType == null)
        {
            LastConnectionError = "System.IO.Ports unavailable";
            return false;
        }

        try
        {
            _serialPort = Activator.CreateInstance(serialType, portName, baudRate);
            serialType.GetMethod("Open", BindingFlags.Public | BindingFlags.Instance)?.Invoke(_serialPort, null);
            return true;
        }
        catch (Exception ex)
        {
            LastConnectionError = ex.Message;
            _serialPort = null;
            return false;
        }
    }

    private void ClosePort()
    {
        if (_serialPort == null) return;
        Type t = _serialPort.GetType();
        try { t.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance)?.Invoke(_serialPort, null); } catch { }
        try { t.GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)?.Invoke(_serialPort, null); } catch { }
        _serialPort = null;
    }

    private void SetState(ConnectionState newState)
    {
        if (State == newState) return;
        State = newState;
        OnConnectionStateChanged?.Invoke(newState);
    }
}
