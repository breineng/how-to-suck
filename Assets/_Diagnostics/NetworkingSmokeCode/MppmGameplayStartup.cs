#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Author only on the diagnostic full-game bootstrap, not on the shipping bootstrap.
    // Uses the final gameplay adapters; it never spawns MppmSmokeProbe or alters contract/physics rules.
    public sealed class MppmGameplayStartup : MonoBehaviour,IPreparedSessionRoleSource
    {
        public NgoGameSession Game;
        public string ScenarioNonce;
        public string BuildId;
        public string ContentHash;
        public bool AutoReadyForDiagnostic;
        private LoopbackSmokeProfile profile;
        public LoopbackSmokeProfile Profile=>profile;
        public PreparedSessionRole ResolveRole()
        {
#if UNITY_EDITOR
            profile=LoopbackSmokeProfile.FromTags(MppmPlayerLabels.ReadTags(),MppmPlayerLabels.IsMainEditor,ScenarioNonce);
            return profile.IsHost?PreparedSessionRole.Authority:PreparedSessionRole.Guest;
#else
            var arguments=Environment.GetCommandLineArgs();
            string Read(string key)
            {
                string found=null;
                for(int i=0;i<arguments.Length;i++)if(arguments[i]==key)
                {if(found!=null||i+1>=arguments.Length)throw new ArgumentException("Duplicate/missing "+key);found=arguments[++i];}
                return found??throw new ArgumentException("Missing "+key);
            }
            if(!int.TryParse(Read("--hts-gameplay-slot"),out var slot)||!int.TryParse(Read("--hts-gameplay-count"),out var count))
                throw new ArgumentException("Invalid diagnostic slot/count.");
            ScenarioNonce=Read("--hts-gameplay-nonce");
            profile=LoopbackSmokeProfile.Create(slot,count,ScenarioNonce,true);
            return profile.IsHost?PreparedSessionRole.Authority:PreparedSessionRole.Guest;
#endif
        }
        private IEnumerator Start()
        {
            if(Game==null||profile==null||Game.Driver==null)throw new InvalidOperationException("Bind this role source before GameBootstrap.Awake.");
            Game.Connection.Configure(NetworkConfiguration.ForSolo(BuildId,ContentHash));
            if(!Game.Connection.StartDiagnosticLoopback(profile))throw new InvalidOperationException("Diagnostic UTP connection failed.");
            double deadline=Time.realtimeSinceStartupAsDouble+(profile.IsHost?120:25);
            while(Game.Connection.Phase!=ConnectionPhase.Lobby||!Game.Manager.IsConnectedClient||
                profile.IsHost&&(Game.Manager.ConnectedClientsIds.Count!=profile.PlayerCount||Game.Connection.ConnectedPlayerCount!=profile.PlayerCount))
            {
                if(Time.realtimeSinceStartupAsDouble>=deadline)throw new TimeoutException("Full-game test players did not connect.");
                yield return null;
            }
            Game.AttachConnectedGame();
            if(AutoReadyForDiagnostic)
            {
                deadline=Time.realtimeSinceStartupAsDouble+35;
                while(Game.Control==null||!Game.Control.IsSpawned||Game.Session.Phase!=SessionPhase.Lobby)
                {if(Time.realtimeSinceStartupAsDouble>=deadline)throw new TimeoutException("Full-game lobby did not synchronize.");yield return null;}
                Game.SetLocalReady(true); // Same public ready endpoint as normal game UI; no automatic run or fake collection.
            }
            Debug.Log("HTS_GAMEPLAY_UTP_READY slot="+profile.Slot+" client="+Game.Manager.LocalClientId+
                " pid="+System.Diagnostics.Process.GetCurrentProcess().Id+" nonce="+profile.Nonce);
        }
    }
}
#endif
