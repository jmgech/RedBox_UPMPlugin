using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dispatche les actions depuis des threads background vers le Main Thread Unity.
///
/// POURQUOI : Unity n'est pas thread-safe. Toutes les opérations sur les GameObjects,
/// l'UI, les Events doivent impérativement s'exécuter sur le Main Thread.
/// La lecture série se fait sur un thread background (Task.Run), donc toute
/// action déclenchée par une donnée reçue doit passer par ce dispatcher.
///
/// USAGE : MainThreadDispatcher.Instance.Enqueue(() => { /* ton code Unity */ });
/// </summary>
public class MainThreadDispatcher : MonoBehaviour
{
    public static MainThreadDispatcher Instance { get; private set; }

    private readonly Queue<Action> _queue = new Queue<Action>();
    private readonly object _lock = new object();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Enfile une action à exécuter sur le Main Thread au prochain frame.
    /// Méthode thread-safe — peut être appelée depuis n'importe quel thread.
    /// </summary>
    public void Enqueue(Action action)
    {
        if (action == null) return;
        lock (_lock)
        {
            _queue.Enqueue(action);
        }
    }

    private void Update()
    {
        // Vide la queue à chaque frame sur le Main Thread
        while (true)
        {
            Action action;
            lock (_lock)
            {
                if (_queue.Count == 0) break;
                action = _queue.Dequeue();
            }
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MainThreadDispatcher] Exception : {ex}");
            }
        }
    }
}
