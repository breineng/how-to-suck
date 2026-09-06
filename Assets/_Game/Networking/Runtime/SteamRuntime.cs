using System;
using Steamworks;

namespace HowToSuck.Networking
{
    // Explicit lifetime owner. Solo does not create or initialize this object.
    public sealed class SteamRuntime : IDisposable
    {
        private static SteamRuntime owner;
        public bool Initialized { get; private set; }
        public uint ActualAppId { get; private set; }
        public ulong LocalSteamId { get; private set; }
        public string LastError { get; private set; }
        public event Action<string> Failed;
        private bool disposed, callbackFaulted;

        public bool TryInitialize(NetworkConfiguration configuration)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SteamRuntime));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (Initialized) return !callbackFaulted && ActualAppId == configuration.AppId;
            if (owner != null && owner != this) return Fail("Steam уже обслуживается другим владельцем сессии.");
            owner = this;
            try
            {
                // No implicit launch of another Steam app and no changes to environment/appid files.
                var result = SteamAPI.InitEx(out _);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                { owner = null; return Fail("Не удалось подключиться к Steam (" + result + "). Одиночная игра доступна."); }
                Initialized = true;
                ActualAppId = SteamUtils.GetAppID().m_AppId;
                if (ActualAppId != configuration.AppId)
                {
                    Shutdown();
                    return Fail("Steam запущен с другим AppID. Проверьте выбранную конфигурацию разработки.");
                }
                LocalSteamId = SteamUser.GetSteamID().m_SteamID;
                if (LocalSteamId == 0) { Shutdown(); return Fail("Steam не сообщил текущего пользователя."); }
                SteamNetworkingUtils.InitRelayNetworkAccess();
                LastError = null;
                return true;
            }
            catch (DllNotFoundException) { Shutdown(); return Fail("Не найдена библиотека Steam для Windows x64. Одиночная игра доступна."); }
            catch (BadImageFormatException) { Shutdown(); return Fail("Разрядность библиотеки Steam не совпадает со сборкой."); }
            catch (EntryPointNotFoundException) { Shutdown(); return Fail("Версии Steamworks.NET и native Steam API не совпадают."); }
            catch (Exception) { Shutdown(); return Fail("Steam не удалось инициализировать. Одиночная игра доступна."); }
        }
        public void Pump()
        {
            if (!Initialized || disposed || callbackFaulted) return;
            try { SteamAPI.RunCallbacks(); }
            catch (Exception) { callbackFaulted = true; Fail("Обработка событий Steam прервалась; сетевую сессию необходимо завершить."); }
        }
        private bool Fail(string error) { LastError = error; Failed?.Invoke(error); return false; }
        private void Shutdown()
        {
            bool wasInitialized = Initialized;
            Initialized = false;
            try { if (wasInitialized) SteamAPI.Shutdown(); }
            catch (Exception) { LastError = "Завершение Steam API прервалось; повторная сетевая сессия требует перезапуска приложения."; }
            finally
            {
                ActualAppId = 0; LocalSteamId = 0;
                if (owner == this) owner = null;
            }
        }
        // Coordinator must shut down NGO transport, then lobby callbacks, before disposing this owner.
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Shutdown(); Failed = null;
        }
    }
}