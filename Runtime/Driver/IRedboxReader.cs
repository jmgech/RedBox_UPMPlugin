/// <summary>
/// High-level abstraction for REDbox reader backends.
/// Implementations can expose USB serial or external Bluetooth transports
/// while sharing the same runtime-facing contract.
/// </summary>
public interface IRedboxReader
{
    bool IsConnected { get; }
    ReaderSource Source { get; }
}

/// <summary>
/// Physical transport origin for the active reader connection.
/// </summary>
public enum ReaderSource
{
    ExternalUsb,
    ExternalBluetooth
}
