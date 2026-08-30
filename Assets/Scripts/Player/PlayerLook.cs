using UnityEngine;
using UnityEngine.InputSystem;

namespace VibeGame1
{
    public class PlayerLook : MonoBehaviour
    {
        public Transform pivot;      // pitch
        public Transform cam;        // the actual camera transform
        public float mouseSensitivity = 0.08f;
        public float stickSensitivity = 180f;

        float yaw, pitch;

        public Vector3 AimForward => cam != null ? cam.forward : transform.forward;
        public Transform Cam => cam;

        void Awake()
        {
            yaw = transform.eulerAngles.y;
            if (cam == null) { var c = GetComponentInChildren<Camera>(); if (c) cam = c.transform; }
        }

        void Update()
        {
            // WebGL / first click: cursor lock only works inside a user gesture.
            if (GameManager.IsPlaying && Cursor.lockState != CursorLockMode.Locked &&
                Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (!GameManager.IsPlaying || InputReader.I == null) return;
            var input = InputReader.I;
            Vector2 d = input.LookDelta;
            if (input.LookIsMouse)
            {
                yaw += d.x * mouseSensitivity;
                pitch -= d.y * mouseSensitivity;
            }
            else
            {
                float dt = Time.unscaledDeltaTime;
                yaw += d.x * stickSensitivity * dt;
                pitch -= d.y * stickSensitivity * dt;
            }
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            Apply();
        }

        void Apply()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (pivot != null) pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        public void SetYaw(float y)
        {
            yaw = y;
            pitch = 0f;
            Apply();
        }
    }
}
