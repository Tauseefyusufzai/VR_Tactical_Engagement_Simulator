using UnityEngine;
using UnityEngine.EventSystems;

namespace XRMultiplayer
{
    /// <summary>
    /// Attached to inventory prop slots to support drag-and-drop mechanics.
    /// </summary>
    public class ExConInventoryDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("The index of the prop prefab in GridMapManager's placeable list.")]
        [SerializeField] private int m_PropIndex;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (ExConGridEditor.Instance != null)
            {
                // Select the tool corresponding to this prop
                ExConGridEditor.Instance.SelectTool(m_PropIndex);
                // Flag that we are active dragging
                ExConGridEditor.Instance.IsDraggingFromUI = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Empty body is required by Unity's EventSystem to recognize dragging
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (ExConGridEditor.Instance != null)
            {
                // Drop the item to spawn it in the room, and end the dragging state
                ExConGridEditor.Instance.DropActiveProp();
            }
        }
    }
}
