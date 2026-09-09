using System;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToSuck.Networking
{
    public sealed class CoopRosterView : MonoBehaviour
    {
        [Serializable] public sealed class Row
        {
            public RawImage Avatar;
            public TMP_Text Initial, Name, Status;
            public Button InviteButton;
            [NonSerialized] public UnityEngine.Events.UnityAction InviteAction;
        }
        public TMP_Text Heading;
        public CoopInviteView InviteView;
        public Row[] Rows=Array.Empty<Row>();
        [Serializable] public sealed class SoloLayoutChange
        {
            public RectTransform Target;
            public Vector2 PositionDelta, SizeDelta;
        }
        public SoloLayoutChange[] SoloLayout=Array.Empty<SoloLayoutChange>();
        private sealed class Profile { public string Name;public Texture2D Avatar;public bool Requested; }
        private readonly Dictionary<ulong,Profile> profiles=new Dictionary<ulong,Profile>();
        private readonly List<ulong> departed=new List<ulong>();
        private NgoGameSession game;private float refreshAt;
        private SoloSessionStartup startup;private SessionMenuView menu;
        private void Start()
        {
            game=NgoGameSession.RequireCurrent();startup=game.GetComponent<SoloSessionStartup>();menu=GetComponentInParent<SessionMenuView>();
            // The connection mode is established before this lobby scene loads. Every return creates a fresh view.
            if(game.Connection.Mode==ConnectionMode.SoloLoopback)
            {
                foreach(var change in SoloLayout)if(change.Target!=null)
                {
                    change.Target.anchoredPosition+=change.PositionDelta;
                    change.Target.sizeDelta+=change.SizeDelta;
                }
                gameObject.SetActive(false);
                return;
            }
            for(int i=0;i<Rows.Length;i++)
            {
                int slot=i;var row=Rows[i];if(row.InviteButton==null)continue;
                row.InviteAction=()=>Invite(slot);row.InviteButton.onClick.AddListener(row.InviteAction);
            }
        }
        private bool CanInvite=>startup!=null&&startup.CanInviteToLobby&&(menu==null||!menu.ModalBlocksLobby);
        private void Invite(int slot)
        {
            if(!isActiveAndEnabled||!CanInvite||game.Control==null||slot<game.Control.Roster.Count)return;
            if(InviteView!=null)InviteView.Open(startup);
        }
        private void Update()
        {
            if(game==null||Time.unscaledTime<refreshAt)return;
            refreshAt=Time.unscaledTime+.2f;
            var control=game.Control;var roster=control!=null&&control.IsSpawned?control.Roster:null;
            int count=roster?.Count??0;
            bool invite=CanInvite;
            if(Heading!=null)Heading.text=$"КОМАНДА  {count} / 4";
            for(int i=0;i<Rows.Length;i++)
            {
                var row=Rows[i];bool occupied=i<count;
                if(row.InviteButton!=null)
                {
                    row.InviteButton.interactable=!occupied&&invite;
                    if(row.InviteButton.targetGraphic!=null)row.InviteButton.targetGraphic.raycastTarget=!occupied&&invite;
                }
                if(!occupied)
                {
                    Show(row,"Место для друга","СВОБОДНО",null,false);
                    if(invite&&row.Status!=null)row.Status.text="ПРИГЛАСИТЬ";
                    continue;
                }
                var member=roster[i];bool local=member.ClientId==game.Connection.Manager.LocalClientId;
                string name=local?"Вы":$"Игрок {i+1}";Texture2D avatar=null;
                if(member.SteamId!=0&&game.Connection.ActiveSteamRuntime?.Initialized==true)
                {
                    var profile=Resolve(member.SteamId);if(!string.IsNullOrEmpty(profile.Name))name=profile.Name;avatar=profile.Avatar;
                }
                string status=(member.Host?"ХОЗЯИН · ":local?"ВЫ · ":"")+(member.Ready?"ГОТОВ":"НЕ ГОТОВ");
                Show(row,name,status,avatar,member.Ready);
            }
            departed.Clear();
            foreach(var pair in profiles)
            {
                bool present=false;for(int i=0;i<count;i++)if(roster[i].SteamId==pair.Key){present=true;break;}
                if(!present)departed.Add(pair.Key);
            }
            foreach(ulong id in departed){if(profiles[id].Avatar!=null)Destroy(profiles[id].Avatar);profiles.Remove(id);}
        }
        private Profile Resolve(ulong id)
        {
            if(!profiles.TryGetValue(id,out var profile)){profile=new Profile();profiles.Add(id,profile);}
            var steamId=new CSteamID(id);
            if(!profile.Requested){SteamFriends.RequestUserInformation(steamId,false);profile.Requested=true;}
            profile.Name=SteamFriends.GetFriendPersonaName(steamId);
            if(profile.Avatar==null)
            {
                int image=SteamFriends.GetMediumFriendAvatar(steamId);
                if(image>0&&SteamUtils.GetImageSize(image,out uint width,out uint height)&&width>0&&height>0&&width<=256&&height<=256)
                {
                    var pixels=new byte[checked((int)(width*height*4))];
                    if(SteamUtils.GetImageRGBA(image,pixels,pixels.Length))
                    {
                        profile.Avatar=new Texture2D((int)width,(int)height,TextureFormat.RGBA32,false){name="Steam lobby avatar",wrapMode=TextureWrapMode.Clamp};
                        profile.Avatar.LoadRawTextureData(pixels);profile.Avatar.Apply(false,true);
                    }
                }
            }
            return profile;
        }
        private static void Show(Row row,string name,string status,Texture2D avatar,bool ready)
        {
            if(row.Name!=null){row.Name.richText=false;row.Name.text=name;}
            if(row.Status!=null){row.Status.text=status;row.Status.color=ready?new Color(.16f,.38f,.23f):new Color(.48f,.40f,.32f);}
            if(row.Avatar!=null){row.Avatar.texture=avatar;row.Avatar.enabled=avatar!=null;row.Avatar.uvRect=new Rect(0,1,1,-1);}
            if(row.Initial!=null){row.Initial.gameObject.SetActive(avatar==null);row.Initial.text=status=="СВОБОДНО"?"+":string.IsNullOrEmpty(name)?"?":System.Globalization.StringInfo.GetNextTextElement(name).ToUpperInvariant();}
        }
        private void OnDestroy()
        {
            foreach(var row in Rows)if(row.InviteButton!=null&&row.InviteAction!=null)row.InviteButton.onClick.RemoveListener(row.InviteAction);
            foreach(var profile in profiles.Values)if(profile.Avatar!=null)Destroy(profile.Avatar);profiles.Clear();
        }
    }
}
