using TMPro;
using UnityEngine.EventSystems;

namespace HarvestSim.Visualization
{
    /// <summary>Evita que la cámara y los atajos reaccionen cuando el usuario está usando el HUD.</summary>
    public static class UiFocus
    {
        public static bool PointerOverUi => EventSystem.current && EventSystem.current.IsPointerOverGameObject();

        public static bool Typing
        {
            get
            {
                var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
                return selected && selected.TryGetComponent<TMP_InputField>(out var field) && field.isFocused;
            }
        }
    }
}
