using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

internal static class ArduinoBridgeBleNative
{
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    private const string NativeLibFileName = "libRKRedboxBleBridge.dylib";
    private const int RtldNow = 2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InitializeDelegate(string gameObjectName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool StartScanAndConnectDelegate(string endpoint);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DisconnectDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetCallbacksDelegate(IntPtr onConnected, IntPtr onData, IntPtr onError);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeStringCallback(IntPtr messagePtr);

    [DllImport("libdl")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("libdl")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    [DllImport("libdl")]
    private static extern int dlclose(IntPtr handle);

    [DllImport("libdl")]
    private static extern IntPtr dlerror();

    private static IntPtr _nativeHandle = IntPtr.Zero;
    private static InitializeDelegate _initialize;
    private static StartScanAndConnectDelegate _startScanAndConnect;
    private static DisconnectDelegate _disconnect;
    private static SetCallbacksDelegate _setCallbacks;
    private static string _targetGameObjectName;
    private static SynchronizationContext _unityContext;
    private static volatile bool _callbacksEnabled;

    private static readonly NativeStringCallback _onConnectedCallback = OnConnectedNative;
    private static readonly NativeStringCallback _onDataCallback = OnDataNative;
    private static readonly NativeStringCallback _onErrorCallback = OnErrorNative;

    private static bool EnsureLoaded(out string error)
    {
        error = null;
        if (_nativeHandle != IntPtr.Zero
            && _initialize != null
            && _startScanAndConnect != null
            && _disconnect != null)
        {
            return true;
        }

        List<string> loadErrors = new List<string>();
        foreach (string candidate in GetCandidatePaths())
        {
            if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
            {
                loadErrors.Add($"missing file: {candidate}");
                continue;
            }

            IntPtr handle = dlopen(candidate, RtldNow);
            if (handle == IntPtr.Zero)
            {
                loadErrors.Add($"dlopen({candidate}) => {ReadDlError()}");
                continue;
            }

            IntPtr initPtr = dlsym(handle, "RKRedboxBle_Initialize");
            IntPtr startPtr = dlsym(handle, "RKRedboxBle_StartScanAndConnect");
            IntPtr stopPtr = dlsym(handle, "RKRedboxBle_Disconnect");
            IntPtr setCallbacksPtr = dlsym(handle, "RKRedboxBle_SetCallbacks");

            if (initPtr == IntPtr.Zero || startPtr == IntPtr.Zero || stopPtr == IntPtr.Zero || setCallbacksPtr == IntPtr.Zero)
            {
                loadErrors.Add($"dlsym missing symbols in {candidate}: {ReadDlError()}");
                dlclose(handle);
                continue;
            }

            _nativeHandle = handle;
            _initialize = Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(initPtr);
            _startScanAndConnect = Marshal.GetDelegateForFunctionPointer<StartScanAndConnectDelegate>(startPtr);
            _disconnect = Marshal.GetDelegateForFunctionPointer<DisconnectDelegate>(stopPtr);
            _setCallbacks = Marshal.GetDelegateForFunctionPointer<SetCallbacksDelegate>(setCallbacksPtr);
            return true;
        }

        error = loadErrors.Count == 0 ? "Unknown dlopen error" : string.Join(" | ", loadErrors);
        return false;
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Emit(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return yielded.Add(value);
        }

        yield return NativeLibFileName;

        string dataPath = Application.dataPath;
        if (!string.IsNullOrWhiteSpace(dataPath))
        {
            string projectRoot = Path.GetFullPath(Path.Combine(dataPath, ".."));

            string embeddedPackage = Path.Combine(projectRoot, "Packages", "com.redbox.unity", "Runtime", "Plugins", "macOS", NativeLibFileName);
            if (Emit(embeddedPackage))
                yield return embeddedPackage;

            string packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(packageCacheRoot))
            {
                foreach (string dir in Directory.GetDirectories(packageCacheRoot, "com.redbox.unity*"))
                {
                    string fromCache = Path.Combine(dir, "Runtime", "Plugins", "macOS", NativeLibFileName);
                    if (Emit(fromCache))
                        yield return fromCache;
                }
            }
        }
    }

    private static string ReadDlError()
    {
        IntPtr ptr = dlerror();
        return ptr == IntPtr.Zero ? "<no dlerror>" : Marshal.PtrToStringAnsi(ptr);
    }
#else
    private static bool EnsureLoaded(out string error)
    {
        error = "BLE native bridge not supported on this platform";
        return false;
    }
#endif

    public static bool Start(string gameObjectName, string endpoint)
    {
        try
        {
            if (!EnsureLoaded(out string loadError))
            {
                Debug.LogWarning($"[ArduinoBridge] BLE natif indisponible: {loadError}");
                return false;
            }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            _unityContext = SynchronizationContext.Current;
            _targetGameObjectName = gameObjectName;
            _callbacksEnabled = true;
            _setCallbacks?.Invoke(
                Marshal.GetFunctionPointerForDelegate(_onConnectedCallback),
                Marshal.GetFunctionPointerForDelegate(_onDataCallback),
                Marshal.GetFunctionPointerForDelegate(_onErrorCallback));

            _initialize(gameObjectName);
            return _startScanAndConnect(endpoint);
#else
            return false;
#endif
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArduinoBridge] BLE natif indisponible: {ex}");
            return false;
        }
    }

    public static void Stop()
    {
        try
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            _callbacksEnabled = false;
            _targetGameObjectName = null;
            _setCallbacks?.Invoke(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _disconnect?.Invoke();
#endif
        }
        catch
        {
            // Best-effort disconnect.
        }
    }

    private static void OnConnectedNative(IntPtr messagePtr)
    {
        DispatchToGameObject("OnBleConnected", PtrToManagedString(messagePtr));
    }

    private static void OnDataNative(IntPtr messagePtr)
    {
        DispatchToGameObject("OnBleData", PtrToManagedString(messagePtr));
    }

    private static void OnErrorNative(IntPtr messagePtr)
    {
        DispatchToGameObject("OnBleError", PtrToManagedString(messagePtr));
    }

    private static string PtrToManagedString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return string.Empty;
        return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
    }

    private static void DispatchToGameObject(string methodName, string payload)
    {
        if (!_callbacksEnabled) return;

        void RunDispatch(object _)
        {
            if (!_callbacksEnabled) return;
            if (!Application.isPlaying) return;
            if (string.IsNullOrWhiteSpace(_targetGameObjectName)) return;

            GameObject target = GameObject.Find(_targetGameObjectName);
            if (target == null) return;

            target.SendMessage(methodName, payload, SendMessageOptions.DontRequireReceiver);
        }

        if (_unityContext != null)
        {
            _unityContext.Post(RunDispatch, null);
            return;
        }

        RunDispatch(null);
    }
}
