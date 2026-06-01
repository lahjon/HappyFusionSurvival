using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
    /// <summary>
    /// Scene-resident NetworkBehaviour that lets any peer trigger audio on all connected clients.
    /// Sits on the same GameObject as AudioManager. Routes every RPC straight to the local
    /// AudioManager.Instance — no audio state is replicated, only the play command.
    ///
    /// Two authority modes:
    ///   StateAuthority → All  : world events (explosions, ambient stings, phase music changes).
    ///   InputAuthority → All  : player-triggered sounds that must be heard by everyone.
    /// </summary>
    public class NetworkedAudioCaller : NetworkBehaviour
    {
        public static NetworkedAudioCaller Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public override void Spawned()
        {
            Instance = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        // ── State-authority calls (world / game events) ────────────────────────

        /// <summary>Play a 3D SFX at a world position for all clients. Call from state authority.</summary>
        public void PlaySFXForAll(string key, Vector3 position)
        {
            if (HasStateAuthority) RpcPlaySFX(key, position);
        }

        /// <summary>Play a 2D (non-spatial) SFX for all clients. Call from state authority.</summary>
        public void PlaySFX2DForAll(string key)
        {
            if (HasStateAuthority) RpcPlaySFX2D(key);
        }

        /// <summary>Start music (fade in) for all clients. Call from state authority.</summary>
        public void PlayMusicForAll(string key)
        {
            if (HasStateAuthority) RpcPlayMusic(key);
        }

        /// <summary>Crossfade to new music for all clients. Call from state authority.</summary>
        public void CrossfadeMusicForAll(string key)
        {
            if (HasStateAuthority) RpcCrossfadeMusic(key);
        }

        /// <summary>Stop music for all clients. Call from state authority.</summary>
        public void StopMusicForAll()
        {
            if (HasStateAuthority) RpcStopMusic();
        }

        // ── Input-authority calls (player-triggered, heard by everyone) ────────

        /// <summary>
        /// Any player can call this to make everyone hear a 3D SFX.
        /// Use for sounds the local player triggers that all peers should hear
        /// (e.g. using an item, activating a trap).
        /// </summary>
        public void PlaySFXFromPlayer(string key, Vector3 position)
        {
            if (Object.HasInputAuthority) RpcPlaySFXFromPlayer(key, position);
        }

        /// <summary>Any player can call this to make everyone hear a 2D SFX.</summary>
        public void PlaySFX2DFromPlayer(string key)
        {
            if (Object.HasInputAuthority) RpcPlaySFX2DFromPlayer(key);
        }

        // ── RPCs — StateAuthority → All ────────────────────────────────────────

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RpcPlaySFX(string key, Vector3 position)
        {
            AudioManager.Instance?.PlaySFX(key, position);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RpcPlaySFX2D(string key)
        {
            AudioManager.Instance?.PlaySFX2D(key);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RpcPlayMusic(string key)
        {
            AudioManager.Instance?.PlayMusic(key);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RpcCrossfadeMusic(string key)
        {
            AudioManager.Instance?.CrossfadeMusic(key);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RpcStopMusic()
        {
            AudioManager.Instance?.StopMusic();
        }

        // ── RPCs — InputAuthority → All ────────────────────────────────────────

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        void RpcPlaySFXFromPlayer(string key, Vector3 position)
        {
            AudioManager.Instance?.PlaySFX(key, position);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        void RpcPlaySFX2DFromPlayer(string key)
        {
            AudioManager.Instance?.PlaySFX2D(key);
        }
    }
}
