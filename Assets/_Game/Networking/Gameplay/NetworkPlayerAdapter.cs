using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
namespace HowToSuck.Networking
{
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkObject),typeof(PlayerMotor))]
    public sealed class NetworkPlayerAdapter : NetworkBehaviour, IPreparedNetworkSpawn, IPlayerIntentSink
    {
        public readonly NetworkVariable<PlayerWire> Snapshot=new NetworkVariable<PlayerWire>(default,
            NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public bool IsPlayerObject=>true;
        public ulong OwnerConnectionId {get;private set;}
        public PlayerMotor Motor {get;private set;}
        public PlayerInputReader Input {get;private set;}
        private NgoGameSession game;
        private PlayerView view;
        private uint revision;
        private string run,tier;
        private IntentWire pending;
        private bool hasPending,available;
        private double nextSend,receiveAt,tokens=8;
        private void Awake()
        {
            game=NgoGameSession.RequireCurrent(); Motor=GetComponent<PlayerMotor>(); Input=GetComponent<PlayerInputReader>();view=GetComponent<PlayerView>();
            var pose=GetComponent<NetworkTransform>();
            if(pose==null||GetComponents<NetworkTransform>().Length!=1||pose.AuthorityMode!=NetworkTransform.AuthorityModes.Server||
                GetComponent<NetworkRigidbody>()!=null)
                throw new InvalidOperationException("Use one server NetworkTransform and the domain physics-mode owner.");
            if(Input==null||view==null)throw new InvalidOperationException("Network player needs its existing input/view components.");
            // Prefab also serializes IsLocal=false/camera off/input disabled; no one-frame remote camera or input.
            Input.enabled=false;view.Initialize(false);Motor.BindMovementAuthority(game.HasAuthority);
            var animation=GetComponent<PlayerAnimationView>();
            if(animation==null||animation.Animator==null||animation.Body==null||animation.Head==null||animation.RemoteTool==null)
                throw new InvalidOperationException("Use the authored full-body player with both tool presentations.");
            animation.World=game.Session.World;
        }
        public void ConfigurePrepared(string runId,uint worldRevision,int playerId,ulong owner,string tierId)
        {
            if(!game.HasAuthority||IsSpawned||playerId<1||playerId>4||worldRevision==0||!Guid.TryParseExact(runId,"N",out _))
                throw new InvalidOperationException("Invalid prepared player.");
            run=runId;revision=worldRevision;tier=tierId;OwnerConnectionId=owner;
            Motor.Initialize(playerId);ConfigureTool(tierId,playerId);
        }
        private void ConfigureTool(string tierId,int playerId)
        {
            VacuumDefinition definition=null;
            foreach(var entry in game.Session.Catalog.Vacuums)if(entry!=null&&entry.TierId==tierId)definition=entry;
            if(definition==null)throw new InvalidOperationException("Replicated vacuum tier is unavailable.");
            var emitter=GetComponent<VacuumEmitter>();var receiver=GetComponent<IntakeReceiver>();
            if(emitter==null||receiver==null)throw new InvalidOperationException("Network player needs the final vacuum components.");
            emitter.Definition=definition;emitter.EmitterId=playerId;emitter.Source=Motor.NozzleAnchor;emitter.OriginGuard=Motor.AuthoritativeAim;emitter.Active=false;
            receiver.PlayerId=playerId;receiver.IntakeId=playerId;
            var issuer=GetComponent<ToolTierIssue>();
            if(issuer==null)throw new InvalidOperationException("Network player has no authored tier issuer.");
            issuer.Prepare(definition,run); // Both authority preparation and guest spawn finish before owner input is enabled.
        }
        public void PrepareNetworkState()
        {
            if(run!=game.Session.World.RunId||Motor.PlayerId<1||!game.Session.World.Players.ContainsKey(Motor.PlayerId))
                throw new InvalidOperationException("Register the player before publication.");
            preparedSnapshot=Capture();preparedForSpawn=true;
        }
        private PlayerWire preparedSnapshot;
        private bool preparedForSpawn;
        protected override void OnNetworkPreSpawn(ref NetworkManager networkManager)
        {
            base.OnNetworkPreSpawn(ref networkManager);
            if(!networkManager.IsServer)return;
            if(!preparedForSpawn)throw new InvalidOperationException("Prepare the domain snapshot before spawning.");
            // NGO2.13 assigns NetworkManagerOwner before this supported callback. Bind the
            // variable before setting Value; the initial spawn message includes the complete state.
            Snapshot.Initialize(this);
            Snapshot.Value=preparedSnapshot;
        }
        public override void OnNetworkSpawn()
        {
            var s=Snapshot.Value;
            if(s.Revision==0||s.PlayerId<1||s.PlayerId>4||!Guid.TryParseExact(s.Run.ToString(),"N",out _))
                throw new InvalidOperationException("Player arrived before its prepared state.");
            run=s.Run.ToString();revision=s.Revision;tier=s.Tier.ToString();OwnerConnectionId=OwnerClientId;
            if(!IsServer){Motor.Initialize(s.PlayerId);ConfigureTool(tier,s.PlayerId);Apply(s);}
            view.Initialize(IsOwner);game.Register(this);
            Snapshot.OnValueChanged+=OnSnapshot;
            if(IsOwner)
            {
                Input.Initialize(Motor.PlayerId,this,run);Input.SetGameplayAvailable(false);Input.enabled=true;
                game.Session.BindNetworkLocalPlayer(Motor,Input);
            }
            if(IsServer)game.Session.World.SnapshotChanged+=Publish;
        }
        private PlayerWire Capture()
        {
            var i=Motor.LastIntent;
            return new PlayerWire{Run=new FixedString64Bytes(run),Tier=new FixedString64Bytes(tier),Revision=revision,PlayerId=Motor.PlayerId,
                Sequence=i.Sequence,Jump=i.JumpPressSequence,Move=i.Move,Yaw=i.Yaw,Pitch=i.Pitch,Vertical=Motor.VerticalVelocity,
                PlanarSpeed=game.Session.World.IsRunning?Motor.PlanarSpeed:0f,Grounded=Motor.IsGrounded,Sprint=i.SprintHeld,Vacuum=i.VacuumHeld,Interact=i.InteractHeld,Frozen=!game.Session.World.IsRunning};
        }
        private void Publish(){if(IsServer&&IsSpawned)Snapshot.Value=Capture();}
        private void OnSnapshot(PlayerWire old,PlayerWire value){if(!IsServer)Apply(value);}
        private void Apply(PlayerWire s)
        {
            if(s.Run.ToString()!=run||s.Revision!=revision||s.PlayerId!=Motor.PlayerId)throw new InvalidOperationException("Player identity changed in place.");
            GetComponent<VacuumEmitter>().Active=s.Vacuum&&!s.Frozen;
            Motor.ApplyReplicaPresentation(new PlayerIntent{RunId=run,Sequence=s.Sequence,JumpPressSequence=s.Jump,Move=s.Move,
                Yaw=s.Yaw,Pitch=s.Pitch,SprintHeld=s.Sprint,VacuumHeld=s.Vacuum,InteractHeld=s.Interact},s.Grounded,s.Vertical,s.PlanarSpeed);
        }
        public void SubmitIntent(int playerId,PlayerIntent intent)
        {
            if(!IsSpawned||!IsOwner||playerId!=Motor.PlayerId||intent.RunId!=run||!intent.IsFinite)return;
            pending=new IntentWire{Run=new FixedString64Bytes(run),Sequence=intent.Sequence,Jump=intent.JumpPressSequence,
                Move=intent.Move,Yaw=intent.Yaw,Pitch=intent.Pitch,Sprint=intent.SprintHeld,Vacuum=intent.VacuumHeld,
                Interact=intent.InteractHeld,SuppressJump=NetworkIntentCopy.JumpSuppressed(intent)};hasPending=true;
        }
        private void Update()
        {
            if(!IsSpawned||!IsOwner||game.IsStopping)return;
            bool running=game.Session.Phase==SessionPhase.Playing&&game.Session.World.IsRunning&&game.Session.RunId==run;
            if(running!=available)
            {
                available=running;Input.SetGameplayAvailable(running);
                if(running)Input.SetMenuOpen(false); // Once at activation; never dismiss a player's subsequent pause menu.
            }
            double now=Time.realtimeSinceStartupAsDouble;
            if(hasPending&&now>=nextSend)
            {
                nextSend=now+1.0/30;hasPending=false;
                if(IsServer)Accept(OwnerClientId,pending);else SubmitIntentRpc(pending);
            }
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner,Delivery=RpcDelivery.Unreliable)]
        private void SubmitIntentRpc(IntentWire input,RpcParams rpc=default)=>Accept(rpc.Receive.SenderClientId,input);
        private void Accept(ulong sender,IntentWire packet)
        {
            // Sender identity is NGO-authenticated, never carried in the packet. RPC only queues intent.
            if(!IsServer||sender!=OwnerClientId||!game.Session.World.IsRunning||packet.Run.ToString()!=run||run!=game.Session.World.RunId)return;
            double now=game.Driver.Now;
            if(now<receiveAt)return;
            tokens=Math.Min(8,tokens+(now-receiveAt)*60);receiveAt=now;if(tokens<1)return;tokens-=1;
            var intent=new PlayerIntent{RunId=run,Sequence=packet.Sequence,JumpPressSequence=packet.Jump,Move=packet.Move,Yaw=packet.Yaw,
                Pitch=packet.Pitch,SprintHeld=packet.Sprint,VacuumHeld=packet.Vacuum,InteractHeld=packet.Interact};
            intent=NetworkIntentCopy.WithJumpSuppression(intent,packet.SuppressJump);
            if(intent.IsFinite)game.Session.World.SubmitIntent(Motor.PlayerId,intent); // Existing run/sequence/timeout and motor clamps.
        }
        public override void OnNetworkDespawn()
        {
            Snapshot.OnValueChanged-=OnSnapshot;
            if(game!=null){game.Session.World.SnapshotChanged-=Publish;game.Unregister(this);}
            if(Input!=null){Input.SetGameplayAvailable(false);Input.enabled=false;}
            if(view!=null)view.Initialize(false);
            GetComponent<ToolTierIssue>()?.Release();
        }
    }
}
