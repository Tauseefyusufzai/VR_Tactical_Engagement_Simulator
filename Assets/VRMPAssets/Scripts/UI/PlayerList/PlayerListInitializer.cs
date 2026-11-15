using UnityEngine;

namespace XRMultiplayer
{
    public class PlayerListInitializer : MonoBehaviour
    {
        [SerializeField] PlayerListUI[] m_PlayerListUIs;

        void Start()
        {
            foreach (var l in m_PlayerListUIs)
            {
                if (l != null)
                    l.InitializeCallbacks();
                else
                    Debug.LogWarning("[VR-DEBUG] PlayerListInitializer: a PlayerListUI slot is null/unassigned in the Inspector.");
            }
        }
    }
}
