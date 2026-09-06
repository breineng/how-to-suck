using System.Collections;
using UnityEngine;
namespace HowToSuck.Networking
{
    public sealed class OfflineMenuReturn : MonoBehaviour
    {
        public void Begin(GameObject oldRoot,GameObject offlinePrefab,string notice=null)=>StartCoroutine(Return(oldRoot,offlinePrefab,notice));
        private IEnumerator Return(GameObject oldRoot,GameObject offlinePrefab,string notice)
        {
            Destroy(oldRoot);yield return null;
            if (oldRoot != null || Unity.Netcode.NetworkManager.Singleton != null)
            { Debug.LogError("Previous session root/manager did not finish destruction; fresh entry remains blocked."); Destroy(gameObject); yield break; }
            var fresh = Instantiate(offlinePrefab);
            var entry = fresh.GetComponent<SoloSessionStartup>();
            if (entry != null) entry.AcceptReturnNotice(notice);
            Destroy(gameObject);
        }
    }
}
