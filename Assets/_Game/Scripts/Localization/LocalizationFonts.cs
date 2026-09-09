using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HowToSuck
{
    public static class LocalizationFonts
    {
        private static readonly HashSet<TMP_FontAsset> Prepared = new HashSet<TMP_FontAsset>();
        private static TMP_FontAsset[] fallbacks;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset(){Prepared.Clear();fallbacks=null;}
        private static void Load()
        {
            if(fallbacks!=null)return;
            fallbacks=new[]{Resources.Load<TMP_FontAsset>("Localization/Fonts/Latin"),Resources.Load<TMP_FontAsset>("Localization/Fonts/Thai"),Resources.Load<TMP_FontAsset>("Localization/Fonts/CJK")};
        }
        public static void Apply(UnityEngine.UI.Text label)
        {
            if(label==null)return;
            Load();
            string language=GameLocalization.Language;
            string family=language=="thai"?"Thai":language=="schinese"||language=="tchinese"||language=="japanese"||language=="koreana"?"CJK":"Latin";
            var selected=fallbacks[family=="Latin"?0:family=="Thai"?1:2];
            if(selected!=null&&selected.sourceFontFile!=null&&label.font!=selected.sourceFontFile)label.font=selected.sourceFontFile;
        }
        public static void Apply(TMP_Text label)
        {
            if(label==null||label.font==null||Prepared.Contains(label.font))return;
            Load();
            var list=label.font.fallbackFontAssetTable;
            if(list==null)label.font.fallbackFontAssetTable=list=new List<TMP_FontAsset>();
            // Keep authored/custom fallbacks; place script fonts after the Latin family so accented
            // letters never borrow CJK metrics. Ordinary localized Oswald glyphs are baked in advance.
            foreach(var fallback in fallbacks)if(fallback!=null)list.Remove(fallback);
            foreach(var fallback in fallbacks)if(fallback!=null&&fallback!=label.font)list.Add(fallback);
            Prepared.Add(label.font);
        }
    }
}
