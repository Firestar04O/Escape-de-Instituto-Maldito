using Unity.Netcode;
using UnityEngine;

namespace SchoolEscape.Multiplayer
{
    [RequireComponent(typeof(NetworkObject), typeof(PrototypePlayer))]
    public sealed class NetworkHostPlayerBridge : NetworkBehaviour
    {
        private readonly NetworkVariable<int> syncedHealth = new NetworkVariable<int>(3);
        private readonly NetworkVariable<bool> syncedCaptive = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> syncedEscaped = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> syncedVisible = new NetworkVariable<bool>(true);
        private PrototypePlayer player;

        public override void OnNetworkSpawn()
        {
            SchoolEscapePrototype session = FindFirstObjectByType<SchoolEscapePrototype>();
            player = GetComponent<PrototypePlayer>();
            if (session == null)
            {
                player.enabled = false;
                return;
            }

            session.AttachNetworkHostPlayer(player, IsOwner && IsServer);
        }

        private void Update()
        {
            if (player == null) player = GetComponent<PrototypePlayer>();
            if (IsServer)
            {
                syncedHealth.Value = player.Health;
                syncedCaptive.Value = player.IsCaptive;
                syncedEscaped.Value = player.IsEscaped;
                syncedVisible.Value = player.PresentationVisible;
            }
            else if (IsClient)
            {
                player.ApplyReplicatedState(
                    syncedHealth.Value,
                    syncedCaptive.Value,
                    syncedEscaped.Value,
                    syncedVisible.Value);
            }
        }
    }
}
