using UnityEngine;

namespace HowToSuck
{
    // Keep the scene visible behind translucent menus while hiding the previous UI page.
    [DisallowMultipleComponent]
    public sealed class ProductUiLayerVisibility : MonoBehaviour
    {
        public GameObject[] ModalPanels;
        public CanvasGroup[] Layers;
        void OnEnable() => Refresh();
        void LateUpdate() => Refresh();
        void Refresh()
        {
            bool visible = true;
            if (ModalPanels != null) foreach (var panel in ModalPanels)
                if (panel != null && panel.activeInHierarchy) { visible = false; break; }
            if (Layers == null) return;
            foreach (var layer in Layers)
            {
                if (layer == null) continue;
                float alpha = visible ? 1f : 0f;
                if (layer.alpha != alpha) layer.alpha = alpha;
                if (layer.interactable != visible) layer.interactable = visible;
                if (layer.blocksRaycasts != visible) layer.blocksRaycasts = visible;
            }
        }
    }
}
