using System.Text;
using TMPro;
using UnityEngine;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Vehículo de un agente: modelo (hijo "Body"), anillo de color en el suelo con su identidad y placa con id, estado,
    /// carga y alertas. Solo muestra el estado del trace; la pose interpolada la calcula <see cref="VehicleMotion"/>.
    /// </summary>
    public sealed class AgentView : MonoBehaviour
    {
        public const float VehicleFootprint = .92f;
        public const float LabelHeight = 7.5f;

        public string AgentId { get; private set; }
        public string AgentType { get; private set; }
        public Color Identity { get; private set; }
        public TextMeshPro Label { get; private set; }
        public Renderer Marker { get; private set; }
        public Transform Body { get; private set; }
        public bool Restricted { get; private set; }
        public bool Refueled { get; private set; }

        MaterialPropertyBlock markerProperties;
        Camera viewer;
        float cellSize = 1;
        int capacity;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void Initialize(AgentData agent, Color identity, HarvestArt art, float cell, Camera camera)
        {
            AgentId = agent.Id; AgentType = agent.Type; Identity = identity; capacity = agent.Capacity;
            gameObject.name = agent.Id;
            cellSize = cell; viewer = camera;
            Body = transform.Find("Body");
            if (Body)
            {
                Body.localScale *= cell * VehicleFootprint;
                // El pivote de los modelos no siempre está en la base: se apoya el modelo sobre el suelo.
                var renderers = Body.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    Body.position += Vector3.up * (transform.position.y - bounds.min.y);
                }
            }

            if (art && art.markerMaterial)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ring.name = "Marker";
                DestroyCollider(ring);
                ring.transform.SetParent(transform, false);
                ring.transform.localPosition = Vector3.up * .12f;
                ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
                ring.transform.localScale = Vector3.one * cell * 1.02f;
                Marker = ring.GetComponent<Renderer>();
                Marker.sharedMaterial = art.markerMaterial;
                Marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Marker.receiveShadows = false;
                markerProperties = new MaterialPropertyBlock();
                SetMarkerColor(identity);
            }

            var text = new GameObject("AgentLabel");
            text.transform.SetParent(transform, false);
            text.transform.localPosition = Vector3.up * LabelHeight;
            Label = text.AddComponent<TextMeshPro>();
            if (art && art.labelFont) Label.font = art.labelFont;
            if (art && art.labelMaterial) Label.fontSharedMaterial = art.labelMaterial;
            Label.alignment = TextAlignmentOptions.Bottom;
            Label.fontSize = 6;
            Label.textWrappingMode = TextWrappingModes.NoWrap;
            Label.richText = true;
            Label.color = Color.white;
            Label.rectTransform.sizeDelta = new Vector2(40, 10);
            Label.rectTransform.pivot = new Vector2(.5f, 0);
            Label.text = agent.Id;
        }

        void SetMarkerColor(Color color)
        {
            if (!Marker) return;
            markerProperties.SetColor(BaseColor, color);
            Marker.SetPropertyBlock(markerProperties);
        }

        public void Render(AgentState state, VehiclePose pose, string alert = null, string proposal = null, string decision = null)
        {
            SetPose(pose);
            Restricted = alert != null && alert.Contains("RESTRICCION:");
            Refueled = alert != null && alert.Contains("RECARGA");
            if (!Restricted) SetMarkerColor(Identity);
            if (!Label) return;
            var builder = new StringBuilder();
            builder.Append("<b><color=#").Append(ColorUtility.ToHtmlStringRGB(Color.Lerp(Identity, Color.white, .35f))).Append('>')
                .Append(AgentId).Append("</color></b>\n");
            builder.Append("<size=80%>").Append(StatusText(state.Status));
            if (capacity > 0) builder.Append(" · ").Append(state.Load).Append('/').Append(capacity);
            builder.Append("</size>");
            if (proposal != null) builder.Append("\n<color=#9fd0ff>Oferta: ").Append(proposal).Append("</color>");
            if (decision != null) builder.Append("\n<color=#c9b6ff>").Append(decision).Append("</color>");
            if (alert != null) builder.Append("\n<color=#ffb4a0>").Append(alert).Append("</color>");
            Label.text = builder.ToString();
        }

        public void SetPose(VehiclePose pose)
        {
            transform.localPosition = pose.Position;
            transform.localRotation = pose.Rotation;
            Billboard();
        }

        public static string StatusText(string status) => status switch
        {
            "EN_BASE" => "En base",
            "YENDO_A_CAMPO" => "Yendo al campo",
            "COSECHANDO" => "Cosechando",
            "LLENA_ESPERANDO" => "Llena, esperando",
            "TRANSFIRIENDO" => "Transfiriendo",
            "REGRESANDO_A_BASE" => "Regresando a base",
            "FIN" => "Terminó",
            "YENDO_A_COSECHADORA" => "Yendo a cosechadora",
            "ESPERANDO_TRANSFERENCIA" => "Esperando transferencia",
            "YENDO_A_BODEGA" => "Yendo a bodega",
            "DESCARGANDO" => "Descargando",
            _ => status ?? ""
        };

        void LateUpdate()
        {
            Billboard();
            if (Marker && (Restricted || Refueled))
            {
                float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 9f);
                var alertColor = Restricted ? new Color(.95f, .2f, .15f) : new Color(.3f, .95f, .4f);
                SetMarkerColor(Color.Lerp(Identity, alertColor, pulse));
            }
        }

        public void FaceCamera() => Billboard();

        /// <summary>Desplaza la placa (en el mundo) para que no se encime con la de un vecino.</summary>
        public void SetLabelOffset(Vector3 offset)
        {
            if (Label) Label.transform.position = LabelAnchor + offset;
        }

        /// <summary>Punto de anclaje de la placa sin el desplazamiento por vecinos.</summary>
        public Vector3 LabelAnchor => transform.position + Vector3.up * LabelHeight;

        /// <summary>La placa mira a la cámara y conserva un tamaño legible en pantalla.</summary>
        void Billboard()
        {
            if (!Label) return;
            var cam = viewer ? viewer : Camera.main;
            if (!cam) return;
            var t = Label.transform;
            t.rotation = cam.transform.rotation;
            float scale;
            if (cam.orthographic) scale = cam.orthographicSize * .045f;
            else scale = Vector3.Distance(cam.transform.position, t.position) * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2) * .09f;
            scale = Mathf.Clamp(scale, .25f, cellSize * .6f);
            t.localScale = Vector3.one * scale;
        }

        static void DestroyCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (!collider) return;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }
    }
}
