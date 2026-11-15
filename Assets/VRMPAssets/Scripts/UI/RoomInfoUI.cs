using UnityEngine;
using TMPro;

namespace XRMultiplayer
{
    /// <summary>
    /// Updates the UI fields displaying current room name and room connection code.
    /// </summary>
    public class RoomInfoUI : MonoBehaviour
    {
        [SerializeField] TMP_Text m_RoomNameText;
        [SerializeField] TMP_Text m_RoomCodeText;
        [SerializeField] TMP_Text m_RoomCodeLabelText;

        void Awake()
        {
            XRINetworkGameManager.ConnectedRoomName.Subscribe(UpdateRoomName);
            XRINetworkGameManager.Connected.Subscribe(ConnectedStatusChanged);
        }

        void OnDestroy()
        {
            XRINetworkGameManager.ConnectedRoomName.Unsubscribe(UpdateRoomName);
            XRINetworkGameManager.Connected.Unsubscribe(ConnectedStatusChanged);
        }

        void ConnectedStatusChanged(bool connected)
        {
            if (connected)
            {
                if (m_RoomCodeText != null)
                {
                    m_RoomCodeText.text = XRINetworkGameManager.ConnectedRoomCode;
                }
                UpdateRoomName(XRINetworkGameManager.ConnectedRoomName.Value);
            }
        }

        void UpdateRoomName(string roomName)
        {
            if (m_RoomNameText != null)
            {
                m_RoomNameText.text = roomName;
            }
            if (m_RoomCodeText != null)
            {
                m_RoomCodeText.text = XRINetworkGameManager.ConnectedRoomCode;
            }
        }
    }
}
