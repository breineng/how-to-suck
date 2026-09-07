#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    [Serializable] public sealed class GameplayFlatSupportSample
    {public Vector3 position;public float height,normalY;public int colliderId;}
    [Serializable] public sealed class GameplayFlatRouteReport
    {
        public bool clear,authority;
        public string reason,run,blocker,support;
        public int frame,playerId,itemCount,playerCount,supportColliderId;
        public ulong worldTick;
        public Vector3 origin,destination,lowerSphere,upperSphere;
        public float distance,radius,minHeight,maxHeight,minNormalY;
        public GameplayFlatSupportSample[] samples=Array.Empty<GameplayFlatSupportSample>();
    }
    // Read-only diagnostic geometry. Never sets poses/grounding, synchronizes transforms,
    // activates colliders, moves loot, changes time, or invokes a motor/physics step.
    public static class GameplayFlatRouteProbe
    {
        public static GameplayFlatRouteReport Check(NgoGameSession game,PlayerMotor player,Vector3 goal)
        {
            var result=new GameplayFlatRouteReport{reason="Uninitialized route",destination=goal,frame=Time.frameCount};
            if(game==null||player==null||game.Session==null||!game.Session.World.IsRunning)return result;
            result.authority=game.HasAuthority;result.run=game.Session.RunId;result.worldTick=game.Session.World.TickCount;
            result.playerId=player.PlayerId;result.itemCount=game.Items.Count;result.playerCount=game.Players.Count;
            result.origin=player.transform.position;
            var controller=player.GetComponent<CharacterController>();
            if(controller==null||!Finite(goal)||!Finite(result.origin)||Mathf.Abs(goal.y-result.origin.y)>.05f)
                return Fail(result,"Finite same-level player capsule required");
            if((player.transform.lossyScale-Vector3.one).sqrMagnitude>.000001f)
                return Fail(result,"Current unit-scale gameplay capsule required");
            Vector3 delta=goal-result.origin;delta.y=0;result.distance=delta.magnitude;
            if(result.distance>9.5f)return Fail(result,"Route exceeds the diagnostic 9.5m bound");
            int world=LayerMask.GetMask("World"),items=LayerMask.GetMask("Items");
            if(world==0||items==0)return Fail(result,"Authored World and Items layers required");
            // Preserve the real sphere centers, inflate radius by 4cm. Root/skin are measured, not corrected.
            result.radius=controller.radius+.04f;
            Vector3 center=result.origin+controller.center;
            float half=Mathf.Max(0,controller.height*.5f-controller.radius);
            result.lowerSphere=center-Vector3.up*half;result.upperSphere=center+Vector3.up*half;
            int mask=world|items;
            foreach(var overlap in Physics.OverlapCapsule(result.lowerSphere,result.upperSphere,result.radius,mask,QueryTriggerInteraction.Ignore))
                return Fail(result,"Initial capsule overlaps World/Items",Path(overlap.transform));
            Vector3 direction=result.distance>.0001f?delta/result.distance:Vector3.zero;
            if(result.distance>.0001f&&Physics.CapsuleCast(result.lowerSphere,result.upperSphere,result.radius,direction,out var obstacle,result.distance,mask,QueryTriggerInteraction.Ignore))
                return Fail(result,"Swept capsule intersects World/Items",Path(obstacle.collider.transform));
            // Guest CharacterControllers are intentionally disabled; use both actual PlayerObject poses explicitly.
            foreach(var other in game.Players)
            {
                if(other==null||other.Motor==player)continue;
                var body=other.GetComponent<CharacterController>();if(body==null)return Fail(result,"Other actual player capsule absent");
                float separation=PlanarSegmentDistance(other.transform.position,result.origin,goal);
                if(separation<controller.radius+body.radius+.12f&&Mathf.Abs(other.transform.position.y-result.origin.y)<controller.height)
                    return Fail(result,"Route approaches another actual PlayerObject",other.Motor.PlayerId.ToString());
            }
            var samples=new List<GameplayFlatSupportSample>();
            float foot=controller.radius*.75f;
            var offsets=new[]{Vector3.zero,Vector3.right*foot,Vector3.left*foot,Vector3.forward*foot,Vector3.back*foot};
            int steps=Mathf.Max(1,Mathf.CeilToInt(result.distance/.2f));
            result.minHeight=0;result.maxHeight=0;result.minNormalY=1;
            Collider support=null;
            for(int n=0;n<=steps;n++)foreach(var offset in offsets)
            {
                Vector3 point=result.origin+direction*(result.distance*n/steps)+offset;
                if(!Physics.Raycast(point+Vector3.up*.35f,Vector3.down,out var hit,.7f,mask,QueryTriggerInteraction.Ignore))
                    return FinishFailure(result,samples,"Missing actual footprint support");
                if((world&(1<<hit.collider.gameObject.layer))==0)return FinishFailure(result,samples,"Footprint support is an item");
                samples.Add(new GameplayFlatSupportSample{position=point,height=hit.point.y,normalY=hit.normal.y,colliderId=hit.collider.GetInstanceID()});
                result.minHeight=samples.Count==1?hit.point.y:Mathf.Min(result.minHeight,hit.point.y);result.maxHeight=samples.Count==1?hit.point.y:Mathf.Max(result.maxHeight,hit.point.y);result.minNormalY=Mathf.Min(result.minNormalY,hit.normal.y);
                if(support==null){support=hit.collider;result.supportColliderId=support.GetInstanceID();result.support=Path(support.transform);}
                if(hit.collider!=support)return FinishFailure(result,samples,"Route crosses a support collider seam");
                if(hit.normal.y<.999f||result.maxHeight-result.minHeight>.003f||Mathf.Abs(hit.point.y-result.origin.y)>.08f)
                    return FinishFailure(result,samples,"Support is not a constant-height horizontal footprint");
            }
            result.samples=samples.ToArray();result.clear=true;result.reason="Actual World+Items capsule, other players and one flat support collider clear";return result;
        }
        public static float PlanarSegmentDistance(Vector3 point,Vector3 start,Vector3 end)
        {var d=end-start;d.y=0;var p=point-start;p.y=0;float t=d.sqrMagnitude>.000001f?Mathf.Clamp01(Vector3.Dot(p,d)/d.sqrMagnitude):0;return (p-d*t).magnitude;}
        private static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static string Path(Transform value)
        {string name=value.name;for(var p=value.parent;p!=null;p=p.parent)name=p.name+"/"+name;return name;}
        private static GameplayFlatRouteReport Fail(GameplayFlatRouteReport value,string reason,string blocker="")
        {value.reason=reason;value.blocker=blocker;return value;}
        private static GameplayFlatRouteReport FinishFailure(GameplayFlatRouteReport value,List<GameplayFlatSupportSample> samples,string reason)
        {value.samples=samples.ToArray();return Fail(value,reason);}
    }
}
#endif
