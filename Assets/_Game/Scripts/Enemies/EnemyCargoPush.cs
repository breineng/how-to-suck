using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    public static class EnemyCargoPush
    {
        // Velocity change is deliberately independent of cargo mass. It is a shove,
        // never a teleport; the item's own collision solver still stops it at walls.
        public static void Push(BoxCollider shape,Rigidbody enemy,Vector3 direction,float dt,string run,bool boss)
        {
            direction.y=0;
            if(dt<=0||direction.sqrMagnitude<.001f)return;
            direction.Normalize();var seen=new HashSet<Rigidbody>();
            foreach(var c in Physics.OverlapBox(enemy.position+enemy.rotation*shape.center+direction*.28f,
                shape.size*.5f+new Vector3(.3f,.08f,.3f),enemy.rotation,LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore))
            {
                var body=c.attachedRigidbody;
                if(body==null||body.isKinematic||!seen.Add(body))continue;
                var item=body.GetComponent<SuckableObject>();
                if(item==null||!item.HasPhysicsAuthority||item.RunId!=run||item.WorldFrozen||item.State!=SuckableState.Available)continue;
                Vector3 away=body.worldCenterOfMass-enemy.worldCenterOfMass;away.y=0;
                // A small sideways component clears piles instead of compressing them into one line.
                Vector3 push=(direction+Vector3.ProjectOnPlane(away.normalized,direction)*.65f).normalized;
                float gap=Mathf.Max(0,(boss?7:5)-Vector3.Dot(body.linearVelocity,push));
                body.AddForce(push*Mathf.Min(gap,(boss?42:30)*dt),ForceMode.VelocityChange);
                body.WakeUp();
            }
        }
    }
}
