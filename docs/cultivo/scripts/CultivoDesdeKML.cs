// CultivoDesdeKML.cs
// Construye un cultivo (lotes, obstáculos y caminos) en Unity a partir de un KML
// trazado en Google Earth.
//
// Uso:
//   1. Renombra tu archivo .kml a .xml (Unity solo lo reconoce como TextAsset así)
//      y arrástralo a la carpeta Assets.
//   2. Crea un GameObject vacío, agrégale este componente y asigna el .xml en "Kml".
//   3. Clic derecho sobre el componente > "Construir cultivo" (o deja activado
//      "Construir Al Iniciar").
//
// Cómo nombrar las cosas en Google Earth (el nombre decide qué es cada cosa):
//   cultivo...    Polígono  -> zona sembrable (puedes tener varios lotes)
//   camino...     Línea     -> camino permanente (franja de "Ancho Camino" metros)
//                 Polígono  -> zona de camino / patio
//   obstaculo...  Polígono  -> volumen sólido (árboles, casa, bordo...)
//                 Línea     -> cerca / muro delgado
//                 Punto     -> poste o árbol aislado (cilindro)
//   surco...      Línea     -> guía de dirección de surcos: dibújala dentro de un lote,
//                             a lo largo de los surcos (ver PasadasCultivo.cs)
//   Si el nombre no empieza con nada de eso: polígono = obstáculo, línea = camino,
//   punto = obstáculo.
//   En el nombre de un lote puedes fijar el rumbo a mano: "cultivo_loteA rumbo=45"
//   (grados desde el norte, en sentido horario).
//
// 1 unidad de Unity = 1 metro. X = este, Z = norte. El origen es el centro del KML.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public class CultivoDesdeKML : MonoBehaviour
{
    [Header("Entrada")]
    public TextAsset kml;
    public bool construirAlIniciar = true;

    [Header("Dimensiones (metros)")]
    public float anchoCamino = 4f;
    public float alturaObstaculo = 8f;
    public float alturaCerca = 1.5f;
    public float grosorCerca = 0.2f;
    public float radioPoste = 0.5f;
    public float margenSuelo = 30f;

    [Header("Materiales (opcionales)")]
    public Material materialCultivo;
    public Material materialCamino;
    public Material materialObstaculo;
    public Material materialSuelo;

    [Header("Resultado (solo lectura)")]
    public double latOrigen;
    public double lonOrigen;
    public float areaCultivableHa;

    readonly List<ElementoCultivo> elementos = new List<ElementoCultivo>();
    const string NombreContenedor = "_CultivoGenerado";
    const double RadioTierra = 6378137.0;

    void Start()
    {
        if (construirAlIniciar && transform.Find(NombreContenedor) == null) Construir();
        else RecolectarElementos();
    }

    // ------------------------------------------------------------------ Construcción

    [ContextMenu("Construir cultivo")]
    public void Construir()
    {
        if (kml == null) { Debug.LogError("CultivoDesdeKML: asigna el archivo KML (renombrado a .xml)."); return; }

        Limpiar();
        var placemarks = LeerKml(kml.text);
        if (placemarks.Count == 0) { Debug.LogWarning("CultivoDesdeKML: el KML no tiene geometrías."); return; }

        // Origen = centro de todas las coordenadas.
        var todos = placemarks.SelectMany(p => p.coords).ToList();
        latOrigen = todos.Average(c => c.y);
        lonOrigen = todos.Average(c => c.x);

        var raiz = new GameObject(NombreContenedor).transform;
        raiz.SetParent(transform, false);
        var grupos = new Dictionary<TipoElemento, Transform>();
        foreach (TipoElemento t in Enum.GetValues(typeof(TipoElemento)))
        {
            var g = new GameObject(t.ToString()).transform;
            g.SetParent(raiz, false);
            grupos[t] = g;
        }

        var matCultivo = materialCultivo ? materialCultivo : CrearMaterial(new Color(0.45f, 0.65f, 0.25f));
        var matCamino = materialCamino ? materialCamino : CrearMaterial(new Color(0.72f, 0.62f, 0.45f));
        var matObst = materialObstaculo ? materialObstaculo : CrearMaterial(new Color(0.15f, 0.35f, 0.15f));
        var matSuelo = materialSuelo ? materialSuelo : CrearMaterial(new Color(0.55f, 0.5f, 0.4f));

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
        areaCultivableHa = 0f;

        foreach (var pm in placemarks)
        {
            var pts = pm.coords.Select(ALocal).ToList();
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var tipo = Clasificar(pm);
            GameObject go;

            if (tipo == TipoElemento.Surco)
            {
                if (pm.geom != Geom.Linea || pts.Count < 2) continue; // las guías solo sirven como líneas
                go = new GameObject(pm.nombre); // sin malla: solo aporta la dirección
            }
            else if (pm.geom == Geom.Poligono)
            {
                pts = LimpiarAnillo(pts);
                if (pts.Count < 3) continue;
                if (tipo == TipoElemento.Obstaculo)
                    go = CrearMalla(pm.nombre, Extruir(pts, alturaObstaculo), matObst, true);
                else
                {
                    float y = tipo == TipoElemento.Camino ? 0.04f : 0.02f;
                    go = CrearMalla(pm.nombre, Plano(pts, y), tipo == TipoElemento.Camino ? matCamino : matCultivo, false);
                    if (tipo == TipoElemento.Cultivo) areaCultivableHa += Mathf.Abs(AreaFirmada(pts)) / 10000f;
                }
            }
            else if (pm.geom == Geom.Linea)
            {
                if (pts.Count < 2) continue;
                if (tipo == TipoElemento.Obstaculo) go = CrearCerca(pm.nombre, pts, matObst);
                else go = CrearMalla(pm.nombre, Cinta(pts, anchoCamino, 0.06f), matCamino, false);
            }
            else // Punto
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = pm.nombre;
                float h = alturaObstaculo * 0.5f;
                go.transform.localScale = new Vector3(radioPoste * 2f, h, radioPoste * 2f); // el cilindro mide 2 de alto
                go.transform.localPosition = new Vector3(pts[0].x, h, pts[0].y);
                go.GetComponent<Renderer>().sharedMaterial = matObst;
            }

            go.transform.SetParent(grupos[tipo], false);
            var el = go.AddComponent<ElementoCultivo>();
            el.tipo = tipo;
            el.esLinea = pm.geom == Geom.Linea;
            el.puntos = pts.ToArray();
            if (tipo == TipoElemento.Cultivo && LeerRumboDelNombre(pm.nombre, out float rumbo))
            {
                el.modoRumbo = ModoRumbo.Manual;
                el.rumboGrados = rumbo;
            }
        }

        // Suelo con collider para que lo demás tenga dónde pararse.
        var suelo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        suelo.name = "Suelo";
        suelo.transform.SetParent(raiz, false);
        Vector2 centro = (min + max) * 0.5f, tam = (max - min) + Vector2.one * margenSuelo * 2f;
        suelo.transform.localPosition = new Vector3(centro.x, 0f, centro.y);
        suelo.transform.localScale = new Vector3(tam.x / 10f, 1f, tam.y / 10f); // el Plane mide 10x10
        suelo.GetComponent<Renderer>().sharedMaterial = matSuelo;

        RecolectarElementos();
        var pasadas = GetComponent<PasadasCultivo>();
        if (pasadas != null) pasadas.Calcular();
        Debug.Log($"CultivoDesdeKML: {placemarks.Count} elementos, {areaCultivableHa:F2} ha de cultivo, " +
                  $"área {max.x - min.x:F0} x {max.y - min.y:F0} m.");
    }

    [ContextMenu("Borrar cultivo generado")]
    public void Limpiar()
    {
        var viejo = transform.Find(NombreContenedor);
        if (viejo != null)
        {
            if (Application.isPlaying) Destroy(viejo.gameObject);
            else DestroyImmediate(viejo.gameObject);
        }
        elementos.Clear();
    }

    void RecolectarElementos()
    {
        elementos.Clear();
        elementos.AddRange(GetComponentsInChildren<ElementoCultivo>());
    }

    // ------------------------------------------------------------------ Consultas para la simulación

    /// <summary>True si el punto (en mundo) cae dentro de un lote, fuera de obstáculos y fuera de caminos.</summary>
    public bool EsCultivable(Vector3 posicionMundo)
    {
        Vector2 p = AMetros(posicionMundo);
        bool enLote = false;
        foreach (var e in elementos)
        {
            if (e.tipo == TipoElemento.Cultivo && !e.esLinea && DentroDePoligono(p, e.puntos)) enLote = true;
        }
        return enLote && !EnObstaculo(posicionMundo) && !EnCamino(posicionMundo);
    }

    public bool EnCamino(Vector3 posicionMundo)
    {
        Vector2 p = AMetros(posicionMundo);
        foreach (var e in elementos)
        {
            if (e.tipo != TipoElemento.Camino) continue;
            if (e.esLinea ? DistanciaAPolilinea(p, e.puntos) <= anchoCamino * 0.5f : DentroDePoligono(p, e.puntos)) return true;
        }
        return false;
    }

    public bool EnObstaculo(Vector3 posicionMundo, float margen = 0f)
    {
        Vector2 p = AMetros(posicionMundo);
        foreach (var e in elementos)
        {
            if (e.tipo != TipoElemento.Obstaculo) continue;
            if (e.esLinea) { if (DistanciaAPolilinea(p, e.puntos) <= grosorCerca * 0.5f + margen) return true; }
            else if (e.puntos.Length == 1) { if (Vector2.Distance(p, e.puntos[0]) <= radioPoste + margen) return true; }
            else if (DentroDePoligono(p, e.puntos) || (margen > 0f && DistanciaAPolilinea(p, e.puntos, true) <= margen)) return true;
        }
        return false;
    }

    /// <summary>Convierte una posición del mundo a latitud/longitud (x = lon, y = lat).</summary>
    public Vector2 ALatLon(Vector3 posicionMundo)
    {
        Vector2 m = AMetros(posicionMundo);
        double lat = latOrigen + m.y / RadioTierra * Mathf.Rad2Deg;
        double lon = lonOrigen + m.x / (RadioTierra * Math.Cos(latOrigen * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
        return new Vector2((float)lon, (float)lat);
    }

    Vector2 AMetros(Vector3 mundo)
    {
        Vector3 l = transform.InverseTransformPoint(mundo);
        return new Vector2(l.x, l.z);
    }

    // ------------------------------------------------------------------ KML

    enum Geom { Poligono, Linea, Punto }

    class Placemark
    {
        public string nombre;
        public Geom geom;
        public List<Vector2d> coords;
    }

    struct Vector2d { public double x, y; public Vector2d(double x, double y) { this.x = x; this.y = y; } }

    static List<Placemark> LeerKml(string texto)
    {
        var lista = new List<Placemark>();
        var doc = XDocument.Parse(texto);
        foreach (var pm in doc.Descendants().Where(e => e.Name.LocalName == "Placemark"))
        {
            var nombreEl = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "name");
            string nombre = nombreEl != null ? nombreEl.Value.Trim() : "sin_nombre";
            int i = 0;
            foreach (var g in pm.Descendants())
            {
                string ln = g.Name.LocalName;
                XElement coordsEl = null;
                Geom tipo;
                if (ln == "Polygon")
                {
                    tipo = Geom.Poligono;
                    var outer = g.Descendants().FirstOrDefault(e => e.Name.LocalName == "outerBoundaryIs");
                    coordsEl = (outer ?? g).Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                }
                else if (ln == "LineString" || ln == "LinearRing" && g.Parent != null && g.Parent.Name.LocalName == "Placemark")
                {
                    tipo = Geom.Linea;
                    coordsEl = g.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                }
                else if (ln == "Point")
                {
                    tipo = Geom.Punto;
                    coordsEl = g.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                }
                else continue;

                if (coordsEl == null) continue;
                var c = ParsearCoordenadas(coordsEl.Value);
                if (c.Count == 0) continue;
                lista.Add(new Placemark { nombre = i++ == 0 ? nombre : nombre + "_" + i, geom = tipo, coords = c });
            }
        }
        return lista;
    }

    static List<Vector2d> ParsearCoordenadas(string s)
    {
        var res = new List<Vector2d>();
        foreach (var tupla in s.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = tupla.Split(',');
            if (partes.Length < 2) continue;
            if (double.TryParse(partes[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
                double.TryParse(partes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                res.Add(new Vector2d(lon, lat));
        }
        return res;
    }

    static TipoElemento Clasificar(Placemark pm)
    {
        string n = QuitarAcentos(pm.nombre.ToLowerInvariant());
        if (n.StartsWith("cultivo") || n.StartsWith("lote")) return TipoElemento.Cultivo;
        if (n.StartsWith("camino") || n.StartsWith("brecha")) return TipoElemento.Camino;
        if (n.StartsWith("obstaculo")) return TipoElemento.Obstaculo;
        if (n.StartsWith("surco") || n.StartsWith("guia")) return TipoElemento.Surco;
        return pm.geom == Geom.Linea ? TipoElemento.Camino : TipoElemento.Obstaculo;
    }

    static bool LeerRumboDelNombre(string nombre, out float rumbo)
    {
        rumbo = 0f;
        var m = System.Text.RegularExpressions.Regex.Match(nombre.ToLowerInvariant(), @"rumbo\s*[=:_ ]?\s*(-?\d+(?:[.,]\d+)?)");
        if (!m.Success) return false;
        if (!float.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out rumbo)) return false;
        rumbo = ((rumbo % 180f) + 180f) % 180f;
        return true;
    }

    static string QuitarAcentos(string s)
    {
        return s.Replace('á', 'a').Replace('é', 'e').Replace('í', 'i').Replace('ó', 'o').Replace('ú', 'u');
    }

    Vector2 ALocal(Vector2d c)
    {
        double norte = (c.y - latOrigen) * Mathf.Deg2Rad * RadioTierra;
        double este = (c.x - lonOrigen) * Mathf.Deg2Rad * RadioTierra * Math.Cos(latOrigen * Mathf.Deg2Rad);
        return new Vector2((float)este, (float)norte);
    }

    // ------------------------------------------------------------------ Geometría

    static List<Vector2> LimpiarAnillo(List<Vector2> pts)
    {
        var r = new List<Vector2>();
        foreach (var p in pts)
            if (r.Count == 0 || (r[r.Count - 1] - p).sqrMagnitude > 1e-4f) r.Add(p);
        if (r.Count > 1 && (r[0] - r[r.Count - 1]).sqrMagnitude < 1e-4f) r.RemoveAt(r.Count - 1);
        return r;
    }

    static float AreaFirmada(IList<Vector2> p)
    {
        float a = 0f;
        for (int i = 0; i < p.Count; i++)
        {
            Vector2 u = p[i], v = p[(i + 1) % p.Count];
            a += u.x * v.y - v.x * u.y;
        }
        return a * 0.5f;
    }

    static float Cruz(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    // Triangulación por recorte de orejas (sirve para polígonos cóncavos sin hoyos).
    static List<int> Triangular(List<Vector2> pts)
    {
        var tris = new List<int>();
        var idx = Enumerable.Range(0, pts.Count).ToList();
        if (AreaFirmada(pts) < 0) idx.Reverse(); // dejar en sentido antihorario
        int guardia = 0;
        while (idx.Count > 3 && guardia++ < 10000)
        {
            bool corto = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                Vector2 a = pts[ia], b = pts[ib], c = pts[ic];
                if (Cruz(a, b, c) <= 1e-6f) continue; // vértice reflejo
                bool contiene = false;
                foreach (int j in idx)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    if (EnTriangulo(pts[j], a, b, c)) { contiene = true; break; }
                }
                if (contiene) continue;
                tris.Add(ia); tris.Add(ib); tris.Add(ic);
                idx.RemoveAt(i);
                corto = true;
                break;
            }
            if (!corto) // polígono raro (auto-intersección): forzar para no colgarse
            {
                tris.Add(idx[idx.Count - 1]); tris.Add(idx[0]); tris.Add(idx[1]);
                idx.RemoveAt(0);
            }
        }
        if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
        return tris;
    }

    static bool EnTriangulo(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        return Cruz(a, b, p) >= 0 && Cruz(b, c, p) >= 0 && Cruz(c, a, p) >= 0;
    }

    // Unity dibuja la cara frontal cuando Cross(b-a, c-a) apunta al observador.
    static void AgregarTriArriba(List<int> t, List<Vector3> v, int a, int b, int c)
    {
        if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y >= 0) { t.Add(a); t.Add(b); t.Add(c); }
        else { t.Add(a); t.Add(c); t.Add(b); }
    }

    static Mesh Plano(List<Vector2> pts, float y)
    {
        var v = pts.Select(p => new Vector3(p.x, y, p.y)).ToList();
        var tri = Triangular(pts);
        var t = new List<int>();
        for (int i = 0; i < tri.Count; i += 3) AgregarTriArriba(t, v, tri[i], tri[i + 1], tri[i + 2]);
        return ArmarMalla(v, t, pts.Select(p => p).ToList());
    }

    static Mesh Extruir(List<Vector2> pts, float h)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        var uv = new List<Vector2>();
        // Tapa superior
        foreach (var p in pts) { v.Add(new Vector3(p.x, h, p.y)); uv.Add(p); }
        var tri = Triangular(pts);
        for (int i = 0; i < tri.Count; i += 3) AgregarTriArriba(t, v, tri[i], tri[i + 1], tri[i + 2]);
        // Paredes hacia afuera
        bool antihorario = AreaFirmada(pts) > 0;
        float recorrido = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
            float largo = Vector2.Distance(a, b);
            int k = v.Count;
            v.Add(new Vector3(a.x, 0, a.y)); v.Add(new Vector3(b.x, 0, b.y));
            v.Add(new Vector3(a.x, h, a.y)); v.Add(new Vector3(b.x, h, b.y));
            uv.Add(new Vector2(recorrido, 0)); uv.Add(new Vector2(recorrido + largo, 0));
            uv.Add(new Vector2(recorrido, h)); uv.Add(new Vector2(recorrido + largo, h));
            recorrido += largo;
            if (antihorario) { t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            else { t.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
        }
        return ArmarMalla(v, t, uv);
    }

    static Mesh Cinta(List<Vector2> pts, float ancho, float y)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        float mitad = ancho * 0.5f, recorrido = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 dirPrev = i > 0 ? (pts[i] - pts[i - 1]).normalized : Vector2.zero;
            Vector2 dirSig = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).normalized : Vector2.zero;
            Vector2 dir = (dirPrev + dirSig).normalized;
            if (dir == Vector2.zero) dir = dirSig != Vector2.zero ? dirSig : dirPrev;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            // Inglete: ensanchar en las curvas para mantener el ancho, con límite.
            Vector2 nRef = new Vector2(-(dirSig != Vector2.zero ? dirSig : dirPrev).y, (dirSig != Vector2.zero ? dirSig : dirPrev).x);
            float escala = Mathf.Min(1f / Mathf.Max(Vector2.Dot(normal, nRef), 0.25f), 3f);
            if (i > 0) recorrido += Vector2.Distance(pts[i], pts[i - 1]);
            Vector2 izq = pts[i] + normal * mitad * escala, der = pts[i] - normal * mitad * escala;
            v.Add(new Vector3(izq.x, y, izq.y)); v.Add(new Vector3(der.x, y, der.y));
            uv.Add(new Vector2(0, recorrido / ancho)); uv.Add(new Vector2(1, recorrido / ancho));
            if (i > 0)
            {
                int k = v.Count - 4;
                AgregarTriArriba(t, v, k, k + 1, k + 2);
                AgregarTriArriba(t, v, k + 1, k + 3, k + 2);
            }
        }
        return ArmarMalla(v, t, uv);
    }

    static Mesh ArmarMalla(List<Vector3> v, List<int> t, List<Vector2> uv)
    {
        var m = new Mesh();
        if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.SetUVs(0, uv);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static GameObject CrearMalla(string nombre, Mesh mesh, Material mat, bool collider)
    {
        mesh.name = nombre;
        var go = new GameObject(nombre);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
        return go;
    }

    GameObject CrearCerca(string nombre, List<Vector2> pts, Material mat)
    {
        var go = new GameObject(nombre);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            float largo = Vector2.Distance(a, b);
            if (largo < 0.01f) continue;
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = nombre + "_seg" + i;
            seg.transform.SetParent(go.transform, false);
            Vector2 m = (a + b) * 0.5f;
            seg.transform.localPosition = new Vector3(m.x, alturaCerca * 0.5f, m.y);
            seg.transform.localRotation = Quaternion.LookRotation(new Vector3(b.x - a.x, 0, b.y - a.y));
            seg.transform.localScale = new Vector3(grosorCerca, alturaCerca, largo);
            seg.GetComponent<Renderer>().sharedMaterial = mat;
        }
        return go;
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

    static float DistanciaAPolilinea(Vector2 p, Vector2[] pts, bool cerrada = false)
    {
        float mejor = float.MaxValue;
        int n = cerrada ? pts.Length : pts.Length - 1;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            mejor = Mathf.Min(mejor, Vector2.Distance(p, a + ab * t));
        }
        return mejor;
    }

    static Material CrearMaterial(Color color)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        string nombreShader = "Standard";
        if (rp != null)
        {
            string n = rp.GetType().Name;
            if (n.Contains("Universal")) nombreShader = "Universal Render Pipeline/Lit";
            else if (n.Contains("HD")) nombreShader = "HDRP/Lit";
        }
        var shader = Shader.Find(nombreShader);
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        return mat;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        foreach (var e in GetComponentsInChildren<ElementoCultivo>())
        {
            if (e.puntos == null || e.puntos.Length < 2) continue;
            Gizmos.color = e.tipo == TipoElemento.Cultivo ? Color.yellow : e.tipo == TipoElemento.Camino ? new Color(1f, 0.55f, 0f)
                         : e.tipo == TipoElemento.Surco ? Color.cyan : Color.red;
            int n = e.esLinea ? e.puntos.Length - 1 : e.puntos.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = e.puntos[i], b = e.puntos[(i + 1) % e.puntos.Length];
                Gizmos.DrawLine(transform.TransformPoint(new Vector3(a.x, 0.2f, a.y)), transform.TransformPoint(new Vector3(b.x, 0.2f, b.y)));
            }
        }
    }
#endif
}
