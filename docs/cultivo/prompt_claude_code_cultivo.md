# Contexto: cultivo real importado desde Google Earth (KML → Unity)

Te paso 3 scripts de C# y un archivo KML. Juntos sirven para meter en este proyecto de Unity un cultivo real (lotes, obstáculos y caminos permanentes), trazado en Google Earth, con su escala real. También generan las pasadas que hace el tractor en cada lote. Antes de tocar nada, lee los tres archivos completos. Abajo te explico qué hace cada uno, sus convenciones y sus limitaciones.

## Archivos

| Archivo | Qué es |
|---|---|
| `CultivoDesdeKML.cs` | Componente principal. Lee el KML y construye la escena: mallas, colliders y suelo. También tiene las consultas espaciales. |
| `ElementoCultivo.cs` | Componente de datos que se agrega a cada elemento generado. También define los enums `TipoElemento` y `ModoRumbo`. Tiene que estar en su propio archivo para que Unity serialice bien el componente. |
| `PasadasCultivo.cs` | Calcula el rumbo de cada lote (la dirección de los surcos) y genera las pasadas rectas del tractor, en ida y vuelta. Va en el mismo GameObject que `CultivoDesdeKML` (`[RequireComponent]`). |
| `*.xml` (el KML) | El KML exportado de Google Earth web, **renombrado a `.xml`** porque Unity no reconoce `.kml` como `TextAsset`. Se asigna en el campo `kml` de `CultivoDesdeKML`. |

El cultivo es de El Pueblito, Teocaltiche, Jalisco. Tiene 3 lotes (A, B y C) de unas 26 ha en total, una isla de árboles en medio del lote C y varios caminos de terracería. El trazo se hizo a mano sobre imagen satelital, así que puede haber errores de algunos metros.

## Convenciones (importante)

- **1 unidad = 1 metro.** **X = este, Z = norte**, Y = arriba. El terreno se toma como **plano**, sin relieve.
- Todo se genera como hijo de un GameObject `_CultivoGenerado`, que cuelga del objeto que tiene `CultivoDesdeKML`. Las coordenadas son **locales a ese transform**. El origen (0,0) es el **promedio de todas las coordenadas del KML**; no es una esquina ni el centroide de un lote. `latOrigen` y `lonOrigen` guardan ese origen.
- La proyección es equirectangular local (R = 6378137 m), suficiente para áreas de menos de 2 km.
- `ElementoCultivo.puntos` es un `Vector2[]` en metros locales, donde **`x` = este y `y` = norte**. Para pasarlo a 3D se usa `new Vector3(p.x, 0, p.y)`.
- **Rumbo:** grados desde el norte en sentido horario, normalizado a **0–180** (30° y 210° son la misma dirección de surco). El vector de avance es `(sin θ, cos θ)` en (x, z). Ejemplos: 0° = surcos norte–sur, 90° = este–oeste.

## Cómo se clasifica cada elemento del KML (por el nombre del Placemark)

El nombre se compara en minúsculas y sin acentos; solo cuenta cómo empieza.

| El nombre empieza con | Geometría | Resultado en Unity |
|---|---|---|
| `cultivo` o `lote` | Polígono | Lote sembrable: malla plana en y=0.02, **sin collider** |
| `camino` o `brecha` | Línea | Franja de `anchoCamino` m (4 por defecto) en y=0.06, sin collider |
| `camino` o `brecha` | Polígono | Zona de camino plana en y=0.04, sin collider |
| `obstaculo` | Polígono | Prisma extruido de `alturaObstaculo` m con **MeshCollider** |
| `obstaculo` | Línea | Cerca: un cubo por segmento, con **BoxCollider** |
| `obstaculo` | Punto | Poste o árbol: cilindro con collider |
| `surco` o `guia` | Línea | **Guía de rumbo**: no genera malla, solo define la dirección de los surcos del lote donde cae |
| (cualquier otro nombre) | | Polígono → obstáculo, línea → camino, punto → obstáculo |

Además se genera un `Suelo`: un Plane con collider que cubre todo, con `margenSuelo` m de margen. Si no se asignan materiales, se crean solos (detecta URP, HDRP o Built-in).

## Rumbo de cada lote (`PasadasCultivo`)

El rumbo se decide por prioridad:
1. **Manual:** `rumbo=45` en el nombre del lote (por ejemplo `cultivo_loteA rumbo=45`). También vale que `modoRumbo = Manual` se ponga a mano en el Inspector, pero eso se pierde al reconstruir desde el KML.
2. **Guia:** una línea `surco...` cuyo punto medio (entre sus extremos) cae dentro del lote. El rumbo sale de su primer y último punto.
3. **Automatico:** prueba de 0 a 180° en pasos de 0.5° y elige el que da menos pasadas. Si hay empate, elige el que cubre más.

El resultado queda escrito en `ElementoCultivo.modoRumbo` y `ElementoCultivo.rumboGrados` de cada lote.

## Algoritmo de pasadas

- Usa líneas paralelas al rumbo, separadas `anchoTrabajo` (6 m), y la primera va a medio ancho de la orilla. El ancho completo del implemento tiene que caber dentro del lote.
- A cada pasada le recorta la `cabecera` (8 m) en ambos extremos, que es donde da vuelta el tractor. Resta los obstáculos con `margenObstaculo` (3 m) y descarta tramos de menos de `largoMinimo` (5 m).
- **Zonas:** si un obstáculo o una entrada del lote parte las pasadas, el lote se divide en zonas. Cada zona se recorre completa en ida y vuelta antes de pasar a la siguiente.
- `PlanLote.pasadas` ya viene **en orden de recorrido**, con el sentido de cada pasada alternado dentro de su zona.

## API para usar desde la simulación

`CultivoDesdeKML`:
- `Construir()` y `Limpiar()`. En el Inspector aparecen como clic derecho → "Construir cultivo" / "Borrar cultivo generado". Con `construirAlIniciar = true`, construye en `Start` solo si todavía no existe `_CultivoGenerado`.
- `bool EsCultivable(Vector3 mundo)`: true si el punto está dentro de un lote, fuera de obstáculos y fuera de caminos.
- `bool EnCamino(Vector3 mundo)`, `bool EnObstaculo(Vector3 mundo, float margen = 0)`.
- `Vector2 ALatLon(Vector3 mundo)` devuelve (lon, lat) en `float`, con precisión aproximada de 1 m.
- `areaCultivableHa`, `latOrigen`, `lonOrigen`.

`PasadasCultivo`:
- `Calcular()`: recalcula rumbos y pasadas. Se llama sola al final de `Construir()`.
- `List<PlanLote> planes`: un plan por lote, con `lote`, `modo`, `rumboGrados`, `numeroPasadas`, `zonas`, `largoTotalM`, `coberturaPct` y `pasadas`.
- `Pasada`: `inicio` y `fin` (`Vector2` en metros locales), `zona` y `Largo`.
- `Vector3 AMundo(Vector2 local)`: convierte un punto de pasada a posición del mundo, a la altura `alturaDibujo`.
- `float RumboEn(Vector3 mundo)`: rumbo del lote donde está el punto, o -1 si no está en ningún lote.
- `Vector3 DireccionMundo(float rumbo)`: vector unitario de avance.
- Estáticos que no dependen de la escena: `Direccion`, `RumboDeLinea`, `RumboOptimo`, `GenerarPasadas`.
- Gizmos en la vista de escena: las pasadas en cian con flecha de sentido y las vueltas en rosa (solo entre pasadas de la misma zona).

## Limitaciones conocidas (revísalas)

1. **Los scripts se compilaron contra una imitación de la API de Unity, no dentro de Unity.** Lo primero es confirmar que compilan en la versión de Unity de este proyecto y corregir lo que haga falta.
2. `PlanLote.elemento` es `[NonSerialized]`. Al reabrir la escena o entrar a Play sin reconstruir, `RumboEn()` devuelve -1 aunque `planes` sí tenga las pasadas guardadas. Hay que llamar a `PasadasCultivo.Calcular()` al iniciar la simulación (o arreglarlo de otra forma).
3. Las consultas de `CultivoDesdeKML` dependen de la lista interna `elementos`, que se llena en `Start()` o en `Construir()`. Si se consultan antes, todo da false.
4. Los polígonos con hoyos (`innerBoundaryIs`) no se soportan: solo se lee el contorno exterior. Los obstáculos dentro de un lote van como polígonos aparte.
5. Los caminos no se restan de las pasadas: se supone que los lotes no se enciman con los caminos.
6. No se planean las vueltas reales (radio de giro), ni el traslado entre zonas, ni el traslado entre lotes. El gizmo rosa es solo una línea recta de referencia.
7. El rumbo y los cambios hechos en el Inspector se pierden al reconstruir desde el KML. Lo permanente va en el KML: el nombre con `rumbo=` o una línea `surco`.

## Flujo para actualizar el cultivo

1. Editar en Google Earth web (proyecto → elemento → Editar → arrastrar vértices) y respetar la convención de nombres.
2. En el menú del proyecto: Exportar como archivo KML. Renombrar a `.xml` y reemplazarlo en Assets.
3. En Unity: clic derecho en `CultivoDesdeKML` → "Construir cultivo". La consola muestra cada lote con su rumbo, su modo (Guia/Manual/Automatico), las pasadas y el porcentaje de cobertura. Si un lote que tiene línea guía dice "Automatico", su línea no cae dentro del lote.

## Lo que necesito que hagas

1. Lee los tres `.cs` y el KML. Confirma que compilan en este proyecto y corrige errores sin cambiar la API pública ni las convenciones de arriba (salvo que haya una buena razón y me la expliques).
2. Integra el cultivo en la escena de la simulación. Pon un GameObject con `CultivoDesdeKML` y `PasadasCultivo`, con el `.xml` asignado, y verifica que se construye y que la consola reporta los 3 lotes.
3. Revisa cómo se mueve hoy el tractor en el proyecto y propón cómo conectarlo con `PasadasCultivo.planes`: recorrer `pasadas` en orden, usar `AMundo()` para los puntos y `DireccionMundo()` para la orientación, y usar `EnObstaculo()`/`EsCultivable()` para validar posiciones. Atiende la limitación 2.
4. Antes de hacer cambios grandes, dime qué encontraste y qué planeas.
