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
            if (Catalog == null) LastError = "Game catalog is missing.";
            else if (!Catalog.TryValidate(out var validationError)) LastError = validationError;
            Application.runInBackground = true;
            World.Initialize(true);
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(LoadScene(MenuSceneName, false));
        }

        public bool StartContract(ContractDefinition contract)
        {
            if (!IsInitialized || Phase != SessionPhase.Lobby) return false;
            if (Catalog == null) return Reject("Game catalog is missing.");
            if (!Catalog.TryValidate(out var error)) return Reject(error);
            if (contract == null || Array.IndexOf(Catalog.Contracts, contract) < 0)
                return Reject("This location is not in the game catalog.");
            if (!contract.TryValidate(out error)) return Reject(error);
            CurrentContract = contract;
            LastError = "";
            // Set the guard before scheduling any asynchronous work.
            SetPhase(SessionPhase.Loading);
            StartCoroutine(LoadScene(contract.SceneName, true));
            return true;
        }

        public bool ReturnToMenu()
        {
            if (!IsInitialized || Phase == SessionPhase.Loading || Phase == SessionPhase.Booting) return false;
            World.Clear();
            CurrentLevel = null;
            CurrentContract = null;
            SetPhase(SessionPhase.Loading);
            StartCoroutine(LoadScene(MenuSceneName, false));
            return true;
        }

        private bool Reject(string error)
        {
            LastError = error;
            Changed?.Invoke();
            return false;
        }

        private IEnumerator LoadScene(string sceneName, bool gameplay)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                LastError = "Scene is unavailable in the build: " + sceneName;
                CurrentContract = null;
                SetPhase(SessionPhase.Lobby);
                yield break;
            }
            SetPhase(SessionPhase.Loading);
            // Advance the driver explicitly so a synchronous loading error cannot strand Loading.
            var loading = driver.Load(sceneName);
            while (true)
            {
                bool next;
                object yielded = null;
                Exception failure = null;
                try { next = loading.MoveNext(); if (next) yielded = loading.Current; }
                catch (Exception exception) { failure = exception; next = false; }
                if (failure != null)
                {
                    LastError = "Could not load location: " + failure.Message;
                    World.Clear();
                    CurrentContract = null;
                    SetPhase(SessionPhase.Lobby);
                    yield break;
                }
                if (!next) break;
                yield return yielded;
            }
            if (gameplay)
            {
                CurrentLevel = FindFirstObjectByType<LevelContext>();
                string error = null;
                if (CurrentLevel == null) error = "Location has no LevelContext.";
                else if (!CurrentLevel.TryValidate(out error)) { }
                else if (CurrentLevel.Contract != CurrentContract) error = "Location definition does not match the selected contract.";
                if (error != null)
                {
                    LastError = error;
                    World.Clear();
                    CurrentLevel = null;
                    CurrentContract = null;
                    yield return LoadScene(MenuSceneName, false);
                    yield break;
                }
                World.SetRunning(true);
                SetPhase(SessionPhase.Playing);
            }
            else SetPhase(SessionPhase.Lobby);
            BindSceneUi();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindSceneUi();
        private void BindSceneUi()
        {
            foreach (var view in FindObjectsByType<SessionMenuView>(FindObjectsSortMode.None)) view.Bind(this);
        }
        private void SetPhase(SessionPhase phase) { Phase = phase; Changed?.Invoke(); }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (IsInitialized) driver?.Stop();
            Changed = null;
        }
    }
}
