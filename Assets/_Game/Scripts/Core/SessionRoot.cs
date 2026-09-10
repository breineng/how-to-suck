using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class SessionRoot : MonoBehaviour
    {
        public AuthorityWorld World;
        public string MenuSceneName = "MainMenu";
        public SessionPhase Phase { get; private set; } = SessionPhase.Booting;
        public GameCatalog Catalog { get; private set; }
        public ContractDefinition CurrentContract { get; private set; }
        private string selectedLobbyContractId="";
        public string SelectedLobbyContractId=>HasAuthority?selectedLobbyContractId:replica?.SelectedContractId??"";
        public ContractDefinition SelectedLobbyContract {
            get {if(Catalog?.Contracts!=null)foreach(var contract in Catalog.Contracts)
                if(contract!=null&&contract.ContractId==SelectedLobbyContractId)return contract;return null;}
        }
        public bool SelectLobbyContract(string requested)
        {
            var ids=new List<string>();if(Catalog?.Contracts!=null)foreach(var contract in Catalog.Contracts)if(contract!=null)ids.Add(contract.ContractId);
            if(!ContractSelectionPolicy.TrySelect(IsInitialized,HasAuthority,Phase,ids,requested,out string selected))return false;
            if(selectedLobbyContractId==selected)return true;
            selectedLobbyContractId=selected;Changed?.Invoke();return true;
        }
        public bool StartSelectedContract()=>StartContract(SelectedLobbyContract);

        public LevelContext CurrentLevel { get; private set; }
        public PlayerMotor LocalPlayer { get; private set; }
        public CampaignState Campaign => Progression?.Campaign;
        public SaveOpenResult CampaignOpenStatus { get; private set; }
        private SaveRepository campaignRepository;
        public bool HasPendingSave => Progression != null && Progression.HasPending;
        public ProgressionService Progression { get; private set; }
        public ContractController Controller { get; private set; }
        private INetworkSessionDriver networkDriver;
        private SessionReplica replica;
        public bool HasAuthority => networkDriver == null || networkDriver.HasAuthority;
        public int DisplayedCrewSize => Mathf.Clamp((driver as IEncounterCrewProvider)?.ConnectedCrewSize ?? 1,1,4);
        public long DisplayedQuota(ContractDefinition contract) => contract == null ? 0 :
            CampaignContractAccess.IsKnown(contract.ContractId) ? CampaignBalance.QuotaForCrew(contract.Quota,DisplayedCrewSize) : contract.Quota;
        public float DisplayedTimeLimit(ContractDefinition contract) => contract == null ? 0 :
            CampaignContractAccess.IsKnown(contract.ContractId) ? CampaignBalance.TimeLimitForCrew(contract.ContractId,DisplayedCrewSize) : contract.TimeLimitSeconds;
        public long DisplayedBalance => HasAuthority ? Campaign?.Balance ?? 0 : replica?.Balance ?? 0;
        public string DisplayedTierId => HasAuthority ? Campaign?.CurrentTierId ?? "" : replica?.CurrentTierId ?? "";
        public int? DisplayedExtraSlots => HasAuthority ? Campaign?.PurchasedExtraSlots :
            replica != null && replica.HasCampaignProgression && CampaignCapacityRules.ValidBonus(replica.PurchasedExtraSlots) ? (int?)replica.PurchasedExtraSlots : null;
        public bool DisplayedContractUnlocked(string id) => HasAuthority ? Progression != null && Progression.IsContractUnlocked(id) :
            replica != null && replica.HasCampaignProgression && CampaignContractAccess.IsUnlocked(id,CampaignContractAccess.FromMask(replica.ClearedContractMask),replica.LegacyContractAccess);
        public bool DisplayedSavePending => HasAuthority ? HasPendingSave : replica?.PendingPayout ?? false;
        public bool CanReturnToMenu => !HasAuthority || Progression != null && !Progression.HasPending;
        public bool CanStartContract => HasAuthority && Phase == SessionPhase.Lobby && Progression != null && Progression.CanStartRun && (networkDriver == null || networkDriver.CanBeginContract);
        public bool WaitingForLobbyReadiness => HasAuthority && Phase == SessionPhase.Lobby && Progression != null && Progression.CanStartRun && networkDriver != null && !networkDriver.CanBeginContract;
        public bool CanStartNewCampaign => IsInitialized && HasAuthority && Phase == SessionPhase.Lobby &&
            campaignRepository != null && Progression != null && Progression.CanStartRun;
        public bool CanReturnToLobby => HasAuthority && Phase == SessionPhase.Results && CanReturnToMenu;
        public bool ReturnToLobby() => CanReturnToLobby && ReturnToMenu();
        public ContractState ContractState => HasAuthority ? (Controller != null ? Controller.State : default) : (replica?.State ?? default);
        public ContractResult Result { get; private set; }
        public string RunId => ContractState.RunId;
        public bool CanRetry => HasAuthority && networkDriver == null && Phase == SessionPhase.Results && CurrentContract != null &&
            Result != null && Progression != null && Progression.CanStartRun;
        public string LastError { get; private set; } = "";
        public bool IsInitialized { get; private set; }
        public event Action Changed;
        private ISessionDriver driver;

        public void Initialize(GameCatalog catalog, ISessionDriver sessionDriver, string ownCampaignDirectory = null)
        {
            if (IsInitialized) return;
            Catalog = catalog;
            driver = sessionDriver ?? throw new ArgumentNullException(nameof(sessionDriver));
            if (World == null) throw new InvalidOperationException("SessionRoot needs AuthorityWorld.");
            IsInitialized = true;
            networkDriver = driver as INetworkSessionDriver;
            if (Catalog == null) LastError = "Game catalog is missing.";
            else if (!Catalog.TryValidate(out var error)) LastError = error;
            else if (HasAuthority)
            {
                // Replica construction never reaches this branch or opens its own campaign file.
                var tiers = new List<CampaignTier>();
                foreach (var tier in Catalog.Vacuums) tiers.Add(new CampaignTier(tier.TierId, tier.Price));
                campaignRepository = new SaveRepository(ownCampaignDirectory ??
                    CampaignStoragePaths.ResolveOwnDirectory(), new CampaignTierCatalog(tiers),
                    () => this != null && HasAuthority && Phase != SessionPhase.ShuttingDown);
                BindOpenedCampaign(campaignRepository.Open());
            }
            if(HasAuthority&&Catalog!=null&&Catalog.TryValidate(out _))selectedLobbyContractId=Catalog.Contracts[0].ContractId;
            Application.runInBackground = true;
            Time.timeScale = 1f;
            World.Initialize(HasAuthority);
            GetComponent<AchievementSessionBridge>()?.Bind(this, ownCampaignDirectory);
            World.SnapshotChanged += OnWorldSnapshot;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (networkDriver != null)
            {
                networkDriver.LobbyChanged += OnNetworkLobbyChanged;
                networkDriver.Bind(this);
            }
            else StartCoroutine(LoadScene(MenuSceneName, false));
        }

        private bool BindOpenedCampaign(SaveOpenResult opened, bool resetLobby = false)
        {
            CampaignOpenStatus = opened;
            if (opened.Ready)
            {
                Progression = new ProgressionService(opened.State, campaignRepository, () => Phase);
                Controller = new ContractController(Progression);
                Controller.Finished += OnContractFinished;
                LastError = opened.Kind == SaveOpenKind.RecoveredBackup ? "Кампания восстановлена из резервной копии. Исходный повреждённый файл сохранён." : "";
            }
            else LastError = "Не удалось открыть кампанию: " + opened.Error;
            // Publish the new campaign and roster revision together, after discarding the old run/result.
            if (resetLobby) networkDriver?.ResetLobbyReadiness();
            Changed?.Invoke();
            return opened.Ready;
        }
        public bool StartNewCampaign(CampaignState observed)
        {
            if (!CanStartNewCampaign || observed == null || !ReferenceEquals(observed, Campaign)) return false;
            var opened = campaignRepository.StartNewCampaign(observed);
            // Validation/storage failure before the reset began leaves the old session usable.
            if (ReferenceEquals(campaignRepository.Confirmed, observed))
                return Reject("Не удалось начать новую игру: " + opened.Error);
            Controller.Finished -= OnContractFinished;
            Progression.CloseForShutdown();
            Progression = null; Controller = null; Result = null;
            CurrentContract = null; CurrentLevel = null; LocalPlayer = null;
            selectedLobbyContractId = Catalog.Contracts[0].ContractId;
            // An interrupted write uses the existing save-recovery dialog and retries the same campaign ID.
            return BindOpenedCampaign(opened, true);
        }
        public bool RetryCampaignOpen()
        {
            if (!HasAuthority || Progression != null || campaignRepository == null ||
                Phase != SessionPhase.Lobby && Phase != SessionPhase.Booting) return false;
            return BindOpenedCampaign(campaignRepository.Open());
        }
        // Bind only to an explicit corruption-reset confirmation in task10 UI.
        public bool StartNewCampaignAfterCorruption(SaveOpenResult observedProblem)
        {
            if (!HasAuthority || Progression != null || campaignRepository == null ||
                Phase != SessionPhase.Lobby || observedProblem == null || observedProblem.Kind != SaveOpenKind.Corrupt ||
                !ReferenceEquals(observedProblem, CampaignOpenStatus)) return false;
            return BindOpenedCampaign(campaignRepository.StartNewAfterCorruption(observedProblem));
        }
        public bool RetryCampaignSave()
        {
            if (!HasAuthority || Progression == null || !Progression.HasPending || Phase == SessionPhase.ShuttingDown) return false;
            bool committed = Progression.PendingChange != null
                ? Progression.RetryPending(Progression.PendingChange)
                : Progression.PendingResult != null && Progression.TryApplyResult(Progression.PendingResult);
            if (!committed) return Reject(Progression.PendingChange?.IsPurchase == true
                ? "Покупка пока не подтверждена. Повторите сохранение в магазине."
                : "Не удалось сохранить кампанию: " + Progression.LastError);
            LastError = ""; Changed?.Invoke(); return true;
        }
        public bool PurchaseExtraSlot(int expectedPurchasedExtraSlots)
        {
            if (!HasAuthority || Progression == null || Phase != SessionPhase.Lobby) return false;
            if (!Progression.TryPurchaseExtraSlot(expectedPurchasedExtraSlots)) return Reject(Progression.HasPending
                ? "Покупка вместимости пока не подтверждена. Повторите сохранение в магазине."
                : "Покупка вместимости недоступна. Проверьте цену, баланс и максимум модели.");
            LastError = ""; Changed?.Invoke(); return true;
        }
        public bool PurchaseNextTier(string requestedTier)
        {
            if (!HasAuthority || Progression == null || Phase != SessionPhase.Lobby) return false;
            if (!Progression.TryPurchaseNext(requestedTier)) return Reject(Progression.HasPending
                ? "Покупка пока не подтверждена. Повторите сохранение в магазине."
                : "Покупка недоступна. Проверьте баланс и текущий уровень в магазине.");
            LastError = ""; Changed?.Invoke(); return true;
        }

        public bool StartContract(ContractDefinition contract)
        {
            if (!IsInitialized || !CanStartContract) return false;
            return BeginContractLoad(contract);
        }

        public bool RetryContract()
        {
            if (!CanRetry) return false;
            return BeginContractLoad(CurrentContract);
        }

        private bool BeginContractLoad(ContractDefinition contract)
        {
            if (Catalog == null) return Reject("Game catalog is missing.");
            if (!Catalog.TryValidate(out var error)) return Reject(error);
            if (contract == null || Array.IndexOf(Catalog.Contracts, contract) < 0)
                return Reject("This location is not in the game catalog.");
            if (!contract.TryValidate(out error)) return Reject(error);
            if (!Progression.CanStartRun) return Reject("The previous contract result has not been settled.");
            // The shipped route is gated by history, never equipment tier. Non-route engineering fixtures remain explicit fixtures.
            if (CampaignContractAccess.IsKnown(contract.ContractId) && !Progression.IsContractUnlocked(contract.ContractId))
                return Reject("Сначала завершите предыдущий контракт маршрута.");
            if (networkDriver != null && !networkDriver.EnterPreparing(contract.ContractId)) return Reject("Players are not ready.");
            World.Clear(); networkDriver?.ClearPlayers();
            CurrentLevel = null; LocalPlayer = null; Result = null;
            CurrentContract = contract; selectedLobbyContractId=contract.ContractId; LastError = "";
            // Reserve before asynchronous loading; exactly this run reaches controller/world/input.
            Controller.Prepare(Guid.NewGuid().ToString("N"), new ContractRules(contract.ContractId,
                DisplayedQuota(contract), DisplayedTimeLimit(contract), contract.FailurePercent, contract.RequiredBossId));
            SetPhase(SessionPhase.Loading);
            StartCoroutine(LoadScene(contract.SceneName, true));
            return true;
        }

        // Pause-menu confirmation is the only running-session caller of this explicit abandonment action.
        public bool AbandonToMenu()
        {
            if (!HasAuthority) { networkDriver?.LeaveGuest(); return true; }
            if (!IsInitialized || Phase != SessionPhase.Playing || !Controller.IsRunning) return false;
            Controller.Abort(driver.Now); // At/after the deadline, timeout still wins inside the controller.
            return Phase == SessionPhase.Results && ReturnToMenu();
        }

        public bool ReturnToMenu()
        {
            if (!HasAuthority) { networkDriver?.LeaveGuest(); return true; }
            if (!IsInitialized || Phase == SessionPhase.Loading || Phase == SessionPhase.Booting ||
                Phase == SessionPhase.Playing || Phase == SessionPhase.ShuttingDown) return false;
            if (Progression == null || Progression.HasPending) return Reject("The campaign save is unresolved; confirmed balance remains unchanged.");
            World.Clear(); networkDriver?.ClearPlayers(); LocalPlayer = null; CurrentLevel = null; CurrentContract = null;
            SetPhase(SessionPhase.Loading);
            StartCoroutine(LoadScene(MenuSceneName, false));
            return true;
        }

        private bool Reject(string error)
        { LastError = error; Changed?.Invoke(); return false; }

        private IEnumerator LoadScene(string sceneName, bool gameplay)
        {
            if (Phase == SessionPhase.ShuttingDown) yield break;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                FailPreparation("Scene is unavailable in the build: " + sceneName);
                SetPhase(SessionPhase.Lobby);
                yield break;
            }
            SetPhase(SessionPhase.Loading);
            IEnumerator loading = null;
            Exception failure = null;
            try { loading = driver.Load(sceneName); }
            catch (Exception exception) { failure = exception; }
            while (failure == null && loading != null)
            {
                bool next = false;
                object yielded = null;
                try { next = loading.MoveNext(); if (next) yielded = loading.Current; }
                catch (Exception exception) { failure = exception; }
                if (failure != null || !next) break;
                yield return yielded;
            }
            if (Phase == SessionPhase.ShuttingDown) yield break;
            if (failure != null || loading == null)
            {
                FailPreparation("Could not load location: " + (failure?.Message ?? "Missing scene loader."));
                if (gameplay) yield return LoadScene(MenuSceneName, false);
                else SetPhase(SessionPhase.Lobby);
                yield break;
            }
            if (gameplay)
            {
                CurrentLevel = FindFirstObjectByType<LevelContext>();
                string error = null;
                if (CurrentLevel == null) error = "Location has no LevelContext.";
                else if (!CurrentLevel.TryValidateContract(CurrentContract,out error)) { }
                if (error == null)
                {
                    if (networkDriver == null) { if (!TryPrepareGameplay(out error)) { } }
                    else
                    {
                        string prepareError = null;
                        yield return PrepareNetworkGameplay(value => prepareError = value);
                        if (Phase == SessionPhase.ShuttingDown) yield break;
                        error = prepareError;
                    }
                }
                if (error != null)
                {
                    FailPreparation(error);
                    yield return LoadScene(MenuSceneName, false);
                    yield break;
                }
                SetPhase(SessionPhase.Playing);
            }
            else SetPhase(SessionPhase.Lobby);
            BindSceneUi();
        }

        private void FailPreparation(string error)
        {
            LastError = error;
            // An activation error after Start must release the reserved run as well.
            if (Controller != null && Controller.IsRunning && Controller.State.RunId == World.RunId) Controller.Abort(driver.Now);
            World.Clear(); networkDriver?.ClearPlayers();
            Controller?.CancelPreparation();
            LocalPlayer = null; CurrentLevel = null; CurrentContract = null;
        }

        public void OpenNetworkLobby()
        {
            if (!HasAuthority || networkDriver == null || Phase != SessionPhase.Booting) throw new InvalidOperationException("Only the newly connected authority opens the initial lobby.");
            StartCoroutine(LoadScene(MenuSceneName, false));
        }
        public void BindNetworkLocalPlayer(PlayerMotor motor, PlayerInputReader reader)
        {
            if (networkDriver == null) throw new InvalidOperationException("No network driver.");
            LocalPlayer = motor;
            foreach (var menu in FindObjectsByType<MenuInputController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) menu.Bind(this, reader);
            BindSceneUi(); Changed?.Invoke();
        }
        public void ApplyReplica(SessionReplica value)
        {
            if (HasAuthority || value == null) throw new InvalidOperationException("Only a guest consumes a session replica.");
            replica = value; Result = value.Result; Phase = value.Phase; LastError = value.Error ?? "";
            CurrentContract = null;
            if (Catalog != null) foreach (var item in Catalog.Contracts) if (item != null && item.ContractId == value.State.ContractId) CurrentContract = item;
            World.ApplyReplicaWorld(value.State.RunId, Phase == SessionPhase.Playing && value.State.Phase == ContractPhase.Running,
                value.ExtractionMask, value.AllInExtraction);
            Changed?.Invoke();
        }
        public void MarkNetworkLost(string reason)
        {
            if (HasAuthority && Controller != null && Controller.IsRunning) Controller.Abort(driver.Now);
            World.Clear(); networkDriver?.ClearPlayers(); LocalPlayer = null;
            LastError = reason; SetPhase(SessionPhase.ShuttingDown);
        }
        private IEnumerator PrepareNetworkGameplay(Action<string> completed)
        {
            IEnumerator operation = null; Exception failure = null;
            try
            {
                VacuumDefinition vacuum = null;
                foreach (var candidate in Catalog.Vacuums) if (candidate != null && candidate.TierId == Campaign.CurrentTierId) vacuum = candidate;
                if (vacuum == null) throw new InvalidOperationException("Campaign vacuum is missing.");
                operation = networkDriver.PrepareGameplay(CurrentLevel, vacuum);
            }
            catch (Exception error) { failure = error; }
            while (failure == null && operation != null)
            {
                bool next = false; object current = null;
                try { next = operation.MoveNext(); if (next) current = operation.Current; }
                catch (Exception error) { failure = error; }
                if (!next || failure != null) break;
                yield return current;
            }
            try { (operation as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            if (Phase == SessionPhase.ShuttingDown) { completed("Network session stopped during preparation."); yield break; }
            if (failure == null)
            {
                try { Controller.Start(driver.Now); World.SetRunning(true); }
                catch (Exception error) { failure = error; }
            }
            completed(failure?.Message);
        }
        private bool TryPrepareGameplay(out string error)
        {
            GameObject player = null;
            IWorldSpawner spawner = driver as IWorldSpawner;
            try
            {
                if (spawner == null || !(driver is IPlayerIntentSink sink))
                    throw new InvalidOperationException("Session driver cannot spawn players or accept input.");
                VacuumDefinition vacuum = null;
                foreach (var candidate in Catalog.Vacuums)
                    if (candidate != null && candidate.TierId == Campaign.CurrentTierId) { vacuum = candidate; break; }
                if (vacuum == null) throw new InvalidOperationException("Campaign vacuum is missing from the catalog.");
                World.PrepareWorld(CurrentLevel, spawner, vacuum, Controller, () => driver.Now, Progression.EffectiveCapacity, 1);
                var spawn = CurrentLevel.PlayerSpawns[0];
                player = spawner.Spawn(CurrentLevel.PlayerPrefab, spawn.position, spawn.rotation);
                if (player == null) throw new InvalidOperationException("Player spawn failed.");
                LocalPlayer = player.GetComponent<PlayerMotor>();
                LocalPlayer.Initialize(1);
                World.RegisterPlayer(LocalPlayer);
                var reader = player.GetComponent<PlayerInputReader>();
                reader.Initialize(1, sink, RunId);
                if (!reader.IsInitialized) throw new InvalidOperationException("Player input did not initialize.");
                foreach (var menu in FindObjectsByType<MenuInputController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    menu.Bind(this, reader);
                Time.timeScale = 1f;
                Controller.Start(driver.Now);
                World.SetRunning(true);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                if (player != null) spawner?.Despawn(player);
                error = "Could not prepare location: " + exception.Message;
                return false;
            }
        }

        private void OnContractFinished(ContractResult result)
        {
            if (!ReferenceEquals(result, Controller.FinalResult) || Result != null || result.RunId != World.RunId) return;
            Result = result;
            // The authoritative pending result already exists before this event. Freeze once, then settle once.
            World.SetRunning(false);
            if (Phase == SessionPhase.ShuttingDown) return;
            GetComponent<AchievementSessionBridge>()?.OnAuthorityFinished(result);
            if (!Progression.TryApplyResult(result))
                LastError = "Не удалось сохранить выплату. Результат остаётся в памяти: " + Progression.LastError;
            else LastError = "";
            // Preserve the old lifecycle order: reentrant Results listeners run after the single save attempt.
            if (Phase != SessionPhase.ShuttingDown) SetPhase(SessionPhase.Results);
        }

        // Readiness and roster changes do not mutate the host world, but must refresh its lobby UI.
        private void OnNetworkLobbyChanged()
        {
            if (HasAuthority && Phase == SessionPhase.Lobby) Changed?.Invoke();
        }
        private void OnWorldSnapshot() => Changed?.Invoke();
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindSceneUi();
        private void BindSceneUi()
        {
            foreach (var view in FindObjectsByType<SessionMenuView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) view.Bind(this);
            foreach (var selector in FindObjectsByType<ContractSelectionView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) selector.Bind(this);
            foreach (var view in FindObjectsByType<ShopView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) view.Bind(this);
            foreach (var view in FindObjectsByType<CampaignRecoveryView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) view.Bind(this);
            foreach (var view in FindObjectsByType<NewCampaignView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) view.Bind(this);
            foreach (var hud in FindObjectsByType<ContractHud>(FindObjectsInactive.Include, FindObjectsSortMode.None)) hud.Bind(this);
            foreach (var view in FindObjectsByType<ResultsView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) view.Bind(this);
        }
        private void SetPhase(SessionPhase phase) { Phase = phase; networkDriver?.PhaseChanged(phase); Changed?.Invoke(); }
        private void OnDestroy()
        {
            if (networkDriver != null) networkDriver.LobbyChanged -= OnNetworkLobbyChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (World != null) World.SnapshotChanged -= OnWorldSnapshot;
            if (Controller != null) Controller.Finished -= OnContractFinished;
            Progression?.CloseForShutdown();
            campaignRepository?.Dispose();
            if (IsInitialized) driver?.Stop();
            Changed = null;
        }
    }
}
