using UnityEngine;
using UnityEngine.UI;
namespace HowToSuck
{
    [DisallowMultipleComponent,RequireComponent(typeof(Text))]
    public sealed class LocalizedLegacyText:MonoBehaviour
    {
        public string Source;
        private Text label;
        private void OnEnable(){GameLocalization.Changed+=Refresh;Refresh();}
        private void OnDisable(){GameLocalization.Changed-=Refresh;}
        public void Refresh(){if(label==null)label=GetComponent<Text>();if(Source==null)Source=label.text;LocalizationFonts.Apply(label);string next=GameLocalization.Text(Source);if(label.text!=next)label.text=next;}
    }
}
