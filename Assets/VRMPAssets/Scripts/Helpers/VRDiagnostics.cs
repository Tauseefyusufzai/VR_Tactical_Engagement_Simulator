using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.XR.CoreUtils;

// Fully qualified to avoid ambiguity between UnityEngine.InputSystem and UnityEngine.XR
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRNode = UnityEngine.XR.XRNode;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace XRMultiplayer
{
    /// <summary>
    /// VRDiagnostics — drop this on any active GameObject in the scene.
    /// Logs camera, locomotion, and controller state to help debug movement/interaction issues.
    /// </summary>
    public class VRDiagnostics : MonoBehaviour
    {
        [Header("Diagnostics Settings")]
        [SerializeField] float m_RepeatLogInterval = 5f;

        XROrigin m_XROrigin;
        Camera m_MainCam;
        LocomotionMediator m_LocomotionMediator;
        TeleportationProvider m_TeleportProvider;

        void Start()
        {
            Debug.Log("========== [VR-DIAG] VRDiagnostics.Start() ===========");

            // ---- 1. CAMERA CHECK ----
            m_MainCam = Camera.main;
            if (m_MainCam == null)
            {
                Debug.LogError("[VR-DIAG] FAIL Camera.main is NULL — no camera tagged 'MainCamera'! This is why the view is stuck.");
            }
            else
            {
                Debug.Log($"[VR-DIAG] OK Camera.main found: '{m_MainCam.name}' on GO '{m_MainCam.gameObject.name}' | stereoEnabled={m_MainCam.stereoEnabled} | active={m_MainCam.gameObject.activeInHierarchy}");
            }

            // Log ALL cameras in the scene
            Camera[] allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            Debug.Log($"[VR-DIAG] Total cameras in scene: {allCams.Length}");
            foreach (var cam in allCams)
            {
                Debug.Log($"[VR-DIAG]   Camera: '{cam.name}' | GOactive={cam.gameObject.activeInHierarchy} | depth={cam.depth} | stereoEnabled={cam.stereoEnabled} | tag={cam.tag}");
            }

            // ---- 2. XR ORIGIN CHECK ----
            m_XROrigin = FindFirstObjectByType<XROrigin>();
            if (m_XROrigin == null)
            {
                Debug.LogError("[VR-DIAG] FAIL XROrigin NOT FOUND in scene! VR head tracking is impossible.");
            }
            else
            {
                Debug.Log($"[VR-DIAG] OK XROrigin: '{m_XROrigin.name}' pos={m_XROrigin.transform.position} GOactive={m_XROrigin.gameObject.activeInHierarchy}");
                Debug.Log($"[VR-DIAG]   CameraFloorOffsetObject: {(m_XROrigin.CameraFloorOffsetObject != null ? m_XROrigin.CameraFloorOffsetObject.name : "NULL")}");
                Debug.Log($"[VR-DIAG]   XROrigin.Camera: {(m_XROrigin.Camera != null ? m_XROrigin.Camera.name : "NULL")}");
                Debug.Log($"[VR-DIAG]   TrackingOriginMode: {m_XROrigin.RequestedTrackingOriginMode}");
            }

            // ---- 3. LOCOMOTION CHECK ----
            m_LocomotionMediator = FindFirstObjectByType<LocomotionMediator>();
            if (m_LocomotionMediator == null)
                Debug.LogError("[VR-DIAG] FAIL LocomotionMediator NOT FOUND — no locomotion system!");
            else
                Debug.Log($"[VR-DIAG] OK LocomotionMediator: '{m_LocomotionMediator.name}' active={m_LocomotionMediator.gameObject.activeInHierarchy}");

            m_TeleportProvider = FindFirstObjectByType<TeleportationProvider>();
            if (m_TeleportProvider == null)
                Debug.LogError("[VR-DIAG] FAIL TeleportationProvider NOT FOUND — teleport will not work.");
            else
                Debug.Log($"[VR-DIAG] OK TeleportationProvider: '{m_TeleportProvider.name}' active={m_TeleportProvider.gameObject.activeInHierarchy}");

            // List ALL locomotion providers (including inactive)
            LocomotionProvider[] lps = FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[VR-DIAG] Total LocomotionProviders (incl. inactive): {lps.Length}");
            foreach (var lp in lps)
                Debug.Log($"[VR-DIAG]   LP: '{lp.name}' type={lp.GetType().Name} enabled={lp.enabled} GOactive={lp.gameObject.activeInHierarchy}");

            // ---- 3.5 FLOOR CHECK (OFFLINE LOBBY) ----
            Vector3 spawnPos = new Vector3(0, 0.5f, -12f);
            if (Physics.Raycast(spawnPos, Vector3.down, out RaycastHit hit, 10f))
            {
                Debug.Log($"[VR-DIAG] OK Floor found at spawn! Hit: '{hit.collider.name}' type={hit.collider.GetType().Name} pos={hit.point}");
                if (hit.collider.isTrigger)
                {
                    Debug.LogError($"[VR-DIAG] FAIL Floor collider '{hit.collider.name}' is set to IS TRIGGER! You will fall through it!");
                }
            }
            else
            {
                Debug.LogError($"[VR-DIAG] FAIL NO FLOOR FOUND below spawn point {spawnPos}! The player will fall infinitely. Please add a Plane or Cube with a solid collider at (0, 0, -12).");
            }

            // ---- 4. XR DEVICE CHECK (deferred 2s) ----
            StartCoroutine(CheckXRDevices());

            // ---- 5. REPEAT TICK ----
            InvokeRepeating(nameof(LogOngoingState), m_RepeatLogInterval, m_RepeatLogInterval);

            Debug.Log("========== [VR-DIAG] End of startup diagnostics ===========");
        }

        IEnumerator CheckXRDevices()
        {
            yield return new WaitForSeconds(2f);

            // HEAD
            var headDevices = new List<XRInputDevice>();
            XRInputDevices.GetDevicesAtXRNode(XRNode.Head, headDevices);
            Debug.Log($"[VR-DIAG] XR Head devices: {headDevices.Count}");
            foreach (var d in headDevices)
            {
                Debug.Log($"[VR-DIAG]   Head: '{d.name}' isValid={d.isValid}");
                if (d.TryGetFeatureValue(XRCommonUsages.devicePosition, out Vector3 pos))
                    Debug.Log($"[VR-DIAG]     headPos={pos}");
                if (d.TryGetFeatureValue(XRCommonUsages.deviceRotation, out Quaternion rot))
                    Debug.Log($"[VR-DIAG]     headRot={rot.eulerAngles}");
            }

            // LEFT CONTROLLER
            var leftDevices = new List<XRInputDevice>();
            XRInputDevices.GetDevicesAtXRNode(XRNode.LeftHand, leftDevices);
            Debug.Log($"[VR-DIAG] XR Left controller devices: {leftDevices.Count}");
            foreach (var d in leftDevices)
            {
                Debug.Log($"[VR-DIAG]   Left: '{d.name}' isValid={d.isValid}");
                if (d.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 stick))
                    Debug.Log($"[VR-DIAG]     Left thumbstick={stick}");
                if (d.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool x))
                    Debug.Log($"[VR-DIAG]     X button={x}");
            }

            // RIGHT CONTROLLER
            var rightDevices = new List<XRInputDevice>();
            XRInputDevices.GetDevicesAtXRNode(XRNode.RightHand, rightDevices);
            Debug.Log($"[VR-DIAG] XR Right controller devices: {rightDevices.Count}");
            foreach (var d in rightDevices)
            {
                Debug.Log($"[VR-DIAG]   Right: '{d.name}' isValid={d.isValid}");
                if (d.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 stick))
                    Debug.Log($"[VR-DIAG]     Right thumbstick={stick}");
                if (d.TryGetFeatureValue(XRCommonUsages.triggerButton, out bool trig))
                    Debug.Log($"[VR-DIAG]     Trigger={trig}");
            }
        }

        void LogOngoingState()
        {
            if (m_XROrigin != null)
            {
                string camPos = m_XROrigin.Camera != null
                    ? m_XROrigin.Camera.transform.position.ToString("F3")
                    : "NO CAM";
                Debug.Log($"[VR-DIAG-TICK] XROrigin pos={m_XROrigin.transform.position:F3} | CamPos={camPos}");
            }

            var headDevices = new List<XRInputDevice>();
            XRInputDevices.GetDevicesAtXRNode(XRNode.Head, headDevices);
            foreach (var d in headDevices)
            {
                if (d.TryGetFeatureValue(XRCommonUsages.devicePosition, out Vector3 pos))
                    Debug.Log($"[VR-DIAG-TICK] Head devicePos={pos:F3}");
                if (d.TryGetFeatureValue(XRCommonUsages.deviceRotation, out Quaternion rot))
                    Debug.Log($"[VR-DIAG-TICK] Head deviceRot={rot.eulerAngles:F1}");
            }
        }

        void OnDestroy()
        {
            CancelInvoke(nameof(LogOngoingState));
        }
    }
}
