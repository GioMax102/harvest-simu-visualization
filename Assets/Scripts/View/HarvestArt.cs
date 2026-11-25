using TMPro;
using UnityEngine;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Referencias de arte de la vista realista. Se genera con Harvest Sim &gt; Generar arte y se asigna a WorldView.
    /// Cambiar un modelo o material aquí no requiere tocar el código de la vista.
    /// </summary>
    [CreateAssetMenu(menuName = "Harvest Sim/Arte de la vista", fileName = "HarvestArt")]
    public sealed class HarvestArt : ScriptableObject
    {
        [Header("Escala")]
        [Tooltip("Metros por celda si el trace no trae mundo.tamano_celda_m (una pasada del cabezal).")]
        public float cellSize = FieldGrid.DefaultCellSize;
        [Tooltip("Celdas de terreno alrededor de la cuadrícula.")]
        public int marginCells = 6;

        [Header("Suelo")]
        public Material soilMaterial;
        public Material stubbleMaterial;
        public Material roadMaterial;
        public Material grassMaterial;
        public Material concreteMaterial;

        [Header("Estructuras")]
        public Material steelMaterial;
        public Material paintedMetalMaterial;
        public Material foliageMaterial;
        public Material barkMaterial;
        public Material rockMaterial;
        public Material woodMaterial;

        [Header("Maíz")]
        public Material cornMaterial;
        [Tooltip("Dosel visto de lejos: evita que el suelo oscuro domine cuando las plantas se ven pequeñas.")]
        public Material canopyMaterial;

        [Header("Vehículos")]
        public AgentView harvesterPrefab;
        public AgentView cartPrefab;
        public Material grainMaterial;

        [Header("Capa informativa")]
        public Material markerMaterial;
        public Material lineMaterial;
        public TMP_FontAsset labelFont;
        [Tooltip("Material de la fuente con contorno, compartido por todas las placas.")]
        public Material labelMaterial;
    }
}
