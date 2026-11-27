using System;
using System.Linq;
using SchoolEscape.Multiplayer;
using Unity.Services.Friends.Models;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;

namespace SchoolEscape.UI
{
    /// <summary>Runtime uGUI controls for Friends invitations and Vivox proximity voice.</summary>
    [DisallowMultipleComponent]
    public sealed class SchoolEscapeSocialUI : MonoBehaviour
    {
        private static readonly Color Surface = new Color(.055f, .07f, .1f, .98f);
        private static readonly Color Primary = new Color(.65f, .08f, .1f, 1f);
        private static readonly Color Secondary = new Color(.14f, .2f, .28f, 1f);
        private static readonly Color Muted = new Color(.72f, .75f, .8f);

        private GameObject lobbyRoot, voiceRoot, inviteRoot;
        private Text playerIdText, friendsStatusText, voiceStatusText, participantText, inviteText, inviteFeedback;
        private InputField friendIdInput;
        private Button acceptFriendButton, rejectFriendButton, micButton, participantMuteButton, acceptInviteButton;
        private Button copyIdButton;
        private Text micLabel, participantMuteLabel;
        private Slider participantVolume;
        private string pendingRequestId, remoteParticipantId;
        private readonly Text[] friendNames = new Text[3];
        private readonly Button[] friendInvites = new Button[3];
        private readonly string[] friendIds = new string[3];
        private int friendPage;
        private bool connecting;
        private string actionError;
        private Action openLobby;
        private float nextRefresh;

        public static SchoolEscapeSocialUI EnsureFor(Transform canvasRoot, GameObject friendsPanel, Action openLobby, Action back)
        {
            SchoolEscapeSocialUI existing = canvasRoot.GetComponent<SchoolEscapeSocialUI>();
            if (existing != null) return existing;
            SchoolEscapeSocialUI ui = canvasRoot.gameObject.AddComponent<SchoolEscapeSocialUI>();
            ui.openLobby = openLobby;
            ui.Build(canvasRoot, friendsPanel, back);
            return ui;
        }

        private void Build(Transform canvasRoot, GameObject friendsPanel, Action back)
        {
            lobbyRoot = CreateCard(friendsPanel.transform, "Semana 14 - Amigos", Vector2.zero, new Vector2(700, 650));
            AddText(lobbyRoot.transform, "AMIGOS E INVITACIÓN", 24, FontStyle.Bold, new Vector2(0, 285), new Vector2(380, 40));
            playerIdText = AddText(lobbyRoot.transform, "Obteniendo tu PlayerId…", 18, FontStyle.Normal, new Vector2(-75, 240), new Vector2(420, 50), Muted, TextAnchor.MiddleLeft);
            copyIdButton = AddButton(lobbyRoot.transform, "COPIAR ID", new Vector2(230, 240), new Vector2(145, 42), Secondary, CopyPlayerId);
            friendIdInput = AddInput(lobbyRoot.transform, "PLAYER ID DE AMIGO", new Vector2(-75, 180), new Vector2(420, 48));
            AddButton(lobbyRoot.transform, "PEGAR ID", new Vector2(230, 180), new Vector2(145, 42), Secondary, PasteFriendId);
            AddButton(lobbyRoot.transform, "AGREGAR AMIGO", new Vector2(0, 125), new Vector2(370, 46), Secondary, AddFriend);
            friendsStatusText = AddText(lobbyRoot.transform, "Amigos: conectando…", 16, FontStyle.Normal, new Vector2(0, 80), new Vector2(370, 46), new Color(.9f, .85f, .55f));
            for (int i = 0; i < friendNames.Length; i++)
            {
                int row = i;
                int y = 25 - i * 42;
                friendNames[i] = AddText(lobbyRoot.transform, "", 19, FontStyle.Normal, new Vector2(-85, y), new Vector2(320, 40), Color.white, TextAnchor.MiddleLeft);
                friendInvites[i] = AddButton(lobbyRoot.transform, "INVITAR", new Vector2(170, y), new Vector2(155, 36), Primary, () => InviteFriend(friendIds[row]));
            }
            AddButton(lobbyRoot.transform, "‹", new Vector2(-305, -20), new Vector2(55, 55), Secondary, () => { friendPage = Mathf.Max(0, friendPage - 1); Refresh(); });
            AddButton(lobbyRoot.transform, "›", new Vector2(305, -20), new Vector2(55, 55), Secondary, () => { friendPage++; Refresh(); });
            AddText(lobbyRoot.transform, "SOLICITUD ENTRANTE", 16, FontStyle.Bold, new Vector2(0, -125), new Vector2(370, 32), Muted);
            acceptFriendButton = AddButton(lobbyRoot.transform, "ACEPTAR", new Vector2(-95, -175), new Vector2(170, 44), Secondary, AcceptFriend);
            rejectFriendButton = AddButton(lobbyRoot.transform, "RECHAZAR", new Vector2(95, -175), new Vector2(170, 44), Secondary, RejectFriend);
            AddButton(lobbyRoot.transform, "VOLVER", new Vector2(0, -260), new Vector2(240, 48), Secondary, () => { if (!connecting) back(); });

            voiceRoot = CreateCard(canvasRoot, "Semana 14 - Voz", new Vector2(735, 390), new Vector2(410, 240));
            voiceStatusText = AddText(voiceRoot.transform, "VOZ DE PROXIMIDAD", 18, FontStyle.Bold, new Vector2(0, 92), new Vector2(370, 34));
            micButton = AddButton(voiceRoot.transform, "SILENCIAR MICRÓFONO", new Vector2(0, 48), new Vector2(370, 42), Primary, ToggleMicrophone);
            micLabel = micButton.GetComponentInChildren<Text>();
            participantText = AddText(voiceRoot.transform, "Esperando al otro jugador…", 16, FontStyle.Normal, new Vector2(-45, 5), new Vector2(280, 34), Muted, TextAnchor.MiddleLeft);
            participantMuteButton = AddButton(voiceRoot.transform, "MUTE", new Vector2(145, 5), new Vector2(80, 34), Secondary, ToggleParticipantMute);
            participantMuteLabel = participantMuteButton.GetComponentInChildren<Text>();
            participantVolume = AddSlider(voiceRoot.transform, new Vector2(0, -48), new Vector2(370, 28));
            participantVolume.minValue = 0; participantVolume.maxValue = 100; participantVolume.value = 50;
            participantVolume.onValueChanged.AddListener(SetParticipantVolume);
            AddText(voiceRoot.transform, "Volumen del participante", 13, FontStyle.Normal, new Vector2(0, -82), new Vector2(370, 25), Muted);
            voiceRoot.SetActive(false);

            inviteRoot = CreateCard(canvasRoot, "Invitación recibida", new Vector2(0, 370), new Vector2(680, 190));
            inviteText = AddText(inviteRoot.transform, "Invitación", 20, FontStyle.Bold, new Vector2(0, 48), new Vector2(620, 65));
            acceptInviteButton = AddButton(inviteRoot.transform, "ACEPTAR E IR A LA SALA", new Vector2(-155, -48), new Vector2(290, 48), Primary, AcceptInvite);
            AddButton(inviteRoot.transform, "AHORA NO", new Vector2(175, -48), new Vector2(230, 48), Secondary, DismissInvite);
            inviteRoot.SetActive(false);
            inviteFeedback = AddText(canvasRoot, "", 22, FontStyle.Bold, new Vector2(0, 470), new Vector2(900, 70), new Color(.95f, .85f, .55f));
            inviteFeedback.raycastTarget = false;
            inviteFeedback.gameObject.SetActive(false);
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            Refresh();
        }

        private void Refresh()
        {
            SchoolEscapeSocialServices social = SchoolEscapeSocialServices.Ensure();
            SchoolEscapeNetworkBootstrap bootstrap = SchoolEscapeNetworkBootstrap.Instance;
            string playerId = social.LocalPlayerId;
            playerIdText.text = string.IsNullOrEmpty(playerId) ? "PlayerId pendiente" : "TU ID  " + playerId;
            copyIdButton.interactable = !string.IsNullOrEmpty(playerId);
            friendsStatusText.text = actionError ?? social.FriendsStatus;
            friendPage = Mathf.Clamp(friendPage, 0, Mathf.Max(0, (social.FriendList.Count - 1) / friendNames.Length));
            for (int i = 0; i < friendNames.Length; i++)
            {
                int index = friendPage * friendNames.Length + i;
                var friend = index < social.FriendList.Count ? social.FriendList[index] : null;
                friendIds[i] = friend?.PlayerId;
                friendNames[i].text = friend == null
                    ? (index == 0 ? "Agrega un amigo mediante su PlayerId." : "")
                    : $"{friend.DisplayName} · {AvailabilityText(friend.Availability)}";
                friendInvites[i].gameObject.SetActive(friend != null);
                friendInvites[i].interactable = !connecting && friend?.IsOnline == true &&
                    (bootstrap == null || bootstrap.CanInvite || (!bootstrap.IsOnlineSession && !bootstrap.IsListening && !bootstrap.IsConnecting));
            }

            pendingRequestId = social.IncomingFriendRequestId();
            acceptFriendButton.interactable = rejectFriendButton.interactable = !string.IsNullOrEmpty(pendingRequestId);
            acceptFriendButton.GetComponentInChildren<Text>().text = string.IsNullOrEmpty(pendingRequestId)
                ? "SIN SOLICITUD" : "ACEPTAR";

            bool showVoice = bootstrap != null && bootstrap.IsOnlineSession && bootstrap.GameplayActive;
            if (voiceRoot.activeSelf != showVoice) voiceRoot.SetActive(showVoice);
            voiceStatusText.text = social.VoiceStatus;
            micLabel.text = social.IsMicMuted ? "ACTIVAR MICRÓFONO" : "SILENCIAR MICRÓFONO";

            VivoxParticipant remote = social.VoiceParticipants.FirstOrDefault(value => !value.IsSelf);
            remoteParticipantId = remote?.PlayerId;
            participantText.text = remote == null ? "Esperando al otro jugador…" :
                $"{remote.DisplayName}{(remote.SpeechDetected ? " · hablando" : string.Empty)}";
            participantMuteButton.interactable = remote != null;
            participantMuteLabel.text = remote != null && remote.IsMuted ? "OÍR" : "MUTE";
            participantVolume.interactable = remote != null;
            if (remote != null && !participantVolume.IsActive()) participantVolume.value = remote.LocalVolume + 50;

            var incoming = social.IncomingInvite;
            bool showInvite = incoming != null && BasicGameFlowUI.IsMenuVisible &&
                (bootstrap == null || (!bootstrap.IsOnlineSession && !bootstrap.IsListening && !bootstrap.IsConnecting));
            if (inviteRoot.activeSelf != showInvite) inviteRoot.SetActive(showInvite);
            if (incoming != null)
                inviteText.text = incoming.IsExpired
                    ? "Esta invitación ya venció."
                    : $"{incoming.senderName} te invita a jugar";
            acceptInviteButton.interactable = !connecting && incoming?.IsExpired == false;
        }

        private async void AddFriend()
        {
            actionError = null;
            try { await SchoolEscapeSocialServices.Ensure().AddFriendAsync(friendIdInput.text); friendIdInput.text = string.Empty; }
            catch (Exception exception) { ShowError(exception); }
        }

        private async void AcceptFriend()
        {
            try { await SchoolEscapeSocialServices.Ensure().AcceptFriendRequestAsync(pendingRequestId); }
            catch (Exception exception) { ShowError(exception); }
        }

        private async void RejectFriend()
        {
            try { await SchoolEscapeSocialServices.Ensure().RejectFriendRequestAsync(pendingRequestId); }
            catch (Exception exception) { ShowError(exception); }
        }

        private async void InviteFriend(string friendId)
        {
            if (connecting || string.IsNullOrEmpty(friendId)) return;
            connecting = true;
            actionError = null;
            var bootstrap = SchoolEscapeNetworkBootstrap.EnsureFor(FindFirstObjectByType<SchoolEscapePrototype>());
            bool created = false;
            try
            {
                if (!bootstrap.IsOnlineSession)
                {
                    created = await bootstrap.CreateOnlineSessionAsync(SchoolEscapeNetworkBootstrap.LoadSavedPlayerName(), friendsRoom: true);
                    if (!created) throw new InvalidOperationException(bootstrap.Status);
                }
                await SchoolEscapeSocialServices.Ensure().SendInviteAsync(friendId);
                openLobby();
            }
            catch (Exception exception)
            {
                if (created) await bootstrap.LeaveOnlineSessionAsync();
                ShowError(exception);
            }
            finally { connecting = false; }
        }

        private async void AcceptInvite()
        {
            if (connecting) return;
            connecting = true;
            try
            {
                if (await SchoolEscapeSocialServices.Ensure().AcceptInviteAsync()) openLobby();
            }
            catch (Exception exception)
            {
                ShowError(exception);
                StopCoroutine(nameof(HideInviteFeedback));
                inviteFeedback.text = exception.Message;
                inviteFeedback.gameObject.SetActive(true);
                StartCoroutine(nameof(HideInviteFeedback));
            }
            finally { connecting = false; }
        }

        private System.Collections.IEnumerator HideInviteFeedback()
        {
            yield return new WaitForSecondsRealtime(6f);
            inviteFeedback.gameObject.SetActive(false);
        }

        private void DismissInvite() => SchoolEscapeSocialServices.Ensure().DismissInvite();
        private void CopyPlayerId()
        {
            string id = SchoolEscapeSocialServices.Ensure().LocalPlayerId;
            if (string.IsNullOrEmpty(id)) return;
            GUIUtility.systemCopyBuffer = id;
            actionError = "Tu PlayerId fue copiado.";
        }

        private void PasteFriendId()
        {
            string id = GUIUtility.systemCopyBuffer?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                actionError = "El portapapeles está vacío.";
                return;
            }
            if (id == SchoolEscapeSocialServices.Ensure().LocalPlayerId)
            {
                actionError = "El portapapeles contiene tu ID. Cópialo desde la otra ventana.";
                return;
            }
            friendIdInput.text = id;
            actionError = null;
        }

        private void ToggleMicrophone()
        {
            SchoolEscapeSocialServices social = SchoolEscapeSocialServices.Ensure();
            social.SetMicrophoneMuted(!social.IsMicMuted);
        }

        private void ToggleParticipantMute()
        {
            if (string.IsNullOrEmpty(remoteParticipantId)) return;
            VivoxParticipant remote = SchoolEscapeSocialServices.Ensure().VoiceParticipants
                .FirstOrDefault(value => value.PlayerId == remoteParticipantId && !value.IsSelf);
            SchoolEscapeSocialServices.Ensure().SetParticipantMuted(remoteParticipantId, remote == null || !remote.IsMuted);
        }

        private void SetParticipantVolume(float value)
        {
            if (!string.IsNullOrEmpty(remoteParticipantId))
                SchoolEscapeSocialServices.Ensure().SetParticipantVolume(remoteParticipantId, Mathf.RoundToInt(value) - 50);
        }

        private void ShowError(Exception exception)
        {
            actionError = exception.Message;
            Debug.LogWarning($"[Week14][UI] {exception.Message}");
        }

        private static string AvailabilityText(Availability availability)
        {
            switch (availability)
            {
                case Availability.Online: return "Online";
                case Availability.Busy: return "Ocupado";
                case Availability.Away: return "Ausente";
                case Availability.Offline: return "Offline";
                default: return availability.ToString();
            }
        }

        private static GameObject CreateCard(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject card = new GameObject(name, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            card.GetComponent<Image>().color = Surface;
            return card;
        }

        private static Text AddText(Transform parent, string value, int size, FontStyle style,
            Vector2 position, Vector2 dimensions, Color? color = null, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            GameObject go = new GameObject("Texto - " + value.Replace("\n", " "), typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = dimensions;
            Text text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.fontStyle = style; text.alignment = alignment; text.color = color ?? Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; text.text = value;
            return text;
        }

        private static Button AddButton(Transform parent, string label, Vector2 position, Vector2 size, Color color,
            UnityEngine.Events.UnityAction action)
        {
            GameObject go = new GameObject("Botón - " + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; go.GetComponent<Image>().color = color;
            Button button = go.GetComponent<Button>(); button.onClick.AddListener(action);
            Text labelText = AddText(go.transform, label, 16, FontStyle.Bold, Vector2.zero, size);
            return button;
        }

        private static InputField AddInput(Transform parent, string hint, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject("Campo - " + hint, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; go.GetComponent<Image>().color = new Color(.03f, .045f, .065f, 1f);
            Text text = AddText(go.transform, string.Empty, 17, FontStyle.Normal, Vector2.zero, size, Color.white, TextAnchor.MiddleLeft);
            Text placeholder = AddText(go.transform, hint, 15, FontStyle.Normal, Vector2.zero, size, Muted, TextAnchor.MiddleLeft);
            text.rectTransform.offsetMin = placeholder.rectTransform.offsetMin = new Vector2(12, 4);
            text.rectTransform.offsetMax = placeholder.rectTransform.offsetMax = new Vector2(-12, -4);
            InputField input = go.GetComponent<InputField>(); input.textComponent = text; input.placeholder = placeholder;
            input.characterLimit = 64; return input;
        }

        private static Slider AddSlider(Transform parent, Vector2 position, Vector2 size)
        {
            GameObject root = new GameObject("Slider - Volumen participante", typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image)); background.transform.SetParent(root.transform, false);
            background.GetComponent<Image>().color = new Color(.15f, .18f, .22f); Stretch(background.GetComponent<RectTransform>());
            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform)); fillArea.transform.SetParent(root.transform, false); Stretch(fillArea.GetComponent<RectTransform>());
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(fillArea.transform, false); Stretch(fill.GetComponent<RectTransform>()); fill.GetComponent<Image>().color = Primary;
            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)); handleArea.transform.SetParent(root.transform, false); Stretch(handleArea.GetComponent<RectTransform>());
            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(handleArea.transform, false);
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(22, 34); handle.GetComponent<Image>().color = Color.white;
            Slider slider = root.GetComponent<Slider>(); slider.fillRect = fill.GetComponent<RectTransform>(); slider.handleRect = handle.GetComponent<RectTransform>(); slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
