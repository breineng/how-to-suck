using System;
using System.IO;
using UnityEngine;

namespace HowToSuck.Networking
{
    // Client credentials belong to the restricted game-client policy, never an organization/admin API key.
    [Serializable]
    public sealed class EosClientConfiguration
    {
        public string ProductId, SandboxId, DeploymentId, ClientId, ClientSecret;
        public bool ForceRelay;
        public const string FileName = "eos-client.json";
        public static string LocalPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../.local", FileName));
        public static bool TryLoad(out EosClientConfiguration configuration, out string status)
        {
            configuration = null;
            status = "Epic Online Services ещё не настроен. Одиночная игра доступна.";
            string path = Application.isEditor ? LocalPath : Path.Combine(Application.streamingAssetsPath, FileName);
            try
            {
                if (!File.Exists(path)) return false;
                if (new FileInfo(path).Length > 16384) return false;
                configuration = JsonUtility.FromJson<EosClientConfiguration>(File.ReadAllText(path));
                if (configuration == null || !configuration.IsValid)
                { configuration = null; status = "Проверьте настройки Epic Online Services. Одиночная игра доступна."; return false; }
                status = "Совместная игра через Epic Online Services. Подключение по коду комнаты.";
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { configuration = null; return false; }
        }
        public bool IsValid => NetworkConfiguration.Hex(ProductId, 32) && NetworkConfiguration.ValidToken(SandboxId, 64) &&
            NetworkConfiguration.Hex(DeploymentId, 32) && NetworkConfiguration.ValidToken(ClientId, 128) &&
            !string.IsNullOrWhiteSpace(ClientSecret) && ClientSecret.Length <= 512 && ClientSecret.IndexOfAny(new[]{'\r','\n','\0'}) < 0;
    }
}
