// PasadasCultivo.cs
// Asigna a cada lote un rumbo (dirección de los surcos = dirección en la que el tractor
// avanza en línea recta) y genera las pasadas del tractor en ida y vuelta.
//
// Ponlo en el mismo GameObject que CultivoDesdeKML. Se recalcula solo cada vez que
// construyes el cultivo, o con clic derecho > "Calcular pasadas".
//
// El rumbo de cada lote sale de (en este orden):
//   1. "rumbo=45" en el nombre del lote en Google Earth          -> Manual
//   2. una línea "surco..." dibujada dentro del lote             -> Guia
//   3. si no hay nada, el rumbo que deja menos vueltas           -> Automatico
// Después puedes cambiar modo y ángulo de cada lote en el Inspector (componente
// ElementoCultivo de cada lote) y volver a "Calcular pasadas". Ojo: al reconstruir
// desde el KML esos cambios se pierden; para que se queden, ponlos en el KML.
//
// Rumbo: grados desde el norte en sentido horario. 0 = surcos norte-sur,
// 90 = este-oeste, 45 = noreste-suroeste. 30 y 210 son la misma dirección, así que va de 0 a 180.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public struct Pasada
{
    public Vector2 inicio; // metros locales (x = este, y = norte)
    public Vector2 fin;
    public int zona;       // pasadas de la misma zona se hacen seguidas, en ida y vuelta
    public float Largo => Vector2.Distance(inicio, fin);
}

[Serializable]
public class PlanLote
{
    public string lote;
    public ModoRumbo modo;
    public float rumboGrados;
    public int numeroPasadas;
    public int zonas;
    public float largoTotalM;
    public float coberturaPct; // qué parte del lote cubren las pasadas
    public List<Pasada> pasadas = new List<Pasada>(); // en orden de recorrido
    [NonSerialized] public ElementoCultivo elemento;
}

[RequireComponent(typeof(CultivoDesdeKML))]
public class PasadasCultivo : MonoBehaviour
{
    [Header("Implemento (metros)")]
    [Tooltip("Ancho de trabajo: distancia entre pasadas vecinas.")]
    public float anchoTrabajo = 6f;
    [Tooltip("Franja en las orillas del lote donde el tractor da la vuelta (cabecera).")]
    public float cabecera = 8f;
    [Tooltip("Distancia extra que se respeta alrededor de los obstáculos.")]
    public float margenObstaculo = 3f;
    [Tooltip("Pasadas más cortas que esto se descartan.")]
    public float largoMinimo = 5f;

    [Header("Resultado")]
    public List<PlanLote> planes = new List<PlanLote>();

    [Header("Vista")]
    public bool dibujarPasadas = true;
    public float alturaDibujo = 0.3f;

    CultivoDesdeKML cultivo;

    // ------------------------------------------------------------------ API

    [ContextMenu("Calcular pasadas")]
    public void Calcular()
    {
        cultivo = GetComponent<CultivoDesdeKML>();
        planes.Clear();
        var elementos = GetComponentsInChildren<ElementoCultivo>();
        var lotes = elementos.Where(e => e.tipo == TipoElemento.Cultivo && !e.esLinea && e.puntos.Length >= 3).ToList();
        var guias = elementos.Where(e => e.tipo == TipoElemento.Surco && e.puntos.Length >= 2).ToList();
        var obstaculos = elementos.Where(e => e.tipo == TipoElemento.Obstaculo).ToList();
        float radioPoste = cultivo != null ? cultivo.radioPoste : 0.5f;

        var obsPoligonos = obstaculos.Where(o => !o.esLinea && o.puntos.Length >= 3).Select(o => o.puntos).ToList();
        var obsLineas = obstaculos.Where(o => o.esLinea).Select(o => o.puntos).ToList();
        var obsPuntos = obstaculos.Where(o => o.puntos.Length == 1).Select(o => o.puntos[0]).ToList();

        foreach (var lote in lotes)
        {
            if (lote.modoRumbo != ModoRumbo.Manual)
            {
                var guia = guias.FirstOrDefault(g => DentroDePoligono(PuntoMedio(g.puntos), lote.puntos));
                if (guia != null)
                {
                    lote.modoRumbo = ModoRumbo.Guia;
                    lote.rumboGrados = RumboDeLinea(guia.puntos);
                }
                else
                {
                    lote.modoRumbo = ModoRumbo.Automatico;
                    lote.rumboGrados = RumboOptimo(lote.puntos, obsPoligonos, obsLineas, obsPuntos, radioPoste,
                                                   anchoTrabajo, cabecera, margenObstaculo, largoMinimo);
                }
            }

            var pasadas = GenerarPasadas(lote.puntos, lote.rumboGrados, obsPoligonos, obsLineas, obsPuntos, radioPoste,
                                         anchoTrabajo, cabecera, margenObstaculo, largoMinimo);
            float largo = pasadas.Sum(p => p.Largo);
            float area = Mathf.Abs(AreaFirmada(lote.puntos));
            planes.Add(new PlanLote
            {
                lote = lote.name,
                elemento = lote,
                modo = lote.modoRumbo,
                rumboGrados = Mathf.Round(lote.rumboGrados * 10f) / 10f,
                numeroPasadas = pasadas.Count,
                zonas = pasadas.Count > 0 ? pasadas.Max(x => x.zona) + 1 : 0,
                largoTotalM = Mathf.Round(largo),
                coberturaPct = area > 0 ? Mathf.Round(largo * anchoTrabajo / area * 1000f) / 10f : 0f,
                pasadas = pasadas
            });
        }

        foreach (var p in planes)
            Debug.Log($"PasadasCultivo: {p.lote} rumbo {p.rumboGrados}° ({p.modo}), {p.numeroPasadas} pasadas en {p.zonas} zona(s), " +
                      $"{p.largoTotalM} m, cubre {p.coberturaPct}% del lote.");
    }

    /// <summary>Rumbo del lote que contiene el punto (en mundo), o -1 si no está en ningún lote.</summary>
    public float RumboEn(Vector3 posicionMundo)
    {
        Vector3 l = transform.InverseTransformPoint(posicionMundo);
        var p = new Vector2(l.x, l.z);
        foreach (var plan in planes)
            if (plan.elemento != null && DentroDePoligono(p, plan.elemento.puntos)) return plan.rumboGrados;
        return -1f;
    }

    /// <summary>Dirección de avance en el mundo para un rumbo (vector unitario en XZ).</summary>
    public Vector3 DireccionMundo(float rumboGrados)
    {
        Vector2 d = Direccion(rumboGrados);
        return transform.TransformDirection(new Vector3(d.x, 0f, d.y)).normalized;
    }

    /// <summary>Convierte un punto de una pasada (metros locales) a posición del mundo.</summary>
    public Vector3 AMundo(Vector2 local)
    {
        return transform.TransformPoint(new Vector3(local.x, alturaDibujo, local.y));
    }

    // ------------------------------------------------------------------ Algoritmo (estático, sin Unity)

    public static Vector2 Direccion(float rumboGrados)
    {
        float r = rumboGrados * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(r), Mathf.Cos(r)); // x = este, y = norte
    }

    /// <summary>Rumbo (0-180) de una línea guía, usando sus extremos.</summary>
    public static float RumboDeLinea(Vector2[] linea)
    {
        Vector2 d = linea[linea.Length - 1] - linea[0];
        float ang = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; // desde el norte, horario
        return ((ang % 180f) + 180f) % 180f;
    }

    /// <summary>Prueba rumbos de 0 a 179.5° y se queda con el que da menos pasadas (menos vueltas);
    /// en empate, el que cubre más.</summary>
    public static float RumboOptimo(Vector2[] lote, List<Vector2[]> obsPoligonos, List<Vector2[]> obsLineas,
                                    List<Vector2> obsPuntos, float radioPoste, float ancho, float cabecera,
                                    float margen, float largoMin)
    {
        float mejor = 0f, mejorLargo = -1f;
        int mejorN = int.MaxValue;
        for (float r = 0f; r < 180f; r += 0.5f)
        {
            var p = GenerarPasadas(lote, r, obsPoligonos, obsLineas, obsPuntos, radioPoste, ancho, cabecera, margen, largoMin);
            float largo = p.Sum(x => x.Largo);
            if (p.Count < mejorN || (p.Count == mejorN && largo > mejorLargo + 0.01f))
            {
                mejorN = p.Count; mejorLargo = largo; mejor = r;
            }
        }
        return mejor;
    }

    /// <summary>Genera pasadas rectas paralelas al rumbo, separadas "ancho", recortadas al lote
    /// (dejando la cabecera) y a los obstáculos (con margen). Donde un obstáculo o una entrada
    /// del lote parte las pasadas, el lote se divide en zonas; cada zona se recorre completa
    /// en ida y vuelta antes de pasar a la siguiente.</summary>
    public static List<Pasada> GenerarPasadas(Vector2[] lote, float rumboGrados, List<Vector2[]> obsPoligonos,
                                              List<Vector2[]> obsLineas, List<Vector2> obsPuntos, float radioPoste,
                                              float ancho, float cabecera, float margen, float largoMin)
    {
        var res = new List<Pasada>();
        if (lote == null || lote.Length < 3 || ancho <= 0f) return res;
        var lineas = new List<KeyValuePair<float, List<Vector2>>>(); // (posición lateral, tramos)
        Vector2 d = Direccion(rumboGrados);          // avance
        Vector2 n = new Vector2(d.y, -d.x);          // lateral

        float minN = float.MaxValue, maxN = float.MinValue;
        foreach (var p in lote) { float a = Vector2.Dot(p, n); minN = Mathf.Min(minN, a); maxN = Mathf.Max(maxN, a); }

        float mitad = ancho * 0.5f;
        // Primera pasada a medio ancho de la orilla, como haría el tractor.
        for (float c = minN + mitad; c <= maxN - mitad + 1e-3f; c += ancho)
        {
            // El implemento ocupa [c - mitad, c + mitad]: el tramo libre debe estar dentro del lote
            // en el centro y en ambos bordes del implemento.
            var libre = Intervalos(lote, n, d, c);
            libre = Interseccion(libre, Intervalos(lote, n, d, c - mitad * 0.98f));
            libre = Interseccion(libre, Intervalos(lote, n, d, c + mitad * 0.98f));
            // Cabecera en cada extremo
            libre = libre.Select(i => new Vector2(i.x + cabecera, i.y - cabecera)).Where(i => i.y > i.x).ToList();

            // Obstáculos
            var bloqueado = new List<Vector2>();
            foreach (var o in obsPoligonos)
                foreach (float off in new[] { -mitad, 0f, mitad })
                    bloqueado.AddRange(Intervalos(o, n, d, c + off).Select(i => new Vector2(i.x - margen, i.y + margen)));
            foreach (var l in obsLineas)
                for (int k = 0; k < l.Length - 1; k++)
                    foreach (float off in new[] { -mitad, 0f, mitad })
                        if (CruceSegmento(l[k], l[k + 1], n, d, c + off, out float t))
                            bloqueado.Add(new Vector2(t - margen, t + margen));
            foreach (var p in obsPuntos)
            {
                float lat = Vector2.Dot(p, n) - c;
                float r = radioPoste + margen;
                if (Mathf.Abs(lat) <= mitad + r)
                {
                    float a = Vector2.Dot(p, d);
                    bloqueado.Add(new Vector2(a - r, a + r));
                }
            }
            libre = Restar(libre, Unir(bloqueado));

            var tramos = libre.Where(i => i.y - i.x >= largoMin).ToList();
            if (tramos.Count > 0) lineas.Add(new KeyValuePair<float, List<Vector2>>(c, tramos));
        }

        // Zonas: un tramo sigue en la zona del tramo vecino solo si se tocan uno a uno
        // (si una línea se parte en dos o dos se juntan, empieza zona nueva).
        var zonas = new List<List<KeyValuePair<float, Vector2>>>();
        var zonaPrev = new List<int>();
        List<Vector2> prev = null;
        foreach (var linea in lineas)
        {
            var zonaAct = new List<int>();
            for (int s = 0; s < linea.Value.Count; s++)
            {
                Vector2 t = linea.Value[s];
                int zona = -1;
                if (prev != null)
                {
                    var toca = Enumerable.Range(0, prev.Count).Where(k => Solapa(prev[k], t)).ToList();
                    if (toca.Count == 1 && linea.Value.Count(o => Solapa(prev[toca[0]], o)) == 1) zona = zonaPrev[toca[0]];
                }
                if (zona < 0) { zona = zonas.Count; zonas.Add(new List<KeyValuePair<float, Vector2>>()); }
                zonas[zona].Add(new KeyValuePair<float, Vector2>(linea.Key, t));
                zonaAct.Add(zona);
            }
            prev = linea.Value; zonaPrev = zonaAct;
        }

        for (int z = 0; z < zonas.Count; z++)
        {
            bool ida = true;
            foreach (var kv in zonas[z])
            {
                Vector2 a = n * kv.Key + d * kv.Value.x, b = n * kv.Key + d * kv.Value.y;
                res.Add(ida ? new Pasada { inicio = a, fin = b, zona = z } : new Pasada { inicio = b, fin = a, zona = z });
                ida = !ida;
            }
        }
        return res;
    }

    static bool Solapa(Vector2 a, Vector2 b) => Mathf.Min(a.y, b.y) - Mathf.Max(a.x, b.x) > 0f;

    // Tramos (en coordenada de avance) donde la recta lateral = c está dentro del polígono.
    static List<Vector2> Intervalos(Vector2[] poli, Vector2 n, Vector2 d, float c)
    {
        var cortes = new List<float>();
        for (int i = 0, j = poli.Length - 1; i < poli.Length; j = i++)
        {
            float ai = Vector2.Dot(poli[i], n), aj = Vector2.Dot(poli[j], n);
            if ((ai > c) != (aj > c))
            {
                float t = (c - ai) / (aj - ai);
                cortes.Add(Mathf.Lerp(Vector2.Dot(poli[i], d), Vector2.Dot(poli[j], d), t));
            }
        }
        cortes.Sort();
        var res = new List<Vector2>();
        for (int k = 0; k + 1 < cortes.Count; k += 2) res.Add(new Vector2(cortes[k], cortes[k + 1]));
        return res;
    }

    static bool CruceSegmento(Vector2 a, Vector2 b, Vector2 n, Vector2 d, float c, out float t)
    {
        float aa = Vector2.Dot(a, n), ab = Vector2.Dot(b, n);
        t = 0f;
        if ((aa > c) == (ab > c)) return false;
        t = Mathf.Lerp(Vector2.Dot(a, d), Vector2.Dot(b, d), (c - aa) / (ab - aa));
        return true;
    }

    static List<Vector2> Interseccion(List<Vector2> A, List<Vector2> B)
    {
        var res = new List<Vector2>();
        int i = 0, j = 0;
        while (i < A.Count && j < B.Count)
        {
            float lo = Mathf.Max(A[i].x, B[j].x), hi = Mathf.Min(A[i].y, B[j].y);
            if (hi > lo) res.Add(new Vector2(lo, hi));
            if (A[i].y < B[j].y) i++; else j++;
        }
        return res;
    }

    static List<Vector2> Unir(List<Vector2> L)
    {
        var s = L.OrderBy(v => v.x).ToList();
        var res = new List<Vector2>();
        foreach (var v in s)
        {
            if (res.Count > 0 && v.x <= res[res.Count - 1].y)
                res[res.Count - 1] = new Vector2(res[res.Count - 1].x, Mathf.Max(res[res.Count - 1].y, v.y));
            else res.Add(v);
        }
        return res;
    }

    static List<Vector2> Restar(List<Vector2> A, List<Vector2> B)
    {
        var res = new List<Vector2>();
        foreach (var a in A)
        {
            float ini = a.x;
            foreach (var b in B)
            {
                if (b.y <= ini || b.x >= a.y) continue;
                if (b.x > ini) res.Add(new Vector2(ini, b.x));
                ini = Mathf.Max(ini, b.y);
                if (ini >= a.y) break;
            }
            if (ini < a.y) res.Add(new Vector2(ini, a.y));
        }
        return res;
    }

    static Vector2 PuntoMedio(Vector2[] l) => (l[0] + l[l.Length - 1]) * 0.5f;

    static float AreaFirmada(Vector2[] p)
    {
        float a = 0f;
        for (int i = 0; i < p.Length; i++)
        {
            Vector2 u = p[i], v = p[(i + 1) % p.Length];
            a += u.x * v.y - v.x * u.y;
        }
        return a * 0.5f;
    }

    static bool DentroDePoligono(Vector2 p, Vector2[] poli)
    {
        bool dentro = false;
        for (int i = 0, j = poli.Length - 1; i < poli.Length; j = i++)
        {
            if ((poli[i].y > p.y) != (poli[j].y > p.y) &&
                p.x < (poli[j].x - poli[i].x) * (p.y - poli[i].y) / (poli[j].y - poli[i].y) + poli[i].x)
                dentro = !dentro;
        }
        return dentro;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!dibujarPasadas || planes == null) return;
        foreach (var plan in planes)
        {
            for (int i = 0; i < plan.pasadas.Count; i++)
            {
                var p = plan.pasadas[i];
                Vector3 a = AMundo(p.inicio), b = AMundo(p.fin);
                Gizmos.color = new Color(0.1f, 0.9f, 1f);
                Gizmos.DrawLine(a, b);
                // flechita en el sentido de avance
                Vector3 dir = (b - a).normalized, lado = Vector3.Cross(Vector3.up, dir) * 0.8f;
                Gizmos.DrawLine(b, b - dir * 1.5f + lado);
                Gizmos.DrawLine(b, b - dir * 1.5f - lado);
                // vuelta hacia la siguiente pasada de la misma zona
                if (i + 1 < plan.pasadas.Count && plan.pasadas[i + 1].zona == p.zona)
                {
                    Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.6f);
                    Gizmos.DrawLine(b, AMundo(plan.pasadas[i + 1].inicio));
                }
            }
        }
    }
#endif
}
