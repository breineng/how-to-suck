using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;
using UnityEngine;
#if UNITY_EDITOR_WIN
using System.IO;
using System.Runtime.InteropServices;
#endif

namespace HowToSuck.Networking
{
    // One explicit owner. Opening the menu or choosing Solo never initializes Epic.
    public sealed class EosRuntime : IDisposable
    {
        private static EosRuntime owner;
        public PlatformInterface Platform { get; private set; }
        public ProductUserId LocalUserId { get; private set; }
        public bool Ready => LocalUserId != null && LocalUserId.IsValid() && !disposed;
        public event Action ReadyChanged;
        public event Action<string> Failed;
        private bool disposed, initialized, loggingIn;
        private double deadline;
        private ulong expiration, loginStatus;
        public bool ForceRelay { get; private set; }
#if UNITY_EDITOR_WIN
        private IntPtr library;
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr module);
#endif
        public bool Start(EosClientConfiguration configuration, string build)
        {
            if (disposed || owner != null || configuration == null || !configuration.IsValid) return false;
            owner = this; ForceRelay = configuration.ForceRelay;
            try
            {
#if UNITY_EDITOR_WIN
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlatformInterface).Assembly);
                library = LoadLibraryW(Path.Combine(package.resolvedPath, "Runtime/Windows/x64/EOSSDK-Win64-Shipping.dll"));
                if (library == IntPtr.Zero) throw new DllNotFoundException();
                Bindings.Hook(library, GetProcAddress);
#endif
                var init = new InitializeOptions { ProductName = "How to Suck", ProductVersion = build };
                var result = PlatformInterface.Initialize(ref init);
                if (result != Result.Success) throw new InvalidOperationException("EOS initialization: " + result);
                initialized = true;
                var options = new Options {
                    ProductId = configuration.ProductId, SandboxId = configuration.SandboxId, DeploymentId = configuration.DeploymentId,
                    ClientCredentials = new ClientCredentials { ClientId = configuration.ClientId, ClientSecret = configuration.ClientSecret },
                    Flags = PlatformFlags.DisableOverlay | (Application.isEditor ? PlatformFlags.LoadingInEditor : PlatformFlags.None),
                    IsServer = false, TickBudgetInMilliseconds = 2
                };
                Platform = PlatformInterface.Create(ref options);
                if (Platform == null) throw new InvalidOperationException("EOS platform creation failed");
                var connect = Platform.GetConnectInterface();
                var expiryOptions = new AddNotifyAuthExpirationOptions();
                expiration = connect.AddNotifyAuthExpiration(ref expiryOptions, null, (ref AuthExpirationCallbackInfo info) => { if (!disposed) Login(); });
                var statusOptions = new AddNotifyLoginStatusChangedOptions();
                loginStatus = connect.AddNotifyLoginStatusChanged(ref statusOptions, null, (ref LoginStatusChangedCallbackInfo info) => {
                    if (!disposed && Ready && info.CurrentStatus == LoginStatus.NotLoggedIn) Fail("Сеанс Epic завершён. Подключитесь заново.");
                });
                deadline = Time.realtimeSinceStartupAsDouble + 30;
                var device = new CreateDeviceIdOptions { DeviceModel = "Windows PC" };
                connect.CreateDeviceId(ref device, null, (ref CreateDeviceIdCallbackInfo info) => {
                    if (disposed) return;
                    if (info.ResultCode == Result.Success || info.ResultCode == Result.DuplicateNotAllowed) Login();
                    else Fail("Epic: не удалось подготовить вход (" + info.ResultCode + ").");
                });
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("EOS startup failed: " + error.GetType().Name);
                Dispose(); return false;
            }
        }
        private void Login()
        {
            if (disposed || loggingIn) return;
            loggingIn = true; deadline = Time.realtimeSinceStartupAsDouble + 30;
            var options = new LoginOptions {
                Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken },
                UserLoginInfo = new UserLoginInfo { DisplayName = "Player" }
            };
            Platform.GetConnectInterface().Login(ref options, null, (ref LoginCallbackInfo info) => {
                if (disposed) return;
                if (info.ResultCode == Result.InvalidUser)
                {
                    var create = new CreateUserOptions { ContinuanceToken = info.ContinuanceToken };
                    Platform.GetConnectInterface().CreateUser(ref create, null, (ref CreateUserCallbackInfo created) => {
                        if (!disposed) CompleteLogin(created.ResultCode, created.LocalUserId);
                    });
                }
                else CompleteLogin(info.ResultCode, info.LocalUserId);
            });
        }
        private void CompleteLogin(Result result, ProductUserId user)
        {
            loggingIn = false; deadline = 0;
            if (result != Result.Success || user == null || !user.IsValid()) { Fail("Не удалось войти в Epic (" + result + ")."); return; }
            if (LocalUserId != null && LocalUserId != user) { Fail("Учётная запись Epic изменилась. Подключитесь заново."); return; }
            bool first = LocalUserId == null; LocalUserId = user;
            if (first) ReadyChanged?.Invoke();
        }
        public void Pump()
        {
            if (disposed || Platform == null) return;
            Platform.Tick();
            if (deadline > 0 && Time.realtimeSinceStartupAsDouble >= deadline) { deadline = 0; Fail("Epic не ответил вовремя. Проверьте соединение и настройки сервиса."); }
        }
        private void Fail(string message) { deadline = 0; Failed?.Invoke(message); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Platform != null)
            {
                var connect = Platform.GetConnectInterface();
                if (expiration != 0) connect.RemoveNotifyAuthExpiration(expiration);
                if (loginStatus != 0) connect.RemoveNotifyLoginStatusChanged(loginStatus);
                Platform.Release(); Platform = null;
            }
            LocalUserId = null;
            if (initialized) { PlatformInterface.Shutdown(); initialized = false; }
#if UNITY_EDITOR_WIN
            if (library != IntPtr.Zero) { Bindings.Unhook(); FreeLibrary(library); library = IntPtr.Zero; }
#endif
            if (owner == this) owner = null;
            ReadyChanged = null; Failed = null;
        }
    }
}
