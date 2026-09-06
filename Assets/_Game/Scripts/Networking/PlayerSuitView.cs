using System;
using UnityEngine;
namespace HowToSuck
{
    // HUD-only accessor for the existing owner actor. No inventory, damage, healing or input writes.
    [DisallowMultipleComponent]
    public sealed class PlayerSuitView:MonoBehaviour
    {
        public PlayerSuitPresentation Value {get;private set;}
        public event Action Changed;
        private string run;private int owner;
        private PlayerSuitPresentation accepted;private bool hasAccepted;
        public void Bind(string runId,int ownerId)
        {
            GameplayReplicaPolicy.RequireRun(runId);
            if(!GameplayReplicaPolicy.Player(ownerId)||run!=null&&(run!=runId||owner!=ownerId))
                throw new InvalidOperationException("Suit view identity cannot change in place; clear the old binding first.");
            run=runId;owner=ownerId;
        }
        public void RequireAcceptable(PlayerSuitPresentation next)
        {
            if(run==null||!next.IsKnown||next.State.RunId!=run||next.State.OwnerId!=owner)
                throw new InvalidOperationException("Suit view received another owner/run.");
            PlayerSuitPresentation.RequireAdvance(hasAccepted?accepted:default,next);
        }
        public void Apply(PlayerSuitPresentation next)
        {
            RequireAcceptable(next);accepted=next;hasAccepted=true;
            if(isActiveAndEnabled)Publish(next);
        }
        private void Publish(PlayerSuitPresentation next){if(Value.SameValues(next))return;Value=next;Changed?.Invoke();}
        private void Hide(){if(!Value.IsKnown)return;Value=default;Changed?.Invoke();}
        public void Clear(){run=null;owner=0;hasAccepted=false;accepted=default;Hide();}
        private void OnDisable()=>Hide();
        private void OnEnable(){if(run!=null&&hasAccepted){RequireAcceptable(accepted);Publish(accepted);}}
        private void OnDestroy()=>Clear();
    }
}
