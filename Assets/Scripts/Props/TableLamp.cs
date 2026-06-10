using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
    /// <summary>
    /// A simple interactable table lamp that reacts to physics and can be toggled on/off.
    /// Inherits from PhysicsBody to gain combat knockback, water buoyancy, and player-push behavior.
    /// </summary>
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(InteractionPrompt))]
    public sealed class TableLamp : PhysicsBody, IInteractable
    {
        [Header("Lamp Settings")]
        [Tooltip("Light component to toggle.")]
        [SerializeField] private Light _light;

        [Tooltip("Renderer to apply emission changes to.")]
        [SerializeField] private Renderer _renderer;

        [Tooltip("Material index on the renderer for emission.")]
        [SerializeField] private int _materialIndex = 0;

        [Header("Style")]
        [Tooltip("Color applied to _EmissionColor when the lamp is on.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color _emissiveOnColor = Color.white;

        [Tooltip("Color applied to _EmissionColor when the lamp is off.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color _emissiveOffColor = Color.black;

        [Tooltip("Distance at which the player can interact with this lamp.")]
        [SerializeField] private float _interactRange = 2.5f;

        [Tooltip("Display name for the interaction prompt.")]
        [SerializeField] private string _displayName = "Table Lamp";

        [Networked, OnChangedRender(nameof(OnIsOnChanged))]
        public NetworkBool IsOn { get; private set; }

        public override void Spawned()
        {
            CachePhysicsRefs();

            if (Object.HasStateAuthority)
            {
                // Host simulates physics
                bool startKinematic = _startMode == StartPhysicsMode.KinematicUntilHit;
                _rb.isKinematic = startKinematic;
                _rb.interpolation = startKinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            }
            else
            {
                // Proxies are kinematic; follow the NetworkTransform
                _rb.isKinematic = true;
                _rb.interpolation = RigidbodyInterpolation.None;
            }

            ApplyState();
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority == false) return;

            // Apply manual player push (SimpleKCC interaction) and check for rest/sleep
            ApplyPlayerPush();
            TickSettle(Runner.DeltaTime);
        }

        // --- IInteractable Implementation ---

        float IInteractable.InteractRange => _interactRange;
        bool IInteractable.CanInteract => true;
        Vector3 IInteractable.InteractionPoint => transform.position;
        string IInteractable.LockedReason => string.Empty;
        string IInteractable.InteractLabel => IsOn ? $"Turn Off {_displayName}" : $"Turn On {_displayName}";

        void IInteractable.OnInteract(InteractionScanner scanner)
        {
            RPC_RequestToggle();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestToggle(RpcInfo info = default)
        {
            // Range validation: ensure the requesting player is actually near the lamp
            var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
            var playerObj = Runner.GetPlayerObject(source);
            if (playerObj != null)
            {
                float allowed = _interactRange * 1.5f;
                if ((playerObj.transform.position - transform.position).sqrMagnitude > allowed * allowed) return;
            }

            IsOn = !IsOn;
        }

        private void OnIsOnChanged()
        {
            ApplyState();
        }

        private void ApplyState()
        {
            if (_light != null) _light.enabled = IsOn;

            if (_renderer != null)
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(block, _materialIndex);
                block.SetColor("_EmissionColor", IsOn ? _emissiveOnColor : _emissiveOffColor);
                _renderer.SetPropertyBlock(block, _materialIndex);

                // Note: If using the standard URP/Built-in shaders, the _EMISSION keyword 
                // might need to be enabled on the material itself. We do this once here.
                if (IsOn && !_renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"))
                {
                    _renderer.material.EnableKeyword("_EMISSION");
                }
            }
        }
    }
}
