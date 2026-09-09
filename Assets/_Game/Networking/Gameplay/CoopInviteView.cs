using System;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HowToSuck.Networking
{
    // Native picker first. Directly launched development players can have Steam without its overlay.
    public sealed class CoopInviteView : MonoBehaviour, ICancelHandler
    {
        public GameObject Panel;
        public TMP_Text Message;
        public Button CloseButton, RefreshButton, FriendTemplate;
        public RectTransform Content;
        public ScrollRect Scroll;
        public bool IsOpen => Panel != null && Panel.activeSelf;
        private sealed class FriendRow
        {
            public ulong Id;
            public Button Button;
            public TMP_Text Name, Status, Action;
            public RawImage Avatar;
            public TMP_Text Initial;
            public Texture2D Texture;
        }
        private readonly List<FriendRow> rows = new List<FriendRow>();
        private readonly Dictionary<ulong, float> sentAt = new Dictionary<ulong, float>();
        private SoloSessionStartup source;
        private SteamRuntime runtime;
        private SteamLobbyService lobby;
        private ulong lastLobby;
        private GameObject previousSelection;
        private float overlayDeadline, refreshAt;
        private uint activationCount;
        private bool waitingForOverlay;

        private void Awake()
        {
            CloseButton.onClick.AddListener(Close);
            RefreshButton.onClick.AddListener(RefreshFriends);
        }
        public void Open(SoloSessionStartup owner)
        {
            if (IsOpen || owner == null || !owner.CanInviteToLobby) return;
            source = owner; runtime = owner.Game.Connection.ActiveSteamRuntime; lobby = owner.Game.Connection.Lobby;
            if (lastLobby != lobby.LobbyId) { sentAt.Clear(); lastLobby = lobby.LobbyId; }
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            activationCount = runtime.OverlayActivationCount;
            Panel.SetActive(true);
            waitingForOverlay = source.OpenInviteOverlay();
            Debug.Log("Steam invite picker: overlay request " + (waitingForOverlay ? "submitted" : "unavailable; using friend list"));
            if (waitingForOverlay)
            {
                overlayDeadline = Time.unscaledTime + 3f;
                Message.text = "Открываем окно Steam…";
                RefreshButton.interactable = false;
            }
            else RefreshFriends();
            Select(CloseButton.gameObject);
        }
        private bool SessionAvailable => source != null && source.CanInviteToLobby &&
            ReferenceEquals(runtime, source.Game.Connection.ActiveSteamRuntime) &&
            ReferenceEquals(lobby, source.Game.Connection.Lobby) && lobby.LobbyId == lastLobby;
        private void Update()
        {
            if (!IsOpen) return;
            if (!SessionAvailable) { Close(); return; }
            if (runtime.OverlayActive || runtime.OverlayActivationCount != activationCount)
            {
                Debug.Log("Steam invite picker: overlay activation confirmed");
                Close(); return;
            }
            if (waitingForOverlay)
            {
                if (Time.unscaledTime < overlayDeadline) return;
                waitingForOverlay = false;
                Debug.LogWarning("Steam invite picker: no overlay activation; using friend list");
                RefreshFriends();
            }
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + .5f;
            foreach (var row in rows) RefreshRow(row);
        }
        private void RefreshFriends()
        {
            if (!IsOpen || !SessionAvailable || waitingForOverlay) return;
            // A rebuild must not leave keyboard focus pointing at a row scheduled for destruction.
            Select(RefreshButton.gameObject);
            ClearRows();
            var friends = new List<CSteamID>();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (id.IsValid() && id.m_SteamID != runtime.LocalSteamId) friends.Add(id);
            }
            friends.Sort((a, b) =>
            {
                int offline = IsOffline(a).CompareTo(IsOffline(b));
                return offline != 0 ? offline : StringComparer.CurrentCultureIgnoreCase.Compare(
                    SteamFriends.GetFriendPersonaName(a), SteamFriends.GetFriendPersonaName(b));
            });
            foreach (var id in friends)
            {
                var button = Instantiate(FriendTemplate, Content); button.gameObject.SetActive(true);
                var row = new FriendRow { Id = id.m_SteamID, Button = button,
                    Name = button.transform.Find("Name").GetComponent<TMP_Text>(),
                    Status = button.transform.Find("Status").GetComponent<TMP_Text>(),
                    Action = button.transform.Find("Action").GetComponent<TMP_Text>(),
                    Avatar = button.transform.Find("Avatar").GetComponent<RawImage>(),
                    Initial = button.transform.Find("Initial").GetComponent<TMP_Text>() };
                button.onClick.AddListener(() => Send(row)); rows.Add(row);
                SteamFriends.RequestUserInformation(id, false); RefreshRow(row);
            }
            Message.text = friends.Count == 0 ? "В списке Steam пока нет друзей." :
                "Оверлей Steam недоступен. Выберите друга — он получит приглашение в Steam.";
            RefreshButton.interactable = true;
            Canvas.ForceUpdateCanvases(); Scroll.verticalNormalizedPosition = 1;
        }
        private static bool IsOffline(CSteamID id) => SteamFriends.GetFriendPersonaState(id) == EPersonaState.k_EPersonaStateOffline;
        private void RefreshRow(FriendRow row)
        {
            var id = new CSteamID(row.Id); string name = SteamFriends.GetFriendPersonaName(id);
            row.Name.richText = false; row.Name.text = string.IsNullOrEmpty(name) ? "Друг Steam" : name;
            row.Initial.text = string.IsNullOrEmpty(name) ? "?" : System.Globalization.StringInfo.GetNextTextElement(name).ToUpperInvariant();
            bool member = lobby.ContainsMember(row.Id), offline = IsOffline(id);
            bool sent = sentAt.TryGetValue(row.Id, out float at) && Time.unscaledTime - at < 15;
            row.Status.text = member ? "УЖЕ В КОМАНДЕ" : offline ? "НЕ В СЕТИ" : "В СЕТИ";
            row.Action.text = sent ? "ОТПРАВЛЕНО" : member ? "В ЛОББИ" : "ПРИГЛАСИТЬ";
            row.Button.interactable = !member && !offline && !sent;
            if (row.Texture == null)
            {
                int handle = SteamFriends.GetMediumFriendAvatar(id);
                if (handle > 0 && SteamUtils.GetImageSize(handle, out uint width, out uint height) &&
                    width > 0 && height > 0 && width <= 256 && height <= 256)
                {
                    byte[] pixels = new byte[checked((int)(width * height * 4))];
                    if (SteamUtils.GetImageRGBA(handle, pixels, pixels.Length))
                    {
                        row.Texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                        row.Texture.LoadRawTextureData(pixels); row.Texture.Apply(false, true);
                        row.Avatar.texture = row.Texture; row.Avatar.uvRect = new Rect(0, 1, 1, -1);
                    }
                }
            }
            row.Avatar.enabled = row.Texture != null; row.Initial.enabled = row.Texture == null;
        }
        private void Send(FriendRow row)
        {
            if (!IsOpen || waitingForOverlay || !SessionAvailable) return;
            RefreshRow(row); if (!row.Button.interactable) return;
            // Never invite while rendering or refreshing: only an explicit click selects a recipient.
            bool accepted = source.InviteFriend(row.Id);
            if (accepted) sentAt[row.Id] = Time.unscaledTime;
            Message.text = accepted ? "Приглашение отправлено. Ждём друга в лобби." :
                "Steam не отправил приглашение. Обновите список и попробуйте ещё раз.";
            Debug.Log("Steam lobby invitation " + (accepted ? "accepted" : "rejected"));
            RefreshRow(row);
        }
        public void Close()
        {
            bool restore = IsOpen;
            if (Panel != null) Panel.SetActive(false);
            waitingForOverlay = false; ClearRows(); source = null; runtime = null; lobby = null;
            if (restore && previousSelection != null && previousSelection.activeInHierarchy) Select(previousSelection);
            previousSelection = null;
        }
        public void OnCancel(BaseEventData data) { if (IsOpen) { Close(); data.Use(); } }
        private void LateUpdate()
        {
            if (!IsOpen || EventSystem.current == null) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(Panel.transform)) Select(CloseButton.gameObject);
        }
        private static void Select(GameObject target) { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target); }
        private void ClearRows()
        {
            foreach (var row in rows)
            {
                if (row.Texture != null) Destroy(row.Texture);
                if (row.Button != null) { row.Button.gameObject.SetActive(false); Destroy(row.Button.gameObject); }
            }
            rows.Clear();
        }
        private void OnDisable() => Close();
        private void OnDestroy()
        {
            CloseButton.onClick.RemoveListener(Close); RefreshButton.onClick.RemoveListener(RefreshFriends); ClearRows();
        }
    }
}
