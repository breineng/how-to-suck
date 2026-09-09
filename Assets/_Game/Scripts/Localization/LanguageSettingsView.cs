using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToSuck
{
    public sealed class LanguageSettingsView : MonoBehaviour
    {
        public LocalMenuView Menu;
        public Button[] Options;
        public TMP_Text Hint;
        private UnityEngine.Events.UnityAction[] actions;
        private void Awake()
        {
            actions=new UnityEngine.Events.UnityAction[Options.Length];
            for(int i=0;i<Options.Length;i++){int index=i;actions[i]=()=>Choose(index);Options[i].onClick.AddListener(actions[i]);}
        }
        private void Choose(int index)
        {
            if(Menu==null||!Menu.IsOpen||Menu.Navigation==null)return;
            var settings=Menu.Navigation.Settings;var draft=settings.DraftCopy();
            draft.Language=index==0?GameLanguages.Automatic:GameLanguages.Codes[index-1];settings.Preview(draft);Refresh();
        }
        public void Refresh()
        {
            if(Menu==null||Menu.Navigation==null)return;
            string preference=Menu.Navigation.Settings.DraftCopy().Language;
            int selected=Array.IndexOf(GameLanguages.Codes,preference)+1;
            for(int i=0;i<Options.Length;i++)
            {
                var image=Options[i].targetGraphic;
                if(image!=null)image.color=i==selected?new Color(.82f,.025f,.03f):new Color(.86f,.85f,.83f);
                var label=Options[i].GetComponentInChildren<TMP_Text>();if(label!=null)label.color=i==selected?Color.white:new Color(.025f,.025f,.024f);
            }
            LocalizedText.Set(Hint,"Язык Steam выбирается автоматически. Без Steam используется язык системы. Нажмите «Применить», чтобы сохранить выбор.");
        }
        private void OnEnable(){GameLocalization.Changed+=Refresh;Refresh();}
        private void OnDisable(){GameLocalization.Changed-=Refresh;}
        private void OnDestroy(){if(actions!=null)for(int i=0;i<Options.Length;i++)if(Options[i]!=null)Options[i].onClick.RemoveListener(actions[i]);}
    }
}
