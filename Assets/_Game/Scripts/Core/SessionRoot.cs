using System;
using System.Collections;
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
        public LevelContext CurrentLevel { get; private set; }
        public PlayerMotor LocalPlayer { get; private set; }
        public CampaignState Campaign { get; private set; }
        public ProgressionService Progression { get; private set; }
        public ContractController Controller { get; private set; }
        public ContractState ContractState => Controller != null ? Controller.State : default;
        public ContractResult Result { get; private set; }
        public string RunId => ContractState.RunId;
        public bool CanRetry => Phase == SessionPhase.Results && CurrentContract != null &&
            Result != null && Progression != null && Progression.CanStartRun;
        public string LastError { get; private set; } = "";
        public bool IsInitialized { get; private set; }
        public event Action Changed;
        private ISessionDriver driver;

        public void Initialize(GameCatalog catalog, ISessionDriver sessionDriver)
        {
            if (IsInitialized) return;
            Catalog = catalog;
            driver = sessionDriver ?? throw new ArgumentNullException(nameof(sessionDriver));
            if (World == null) throw new InvalidOperationException("SessionRoot needs AuthorityWorld.");
            IsInitialized = true;
            Campaign = new CampaignState(Guid.NewGuid().ToString("N"), "mk1", 0);
            Progression = new ProgressionService(Campaign);
            Controller = new ContractController(Progression);
            Controller.Finished += OnContractFinished;
            if (Catalog == null) LastError = "Game catalog is missing.";
            else if (!Catalog.TryValidate(out var error)) LastError = error;
            Application.runInBackground = true;
            Time.timeScale = 1f;
            World.Initialize(true);
            World.SnapshotChanged += OnWorldSnapshot;
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(LoadScene(MenuSceneName, false));
        }

        public bool StartContract(ContractDefinition contract)
        {
            if (!IsInitialized || Phase != SessionPhase.Lobby) return false;
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
            World.Clear();
            CurrentLevel = null; LocalPlayer = null; Result = null;
            CurrentContract = contract; LastError = "";
            // Reserve before asynchronous loading; exactly this run reaches controller/world/input.
            Controller.Prepare(Guid.NewGuid().ToString("N"), new ContractRules(contract.ContractId,
                contract.Quota, contract.TimeLimitSeconds, contract.FailurePercent));
            SetPhase(SessionPhase.Loading);
            StartCoroutine(LoadScene(contract.SceneName, true));
            return true;
        }

        // Pause-menu confirmation is the only running-session caller of this explicit abandonment action.
        public bool AbandonToMenu()
        {
            if (!IsInitialized || Phase != SessionPhase.Playing || !Controller.IsRunning) return false;
            Controller.Abort(driver.Now); // At/after the deadline, timeout still wins inside the controller.
            return Phase == SessionPhase.Results && ReturnToMenu();
        }

        public bool ReturnToMenu()
        {
            if (!IsInitialized || Phase == SessionPhase.Loading || Phase == SessionPhase.Booting ||
                Phase == SessionPhase.Playing || Phase == SessionPhase.ShuttingDown) return false;
            if (Progression.PendingResult != null) return Reject("The contract payout could not be applied; the result is retained.");
            World.Clear(); LocalPlayer = null; CurrentLevel = null; CurrentContract = null;
            SetPhase(SessionPhase.Loading);
            StartCoroutine(LoadScene(MenuSceneName, false));
            return true;
        }

        private bool Reject(string error)
        { LastError = error; Changed?.Invoke(); return false; }

        private IEnumerator LoadScene(string sceneName, bool gameplay)
        {
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
                else if (!CurrentLevel.TryValidate(out error)) { }
                else if (CurrentLevel.Contract != CurrentContract) error = "Location definition does not match the selected contract.";
                if (error == null && !TryPrepareGameplay(out error)) { }
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
            if (Controller.IsRunning && Controller.State.RunId == World.RunId) Controller.Abort(driver.Now);
            World.Clear();
            Controller.CancelPreparation();
            LocalPlayer = null; CurrentLevel = null; CurrentContract = null;
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
                World.PrepareWorld(CurrentLevel, spawner, vacuum, Controller, () => driver.Now);
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
            if (!Progression.TryApplyResult(result))
                LastError = "Не удалось начислить выплату. Итог сохранён; новый контракт пока недоступен.";
            SetPhase(SessionPhase.Results);
        }

        private void OnWorldSnapshot() => Changed?.Invoke();
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindSceneUi();
        private void BindSceneUi()
        {
            foreach (var view in FindObjectsByType<SessionMenuView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) view.Bind(this);
            foreach (var hud in FindObjectsByType<ContractHud>(FindObjectsInactive.Include, FindObjectsSortMode.None)) hud.Bind(this);
            foreach (var view in FindObjectsByType<ResultsView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) view.Bind(this);
        }
        private void SetPhase(SessionPhase phase) { Phase = phase; Changed?.Invoke(); }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (World != null) World.SnapshotChanged -= OnWorldSnapshot;
            if (Controller != null) Controller.Finished -= OnContractFinished;
            if (IsInitialized) driver?.Stop();
            Changed = null;
        }
    }
}