using UnityEngine;

namespace HarvestSim.Visualization
{
    public sealed class AgentView : MonoBehaviour
    {
        string agentId;
        TextMesh label;
        Renderer bodyRenderer;
        bool restricted;
        public void Initialize(AgentData agent, int variant = 0)
        {
            agentId = agent.Id; gameObject.name = agent.Id;
            var text = new GameObject("AgentLabel");
            text.transform.SetParent(transform, false);
            text.transform.localPosition = new Vector3(0, .9f, .38f);
            text.transform.localRotation = Quaternion.Euler(90, 0, 0);
            label = text.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<Renderer>().sharedMaterial = label.font.material;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 32; label.characterSize = .06f;
            label.color = Color.black;
            bodyRenderer = transform.Find("Body")?.GetComponent<Renderer>();
            if (bodyRenderer)
            {
                var tint = Color.HSVToRGB(((agent.Type == "carro" ? .58f : .32f) + variant * .06f) % 1f, .72f, .8f);
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", tint);
                bodyRenderer.SetPropertyBlock(properties);
            }
        }
        public void Render(AgentState state, Vector3 position, string alert = null, string proposal = null)
        {
            transform.localPosition = position;
            float angle = state.Orientation switch { "E" => 90, "S" => 180, "O" => 270, _ => 0 };
            transform.localRotation = Quaternion.Euler(0, angle, 0);
            restricted = alert != null && alert.Contains("RESTRICCION:");
            if (label)
            {
                label.text = agentId + (proposal == null ? "" : "\nOferta: " + proposal) + (alert == null ? "" : "\n" + alert);
                string[] lines = label.text.Split('\n');
                int longest = 1;
                foreach (string line in lines) longest = Mathf.Max(longest, line.Length);
                label.characterSize = .06f * Mathf.Min(1, 8f / longest, 2f / lines.Length);
                PositionLabel();
            }
        }
        public void Interpolate(AgentState from, AgentState to, Vector3 start, Vector3 end, float t)
        {
            transform.localPosition = Vector3.Lerp(start, end, t);
            float a = from.Orientation switch { "E" => 90, "S" => 180, "O" => 270, _ => 0 };
            float b = to.Orientation switch { "E" => 90, "S" => 180, "O" => 270, _ => 0 };
            transform.localRotation = Quaternion.Slerp(Quaternion.Euler(0, a, 0), Quaternion.Euler(0, b, 0), t);
            if (label) PositionLabel();
        }
        void PositionLabel()
        {
            label.transform.position = transform.position + new Vector3(0, .9f, .38f);
            label.transform.rotation = Quaternion.Euler(90, 0, 0);
        }
        void Update()
        {
            if (bodyRenderer) bodyRenderer.enabled = !restricted || Mathf.FloorToInt(Time.unscaledTime * 4) % 2 == 0;
        }
    }
}
