using System;
using UnityEngine;
namespace HowToSuck
{
    public enum EnemyAttackKind { Melee, Charge, Spit }
    public enum EnemyPhase { Idle, Move, Tell, Attack, Recover, Hit, Defeated }

    [CreateAssetMenu(menuName="How to Suck/Enemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public string EnemyId, DisplayName;
        public bool IsBoss;
        public EnemyAttackKind AttackKind;
        public int BaseHealth=40;
        public float MoveSpeed=2.2f, DetectionRange=8, AttackRange=1.5f, HomeRadius=12;
        public float TellSeconds=.65f, AttackSeconds=.8f, RecoverySeconds=1.2f;
        public float ChargeSpeed=4.5f, ProjectileSpeed=5, ProjectileLifetime=2.5f;
        public GameObject DefeatedCargoPrefab;
        public bool HasSweep;
        public BossChapterPattern ChapterIIPattern; // None preserves every existing definition.
        public float SweepRange=3.5f, SweepTellSeconds=1, SweepAttackSeconds=.7f;
        public bool TryValidate(out string error)
        {
            bool valid=!string.IsNullOrWhiteSpace(EnemyId)&&EnemyId==EnemyId.Trim()&&!string.IsNullOrWhiteSpace(DisplayName)&&
                Enum.IsDefined(typeof(EnemyAttackKind),AttackKind)&&BaseHealth>0&&BaseHealth<=10000&&
                Positive(MoveSpeed)&&Positive(DetectionRange)&&Positive(AttackRange)&&Positive(HomeRadius)&&
                Positive(TellSeconds)&&Positive(AttackSeconds)&&Positive(RecoverySeconds)&&Positive(ChargeSpeed)&&
                Positive(ProjectileSpeed)&&Positive(ProjectileLifetime)&&Positive(SweepRange)&&Positive(SweepTellSeconds)&&Positive(SweepAttackSeconds);
            valid &= Enum.IsDefined(typeof(BossChapterPattern),ChapterIIPattern) &&
                (ChapterIIPattern==BossChapterPattern.None || IsBoss&&!HasSweep&&
                 (ChapterIIPattern==BossChapterPattern.AlternatingLunge&&AttackKind==EnemyAttackKind.Melee ||
                  ChapterIIPattern==BossChapterPattern.RedirectedCharge&&AttackKind==EnemyAttackKind.Charge));
            if(IsBoss)
            {
                var cargo=DefeatedCargoPrefab!=null?DefeatedCargoPrefab.GetComponent<SuckableObject>():null;
                valid&=cargo!=null&&cargo.Definition!=null&&cargo.Definition.CargoRole==CargoRole.BossBody&&cargo.Definition.Value==0;
            }
            else valid&=DefeatedCargoPrefab==null;
            error=valid?null:"Enemy needs a stable identity, finite positive combat settings and a zero-value whole boss cargo prefab.";
            return valid;
        }
        internal static bool Positive(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x)&&x>0;
        public int HealthForCrew(int count)
        {
            if(count<1||count>4)throw new ArgumentOutOfRangeException(nameof(count));
            return Mathf.CeilToInt(BaseHealth*(count==1?1:count==2?1.35f:count==3?1.65f:1.9f));
        }
    }
    [Serializable] public sealed class EnemyEncounter
    {
        public string ContractId;
        public EnemySpawnPoint[] Spawns=Array.Empty<EnemySpawnPoint>();
    }
    public readonly struct EnemySnapshot
    {
        public readonly string RunId, EnemyId;
        public readonly ulong InstanceId;
        public readonly BossKey BossKey;
        public readonly int Health, MaximumHealth;
        public readonly EnemyPhase Phase;
        public readonly uint PhaseRevision, AttackRevision;
        public readonly double PhaseStartedAt, ObservedAt;
        public readonly bool Frozen, Sweep, ProjectileActive;
        public readonly Vector3 ProjectilePosition;
        public EnemySnapshot(string run,string enemyId,ulong id,BossKey boss,int health,int maximum,EnemyPhase phase,
            uint phaseRevision,uint attackRevision,double phaseStarted,double now,bool frozen,bool sweep,bool projectile,Vector3 projectilePosition)
        { RunId=run;EnemyId=enemyId;InstanceId=id;BossKey=boss;Health=health;MaximumHealth=maximum;Phase=phase;
            PhaseRevision=phaseRevision;AttackRevision=attackRevision;PhaseStartedAt=phaseStarted;ObservedAt=now;
            Frozen=frozen;Sweep=sweep;ProjectileActive=projectile;ProjectilePosition=projectilePosition; }
    }
}
