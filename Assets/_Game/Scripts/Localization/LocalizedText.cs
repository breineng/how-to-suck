using TMPro;
using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent,RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [TextArea] public string Source;
        public bool Literal; // Steam persona names and native language names are opaque user data.
        public bool EscapeKeycaps; // Apply keycap markup after translating the complete source sentence.
        private TMP_Text label;
        public static void Set(UnityEngine.UI.Text target,string source)
        {
            if(target==null)return;
            var binding=target.GetComponent<LocalizedLegacyText>();
            if(binding==null)binding=target.gameObject.AddComponent<LocalizedLegacyText>();
            binding.Source=source;binding.Refresh();
        }
        public static void Set(TMP_Text target,string source)
        {
            if(target==null)return;
            var binding=target.GetComponent<LocalizedText>();
            if(binding==null)binding=target.gameObject.AddComponent<LocalizedText>();
            if(binding.Source==source&&binding.label!=null&&binding.label.text==binding.DisplayValue())return;
            binding.Source=source;binding.Refresh();
        }
        public static void SetLiteral(TMP_Text target,string source)
        {
            if(target==null)return;
            var binding=target.GetComponent<LocalizedText>();
            if(binding==null)binding=target.gameObject.AddComponent<LocalizedText>();
            binding.Literal=true;binding.Source=source;binding.Refresh();
        }
        private void Awake(){label=GetComponent<TMP_Text>();if(Source==null)Source=label.text;}
        private void OnEnable(){GameLocalization.Changed+=Refresh;Refresh();}
        private void OnDisable(){GameLocalization.Changed-=Refresh;}
        private string DisplayValue()
        {
            string value=Literal?Source:GameLocalization.Text(Source);
            return EscapeKeycaps?InlineKeycaps.FormatEscapeKeys(value):value;
        }
        public void Refresh()
        {
            if(label==null)label=GetComponent<TMP_Text>();
            if(Source==null)Source=label.text;
            LocalizationFonts.Apply(label);
            string value=DisplayValue();
            if(label.text!=value)label.text=value;
        }
    }
}
