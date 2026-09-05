using UnityEngine;

namespace HowToSuck
{
    /// <summary>Drives only the truck's authored amber lamps, using shared intake state.</summary>
    [DisallowMultipleComponent]
    public sealed class TruckView : MonoBehaviour
    {
        public TruckIntake Intake;
        public Renderer LampRenderer;
        public int LampMaterialIndex = -1;
        [ColorUsage(false, true)] public Color PoweredEmission = new Color(1.7f, .85f, .18f, 1f);
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock previous;
        private MaterialPropertyBlock current;
        private bool captured;

        private void OnEnable()
        {
            if (LampRenderer == null || LampMaterialIndex < 0 ||
                LampMaterialIndex >= LampRenderer.sharedMaterials.Length) return;
            previous = new MaterialPropertyBlock();
            current = new MaterialPropertyBlock();
            LampRenderer.GetPropertyBlock(previous, LampMaterialIndex);
            captured = true;
        }

        private void LateUpdate()
        {
            if (!captured || LampRenderer == null) return;
            bool busy = Intake != null && Intake.Receiver != null && Intake.Receiver.Busy;
            bool active = Intake != null && Intake.FieldActive;
            float intensity = busy ? 1.5f + .25f * Mathf.Sin(Time.time * 14f) : active ? 1f : .015f;
            LampRenderer.GetPropertyBlock(current, LampMaterialIndex);
            current.SetColor(EmissionColor, PoweredEmission * intensity);
            LampRenderer.SetPropertyBlock(current, LampMaterialIndex);
        }

        private void OnDisable()
        {
            if (captured && LampRenderer != null)
                LampRenderer.SetPropertyBlock(previous.isEmpty ? null : previous, LampMaterialIndex);
            captured = false;
        }
    }
}