using System;
using System.Globalization;

namespace HowToSuck
{
    public static class GameLanguages
    {
        public const string Automatic = "steam";
        public static readonly string[] Codes = { "english", "russian", "schinese", "tchinese", "spanish", "latam", "brazilian", "german", "french", "japanese", "koreana", "polish", "czech", "turkish", "thai", "ukrainian", "italian" };
        public static readonly string[] Names = { "English", "Русский", "简体中文", "繁體中文", "Español (España)", "Español (Latinoamérica)", "Português (Brasil)", "Deutsch", "Français", "日本語", "한국어", "Polski", "Čeština", "Türkçe", "ไทย", "Українська", "Italiano" };
        public static bool IsSupported(string code) => Array.IndexOf(Codes, code) >= 0;
        public static bool IsValidPreference(string code) => string.IsNullOrEmpty(code) || code == Automatic || IsSupported(code);
        public static string Resolve(string preference, string steamLanguage, string systemCulture)
        {
            if (IsSupported(preference)) return preference;
            if (!string.IsNullOrEmpty(steamLanguage)) return IsSupported(steamLanguage) ? steamLanguage : "english";
            string culture = (systemCulture ?? "").ToLowerInvariant();
            if (culture.StartsWith("zh")) return culture.Contains("tw") || culture.Contains("hk") || culture.Contains("hant") || culture.Contains("mo") ? "tchinese" : "schinese";
            if (culture.StartsWith("es")) return culture == "es" || culture == "es-es" ? "spanish" : "latam";
            if (culture.StartsWith("pt")) return "brazilian";
            string[] cultures = {"en", "ru", "de", "fr", "ja", "ko", "pl", "cs", "tr", "th", "uk", "it"};
            string[] languages = {"english", "russian", "german", "french", "japanese", "koreana", "polish", "czech", "turkish", "thai", "ukrainian", "italian"};
            for (int i = 0; i < cultures.Length; i++) if (culture == cultures[i] || culture.StartsWith(cultures[i] + "-")) return languages[i];
            return "english";
        }
    }
}
