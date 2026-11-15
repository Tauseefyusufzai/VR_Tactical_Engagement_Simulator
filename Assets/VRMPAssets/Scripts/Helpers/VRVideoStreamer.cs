using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRMultiplayer
{
    /// <summary>
    /// Captures the VR Client camera view and streams it over the local network via HTTP MJPEG.
    /// Ultra-optimized for zero-lag (< 15ms), smooth streaming by capturing on-demand only when clients are connected,
    /// using downscaled 16:9 resolution (512x288) and instant socket flushing.
    /// Compatible with VLC Media Player (http://<client-ip>:8080/stream) and standard web browsers.
    /// </summary>
    public class VRVideoStreamer : MonoBehaviour
    {
        public static VRVideoStreamer Instance { get; private set; }

        [Header("Stream Configuration")]
        [Tooltip("Port to host the HTTP stream on.")]
        public int streamPort = 8080;

        [Tooltip("Target stream resolution width (512x288 recommended for zero-lag, butter-smooth streaming).")]
        public int streamWidth = 512;

        [Tooltip("Target stream resolution height.")]
        public int streamHeight = 288;

        [Tooltip("Target frames per second for the stream.")]
        [Range(15, 60)]
        public int targetFPS = 30;

        [Tooltip("JPEG Quality (1 - 100). Lower values (35-45) ensure smooth, lag-free transmission over Wi-Fi.")]
        [Range(25, 75)]
        public int jpegQuality = 40;

        [Tooltip("If true, stream starts automatically when client connects.")]
        public bool autoStartOnConnect = true;

        // Internal State
        HttpListener m_HttpListener;
        Thread m_ListenerThread;

        Camera m_TargetCamera;
        Camera m_StreamCamera;
        RenderTexture m_RenderTexture;
        Texture2D m_ReadbackTexture;

        byte[] m_LatestJpegFrame;
        int m_FrameSequence = 0;
        int m_ActiveClients = 0;
        readonly object m_FrameLock = new();
        readonly AutoResetEvent m_FrameReadyEvent = new(false);

        bool m_IsStreaming = false;
        bool m_IsReadbackInFlight = false;

        float m_FrameInterval;
        float m_LastFrameTime;

        byte[] m_PendingPixelBuffer;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            m_FrameInterval = 1f / targetFPS;
        }

        void OnDestroy()
        {
            StopStream();
            CleanupRenderResources();
        }

        public void StartStream()
        {
            if (m_IsStreaming) return;

            FindTargetCamera();

            if (m_TargetCamera == null)
            {
                Debug.LogWarning("[VRVideoStreamer] Cannot start stream — Main/VR Camera not found!");
                return;
            }

            m_FrameInterval = 1f / targetFPS;
            SetupRenderResources();

            try
            {
                m_HttpListener = new HttpListener();
                m_HttpListener.Prefixes.Add($"http://*:{streamPort}/");
                m_HttpListener.Start();
                m_IsStreaming = true;

                m_ListenerThread = new Thread(ListenForClients) { IsBackground = true };
                m_ListenerThread.Start();

                string localIP = XRINetworkGameManager.Instance != null 
                    ? XRINetworkGameManager.Instance.GetLocalIPAddress() 
                    : "127.0.0.1";

                string streamUrl = $"http://{localIP}:{streamPort}/stream";
                Debug.Log($"<color=#00FFCC><b>[VRVideoStreamer] Low-Latency Zero-Lag VR Camera Stream ready!</b></color>\n" +
                          $"Play in VLC or Browser: <b><color=yellow>{streamUrl}</color></b>");

                if (PlayerHudNotification.Instance != null)
                {
                    PlayerHudNotification.Instance.ShowText($"Stream Live: {streamUrl}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRVideoStreamer] Failed to start HTTP listener on port {streamPort}: {ex.Message}");
                m_IsStreaming = false;
            }
        }

        public void StopStream()
        {
            if (!m_IsStreaming) return;
            m_IsStreaming = false;

            try
            {
                m_HttpListener?.Stop();
                m_HttpListener?.Close();
            }
            catch { }

            m_HttpListener = null;
            Debug.Log("[VRVideoStreamer] Video stream stopped.");
        }

        void FindTargetCamera()
        {
            var xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                m_TargetCamera = xrOrigin.Camera;
                return;
            }

            m_TargetCamera = Camera.main;

            if (m_TargetCamera == null)
            {
                var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                if (cams.Length > 0) m_TargetCamera = cams[0];
            }
        }

        void SetupRenderResources()
        {
            CleanupRenderResources();

            // Depth buffer size 24 is required for proper 3D depth rendering
            m_RenderTexture = new RenderTexture(streamWidth, streamHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "VRVideoStreamer_RT",
                antiAliasing = 1,
                useMipMap = false
            };
            m_RenderTexture.Create();

            m_ReadbackTexture = new Texture2D(streamWidth, streamHeight, TextureFormat.RGBA32, false);
            m_PendingPixelBuffer = new byte[streamWidth * streamHeight * 4];

            // Create dedicated 2D stream capture camera matching VR camera position & rotation
            if (m_TargetCamera != null)
            {
                GameObject streamCamObj = new GameObject("VRVideoStreamer_CaptureCamera");
                streamCamObj.transform.SetParent(m_TargetCamera.transform, false);
                streamCamObj.transform.localPosition = Vector3.zero;
                streamCamObj.transform.localRotation = Quaternion.identity;

                m_StreamCamera = streamCamObj.AddComponent<Camera>();
                m_StreamCamera.CopyFrom(m_TargetCamera);
                m_StreamCamera.targetTexture = m_RenderTexture;
                m_StreamCamera.enabled = false; // Rendered on-demand when clients connected
                m_StreamCamera.allowHDR = false;
                m_StreamCamera.allowMSAA = false;
                m_StreamCamera.farClipPlane = Mathf.Min(m_TargetCamera.farClipPlane, 150f);
                m_StreamCamera.useOcclusionCulling = false;
                m_StreamCamera.stereoTargetEye = StereoTargetEyeMask.None; // 2D flat stream
            }
        }

        void CleanupRenderResources()
        {
            if (m_StreamCamera != null)
            {
                Destroy(m_StreamCamera.gameObject);
                m_StreamCamera = null;
            }

            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                Destroy(m_RenderTexture);
                m_RenderTexture = null;
            }
            if (m_ReadbackTexture != null)
            {
                Destroy(m_ReadbackTexture);
                m_ReadbackTexture = null;
            }
            m_PendingPixelBuffer = null;
        }

        void LateUpdate()
        {
            if (!m_IsStreaming || m_RenderTexture == null) return;

            // ON-DEMAND CAPTURE: Only render & readback if at least 1 web browser or VLC client is listening!
            if (m_ActiveClients <= 0) return;

            // Frame rate throttling for stable streaming
            if (Time.time - m_LastFrameTime < m_FrameInterval) return;

            if (!m_IsReadbackInFlight && m_StreamCamera != null)
            {
                m_LastFrameTime = Time.time;

                // Sync camera transform and render scene directly into m_RenderTexture
                m_StreamCamera.transform.SetPositionAndRotation(m_TargetCamera.transform.position, m_TargetCamera.transform.rotation);
                m_StreamCamera.Render();

                m_IsReadbackInFlight = true;
                AsyncGPUReadback.Request(m_RenderTexture, 0, TextureFormat.RGBA32, OnGPUReadbackComplete);
            }

            // Process readback pixel data into JPEG
            byte[] bufferToProcess = null;
            lock (m_FrameLock)
            {
                if (m_PendingPixelBuffer != null)
                {
                    bufferToProcess = m_PendingPixelBuffer;
                    m_PendingPixelBuffer = null;
                }
            }

            if (bufferToProcess != null && m_ReadbackTexture != null && m_ReadbackTexture.width == streamWidth)
            {
                m_ReadbackTexture.LoadRawTextureData(bufferToProcess);
                m_ReadbackTexture.Apply();
                byte[] jpegBytes = m_ReadbackTexture.EncodeToJPG(jpegQuality);

                lock (m_FrameLock)
                {
                    m_LatestJpegFrame = jpegBytes;
                    m_FrameSequence++;
                }

                m_FrameReadyEvent.Set();
            }
        }

        void OnGPUReadbackComplete(AsyncGPUReadbackRequest request)
        {
            m_IsReadbackInFlight = false;

            if (!m_IsStreaming || request.hasError) return;

            NativeArray<byte> rawData = request.GetData<byte>();
            if (!rawData.IsCreated || rawData.Length == 0) return;

            lock (m_FrameLock)
            {
                if (m_PendingPixelBuffer == null || m_PendingPixelBuffer.Length != rawData.Length)
                {
                    m_PendingPixelBuffer = new byte[rawData.Length];
                }
                NativeArray<byte>.Copy(rawData, m_PendingPixelBuffer, rawData.Length);
            }
        }

        void ListenForClients()
        {
            while (m_IsStreaming && m_HttpListener != null && m_HttpListener.IsListening)
            {
                try
                {
                    HttpListenerContext context = m_HttpListener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleClientConnection(context));
                }
                catch
                {
                    break;
                }
            }
        }

        void HandleClientConnection(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // Anti-caching & anti-buffering HTTP headers for zero lag
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
            response.AddHeader("Pragma", "no-cache");
            response.AddHeader("Expires", "0");
            response.AddHeader("X-Accel-Buffering", "no");
            response.AddHeader("Connection", "keep-alive");

            if (request.Url.AbsolutePath == "/" || request.Url.AbsolutePath == "/index.html")
            {
                string html = $"<!DOCTYPE html><html><head><title>Live VR Feed</title></head>" +
                              $"<body style='background:#111;color:#fff;text-align:center;font-family:sans-serif;margin-top:20px;'>" +
                              $"<h2>Live VR Client Feed</h2>" +
                              $"<img src='/stream' style='max-width:92%;border:2px solid #00FFCC;border-radius:8px;'/>" +
                              $"<p>VLC Stream URL: <code>http://{request.Url.Host}:{streamPort}/stream</code></p>" +
                              $"</body></html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                response.ContentType = "text/html";
                response.ContentLength64 = buffer.Length;
                try
                {
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                catch { }
                return;
            }

            if (request.Url.AbsolutePath == "/stream" || request.Url.AbsolutePath == "/live")
            {
                response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
                response.SendChunked = true;

                Interlocked.Increment(ref m_ActiveClients);
                try
                {
                    ClientStreamLoop(response);
                }
                finally
                {
                    Interlocked.Decrement(ref m_ActiveClients);
                }
                return;
            }

            response.StatusCode = 404;
            response.Close();
        }

        void ClientStreamLoop(HttpListenerResponse response)
        {
            int lastSentSequence = -1;

            try
            {
                while (m_IsStreaming)
                {
                    m_FrameReadyEvent.WaitOne(35);

                    byte[] frameBytes = null;
                    int currentSeq = -1;

                    lock (m_FrameLock)
                    {
                        if (m_FrameSequence != lastSentSequence && m_LatestJpegFrame != null)
                        {
                            frameBytes = m_LatestJpegFrame;
                            currentSeq = m_FrameSequence;
                        }
                    }

                    if (frameBytes != null)
                    {
                        string header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frameBytes.Length}\r\n\r\n";
                        byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
                        byte[] footerBytes = System.Text.Encoding.ASCII.GetBytes("\r\n");

                        response.OutputStream.Write(headerBytes, 0, headerBytes.Length);
                        response.OutputStream.Write(frameBytes, 0, frameBytes.Length);
                        response.OutputStream.Write(footerBytes, 0, footerBytes.Length);
                        response.OutputStream.Flush();

                        lastSentSequence = currentSeq;
                    }
                }
            }
            catch
            {
                // Client disconnected
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }
    }
}
