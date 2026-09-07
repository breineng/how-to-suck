#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    [Serializable] public sealed class GameplayCommand
    {
        public string nonce,run,kind;
        public int slot,sequence;
        public int routePlayerId; // flat-probe host query only; flat-move always uses this process owner.
        public double startAtServerTime;
        public float seconds=1,stopDistance=.18f;
        public float sweepYaw=35,sweepPitch=20,sweepHz=.3f;
        public ulong itemId,enemyId;
        public string contractId;
        public Vector3 destination;
        public bool forward,backward,left,right,vacuum,interact,jump,sprint,fire;
    }
    [Serializable] public sealed class GameplayObservation
    {
        public string nonce,utc,run,phase,terminal,lastCommandStatus,lastCommandDetail,renderEvidence,scene;
        public int slot,pid,frame,lastSequence,activeSequence,playerCount,itemCount;
        public ulong clientId,worldTicks,inputFrames;
        public double realtime,serverTime,startedAt,deadline,observedAt,hold,remaining;
        public long money,quota,balance,payout;
        public bool authority,running,localCampaignExists,allInside,localInside,pendingPayout,canStart,localReady;
        public GameplayPlayerObservation[] players;
        public GameplayItemObservation[] items;
        public GameplayCollectionObservation[] collections;
        public Vector3[] clearMoveDestinations;
        public Vector3 extractionCenter,extractionSize,truckSource,truckForward;
        public string[] runtimeErrors;
        public ulong observedTargetId,targetSharedSelectionTicks,targetLastSharedTick;
        public int boundsLostCount;
        public Vector3 boundsMinimum,boundsMaximum;
        public GameplayTargetSelection[] targetSelections,targetLastSharedSelections;
        public string targetWitnessScope;
        public LiveGuestObservation current;
        public bool actionFire;
        public int focusReacquisitions,cloneBindings,boundActionInstanceId;
        public ulong neutralDynamicFrames;
        public bool readerFocused,actionsEnabled,actionVacuum,actionInteract,neutralReleaseObserved;
        public Vector2 actionMove;
    }
    [Serializable] public sealed class GameplayTargetSelection
    {
        public ulong targetId,clientId,worldTick;
        public int playerId;
        public bool sourceActive,bodySimulating,selectedBySharedCollector,intakeGeometryEligible;
        public bool sourcePathClear,targetSurfaceVisible;
        public float distance;
        public Vector3 source,forward,selectedPoint,visiblePoint;
    }
    [Serializable] public sealed class GameplayPlayerObservation
    {
        public ulong clientId,objectId;
        public int playerId;
        public uint readerSequence;
        public Vector2 readerMove,motorMove;
        public bool readerVacuum,readerInteract;
        public Vector3 position;
        public float yaw,pitch;
        public uint sequence,jumpSequence;
        public bool owner,controllerEnabled,motorAuthority,inputInitialized,inputEnabled,inputAvailable,menu,vacuum,interact,rigPresent;
    }
    [Serializable] public sealed class GameplayItemObservation
    {
        public ulong id,objectId;
        public string type,state;
        public Vector3 position,center,size;
        public bool kinematic,authority,frozen,playerEligible,truckEligible;
        public float requiredSize;
        public long value;
    }
    [Serializable] public sealed class GameplayCollectionObservation
    {public string run,type;public ulong id;public int playerId,intakeId;public long value;public bool truck;}
    [Serializable] public sealed class GameplayCommandResult
    {
        public string nonce,run,kind,status,detail,utc;
        public int slot,pid,sequence;
        public double startAt,endAt;
        public Vector3 startPosition,endPosition;
        public long startMoney,endMoney;
        public ulong queuedInputFrames;
        public GameplayFlatRouteReport flatRoute; // Actual read-only native route receipt; absent for legacy commands.
        public int flatRouteChecks; // Successful/failed moving native rechecks; no physics mutations.
    }
}
#endif
