using Unity.Netcode;
using UnityEngine;

namespace SchoolEscape.Multiplayer
{
    [RequireComponent(typeof(NetworkObject), typeof(PrototypePlayer))]
    public sealed class NetworkHostPlayerBridge : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            SchoolEscapePrototype session = FindFirstObjectByType<SchoolEscapePrototype>();
            PrototypePlayer player = GetComponent<PrototypePlayer>();
            if (session == null)
            {
                player.enabled = false;
                return;
            }

            session.AttachNetworkHostPlayer(player, IsOwner && IsServer);
        }
    }
}
