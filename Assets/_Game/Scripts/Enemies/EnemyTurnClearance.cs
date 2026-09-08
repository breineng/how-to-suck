using UnityEngine;

namespace HowToSuck
{
    // Attack preparation must be able to reach its facing pose, not just fit in
    // the already-rotated, raised volume used to check the airborne leap.
    public static class EnemyTurnClearance
    {
        public static bool CanTurn(BoxCollider shape,Rigidbody body,Vector3 direction,string run,float degreesPerStep)
        {
            direction.y=0;
            if(direction.sqrMagnitude<.0001f)return true;
            Quaternion target=Quaternion.LookRotation(direction),from=body.rotation;
            float step=Mathf.Clamp(degreesPerStep,1,10);
            int count=Mathf.CeilToInt(Quaternion.Angle(from,target)/step);
            for(int i=0;i<count;i++)
            {
                var next=Quaternion.RotateTowards(from,target,step);
                if(!CanStep(shape,body,from,next,run))return false;
                from=next;
            }
            return true;
        }

        public static bool CanStep(BoxCollider shape,Rigidbody body,Quaternion from,Quaternion next,string run)
        {
            if(Quaternion.Angle(from,next)<=.001f)return true;
            float radius=shape.center.magnitude+shape.size.magnitude*.5f;
            float travel=2*radius*Mathf.Sin(Quaternion.Angle(from,next)*Mathf.Deg2Rad*.5f);
            foreach(var collider in Physics.OverlapBox(body.position+from*shape.center,
                shape.size*.5f+Vector3.one*(travel+.025f),from,LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore))
            {
                if(collider.transform.IsChildOf(body.transform)||collider.attachedRigidbody==null)continue;
                var item=collider.attachedRigidbody.GetComponent<SuckableObject>();
                if(item!=null&&item.RunId==run&&item.State==SuckableState.Available&&collider.attachedRigidbody.mass>body.mass)return false;
            }
            foreach(var collider in Physics.OverlapBox(body.position+next*shape.center,shape.size*.5f,next,
                LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))
            {
                if(!Physics.ComputePenetration(shape,body.position,next,collider,collider.transform.position,collider.transform.rotation,out _,out float depth)||depth<.015f)continue;
                if(Physics.ComputePenetration(shape,body.position,body.rotation,collider,collider.transform.position,collider.transform.rotation,out _,out float previous)&&depth<previous-.001f)continue;
                return false;
            }
            return true;
        }
    }
}
