using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HarvestSim.Visualization
{
    /// <summary>Tarjeta de un agente en el panel: color propio, estado, carga y combustible. Clic: seguirlo con la cámara.</summary>
    public sealed class HudAgentCard : MonoBehaviour
    {
        public Image colorBar, background, highlight;
        public TMP_Text title, subtitle, state, loadText, fuelText;
        public RectTransform loadFill, fuelFill;
        public Image fuelFillImage;
        public Button button;

        public string AgentId { get; private set; }

        public void Setup(AgentData agent, Color identity, System.Action<string> onSelect)
        {
            AgentId = agent.Id;
            gameObject.name = "Tarjeta_" + agent.Id;
            if (colorBar) colorBar.color = identity;
            if (title) title.text = agent.Id;
            if (subtitle) subtitle.text = HudFormat.AgentType(agent.Type);
            if (button)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onSelect?.Invoke(AgentId));
            }
        }

        public void Render(AgentState value, int capacity, float tank, string alert, bool followed)
        {
            if (state)
            {
                string text = AgentView.StatusText(value.Status);
                if (alert != null) text += $"  <color={HudFormat.Bad}>{alert.Replace("\n", " · ")}</color>";
                state.text = text;
            }
            float load = capacity > 0 ? Mathf.Clamp01(value.Load / (float)capacity) : 0;
            SetFill(loadFill, load);
            if (loadText) loadText.text = capacity > 0 ? $"Carga {value.Load}/{capacity}" : $"Carga {value.Load}";
            bool hasFuel = value.Fuel.HasValue;
            if (fuelText) fuelText.transform.parent.gameObject.SetActive(hasFuel);
            if (hasFuel)
            {
                float fuel = tank > 0 ? Mathf.Clamp01(value.Fuel.Value / tank) : 1;
                SetFill(fuelFill, fuel);
                if (fuelFillImage) fuelFillImage.color = fuel < .2f ? new Color(.95f, .36f, .3f) : fuel < .4f ? new Color(.95f, .7f, .24f) : new Color(.4f, .78f, .52f);
                if (fuelText) fuelText.text = tank > 0 ? $"Combustible {fuel * 100:0}%" : $"Combustible {value.Fuel.Value:0.#}";
            }
            if (highlight) highlight.enabled = followed;
        }

        static void SetFill(RectTransform fill, float value)
        {
            if (!fill) return;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(value, 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }
    }
}
