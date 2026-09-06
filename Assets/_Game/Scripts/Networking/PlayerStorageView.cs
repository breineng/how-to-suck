using System;
using UnityEngine;
namespace HowToSuck
{
    // The ordinary HUD can read LocalPlayer.GetComponent<PlayerStorageView>().Value.
    // There is no queue, physics or purchase authority here, and no static state survives an actor/run.
    [DisallowMultipleComponent]
    public sealed class PlayerStorageView:MonoBehaviour
    {
        public PlayerStorageSnapshot Value {get;private set;}
        public event Action Changed;
        private string run;private int owner;
        private PlayerStorageSnapshot accepted;
        public void Bind(string runId,int ownerId)
        {
            GameplayReplicaPolicy.RequireRun(runId);
            if(!GameplayReplicaPolicy.Player(ownerId)||run!=null&&(run!=runId||owner!=ownerId))throw new InvalidOperationException("Storage view identity cannot change in place.");
            run=runId;owner=ownerId;
        }
        public void Apply(PlayerStorageSnapshot value)
        {
            if(run==null||!value.IsKnown||value.RunId!=run||value.OwnerId!=owner)throw new InvalidOperationException("Storage view received another actor/run.");
            accepted=value; // Retain validated facts while disabled; never infer an empty inventory.
            if(!isActiveAndEnabled)return;
            PublishValue(value);
        }
        public void Clear(){run=null;owner=0;accepted=default;ClearValue();}
        private void PublishValue(PlayerStorageSnapshot value){if(Value.SameValues(value))return;Value=value;Changed?.Invoke();}
        private void OnEnable(){if(accepted.IsKnown)PublishValue(accepted);}
        private void ClearValue(){if(!Value.IsKnown)return;Value=default;Changed?.Invoke();}
        private void OnDisable()=>ClearValue();
        private void OnDestroy()=>Clear();
    }
}
