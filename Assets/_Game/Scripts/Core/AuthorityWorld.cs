using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
    public sealed class AuthorityWorld : MonoBehaviour
    {
        public bool HasAuthority { get; private set; }
        public bool IsRunning { get; private set; }
        public ulong TickCount { get; private set; }
        public int PlayerCount => players.Count;
        public IReadOnlyDictionary<int,PlayerMotor> Players => players;
        public LootRegistry Loot { get; } = new LootRegistry();
        public IngestionService Ingestion { get; private set; }
        private readonly Dictionary<int,PlayerMotor> players=new Dictionary<int,PlayerMotor>();
        private readonly Dictionary<int,PlayerIntentBuffer> inputs=new Dictionary<int,PlayerIntentBuffer>();
        private readonly Dictionary<int,VacuumEmitter> emitters=new Dictionary<int,VacuumEmitter>();
        private readonly List<IntakeReceiver> receivers=new List<IntakeReceiver>();
        private SuctionSystem suction;
        private IWorldSpawner spawner;
        public void Initialize(bool authority)
        {
            HasAuthority=authority;suction=new SuctionSystem(Loot);
            Ingestion=new IngestionService(Loot,suction,instance=>spawner?.Despawn(instance));
        }
        public void PrepareWorld(LevelContext level,IWorldSpawner worldSpawner,VacuumDefinition vacuum)
        {
            spawner=worldSpawner;
            Loot.Begin(Guid.NewGuid().ToString("N"));Ingestion.Begin(Loot.RunId);
            foreach(var spawn in level.LootSpawns)
            {
                var instance=spawner.Spawn(spawn.Prefab,spawn.transform.position,spawn.transform.rotation);
                var item=instance.GetComponent<SuckableObject>();
                try{Loot.Register(item);item.SetWorldFrozen(true);}
                catch{spawner.Despawn(instance);throw;}
            }
            foreach(var emitter in emitters.Values)if(emitter!=null)emitter.Definition=vacuum;
        }
        public void SetRunning(bool running)
        {
            IsRunning=running;Ingestion?.SetRunning(running);
            foreach(var item in Loot.Items.Values)if(item!=null)item.SetWorldFrozen(!running);
            if(!running)foreach(var source in emitters.Values)if(source!=null)source.Active=false;
        }
        public void RegisterPlayer(PlayerMotor motor)
        {
            players.Add(motor.PlayerId,motor);
            var buffer=new PlayerIntentBuffer();buffer.Clear(motor.transform.eulerAngles.y,0);inputs.Add(motor.PlayerId,buffer);
            var emitter=motor.GetComponent<VacuumEmitter>();
            if(emitter!=null){emitter.Source=motor.NozzleAnchor;emitter.OriginGuard=motor.AuthoritativeAim;emitter.EmitterId=motor.PlayerId;emitters.Add(motor.PlayerId,emitter);}
            var receiver=motor.GetComponent<IntakeReceiver>();
            if(receiver!=null){receiver.IntakeId=motor.PlayerId;receiver.PlayerId=motor.PlayerId;receivers.Add(receiver);}
        }
        public void RemovePlayer(int id)
        {
            players.Remove(id);inputs.Remove(id);emitters.Remove(id);
            receivers.RemoveAll(r=>r==null || (!r.IsTruck && r.PlayerId==id));
        }
        public bool SubmitIntent(int id,PlayerIntent intent)=>HasAuthority && IsRunning && inputs.TryGetValue(id,out var buffer) && buffer.TrySubmit(intent,Time.realtimeSinceStartupAsDouble);
        public void Clear()
        {
            SetRunning(false);TickCount=0;
            foreach(var item in Loot.Items.Values)if(item!=null)spawner?.Despawn(item.gameObject);
            Ingestion?.Clear();Loot.Clear();players.Clear();inputs.Clear();emitters.Clear();receivers.Clear();
        }
        private void FixedUpdate()
        {
            if(!HasAuthority || !IsRunning)return;
            TickCount++;double now=Time.realtimeSinceStartupAsDouble;suction.BeginStep();
            foreach(var pair in players)
            {
                if(pair.Value==null)continue;
                pair.Value.Step(inputs[pair.Key].Read(now),Time.fixedDeltaTime);
                if(emitters.TryGetValue(pair.Key,out var emitter)){emitter.Active=pair.Value.LastIntent.VacuumHeld;suction.Apply(emitter);}
            }
            Ingestion.Step(now,receivers);
        }
    }
}