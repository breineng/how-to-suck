using UnityEngine;

namespace HowToSuck
{
    // A continuous entrance check for a deliberately aimed, still-live shot. The
    // sphere's front half catches the leading part before it can pass the gate.
    internal static class TruckDeliveryGeometry
    {
        internal static bool CrossesOpening(TruckIntake truck,SuckableObject item,Vector3 from,Vector3 to)
        {
            var receiver=truck.Receiver;
            if(item==null||!receiver.Accepts(item)||!Finite(from)||!Finite(to))return false;
            Vector3 normal=receiver.Rotation*Vector3.forward,offset=from-receiver.Position,delta=to-from;
            if(Vector3.Dot(offset,normal)<-.025f)return false;
            float radius=receiver.AdmissionRadius+Mathf.Clamp(item.RequiredIntakeSize*.18f,.035f,.18f);
            float t=0,length=delta.magnitude;
            if(offset.sqrMagnitude>radius*radius)
            {
                if(length<.0001f||Vector3.Dot(delta,normal)>=0)return false;
                float along=Vector3.Dot(offset,delta/length),discriminant=along*along-offset.sqrMagnitude+radius*radius;
                if(discriminant<0)return false;
                t=(-along-Mathf.Sqrt(discriminant))/length;if(t<0||t>1)return false;
            }
            Vector3 entry=from+delta*t;
            if(Vector3.Dot(entry-receiver.Position,normal)<-.025f)return false;
            return Clear(truck,item,from,entry)&&Clear(truck,item,entry,receiver.Position);
        }
        private static bool Clear(TruckIntake truck,SuckableObject item,Vector3 from,Vector3 to)
        {
            Vector3 delta=to-from;float length=delta.magnitude;if(length<.001f)return true;
            foreach(var hit in Physics.RaycastAll(from,delta/length,length,LayerMask.GetMask("World","Items","Enemies"),QueryTriggerInteraction.Ignore))
                if(hit.rigidbody!=item.Body&&!hit.collider.transform.IsChildOf(truck.transform))return false;
            return true;
        }
        private static bool Finite(Vector3 p)=>ItemFireRules.Finite(p.x)&&ItemFireRules.Finite(p.y)&&ItemFireRules.Finite(p.z);
    }
}
