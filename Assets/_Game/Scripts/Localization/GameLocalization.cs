using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HowToSuck
{
    // Language affects presentation only. Campaigns, network messages and asset names retain their source identity.
    public static class GameLocalization
    {
        public static event Action Changed;
        public static string Language { get; private set; } = "english";
        public static string Preference { get; private set; } = GameLanguages.Automatic;
        public static string SteamLanguage { get; private set; }
        public static int Revision { get; private set; }
        private static TranslationCatalog catalog;
        private static readonly Dictionary<string,string> cache = new Dictionary<string,string>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Changed = null; Preference = GameLanguages.Automatic; SteamLanguage = null; catalog = null; cache.Clear();
            Language = GameLanguages.Resolve(Preference, null, CultureInfo.CurrentUICulture.Name); Revision++;
        }
        public static void Select(string preference)
        {
            Preference = GameLanguages.IsValidPreference(preference) && !string.IsNullOrEmpty(preference) ? preference : GameLanguages.Automatic;
            RefreshLanguage();
        }
        public static void SetSteamLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return;
            string normalized=language.Trim().ToLowerInvariant();
            if(normalized==SteamLanguage)return;
            SteamLanguage = normalized; RefreshLanguage();
        }
        private static void RefreshLanguage()
        {
            string next = GameLanguages.Resolve(Preference, SteamLanguage, CultureInfo.CurrentUICulture.Name);
            if (next == Language) return;
            Language = next; catalog = null; cache.Clear(); Revision++; Changed?.Invoke();
        }
        public static string Text(string source)
        {
            if (string.IsNullOrEmpty(source)) return source ?? "";
            if (cache.TryGetValue(source, out var value)) return value;
            if (catalog == null) catalog = TranslationCatalog.Load(Language);
            value = catalog.Translate(source);
            // HUD counters have unbounded combinations. Keep the per-language cache bounded.
            if (cache.Count >= 2048) cache.Clear();
            cache[source] = value; return value;
        }
    }
}
