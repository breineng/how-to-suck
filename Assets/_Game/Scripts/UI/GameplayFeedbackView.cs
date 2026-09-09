using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HowToSuck.Audio;

namespace HowToSuck
{
    [DefaultExecutionOrder(12000),DisallowMultipleComponent]
    public sealed class GameplayFeedbackView:MonoBehaviour
    {
        public GameObject Root,BossPanel,FullPanel;
        public Image DamageVignette,HitMarker,BossFill,RepairFill;
        public TMP_Text BossName,BossHealth,PickupText,RepairText,FullText;
        public RectTransform Reticle;
        public GameObject DownedPanel,ChargePanel;
        public TMP_Text DownedText,ReviveHint,ChargeText;
        public Image ReviveFill,ChargeFill;
        GameAudioRoot audioRoot;
        SessionRoot session;
        PlayerMotor player;
        PlayerView view;
        PlayerStorageSnapshot storage;
        ParticleSystem suction;
        float damage,hit,pickup,recoil,fullSoundAt,particleAt;
        bool grounded,knownGround;
        float airborneTime,airbornePeak,groundHeight;
        PlayerMotor movementPlayer;
        uint movementRevision;
        string run;
        double scanAt;
        EnemyActor boss;
        AudioSource ambient;
        Camera kickedCamera;
        Quaternion cameraRotation;
        bool kicked;
        PlayerMotor[] crew=System.Array.Empty<PlayerMotor>();
        float crewScan;
        void Update(){RestoreCamera();}
        void LateUpdate()
        {
            if(audioRoot!=GameAudioRoot.Current)
            {
                if(audioRoot!=null)audioRoot.Presented-=OnFact;
                audioRoot=GameAudioRoot.Current;session=audioRoot!=null?audioRoot.Session:null;
                if(audioRoot!=null)audioRoot.Presented+=OnFact;
            }
            player=session!=null?session.LocalPlayer:null;
            var input=player!=null?player.GetComponent<PlayerInputReader>():null;
            bool playing=audioRoot!=null&&audioRoot.Playing&&player!=null;
            bool visible=playing&&(input==null||!input.MenuOpen);
            if(Root!=null)Root.SetActive(visible);
            if(!playing){if(ambient!=null)ambient.Stop();knownGround=false;return;}
            view=player.GetComponent<PlayerView>();
            if(run!=session.RunId){run=session.RunId;storage=default;boss=null;scanAt=0;damage=hit=recoil=pickup=0;knownGround=false;StartAmbient();}
            if(ambient!=null){ambient.volume=audioRoot.SourceBusGain(AudioBus.Impacts)*.18f;if(!ambient.isPlaying)ambient.Play();}
            if(!visible){knownGround=false;return;}
            float dt=Time.unscaledDeltaTime;
            damage=Mathf.MoveTowards(damage,0,dt*1.2f);hit=Mathf.MoveTowards(hit,0,dt*4);pickup=Mathf.MoveTowards(pickup,0,dt*.9f);recoil=Mathf.MoveTowards(recoil,0,dt*6);
            Tint(DamageVignette,new Color(.85f,0,.015f,damage*.82f));Tint(HitMarker,new Color(1,.94f,.8f,hit));
            if(Reticle!=null)Reticle.localScale=Vector3.one*(1+recoil*.5f+hit*.2f);
            if(view!=null&&view.Camera!=null&&recoil>0)
            {
                kickedCamera=view.Camera;cameraRotation=kickedCamera.transform.localRotation;kicked=true;
                kickedCamera.transform.localRotation=cameraRotation*Quaternion.Euler(-recoil*2f,Mathf.Sin(Time.unscaledTime*55)*recoil*.16f,0);
                if(view.ViewModelRoot!=null)view.ViewModelRoot.position-=kickedCamera.transform.forward*(recoil*.11f);
            }
            var current=player.GetComponent<PlayerStorageView>()?.Value??default;
            if(current.IsKnown&&storage.IsKnown&&current.RunId==storage.RunId&&current.Count>storage.Count)
            {
                pickup=1;audioRoot.Action(SfxId.ItemStored,player.transform.position);
                if(PickupText!=null)PickupText.text="ПРЕДМЕТ СОБРАН  +1";
            }
            storage=current;
            if(PickupText!=null)PickupText.color=new Color(.76f,1,.78f,pickup);
            bool full=current.IsKnown&&current.Count+current.Reserved>=current.Capacity&&player.LastIntent.VacuumHeld;
            var vacuum=player.GetComponent<VacuumEmitter>();
            var pull=vacuum!=null&&player.LastIntent.VacuumHeld?vacuum.PullMode:SuctionMode.Idle;
            bool hauling=pull==SuctionMode.Holding||pull==SuctionMode.NeedHelp;
            if(FullPanel!=null)FullPanel.SetActive(full||hauling);
            if(FullText!=null)FullText.text=hauling?
                (pull==SuctionMode.NeedHelp?"ТЯЖЕЛО — НУЖНА ПОМОЩЬ":"ПРЕДМЕТ УДЕРЖИВАЕТСЯ")+$" · ТЯНУТ: {vacuum.PullingPlayers}\n"+(full?"Хранилище полно · несите предмет к грузовику":"Доставьте предмет к соплу грузовика"):
                "ХРАНИЛИЩЕ ПОЛНО\nМожно переносить предметы · выстрелите, чтобы освободить слот";
            if(full&&!hauling&&Time.unscaledTime>=fullSoundAt){fullSoundAt=Time.unscaledTime+1.1f;audioRoot.Action(SfxId.VacuumBlocked,player.transform.position);}
            var suit=player.GetComponent<PlayerSuitView>()?.Value??default;
            if(Time.unscaledTime>=crewScan){crewScan=Time.unscaledTime+.25f;crew=FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);}
            PlayerMotor fallen=null;float nearest=EnemySimulationService.ReviveDistance*EnemySimulationService.ReviveDistance;
            foreach(var member in crew)
            {
                if(member==null||member==player||!member.IsDowned)continue;
                float distance=(member.transform.position-player.transform.position).sqrMagnitude;
                if(distance>nearest||Physics.Linecast(player.transform.position+Vector3.up*.9f,member.transform.position+Vector3.up*.45f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))continue;
                nearest=distance;fallen=member;
            }
            string interactKey=InlineKeycaps.Key(input!=null?input.GameplayBindingDisplay("Interact"):"E");
            bool down=player.IsDowned,solo=crew.Length<=1;
            if(DownedPanel!=null)DownedPanel.SetActive(down);
            InlineKeycaps.Set(DownedText,solo?$"ВЫ ВЫВЕДЕНЫ ИЗ СТРОЯ\nУдерживайте {interactKey} 3 секунды — подняться\n25% костюма · −15 секунд":suit.State.RepairProgress>0?"ВАС ПОДНИМАЮТ\nПосле подъёма — 25% костюма":"ВЫ ВЫВЕДЕНЫ ИЗ СТРОЯ\nДождитесь помощи товарища");
            if(ReviveHint!=null){ReviveHint.gameObject.SetActive(!down&&fallen!=null);InlineKeycaps.Set(ReviveHint,$"Удерживайте {interactKey} 3 секунды — поднять товарища · 25% HP");}
            float revive=down?suit.State.RepairProgress:fallen!=null?(fallen.GetComponent<PlayerSuitView>()?.Value.State.RepairProgress??0):0;
            if(ReviveFill!=null){ReviveFill.transform.parent.gameObject.SetActive(down||fallen!=null);ReviveFill.fillAmount=revive;}
            float charge=player.FireCharge;
            if(ChargePanel!=null)ChargePanel.SetActive(!down&&charge>0);
            if(ChargeFill!=null)ChargeFill.fillAmount=charge;
            if(ChargeText!=null)ChargeText.text=$"ЗАРЯД {Mathf.RoundToInt(charge*100)}% · УРОН ×{1+1.5f*charge:0.0}";
            bool atTruck=session.World!=null&&session.World.IsPlayerInExtraction(player.PlayerId)&&!session.ContractState.ObjectivesComplete;
            if(RepairText!=null)
            {
                RepairText.gameObject.SetActive(atTruck&&!down&&fallen==null);
                InlineKeycaps.Set(RepairText,suit.State.RepairCharges<=0?"РЕМОНТ ИСЧЕРПАН · следующий заряд за 25% квоты":suit.State.Health>=100?$"КОСТЮМ ЦЕЛ · зарядов: {suit.State.RepairCharges}":$"Удерживайте {interactKey} — ремонт +45 HP · зарядов: {suit.State.RepairCharges}");
            }
            if(RepairFill!=null){RepairFill.transform.parent.gameObject.SetActive(atTruck&&!down&&fallen==null&&suit.State.RepairProgress>0);RepairFill.fillAmount=suit.State.RepairProgress;}
            if(Time.unscaledTimeAsDouble>=scanAt)
            {
                scanAt=Time.unscaledTimeAsDouble+.4;
                if(boss==null)foreach(var actor in FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))if(actor.RunId==run&&actor.BossKey.IsValid){boss=actor;break;}
            }
            bool showBoss=boss!=null&&boss.Health>0;
            if(BossPanel!=null)BossPanel.SetActive(showBoss);
            if(showBoss){BossName.text=boss.Definition.DisplayName+(boss.Health<=boss.MaximumHealth/2?" · ЯРОСТЬ":"");BossHealth.text=$"{boss.Health} / {boss.MaximumHealth}";BossFill.fillAmount=Mathf.MoveTowards(BossFill.fillAmount,(float)boss.Health/boss.MaximumHealth,dt*2);}
            if(!down){Steps(Time.deltaTime);SuctionParticles();}else knownGround=false;
        }
        void OnFact(CommittedAudioFact fact)
        {
            if(session==null||player==null||fact.Run!=session.RunId)return;
            bool local=fact.Owner==player.PlayerId;
            if(fact.Kind==CommittedAudioKind.ShotLaunch)
            {
                if(local)recoil=1;
                var position=local?player.GetComponent<IntakeReceiver>()?.PresentationTarget?.position??fact.Position:fact.Position;
                CombatParticles.Burst(position,new Color(.8f,.9f,1),35,5,.11f,.3f);
            }
            if(fact.Kind==CommittedAudioKind.EnemyHit){if(local)hit=1;CombatParticles.Burst(fact.Position,new Color(1,.26f,.04f),25,3,.14f,.45f);}
            if(fact.Kind==CommittedAudioKind.SuitHit&&local)damage=1;
            if(fact.Kind==CommittedAudioKind.SuitRecovered&&local){pickup=1;if(PickupText!=null)PickupText.text="КОСТЮМ ВОССТАНОВЛЕН";}
        }
        void Steps(float dt)
        {
            if(movementPlayer!=player||movementRevision!=player.PresentationResetRevision)
            {movementPlayer=player;movementRevision=player.PresentationResetRevision;knownGround=false;}
            bool onGround=player.IsGrounded;
            if(knownGround&&grounded&&!onGround&&player.VerticalVelocity>1)audioRoot.Action(SfxId.Jump,player.transform.position);
            float gain=SampleLanding(onGround,player.transform.position.y,dt);
            if(gain>0)audioRoot.Action(SfxId.Land,player.transform.position,false,gain);
        }
        float SampleLanding(bool onGround,float height,float dt)
        {
            if(!knownGround)
            {
                grounded=onGround;knownGround=true;groundHeight=height;
                airbornePeak=height;airborneTime=-1;return 0;
            }
            float gain=0;
            if(!onGround)
            {
                if(grounded){airbornePeak=Mathf.Max(groundHeight,height);airborneTime=0;}
                if(airborneTime>=0){airbornePeak=Mathf.Max(airbornePeak,height);airborneTime+=dt;}
            }
            else
            {
                // Ground contact can flicker on thresholds and stairs. Measure the
                // actual descent from the apex; the motor already reset impact speed.
                float drop=airbornePeak-height;
                if(!grounded&&airborneTime>=.12f&&drop>=.45f)
                    gain=Mathf.Lerp(.35f,.8f,Mathf.InverseLerp(.45f,2f,drop));
                groundHeight=height;airborneTime=-1;
            }
            grounded=onGround;
            return gain;
        }
        void SuctionParticles()
        {
            if(!player.LastIntent.VacuumHeld||Time.unscaledTime<particleAt)return;
            particleAt=Time.unscaledTime+.035f;
            if(suction==null)suction=CombatParticles.Create("Suction dust",player.transform.position,.25f);
            var receiver=player.GetComponent<IntakeReceiver>();var anchor=receiver!=null&&receiver.PresentationTarget!=null?receiver.PresentationTarget:player.NozzleAnchor;if(anchor==null)return;
            for(int i=0;i<3;i++)
            {var p=anchor.position+anchor.forward*Random.Range(.3f,1.2f)+anchor.right*Random.Range(-.12f,.12f)+anchor.up*Random.Range(-.12f,.12f);var emit=new ParticleSystem.EmitParams{position=p,velocity=(anchor.position-p)/.22f,startLifetime=.22f,startSize=Random.Range(.016f,.045f),startColor=new Color(.8f,.88f,.93f,.7f)};suction.Emit(emit,1);}
        }
        void StartAmbient()
        {
            if(ambient==null)ambient=GameAudioRoot.CreateSource(transform,"Location ambience");
            string id=session.ContractState.ContractId??"";var entry=audioRoot.Bindings.Find(id.StartsWith("old_house")?SfxId.AmbientHouse:id.StartsWith("supermarket")?SfxId.AmbientMarket:SfxId.AmbientWarehouse);
            if(entry==null||entry.Clip==null)return;ambient.Stop();ambient.clip=entry.Clip;ambient.loop=true;ambient.spatialBlend=0;ambient.outputAudioMixerGroup=audioRoot.Bindings.Impacts;ambient.Play();
        }
        void RestoreCamera(){if(kicked&&kickedCamera!=null)kickedCamera.transform.localRotation=cameraRotation;kicked=false;}
        static void Tint(Graphic image,Color color){if(image!=null)image.color=color;}
        void OnDisable(){RestoreCamera();knownGround=false;if(audioRoot!=null)audioRoot.Presented-=OnFact;audioRoot=null;if(ambient!=null)ambient.Stop();}
        void OnDestroy(){if(suction!=null)Destroy(suction.gameObject);}
    }
}
