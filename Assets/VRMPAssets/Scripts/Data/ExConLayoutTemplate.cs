using UnityEngine;
using System.Collections.Generic;

namespace XRMultiplayer
{
    [System.Serializable]
    public struct PlacedObjectRecord
    {
        public Vector2Int GridPosition;
        public int PrefabIndex;
        public float YRotation;
        public Vector3 Scale;
    }

    [CreateAssetMenu(fileName = "NewExConLayout", menuName = "XR Multiplayer/ExCon Layout Template")]
    public class ExConLayoutTemplate : ScriptableObject
    {
        [Tooltip("The user-friendly name of the layout.")]
        public string LayoutName;

        [Tooltip("2D preview thumbnail of this layout design.")]
        public Sprite LayoutPreviewSprite;

        [Tooltip("List of pre-placed objects in this layout.")]
        public List<PlacedObjectRecord> PlacedObjects = new List<PlacedObjectRecord>();
    }
}
