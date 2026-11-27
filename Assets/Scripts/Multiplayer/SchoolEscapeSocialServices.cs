using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Friends;
using Unity.Services.Friends.Models;
using Unity.Services.Friends.Notifications;
using Unity.Services.Friends.Options;
using Unity.Services.Vivox;
using UnityEngine;

namespace SchoolEscape.Multiplayer
{
    /// <summary>
    /// Owns the Week 14 social lifecycle. Authentication remains shared with the
    /// multiplayer bootstrap; Vivox and Friends failures never tear down a match.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SchoolEscapeSocialServices : MonoBehaviour
    {
        public const int AudibleDistance = 32;
        public const int ConversationalDistance = 1;
        public const float PositionUpdateInterval = .3f;
        private const string InviteKind = "school_escape_invite";
        private const int InviteVersion = 1;

        public static SchoolEscapeSocialServices Instance { get; private set; }
        public static SchoolEscapeSocialServices Ensure()
        {
            if (Instance != null) return Instance;
            SchoolEscapeSocialServices existing = FindFirstObjectByType<SchoolEscapeSocialServices>();
            if (existing != null) return existing;
            return new GameObject("School Escape Voice & Friends").AddComponent<SchoolEscapeSocialServices>();
        }

        public string VoiceStatus { get; private set; } = "Voz: fuera de una sala";
        public string FriendsStatus { get; private set; } = "Amigos: sin inicializar";
        public string LocalPlayerId => UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : string.Empty;
        public string ChannelName { get; private set; } = string.Empty;
        public bool IsVoiceJoined { get; private set; }
        public bool IsMicMuted { get; private set; }
        public bool FriendsReady { get; private set; }
        public SocialInvite IncomingInvite { get; private set; }
        public IReadOnlyList<FriendSummary> FriendList => friends;
        public IReadOnlyList<VivoxParticipant> VoiceParticipants => participants;

        public event Action StateChanged;

        private readonly List<FriendSummary> friends = new List<FriendSummary>();
        private readonly List<VivoxParticipant> participants = new List<VivoxParticipant>();
        private bool eventsHooked;
        private bool initializing;
        private bool sessionActive;
        private int sessionGeneration;
        private float nextPositionUpdate;
        private GameObject positionAnchor;
        private string publishedPresence;
        private bool updatingPresence;
        private float nextPresenceUpdate;

        [Serializable]
        public sealed class PresenceActivity
        {
            public string state;
            public string sessionCode;
            public PresenceActivity() { }
            public PresenceActivity(string stateValue, string code)
            {
                state = stateValue;
                sessionCode = code;
            }
        }

        [Serializable]
        public sealed class SocialInvite
        {
            public string type;
            public int version;
            public string sessionCode;
            public string sessionId;
            public string senderName;
            public long expiresUtcTicks;
            public string senderPlayerId;
            public SocialInvite() { }
            public bool IsExpired => expiresUtcTicks <= DateTime.UtcNow.Ticks;
        }

        public sealed class FriendSummary
        {
            public string PlayerId;
            public string DisplayName;
            public Availability Availability;
            public bool IsOnline => Availability == Availability.Online;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (FriendsReady && !updatingPresence && Time.unscaledTime >= nextPresenceUpdate)
            {
                nextPresenceUpdate = Time.unscaledTime + 2f;
                var bootstrap = SchoolEscapeNetworkBootstrap.Instance;
                string presence = bootstrap?.GameplayActive == true ? "En partida" :
                    bootstrap?.IsOnlineSession == true ? "En grupo" : "Disponible";
                if (presence != publishedPresence) _ = PublishPresenceAsync(presence);
            }
            if (!IsVoiceJoined || Time.unscaledTime < nextPositionUpdate) return;
            nextPositionUpdate = Time.unscaledTime + PositionUpdateInterval;
            IVivoxService vivox = VivoxService.Instance;
            if (vivox == null || !vivox.IsLoggedIn || !vivox.ActiveChannels.ContainsKey(ChannelName))
            {
                OnVoiceChannelLeft(ChannelName);
                return;
            }

            if (positionAnchor == null) positionAnchor = FindLocalVoiceAnchor();
            if (positionAnchor == null) return;

            try
            {
                vivox.Set3DPosition(positionAnchor, ChannelName);
            }
            catch (Exception exception)
            {
                VoiceStatus = "Voz: no se pudo actualizar la posición";
                Debug.LogWarning($"[Week14][Vivox] Set3DPosition: {exception.Message}");
                NotifyChanged();
            }
        }

        private async Task PublishPresenceAsync(string presence)
        {
            updatingPresence = true;
            try
            {
                await FriendsService.Instance.SetPresenceAsync(
                    presence == "Disponible" ? Availability.Online : Availability.Busy,
                    new PresenceActivity(presence, string.Empty));
                publishedPresence = presence;
            }
            catch (Exception exception)
            {
                nextPresenceUpdate = Time.unscaledTime + 10f;
                Debug.LogWarning($"[Friends] Presencia: {exception.Message}");
            }
            finally { updatingPresence = false; }
        }

        public async void EnterSession(string sessionId, string sessionCode, string displayName)
        {
            sessionActive = true;
            int generation = ++sessionGeneration;
            if (initializing)
            {
                await WaitForInitializationAsync();
                if (generation != sessionGeneration) return;
            }

            initializing = true;
            try
            {
                await SchoolEscapeNetworkBootstrap.EnsureServicesReadyAsync();
                await Task.WhenAll(
                    ConnectFriendsAsync(),
                    JoinVoiceAsync(sessionId, displayName, generation));
            }
            catch (Exception exception)
            {
                VoiceStatus = $"Voz no disponible: {ShortMessage(exception)}";
                Debug.LogWarning($"[Week14] No se pudo iniciar voz/social: {exception}");
            }
            finally
            {
                initializing = false;
                NotifyChanged();
            }
        }

        private async Task WaitForInitializationAsync()
        {
            while (initializing) await Task.Yield();
        }

        private Task friendsConnection;

        public Task ConnectFriendsAsync()
        {
            if (FriendsReady) return Task.CompletedTask;
            if (friendsConnection == null || friendsConnection.IsCompleted)
                friendsConnection = InitializeFriendsAsync();
            return friendsConnection;
        }

        private async Task InitializeFriendsAsync()
        {
            try
            {
                if (!FriendsReady)
                {
                    FriendsStatus = "Amigos: conectando…";
                    await SchoolEscapeNetworkBootstrap.EnsureServicesReadyAsync();
                    HookEventsOnce();
                    var options = new InitializeOptions()
                        .WithEvents(true)
                        .WithMemberPresence(true)
                        .WithMemberProfile(true);
                    await FriendsService.Instance.InitializeAsync(options);
                }

                await FriendsService.Instance.SetPresenceAsync(
                    Availability.Online, new PresenceActivity("Disponible", string.Empty));
                publishedPresence = "Disponible";
                FriendsReady = true;
                RefreshFriends();
                FriendsStatus = "Amigos: conectado";
            }
            catch (Exception exception)
            {
                FriendsReady = false;
                FriendsStatus = $"Amigos no disponible: {ShortMessage(exception)}";
                Debug.LogWarning($"[Week14][Friends] Inicialización: {exception.Message}");
            }
        }

        private async Task JoinVoiceAsync(string sessionId, string displayName, int generation)
        {
            if (generation != sessionGeneration) return;
            VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantChanged;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantChanged;
            VivoxService.Instance.ChannelLeft -= OnVoiceChannelLeft;
            VivoxService.Instance.ParticipantAddedToChannel += OnParticipantChanged;
            VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantChanged;
            VivoxService.Instance.ChannelLeft += OnVoiceChannelLeft;
            string requestedChannel = BuildChannelName(sessionId);
            VoiceStatus = "Voz: conectando…";

            if (VivoxService.Instance.InitializationState != VivoxInitializationState.Initialized)
                await VivoxService.Instance.InitializeAsync();
            if (!VivoxService.Instance.IsLoggedIn)
                await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = SanitizeDisplayName(displayName) });

            if (IsVoiceJoined && !string.Equals(ChannelName, requestedChannel, StringComparison.Ordinal))
                await VivoxService.Instance.LeaveChannelAsync(ChannelName);

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif

            if (generation != sessionGeneration) return;
            if (!VivoxService.Instance.ActiveChannels.ContainsKey(requestedChannel))
            {
                var properties = new Channel3DProperties(
                    AudibleDistance,
                    ConversationalDistance,
                    1f,
                    AudioFadeModel.InverseByDistance);
                await VivoxService.Instance.JoinPositionalChannelAsync(
                    requestedChannel, ChatCapability.AudioOnly, properties);
            }

            if (generation != sessionGeneration)
            {
                await VivoxService.Instance.LeaveChannelAsync(requestedChannel);
                return;
            }
            ChannelName = requestedChannel;
            IsVoiceJoined = true;
            IsMicMuted = VivoxService.Instance.IsInputDeviceMuted;
            positionAnchor = null;
            RefreshParticipants();
            VoiceStatus = $"Voz de proximidad activa · {AudibleDistance} m";
        }

        public async void LeaveSession()
        {
            if (!sessionActive) return;
            sessionActive = false;
            ++sessionGeneration;
            string oldChannel = ChannelName;
            IsVoiceJoined = false;
            ChannelName = string.Empty;
            positionAnchor = null;
            participants.Clear();
            IncomingInvite = null;
            VoiceStatus = "Voz: fuera de una sala";
            NotifyChanged();

            try
            {
                IVivoxService vivox = VivoxService.Instance;
                if (!string.IsNullOrEmpty(oldChannel) && vivox != null && vivox.IsLoggedIn &&
                    vivox.ActiveChannels.ContainsKey(oldChannel))
                    await vivox.LeaveChannelAsync(oldChannel);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Week14] Salida de servicios sociales: {exception.Message}");
            }
        }

        public void SetMicrophoneMuted(bool muted)
        {
            IVivoxService vivox = VivoxService.Instance;
            if (vivox == null || !vivox.IsLoggedIn) return;
            if (muted) vivox.MuteInputDevice();
            else vivox.UnmuteInputDevice();
            IsMicMuted = muted;
            NotifyChanged();
        }

        public void SetParticipantVolume(string playerId, int volume)
        {
            VivoxParticipant participant = participants.FirstOrDefault(
                value => !value.IsSelf && value.PlayerId == playerId);
            participant?.SetLocalVolume(Mathf.Clamp(volume, -50, 50));
            NotifyChanged();
        }

        public void SetParticipantMuted(string playerId, bool muted)
        {
            VivoxParticipant participant = participants.FirstOrDefault(
                value => !value.IsSelf && value.PlayerId == playerId);
            if (participant == null) return;
            if (muted) participant.MutePlayerLocally();
            else participant.UnmutePlayerLocally();
            NotifyChanged();
        }

        public async Task AddFriendAsync(string playerId)
        {
            if (!FriendsReady) throw new InvalidOperationException("Friends todavía no está disponible.");
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Escribe un PlayerId.");
            string targetId = playerId.Trim();
            if (string.Equals(targetId, LocalPlayerId, StringComparison.Ordinal))
                throw new ArgumentException("Ese es tu propio PlayerId. Copia el ID de la otra ventana.");
            await FriendsService.Instance.AddFriendAsync(targetId);
            await FriendsService.Instance.ForceRelationshipsRefreshAsync();
            RefreshFriends();
            FriendsStatus = "Solicitud de amistad enviada";
            NotifyChanged();
        }

        public async Task AcceptFriendRequestAsync(string playerId)
        {
            await AddFriendAsync(playerId);
            FriendsStatus = "Amistad aceptada";
            NotifyChanged();
        }

        public async Task RejectFriendRequestAsync(string playerId)
        {
            if (!FriendsReady) return;
            await FriendsService.Instance.DeleteIncomingFriendRequestAsync(playerId);
            RefreshFriends();
            FriendsStatus = "Solicitud rechazada";
            NotifyChanged();
        }

        public async Task SendInviteAsync(string friendPlayerId)
        {
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            if (!FriendsReady) throw new InvalidOperationException("Friends todavía no está disponible.");
            if (bootstrap == null || !bootstrap.CanInvite)
                throw new InvalidOperationException("Solo puedes invitar desde un lobby disponible.");
            if (!friends.Any(friend => friend.PlayerId == friendPlayerId && friend.IsOnline))
                throw new InvalidOperationException("El amigo ya no está disponible.");

            var invite = new SocialInvite
            {
                type = InviteKind,
                version = InviteVersion,
                sessionCode = bootstrap.SessionCode,
                sessionId = bootstrap.SessionId,
                senderName = bootstrap.LocalPlayerName,
                senderPlayerId = LocalPlayerId,
                expiresUtcTicks = DateTime.UtcNow.AddMinutes(10).Ticks
            };
            await FriendsService.Instance.MessageAsync(friendPlayerId, invite);
            FriendsStatus = "Invitación enviada";
            NotifyChanged();
        }

        public async Task<bool> AcceptInviteAsync()
        {
            SocialInvite invite = IncomingInvite;
            if (invite == null) return false;
            if (invite.IsExpired)
            {
                IncomingInvite = null;
                FriendsStatus = "La invitación venció";
                NotifyChanged();
                return false;
            }

            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.EnsureFor(
                FindFirstObjectByType<SchoolEscapePrototype>());
            if (bootstrap.IsOnlineSession || bootstrap.IsListening || bootstrap.IsConnecting)
                throw new InvalidOperationException("Vuelve al centro de juego para aceptar la invitación.");
            bool joined = await bootstrap.JoinOnlineSessionAsync(
                invite.sessionCode, SchoolEscapeNetworkBootstrap.LoadSavedPlayerName(), invite.sessionId);
            if (IncomingInvite == invite) IncomingInvite = null;
            if (!joined) throw new InvalidOperationException("La sala puede haber vencido, llenado o comenzado. " + bootstrap.Status);
            NotifyChanged();
            return joined;
        }

        public void DismissInvite()
        {
            IncomingInvite = null;
            NotifyChanged();
        }

        private void HookEventsOnce()
        {
            if (eventsHooked) return;
            FriendsService.Instance.RelationshipAdded += OnRelationshipAdded;
            FriendsService.Instance.RelationshipDeleted += OnRelationshipDeleted;
            FriendsService.Instance.PresenceUpdated += OnPresenceUpdated;
            FriendsService.Instance.MessageReceived += OnMessageReceived;
            eventsHooked = true;
        }

        private void OnParticipantChanged(VivoxParticipant _) { RefreshParticipants(); NotifyChanged(); }

        private void OnVoiceChannelLeft(string channelName)
        {
            if (!IsVoiceJoined || !string.Equals(channelName, ChannelName, StringComparison.Ordinal)) return;
            IsVoiceJoined = false;
            ChannelName = string.Empty;
            positionAnchor = null;
            participants.Clear();
            VoiceStatus = "Voz: canal desconectado";
            NotifyChanged();
        }
        private void OnRelationshipAdded(IRelationshipAddedEvent _) { RefreshFriends(); NotifyChanged(); }
        private void OnRelationshipDeleted(IRelationshipDeletedEvent _) { RefreshFriends(); NotifyChanged(); }
        private void OnPresenceUpdated(IPresenceUpdatedEvent _) { RefreshFriends(); NotifyChanged(); }

        private void OnMessageReceived(IMessageReceivedEvent message)
        {
            try
            {
                SocialInvite invite = message.GetAs<SocialInvite>();
                if (invite == null || invite.type != InviteKind || invite.version != InviteVersion ||
                    string.IsNullOrWhiteSpace(invite.sessionCode) || string.IsNullOrWhiteSpace(invite.sessionId)) return;
                invite.senderPlayerId = message.UserId;
                IncomingInvite = invite;
                FriendsStatus = invite.IsExpired
                    ? "Llegó una invitación vencida"
                    : $"{invite.senderName} te invitó a su sala";
                NotifyChanged();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Week14][Friends] Mensaje no reconocido: {exception.Message}");
            }
        }

        private void RefreshFriends()
        {
            friends.Clear();
            if (!FriendsReady) return;
            foreach (Relationship relationship in FriendsService.Instance.Friends)
            {
                Member member = relationship.Member;
                if (member == null) continue;
                friends.Add(new FriendSummary
                {
                    PlayerId = member.Id,
                    DisplayName = string.IsNullOrWhiteSpace(member.Profile?.Name) ? ShortId(member.Id) : member.Profile.Name,
                    Availability = member.Presence?.Availability ?? Availability.Unknown
                });
            }
        }

        private void RefreshParticipants()
        {
            participants.Clear();
            if (!IsVoiceJoined || string.IsNullOrEmpty(ChannelName)) return;
            if (VivoxService.Instance.ActiveChannels.TryGetValue(ChannelName, out var channelParticipants))
                participants.AddRange(channelParticipants);
        }

        public string IncomingFriendRequestId()
        {
            if (!FriendsReady || FriendsService.Instance.IncomingFriendRequests.Count == 0) return string.Empty;
            return FriendsService.Instance.IncomingFriendRequests[0].Member?.Id ?? string.Empty;
        }

        private static GameObject FindLocalVoiceAnchor()
        {
            if (NetworkClientAllyBridge.LocalOwner != null)
                return NetworkClientAllyBridge.LocalOwner.gameObject;
            NetworkHostPlayerBridge host = FindFirstObjectByType<NetworkHostPlayerBridge>();
            return host != null ? host.gameObject : null;
        }

        private static string BuildChannelName(string sessionId)
        {
            string source = string.IsNullOrWhiteSpace(sessionId) ? "local" : sessionId;
            char[] safe = source.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray();
            string value = new string(safe);
            if (value.Length > 80) value = value.Substring(0, 80);
            return "escape_" + value;
        }

        private static string SanitizeDisplayName(string value)
        {
            string result = string.IsNullOrWhiteSpace(value) ? "Jugador" : value.Trim();
            return result.Length <= 30 ? result : result.Substring(0, 30);
        }

        private static string ShortId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Jugador";
            return value.Length <= 10 ? value : value.Substring(0, 10) + "…";
        }

        private static string ShortMessage(Exception exception)
        {
            string value = exception?.Message ?? "error desconocido";
            int newline = value.IndexOf('\n');
            if (newline >= 0) value = value.Substring(0, newline);
            return value.Length <= 100 ? value : value.Substring(0, 100) + "…";
        }

        private void NotifyChanged() => StateChanged?.Invoke();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!eventsHooked) return;
            try
            {
                VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantChanged;
                VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantChanged;
                VivoxService.Instance.ChannelLeft -= OnVoiceChannelLeft;
                FriendsService.Instance.RelationshipAdded -= OnRelationshipAdded;
                FriendsService.Instance.RelationshipDeleted -= OnRelationshipDeleted;
                FriendsService.Instance.PresenceUpdated -= OnPresenceUpdated;
                FriendsService.Instance.MessageReceived -= OnMessageReceived;
            }
            catch { }
        }
    }
}
