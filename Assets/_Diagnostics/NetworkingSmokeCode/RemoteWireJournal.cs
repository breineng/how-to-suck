#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Diagnostic values only. Every callback copies the actual NetworkVariable value;
    // serialization and file writes occur after timing stops, never in the publication callback.
    internal sealed class RemoteWireJournal : IDisposable
    {
        internal const int Capacity=32768;
        private struct Entry {
            public ulong id,objectId,owner,worldTick;
            public bool server;
            public int frame;
            public double engineTime,observerWall;
            public string kind;
            public PlayerWire wire;
        }
        private sealed class Binding {
            public NetworkPlayerAdapter player;
            public Unity.Netcode.NetworkVariable<PlayerWire>.OnValueChangedDelegate callback;
            public ulong lastEvent;
            public PlayerWire lastValue;
        }
        [Serializable] private sealed class Row {
            public string kind,run,tier,nonce;
            public int slot,pid,frame,playerId;
            public ulong eventId,objectId,ownerClient,worldTick;
            public bool server;
            public uint revision,sequence,jump;
            public int moveXBits,moveYBits,yawBits,pitchBits,verticalBits,speedBits;
            public bool grounded,sprint,vacuum,interact,frozen;
            public double frameTime,observerWall;
        }
        private readonly Entry[] entries=new Entry[Capacity];
        private readonly Dictionary<ulong,Binding> bindings=new Dictionary<ulong,Binding>();
        private readonly List<ulong> remove=new List<ulong>();
        private int count;
        private string error;
        private bool disposed;
        private NgoGameSession game;
        public int Count=>count;
        public string Error=>error;
        public void Observe(NgoGameSession current)
        {
            if(disposed)throw new ObjectDisposedException(nameof(RemoteWireJournal));
            game=current;
            remove.Clear();
            foreach(var pair in bindings)
                if(pair.Value.player==null||!pair.Value.player.IsSpawned||pair.Value.player.NetworkObjectId!=pair.Key)
                    remove.Add(pair.Key);
            foreach(var key in remove){Unbind(bindings[key]);bindings.Remove(key);}
            foreach(var p in current.Players)
            {
                if(p==null||!p.IsSpawned)continue;
                if(bindings.TryGetValue(p.NetworkObjectId,out var prior)) {
                    if(prior.player!=p)throw new InvalidOperationException("Network object identity reused while the observer was bound.");
                    continue;
                }
                var b=new Binding{player=p};
                b.callback=(old,value)=>Record(b,value,"change");
                bindings.Add(p.NetworkObjectId,b);
                p.Snapshot.OnValueChanged+=b.callback;
                Record(b,p.Snapshot.Value,"initial");
            }
            ThrowIfInvalid();
        }
        private void Record(Binding b,PlayerWire value,string kind)
        {
            if(disposed||error!=null)return;
            // All failures belong to diagnostics. Never throw into the producer callback.
            try {
                if(count>=Capacity){error="Wire journal capacity exceeded; evidence is incomplete.";return;}
                var p=b.player;
                if(p==null||!p.IsSpawned){error="Wire changed after its observed object was despawned.";return;}
                ulong id=(ulong)count+1;
                entries[count++]=new Entry{id=id,objectId=p.NetworkObjectId,owner=p.OwnerClientId,server=p.IsServer,
                    worldTick=game.Session.World.TickCount,frame=Time.frameCount,engineTime=Time.unscaledTimeAsDouble,
                    observerWall=Time.realtimeSinceStartupAsDouble,kind=kind,wire=value};
                b.lastEvent=id;b.lastValue=value;
            }catch(Exception e){error=e.ToString();}
        }
        public ulong EventFor(NetworkPlayerAdapter p,PlayerWire value)
        {
            ThrowIfInvalid();
            if(!bindings.TryGetValue(p.NetworkObjectId,out var b)||b.player!=p||!b.lastValue.Equals(value))
                throw new InvalidOperationException("Render sample lacks its exact observed NetworkVariable event.");
            return b.lastEvent;
        }
        public void ThrowIfInvalid(){if(error!=null)throw new InvalidOperationException(error);}
        public void Write(string path,string nonce,int slot,int pid)
        {
            ThrowIfInvalid();
            using(var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),
                new System.Text.UTF8Encoding(false),65536))
            for(int i=0;i<count;i++) {
                var e=entries[i];var w=e.wire;
                writer.WriteLine(JsonUtility.ToJson(new Row{kind=e.kind,run=w.Run.ToString(),tier=w.Tier.ToString(),
                    nonce=nonce,slot=slot,pid=pid,eventId=e.id,objectId=e.objectId,ownerClient=e.owner,server=e.server,
                    worldTick=e.worldTick,frame=e.frame,frameTime=e.engineTime,observerWall=e.observerWall,
                    revision=w.Revision,playerId=w.PlayerId,sequence=w.Sequence,jump=w.Jump,
                    moveXBits=BitConverter.SingleToInt32Bits(w.Move.x),moveYBits=BitConverter.SingleToInt32Bits(w.Move.y),
                    yawBits=BitConverter.SingleToInt32Bits(w.Yaw),pitchBits=BitConverter.SingleToInt32Bits(w.Pitch),
                    verticalBits=BitConverter.SingleToInt32Bits(w.Vertical),speedBits=BitConverter.SingleToInt32Bits(w.PlanarSpeed),
                    grounded=w.Grounded,sprint=w.Sprint,vacuum=w.Vacuum,interact=w.Interact,frozen=w.Frozen}));
            }
        }
        private static void Unbind(Binding b){if(b.player!=null)b.player.Snapshot.OnValueChanged-=b.callback;}
        public void Dispose(){if(disposed)return;disposed=true;foreach(var b in bindings.Values)Unbind(b);bindings.Clear();}
    }
}
#endif
