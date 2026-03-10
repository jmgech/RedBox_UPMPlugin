// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  FICHIER DÉPRÉCIÉ — NE PAS UTILISER                                     ║
// ║                                                                          ║
// ║  Ce fichier est conservé à titre d'archive uniquement.                  ║
// ║  La classe SerialHandler a été remplacée par ArduinoBridge.cs           ║
// ║  qui offre :                                                             ║
// ║    ✓ Thread safety via MainThreadDispatcher                              ║
// ║    ✓ Reconnexion automatique                                             ║
// ║    ✓ Auto-détection de port (Windows / Mac / Linux)                     ║
// ║    ✓ Configuration via HardwareSettings ScriptableObject                 ║
// ║    ✓ Support Bluetooth                                                   ║
// ║    ✓ SimulateScan() pour tests sans hardware                             ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// DÉPRÉCIÉ. Utiliser <see cref="ArduinoBridge"/> à la place.
/// Conservé pour référence historique — ne pas instancier.
/// </summary>
[Obsolete("SerialHandler est déprécié. Utilisez ArduinoBridge.", error: false)]
public class SerialHandler : IDisposable
{
    private object serialPort;

#pragma warning disable CS0067
    public event Action<string> OnDataReceived;
    public event Action<bool>   OnReadyState;
#pragma warning restore CS0067

    public UnityEvent OnDeviceConnected    = new UnityEvent();
    public UnityEvent OnDeviceDisconnected = new UnityEvent();

    [Obsolete("Ne pas instancier. Voir ArduinoBridge.")]
    public SerialHandler()
    {
        Debug.LogError("[SerialHandler] Cette classe est dépréciée. Utilisez ArduinoBridge.");
    }

    public void Dispose()
    {
        serialPort = null;
    }
}
