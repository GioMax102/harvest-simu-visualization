using UnityEngine;
using UnityEngine.EventSystems;

namespace HarvestSim.Visualization
{
    /// <summary>Muestra un texto breve al pasar el cursor sobre un botón de ícono.</summary>
    public sealed class HudTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string text;
        SimulationHUD hud;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!hud) hud = GetComponentInParent<SimulationHUD>();
            if (hud) hud.ShowTooltip(text, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (hud) hud.HideTooltip();
        }

        void OnDisable() { if (hud) hud.HideTooltip(); }
    }
}
