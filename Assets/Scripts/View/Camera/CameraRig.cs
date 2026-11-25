using UnityEngine;
using UnityEngine.InputSystem;

namespace HarvestSim.Visualization
{
    public enum CameraMode { Orbit, Top, Follow }

    /// <summary>
    /// Cámara del mundo. Órbita: clic derecho gira, clic central o WASD/flechas desplazan, la rueda acerca hacia el cursor.
    /// Cenital: ortográfica desde arriba (rueda y desplazamiento). Seguir: órbita centrada en un agente.
    /// F reencuadra el campo. El movimiento se amortigua; nada de esto toca el estado de la simulación.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public float orbitSpeed = .25f, panSpeed = 1f, zoomStep = .12f, damping = 10f;
        public float minPitch = 12, maxPitch = 85, minDistance = 12;
        [Tooltip("Fracción de pantalla tapada por el HUD (izquierda, derecha, arriba, abajo); el encuadre usa el área libre.")]
        public Vector4 overlayInsets;

        public CameraMode Mode { get; private set; } = CameraMode.Orbit;
        public Transform FollowTarget { get; private set; }
        public Camera Camera => cachedCamera ? cachedCamera : cachedCamera = GetComponent<Camera>();

        Camera cachedCamera;
        Bounds area = new(Vector3.zero, Vector3.one * 100);
        Vector3 focus, targetFocus;
        float yaw = 30, pitch = 42, distance = 150, targetYaw = 30, targetPitch = 42, targetDistance = 150;
        float topSize = 80, targetTopSize = 80;

        /// <summary>Área a encuadrar (la cuadrícula en el mundo) y reinicio de la vista.</summary>
        public void SetArea(Bounds bounds, bool reset)
        {
            area = bounds;
            if (reset) Frame(snap: true);
        }

        public void SetMode(CameraMode mode, Transform target = null)
        {
            Mode = mode;
            FollowTarget = mode == CameraMode.Follow ? target : null;
            if (mode == CameraMode.Follow && !target) Mode = CameraMode.Orbit;
            if (mode == CameraMode.Follow) targetDistance = Mathf.Max(minDistance * 2.5f, 45);
            Apply(snap: false);
        }

        /// <summary>Encuadra toda el área con la vista actual (órbita o cenital).</summary>
        public void Frame(bool snap = false)
        {
            targetFocus = area.center;
            if (Mode == CameraMode.Follow) Mode = CameraMode.Orbit;
            var cam = Camera;
            float aspect = Mathf.Max(.1f, cam.aspect);
            float freeX = Mathf.Clamp(1 - overlayInsets.x - overlayInsets.y, .2f, 1);
            float freeY = Mathf.Clamp(1 - overlayInsets.z - overlayInsets.w, .2f, 1);
            // Desplazamiento del centro del área libre respecto al centro de la pantalla (en fracciones).
            float shiftX = (overlayInsets.y - overlayInsets.x) / 2, shiftY = (overlayInsets.w - overlayInsets.z) / 2;
            targetTopSize = Mathf.Max(area.extents.z / freeY, area.extents.x / (aspect * freeX)) * 1.12f + 4;
            float radius = new Vector2(area.extents.x, area.extents.z).magnitude + 6;
            float tanV = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
            float vertical = Mathf.Atan(tanV * freeY);
            float horizontal = Mathf.Atan(tanV * aspect * freeX);
            targetDistance = radius / Mathf.Sin(Mathf.Min(vertical, horizontal));
            if (Mode == CameraMode.Top)
                targetFocus += new Vector3(shiftX * 2 * targetTopSize * aspect, 0, -shiftY * 2 * targetTopSize);
            else
            {
                var rotation = Quaternion.Euler(targetPitch, targetYaw, 0);
                float height = 2 * targetDistance * tanV, width = height * aspect;
                var groundForward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up).normalized;
                targetFocus += rotation * Vector3.right * shiftX * width
                             - groundForward * shiftY * height / Mathf.Max(.3f, Mathf.Sin(targetPitch * Mathf.Deg2Rad));
            }
            Apply(snap);
        }

        /// <summary>Coloca la órbita en un punto y ángulo concretos (capturas, botones de vista).</summary>
        public void SetOrbit(Vector3 focusPoint, float yawDegrees, float pitchDegrees, float orbitDistance, bool snap = true)
        {
            Mode = CameraMode.Orbit; FollowTarget = null;
            targetFocus = focusPoint; targetYaw = yawDegrees;
            targetPitch = Mathf.Clamp(pitchDegrees, minPitch, maxPitch);
            targetDistance = Mathf.Max(minDistance, orbitDistance);
            Apply(snap);
        }

        /// <summary>Salta a la posición objetivo sin amortiguación.</summary>
        public void Snap() => Apply(snap: true);

        public void ResetView(CameraMode mode)
        {
            Mode = mode; FollowTarget = null;
            targetYaw = 30; targetPitch = 42;
            Frame(snap: true);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            HandleInput(Time.unscaledDeltaTime);
            Apply(snap: false, Time.unscaledDeltaTime);
        }

        void HandleInput(float dt)
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var cam = Camera;
            bool overView = mouse != null && cam.pixelRect.Contains(mouse.position.ReadValue()) && !UiFocus.PointerOverUi;
            float scale = Mode == CameraMode.Top ? targetTopSize : targetDistance;
            if (mouse != null && overView)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0) Zoom(scroll > 0 ? 1 - zoomStep : 1 + zoomStep, mouse.position.ReadValue());
                var delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed && Mode != CameraMode.Top)
                {
                    targetYaw += delta.x * orbitSpeed;
                    targetPitch = Mathf.Clamp(targetPitch - delta.y * orbitSpeed, minPitch, maxPitch);
                }
                if (mouse.middleButton.isPressed) Pan(-delta.x, -delta.y, scale * .0022f * panSpeed);
            }
            if (keyboard != null && !UiFocus.Typing)
            {
                float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                if (x != 0 || y != 0) Pan(x, y, scale * .9f * panSpeed * dt);
                if (keyboard.fKey.wasPressedThisFrame) Frame();
            }
        }

        void Pan(float right, float forward, float amount)
        {
            if (Mode == CameraMode.Follow) Mode = CameraMode.Orbit;
            var yawRotation = Quaternion.Euler(0, Mode == CameraMode.Top ? 0 : targetYaw, 0);
            targetFocus += yawRotation * new Vector3(right, 0, forward) * amount;
            var limit = area; limit.Expand(new Vector3(area.size.x, 0, area.size.z));
            targetFocus = limit.ClosestPoint(targetFocus);
        }

        void Zoom(float factor, Vector2 screenPoint)
        {
            var cam = Camera;
            // Acercar hacia el punto bajo el cursor sobre el plano del suelo.
            var ray = cam.ScreenPointToRay(screenPoint);
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float hit) && Mode != CameraMode.Follow)
            {
                var point = ray.GetPoint(hit);
                targetFocus = Vector3.Lerp(targetFocus, point, (1 - factor) * .9f);
                var limit = area; limit.Expand(new Vector3(area.size.x, 0, area.size.z));
                targetFocus = limit.ClosestPoint(targetFocus);
            }
            if (Mode == CameraMode.Top) targetTopSize = Mathf.Clamp(targetTopSize * factor, 8, area.size.magnitude * 1.5f);
            else targetDistance = Mathf.Clamp(targetDistance * factor, minDistance, area.size.magnitude * 3);
        }

        /// <summary>Coloca la cámara. Con <paramref name="snap"/> salta al objetivo; si no, se acerca con amortiguación.</summary>
        void Apply(bool snap, float dt = 0)
        {
            var cam = Camera;
            if (Mode == CameraMode.Follow && FollowTarget) targetFocus = FollowTarget.position;
            float k = snap ? 1 : 1 - Mathf.Exp(-damping * dt);
            focus = Vector3.Lerp(focus, targetFocus, k);
            yaw = Mathf.LerpAngle(yaw, targetYaw, k);
            pitch = Mathf.Lerp(pitch, targetPitch, k);
            distance = Mathf.Lerp(distance, targetDistance, k);
            topSize = Mathf.Lerp(topSize, targetTopSize, k);
            if (Mode == CameraMode.Top)
            {
                cam.orthographic = true;
                cam.orthographicSize = topSize;
                transform.SetPositionAndRotation(focus + Vector3.up * 400, Quaternion.Euler(90, 0, 0));
                cam.nearClipPlane = 1; cam.farClipPlane = 1000;
            }
            else
            {
                cam.orthographic = false;
                var rotation = Quaternion.Euler(pitch, yaw, 0);
                transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
                cam.nearClipPlane = Mathf.Clamp(distance * .01f, .3f, 5f);
                cam.farClipPlane = Mathf.Max(3000, distance * 6);
            }
        }
    }
}
