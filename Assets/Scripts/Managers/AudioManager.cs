using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Starter.Shooter
{
    [System.Serializable]
    public class SFXEntry
    {
        public string key;
        public AudioClip clip;
        [Range(0f, 1f)]   public float volume       = 1f;
        [Range(0f, 0.5f)] public float pitchVariance = 0f;
    }

    [System.Serializable]
    public class MusicEntry
    {
        public string key;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [System.Serializable]
    public class UIEntry
    {
        public string key;
        public AudioClip clip;
        [Range(0f, 1f)]   public float volume = 1f;
        [Range(0.5f, 2f)] public float pitch  = 1f;
    }

    public class AudioManager : Singleton<AudioManager>
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Mixer")]
        [SerializeField] private AudioMixer mixer;

        [Header("Pool Sizes")]
        [SerializeField, Min(4)] private int sfxPoolSize = 20;
        [SerializeField, Min(2)] private int uiPoolSize  = 8;

        [Header("Default Volumes (0–1)")]
        [SerializeField, Range(0f, 1f)] private float defaultMasterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float defaultMusicVolume  = 0.8f;
        [SerializeField, Range(0f, 1f)] private float defaultSfxVolume    = 1f;
        [SerializeField, Range(0f, 1f)] private float defaultUiVolume     = 1f;

        [Header("Music")]
        [SerializeField] private float defaultFadeDuration = 1.5f;

        [Header("SFX Library")]
        [SerializeField] private SFXEntry[] sfxLibrary;

        [Header("Music Library")]
        [SerializeField] private MusicEntry[] musicLibrary;

        [Header("UI Library")]
        [SerializeField] private UIEntry[] uiLibrary;

        // ── Mixer param names ──────────────────────────────────────────────────
        public const string P_MASTER = "MasterVolume";
        public const string P_MUSIC  = "MusicVolume";
        public const string P_SFX    = "SFXVolume";
        public const string P_UI     = "UIVolume";

        // ── PlayerPrefs keys ───────────────────────────────────────────────────
        const string K_MASTER = "vol_master";
        const string K_MUSIC  = "vol_music";
        const string K_SFX    = "vol_sfx";
        const string K_UI     = "vol_ui";

        // ── Volume state (linear 0–1) ──────────────────────────────────────────
        float masterVolume, musicVolume, sfxVolume, uiVolume;

        public float MasterVolume => masterVolume;
        public float MusicVolume  => musicVolume;
        public float SfxVolume    => sfxVolume;
        public float UiVolume     => uiVolume;

        // ── Mixer groups ───────────────────────────────────────────────────────
        AudioMixerGroup musicGroup, sfxGroup, uiGroup;

        // ── Music ──────────────────────────────────────────────────────────────
        AudioSource musicA, musicB;
        bool        usingMusicA;
        Coroutine   musicRoutine;

        AudioSource ActiveMusic => usingMusicA ? musicA : musicB;

        // ── Pools ──────────────────────────────────────────────────────────────
        readonly List<AudioSource> sfxPool = new();
        readonly List<AudioSource> uiPool  = new();

        // ── Library lookups ────────────────────────────────────────────────────
        readonly Dictionary<string, SFXEntry>   sfxDict   = new();
        readonly Dictionary<string, MusicEntry> musicDict = new();
        readonly Dictionary<string, UIEntry>    uiDict    = new();

        // ── Lifecycle ──────────────────────────────────────────────────────────
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return; // duplicate — base already destroyed us

            DontDestroyOnLoad(gameObject);
            BuildLibraries();
            ResolveGroups();
            LoadAndApplyVolumes();
            SetupMusicSources();
            BuildPool(sfxPool, sfxPoolSize, "SFX", sfxGroup, spatialBlend: 1f);
            BuildPool(uiPool,  uiPoolSize,  "UI",  uiGroup,  spatialBlend: 0f);
        }

        // ── Libraries ─────────────────────────────────────────────────────────
        void BuildLibraries()
        {
            if (sfxLibrary != null)
                foreach (var e in sfxLibrary)
                    if (!string.IsNullOrEmpty(e.key)) sfxDict[e.key] = e;

            if (musicLibrary != null)
                foreach (var e in musicLibrary)
                    if (!string.IsNullOrEmpty(e.key)) musicDict[e.key] = e;

            if (uiLibrary != null)
                foreach (var e in uiLibrary)
                    if (!string.IsNullOrEmpty(e.key)) uiDict[e.key] = e;
        }

        void ResolveGroups()
        {
            if (mixer == null) { Debug.LogError("[AudioManager] Mixer not assigned."); return; }
            musicGroup = FindGroup("Music");
            sfxGroup   = FindGroup("SFX");
            uiGroup    = FindGroup("UI");
        }

        AudioMixerGroup FindGroup(string groupName)
        {
            var results = mixer.FindMatchingGroups(groupName);
            if (results == null || results.Length == 0)
                Debug.LogError($"[AudioManager] Mixer group '{groupName}' not found.");
            return results != null && results.Length > 0 ? results[0] : null;
        }

        // ── Volume ─────────────────────────────────────────────────────────────
        void LoadAndApplyVolumes()
        {
            masterVolume = PlayerPrefs.GetFloat(K_MASTER, defaultMasterVolume);
            musicVolume  = PlayerPrefs.GetFloat(K_MUSIC,  defaultMusicVolume);
            sfxVolume    = PlayerPrefs.GetFloat(K_SFX,    defaultSfxVolume);
            uiVolume     = PlayerPrefs.GetFloat(K_UI,     defaultUiVolume);
            SetMixerDb(P_MASTER, masterVolume);
            SetMixerDb(P_MUSIC,  musicVolume);
            SetMixerDb(P_SFX,    sfxVolume);
            SetMixerDb(P_UI,     uiVolume);
        }

        void SetMixerDb(string param, float linear) =>
            mixer?.SetFloat(param, LinearToDb(linear));

        void SaveVolumes()
        {
            PlayerPrefs.SetFloat(K_MASTER, masterVolume);
            PlayerPrefs.SetFloat(K_MUSIC,  musicVolume);
            PlayerPrefs.SetFloat(K_SFX,    sfxVolume);
            PlayerPrefs.SetFloat(K_UI,     uiVolume);
            PlayerPrefs.Save();
        }

        public void SetMasterVolume(float v) { masterVolume = Mathf.Clamp01(v); SetMixerDb(P_MASTER, v); SaveVolumes(); }
        public void SetMusicVolume(float v)  { musicVolume  = Mathf.Clamp01(v); SetMixerDb(P_MUSIC,  v); SaveVolumes(); }
        public void SetSfxVolume(float v)    { sfxVolume    = Mathf.Clamp01(v); SetMixerDb(P_SFX,    v); SaveVolumes(); }
        public void SetUiVolume(float v)     { uiVolume     = Mathf.Clamp01(v); SetMixerDb(P_UI,     v); SaveVolumes(); }

        public bool TryGetMixerVolume(string param, out float linear)
        {
            if (mixer != null && mixer.GetFloat(param, out float db))
            {
                linear = DbToLinear(db);
                return true;
            }
            linear = 1f;
            return false;
        }

        static float LinearToDb(float linear) =>
            linear <= 0f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(linear));

        static float DbToLinear(float db) =>
            db <= -80f ? 0f : Mathf.Pow(10f, db / 20f);

        // ── Music — key overloads ──────────────────────────────────────────────
        public void PlayMusic(string key, bool loop = true, float fadeDuration = -1f)
        {
            if (!musicDict.TryGetValue(key, out var entry))
            { Debug.LogWarning($"[AudioManager] Music key '{key}' not found."); return; }
            PlayMusicEntry(entry, loop, fadeDuration, crossfade: false);
        }

        public void CrossfadeMusic(string key, bool loop = true, float fadeDuration = -1f)
        {
            if (!musicDict.TryGetValue(key, out var entry))
            { Debug.LogWarning($"[AudioManager] Music key '{key}' not found."); return; }
            PlayMusicEntry(entry, loop, fadeDuration, crossfade: true);
        }

        // ── Music — clip overloads ─────────────────────────────────────────────
        public void PlayMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f)
        {
            if (clip == null) return;
            if (fadeDuration < 0f) fadeDuration = defaultFadeDuration;
            StopMusicRoutine();
            var src  = ActiveMusic;
            src.clip = clip; src.loop = loop; src.volume = 0f;
            src.Play();
            musicRoutine = StartCoroutine(FadeSource(src, 0f, 1f, fadeDuration, () => musicRoutine = null));
        }

        public void CrossfadeMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f)
        {
            if (clip == null) return;
            if (fadeDuration < 0f) fadeDuration = defaultFadeDuration;
            StopMusicRoutine();
            var outgoing = ActiveMusic;
            usingMusicA  = !usingMusicA;
            var incoming = ActiveMusic;
            incoming.clip = clip; incoming.loop = loop; incoming.volume = 0f;
            incoming.Play();
            musicRoutine = StartCoroutine(CrossfadeRoutine(outgoing, incoming, fadeDuration));
        }

        void PlayMusicEntry(MusicEntry entry, bool loop, float fadeDuration, bool crossfade)
        {
            if (fadeDuration < 0f) fadeDuration = defaultFadeDuration;
            StopMusicRoutine();
            var outgoing = ActiveMusic;
            if (crossfade) usingMusicA = !usingMusicA;
            var incoming = ActiveMusic;
            incoming.clip = entry.clip; incoming.loop = loop; incoming.volume = 0f;
            incoming.Play();
            float targetVol = entry.volume;
            if (crossfade)
                musicRoutine = StartCoroutine(CrossfadeRoutine(outgoing, incoming, fadeDuration, targetVol));
            else
                musicRoutine = StartCoroutine(FadeSource(incoming, 0f, targetVol, fadeDuration, () => musicRoutine = null));
        }

        // ── Music — control ────────────────────────────────────────────────────
        public void StopMusic(float fadeDuration = -1f)
        {
            if (fadeDuration < 0f) fadeDuration = defaultFadeDuration;
            StopMusicRoutine();
            var src = ActiveMusic;
            if (!src.isPlaying) return;
            musicRoutine = StartCoroutine(FadeSource(src, src.volume, 0f, fadeDuration, () =>
            {
                src.Stop();
                musicRoutine = null;
            }));
        }

        public void PauseMusic()  => ActiveMusic.Pause();
        public void ResumeMusic() => ActiveMusic.UnPause();
        public bool IsMusicPlaying => ActiveMusic.isPlaying;

        void StopMusicRoutine()
        {
            if (musicRoutine != null) { StopCoroutine(musicRoutine); musicRoutine = null; }
        }

        /// <summary>Fade out current music, hold silence, then fade in the new clip.</summary>
        public void TransitionMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f, float silenceDuration = 0f)
        {
            if (clip == null) { StopMusic(fadeDuration); return; }
            if (fadeDuration < 0f) fadeDuration = defaultFadeDuration;
            StopMusicRoutine();
            musicRoutine = StartCoroutine(TransitionRoutine(clip, loop, fadeDuration, silenceDuration));
        }

        IEnumerator TransitionRoutine(AudioClip clip, bool loop, float fadeDuration, float silenceDuration)
        {
            // Fade out the currently playing source
            var outgoing = ActiveMusic;
            if (outgoing.isPlaying)
            {
                float startVol = outgoing.volume;
                float elapsed  = 0f;
                while (elapsed < fadeDuration)
                {
                    elapsed        += Time.unscaledDeltaTime;
                    outgoing.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(elapsed / fadeDuration));
                    yield return null;
                }
                outgoing.Stop();
                outgoing.volume = 0f;
            }

            // Silence gap
            if (silenceDuration > 0f)
                yield return new WaitForSecondsRealtime(silenceDuration);

            // Fade in on the other source
            usingMusicA = !usingMusicA;
            var incoming = ActiveMusic;
            incoming.clip   = clip;
            incoming.loop   = loop;
            incoming.volume = 0f;
            incoming.Play();

            float vol     = 1f;
            float elapsed2 = 0f;
            while (elapsed2 < fadeDuration)
            {
                elapsed2        += Time.unscaledDeltaTime;
                incoming.volume  = Mathf.Lerp(0f, vol, Mathf.Clamp01(elapsed2 / fadeDuration));
                yield return null;
            }
            incoming.volume = vol;
            musicRoutine    = null;
        }

        // ── SFX — clip overloads ───────────────────────────────────────────────
        public AudioSource PlaySFX(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariance = 0f)
        {
            if (clip == null) return null;
            var src = ClaimSource(sfxPool);
            src.transform.position = position;
            src.clip = clip; src.loop = false; src.volume = volume;
            src.pitch = PitchedBy(pitchVariance); src.spatialBlend = 1f;
            src.Play();
            return src;
        }

        public AudioSource PlaySFX2D(AudioClip clip, float volume = 1f, float pitchVariance = 0f)
        {
            if (clip == null) return null;
            var src = ClaimSource(sfxPool);
            src.clip = clip; src.loop = false; src.volume = volume;
            src.pitch = PitchedBy(pitchVariance); src.spatialBlend = 0f;
            src.Play();
            return src;
        }

        public AudioSource PlaySFXRandom(AudioClip[] clips, Vector3 position, float volume = 1f, float pitchVariance = 0.05f)
        {
            if (clips == null || clips.Length == 0) return null;
            return PlaySFX(clips[Random.Range(0, clips.Length)], position, volume, pitchVariance);
        }

        public AudioSource PlaySFX2DRandom(AudioClip[] clips, float volume = 1f, float pitchVariance = 0.05f)
        {
            if (clips == null || clips.Length == 0) return null;
            return PlaySFX2D(clips[Random.Range(0, clips.Length)], volume, pitchVariance);
        }

        public AudioSource PlaySFXLooping(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return null;
            var src = ClaimSource(sfxPool);
            src.transform.position = position;
            src.clip = clip; src.loop = true; src.volume = volume;
            src.pitch = 1f; src.spatialBlend = 1f;
            src.Play();
            return src;
        }

        // ── SFX — key overloads ────────────────────────────────────────────────
        /// <summary>Returns true if the given key is registered in the SFX library.</summary>
        public bool HasSFX(string key) => sfxDict.ContainsKey(key);

        public AudioSource PlaySFX(string key, Vector3 position)
        {
            if (!sfxDict.TryGetValue(key, out var e))
            { Debug.LogWarning($"[AudioManager] SFX key '{key}' not found."); return null; }
            return PlaySFX(e.clip, position, e.volume, e.pitchVariance);
        }

        public AudioSource PlaySFX2D(string key)
        {
            if (!sfxDict.TryGetValue(key, out var e))
            { Debug.LogWarning($"[AudioManager] SFX key '{key}' not found."); return null; }
            return PlaySFX2D(e.clip, e.volume, e.pitchVariance);
        }

        public AudioSource PlaySFXLooping(string key, Vector3 position)
        {
            if (!sfxDict.TryGetValue(key, out var e))
            { Debug.LogWarning($"[AudioManager] SFX key '{key}' not found."); return null; }
            return PlaySFXLooping(e.clip, position, e.volume);
        }

        public void StopAllSFX()
        {
            foreach (var s in sfxPool) { s.Stop(); s.clip = null; }
        }

        // ── UI — clip overloads ────────────────────────────────────────────────
        public AudioSource PlayUI(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return null;
            var src = ClaimSource(uiPool);
            src.clip = clip; src.loop = false; src.volume = volume;
            src.pitch = pitch; src.spatialBlend = 0f;
            src.Play();
            return src;
        }

        // ── UI — key overloads ─────────────────────────────────────────────────
        public AudioSource PlayUI(string key)
        {
            if (!uiDict.TryGetValue(key, out var e))
            { Debug.LogWarning($"[AudioManager] UI key '{key}' not found."); return null; }
            return PlayUI(e.clip, e.volume, e.pitch);
        }

        // ── Pause ──────────────────────────────────────────────────────────────
        public void OnGamePause()
        {
            PauseMusic();
            foreach (var s in sfxPool) if (s.isPlaying) s.Pause();
        }

        public void OnGameResume()
        {
            ResumeMusic();
            foreach (var s in sfxPool) s.UnPause();
        }

        // ── Internals ──────────────────────────────────────────────────────────
        void SetupMusicSources()
        {
            var go = new GameObject("Music");
            go.transform.SetParent(transform);
            musicA = MakeSource(go, "MusicA", musicGroup, 0f);
            musicB = MakeSource(go, "MusicB", musicGroup, 0f);
        }

        void BuildPool(List<AudioSource> pool, int count, string label, AudioMixerGroup group, float spatialBlend)
        {
            var go = new GameObject(label + "Pool");
            go.transform.SetParent(transform);
            for (int i = 0; i < count; i++)
                pool.Add(MakeSource(go, label + i, group, spatialBlend));
        }

        AudioSource MakeSource(GameObject parent, string label, AudioMixerGroup group, float spatialBlend)
        {
            var go  = new GameObject(label);
            go.transform.SetParent(parent.transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake           = false;
            src.spatialBlend          = spatialBlend;
            src.outputAudioMixerGroup = group;
            return src;
        }

        AudioSource ClaimSource(List<AudioSource> pool)
        {
            foreach (var s in pool)
                if (!s.isPlaying) return s;

            // Pool exhausted — steal the source closest to finishing
            AudioSource steal = pool[0];
            float lowestRemaining = float.MaxValue;
            foreach (var s in pool)
            {
                if (s.clip == null) return s;
                float remaining = s.clip.length - s.time;
                if (remaining < lowestRemaining) { lowestRemaining = remaining; steal = s; }
            }
            steal.Stop();
            return steal;
        }

        IEnumerator CrossfadeRoutine(AudioSource outgoing, AudioSource incoming, float duration, float targetVol = 1f)
        {
            float startVol = outgoing.volume;
            float elapsed  = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t  = Mathf.Clamp01(elapsed / duration);
                outgoing.volume = Mathf.Lerp(startVol, 0f,        t);
                incoming.volume = Mathf.Lerp(0f,       targetVol, t);
                yield return null;
            }
            outgoing.Stop();
            outgoing.volume = 0f;
            incoming.volume = targetVol;
            musicRoutine    = null;
        }

        IEnumerator FadeSource(AudioSource src, float from, float to, float duration, System.Action onDone = null)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed    += Time.unscaledDeltaTime;
                src.volume  = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            src.volume = to;
            onDone?.Invoke();
        }

        static float PitchedBy(float variance) =>
            variance > 0f ? 1f + Random.Range(-variance, variance) : 1f;

        // ── Multiplayer ────────────────────────────────────────────────────────
        // Forwards to NetworkedAudioCaller (NetworkBehaviour). Requires state or
        // input authority as noted — silently ignored if the caller has neither.

        /// <summary>Play a 3D SFX at a world position on all clients. State authority only.</summary>
        public void PlaySFXForAll(string key, Vector3 position)       => NetworkedAudioCaller.Instance?.PlaySFXForAll(key, position);

        /// <summary>Play a 2D SFX on all clients. State authority only.</summary>
        public void PlaySFX2DForAll(string key)                        => NetworkedAudioCaller.Instance?.PlaySFX2DForAll(key);

        /// <summary>Start music on all clients. State authority only.</summary>
        public void PlayMusicForAll(string key)                        => NetworkedAudioCaller.Instance?.PlayMusicForAll(key);

        /// <summary>Crossfade to new music on all clients. State authority only.</summary>
        public void CrossfadeMusicForAll(string key)                   => NetworkedAudioCaller.Instance?.CrossfadeMusicForAll(key);

        /// <summary>Stop music on all clients. State authority only.</summary>
        public void StopMusicForAll()                                  => NetworkedAudioCaller.Instance?.StopMusicForAll();

        /// <summary>Play a 3D SFX on all clients, triggered by the local player. Input authority only.</summary>
        public void PlaySFXFromPlayer(string key, Vector3 position)   => NetworkedAudioCaller.Instance?.PlaySFXFromPlayer(key, position);

        /// <summary>Play a 2D SFX on all clients, triggered by the local player. Input authority only.</summary>
        public void PlaySFX2DFromPlayer(string key)                    => NetworkedAudioCaller.Instance?.PlaySFX2DFromPlayer(key);
    }
}
