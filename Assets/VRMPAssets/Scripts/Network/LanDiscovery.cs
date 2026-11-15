using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Automatic LAN host discovery via UDP broadcast.
    ///
    /// HOST side: Call StartBroadcasting(ip, port) after StartHost().
    ///            The host sends "VIRTAC_HOST:{ip}:{port}" every second on UDP broadcast port 47777.
    ///
    /// CLIENT side: Call StartListening() then subscribe to OnHostDiscovered.
    ///              When a broadcast arrives, the event fires with (hostIp, hostPort).
    ///              Call StopAll() after connecting.
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        // ----- Constants -----
        public const int DiscoveryPort = 47777;
        const string k_Token = "VIRTAC_HOST";
        const float k_BroadcastInterval = 1.0f;

        // ----- Events -----
        /// <summary>Fires on the client when a host broadcast is received. Args: (hostIp, hostPort)</summary>
        public event Action<string, ushort> OnHostDiscovered;

        // ----- Internal -----
        UdpClient m_Broadcaster;
        UdpClient m_Listener;
        Coroutine m_BroadcastCoroutine;
        Coroutine m_ListenCoroutine;

        bool m_IsBroadcasting;
        bool m_IsListening;

        // ----- Public API -----

        /// <summary>Start broadcasting host presence on the LAN (call from host after StartHost).</summary>
        public void StartBroadcasting(string ip, ushort port)
        {
            StopBroadcasting();
            m_IsBroadcasting = true;
            m_BroadcastCoroutine = StartCoroutine(BroadcastCoroutine(ip, port));
            Debug.Log($"[LanDiscovery] Broadcasting host at {ip}:{port} on port {DiscoveryPort}");
        }

        /// <summary>Stop broadcasting (call from host on disconnect/quit).</summary>
        public void StopBroadcasting()
        {
            m_IsBroadcasting = false;
            if (m_BroadcastCoroutine != null)
            {
                StopCoroutine(m_BroadcastCoroutine);
                m_BroadcastCoroutine = null;
            }
            try { m_Broadcaster?.Close(); } catch { }
            m_Broadcaster = null;
        }

        /// <summary>Start listening for host broadcasts (call from client before joining).</summary>
        public void StartListening()
        {
            StopListening();
            m_IsListening = true;
            m_ListenCoroutine = StartCoroutine(ListenCoroutine());
            Debug.Log($"[LanDiscovery] Listening for host broadcasts on port {DiscoveryPort}...");
        }

        /// <summary>Stop listening (call from client after connected or on timeout).</summary>
        public void StopListening()
        {
            m_IsListening = false;
            if (m_ListenCoroutine != null)
            {
                StopCoroutine(m_ListenCoroutine);
                m_ListenCoroutine = null;
            }
            try { m_Listener?.Close(); } catch { }
            m_Listener = null;
        }

        /// <summary>Stop everything — broadcasting and listening.</summary>
        public void StopAll()
        {
            StopBroadcasting();
            StopListening();
        }

        // ----- Coroutines -----

        IEnumerator BroadcastCoroutine(string ip, ushort port)
        {
            string message = $"{k_Token}:{ip}:{port}";
            byte[] data = Encoding.UTF8.GetBytes(message);
            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            var wait = new WaitForSeconds(k_BroadcastInterval);

            while (m_IsBroadcasting)
            {
                try
                {
                    if (m_Broadcaster == null)
                    {
                        m_Broadcaster = new UdpClient();
                        m_Broadcaster.EnableBroadcast = true;
                    }
                    m_Broadcaster.Send(data, data.Length, endpoint);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LanDiscovery] Broadcast error: {e.Message}");
                    try { m_Broadcaster?.Close(); } catch { }
                    m_Broadcaster = null;
                }
                yield return wait;
            }
        }

        IEnumerator ListenCoroutine()
        {
            // Try to bind listener; port may be in use if host and client are on same machine
            try
            {
                m_Listener = new UdpClient();
                m_Listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                m_Listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                m_Listener.Client.Blocking = false; // Non-blocking so we can poll in coroutine
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LanDiscovery] Could not open listener on port {DiscoveryPort}: {e.Message}");
                yield break;
            }

            var endpoint = new IPEndPoint(IPAddress.Any, 0);

            while (m_IsListening)
            {
                try
                {
                    // Poll without blocking — Available returns bytes ready to read
                    while (m_Listener != null && m_Listener.Available > 0)
                    {
                        byte[] data = m_Listener.Receive(ref endpoint);
                        string message = Encoding.UTF8.GetString(data);

                        if (message.StartsWith(k_Token + ":"))
                        {
                            // Format: VIRTAC_HOST:{ip}:{port}
                            string[] parts = message.Split(':');
                            if (parts.Length == 3 && ushort.TryParse(parts[2], out ushort hostPort))
                            {
                                string hostIp = parts[1];
                                Debug.Log($"[LanDiscovery] Host discovered! IP={hostIp} Port={hostPort}");
                                OnHostDiscovered?.Invoke(hostIp, hostPort);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LanDiscovery] Listen error: {e.Message}");
                }

                yield return null; // Poll every frame
            }
        }

        void OnDestroy()
        {
            StopAll();
        }
    }
}
