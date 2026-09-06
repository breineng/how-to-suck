using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace HowToSuck
{
    // Every scene owns its UI action state, including its initial stationary pointer.
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    [RequireComponent(typeof(InputSystemUIInputModule))]
    public sealed class UiInputLifetime : MonoBehaviour
    {
        private InputSystemUIInputModule module;
        private InputActionAsset ownedActions;
        private InputActionReference[] ownedReferences;

        private void Awake()
        {
            module = GetComponent<InputSystemUIInputModule>();
            if (module.actionsAsset == null) return;
            bool wasEnabled = module.enabled;
            module.enabled = false;
            ownedActions = Instantiate(module.actionsAsset);
            ownedActions.name = module.actionsAsset.name + " (Scene UI)";
            ownedActions.hideFlags = HideFlags.DontSave;
            var point = ownedActions.FindAction("UI/Point", false);
            if (point != null) point.wantsInitialStateCheck = true;
            // The supported setter remaps all references into the new asset by map/action.
            module.actionsAsset = ownedActions;
            ownedReferences = new[] { module.point, module.move, module.leftClick, module.rightClick,
                module.middleClick, module.scrollWheel, module.submit, module.cancel,
                module.trackedDevicePosition, module.trackedDeviceOrientation }
                .Where(r => r != null && r.asset == ownedActions).Distinct().ToArray();
            module.enabled = wasEnabled;
        }

        private void OnDestroy()
        {
            if (ownedActions == null) return;
            if (module != null && module.actionsAsset == ownedActions) module.enabled = false;
            ownedActions.Disable();
            if (ownedReferences != null)
                foreach (var reference in ownedReferences) if (reference != null) Destroy(reference);
            Destroy(ownedActions);
        }
    }
}