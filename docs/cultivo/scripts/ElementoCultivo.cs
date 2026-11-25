// ElementoCultivo.cs — va junto a CultivoDesdeKML.cs (Unity necesita un archivo por componente).
using UnityEngine;

public enum TipoElemento { Cultivo, Camino, Obstaculo, Surco }

// Cómo se decide el rumbo (dirección de los surcos) de un lote.
public enum ModoRumbo
{
    Automatico, // el que deja menos vueltas del tractor
    Guia,       // tomado de una línea "surco..." dibujada dentro del lote
    Manual      // escrito a mano (en el Inspector o con "rumbo=45" en el nombre)
}

public class ElementoCultivo : MonoBehaviour
{
    public TipoElemento tipo;
    public bool esLinea;
    // Contorno (o eje de la línea) en metros, plano XZ (x = este, y = norte).
    public Vector2[] puntos;

    [Header("Solo para lotes de cultivo")]
    public ModoRumbo modoRumbo = ModoRumbo.Automatico;
    [Tooltip("Dirección de los surcos en grados desde el norte, sentido horario (0 = norte-sur, 90 = este-oeste).")]
    [Range(0f, 180f)] public float rumboGrados;
}
