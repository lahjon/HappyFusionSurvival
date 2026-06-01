using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _applicationIsQuitting;


    public static T Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            // Don't create new objects during teardown (editor or build).
            // In the editor, also guard against creation after play mode ends.
            if (_applicationIsQuitting || !Application.isPlaying)
                return null;

            _instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (_instance == null)
            {
                var go = new GameObject(typeof(T).Name);
                _instance = go.AddComponent<T>();
            }

            return _instance;
        }
    }

    protected virtual void Awake()
    {
        _applicationIsQuitting = false; // clear stale flag from previous session
        if (_instance == null)
        {
            _instance = this as T;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _applicationIsQuitting = true;
    }
}