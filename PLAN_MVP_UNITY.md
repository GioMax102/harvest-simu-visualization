# Plan de trabajo: visualizador MVP de harvest_sim en Unity

Fecha: 19 de noviembre de 2026. Estado: scripts del MVP implementados; integración visual en Unity pendiente por licencia del Editor.

## Avance de implementación

- Implementados los siete scripts de ejecución del plan y `Assets/Editor/HarvestSceneBuilder.cs`.
- El menú **Harvest Sim > Crear o abrir escena MVP** genera escena, prefabs, materiales, UI y referencias; el usuario no necesita colocarlos a mano. Es necesario ejecutarlo con Unity abierto y fuera de Play Mode.
- Declarado el paquete JSON oficial 3.2.2, conservando los paquetes Linux previamente modificados.
- Incluidos cuatro traces validados, oráculo Python de 642 estados, 20 casos de datos aprobados fuera del Editor y una prueba PlayMode pendiente de ejecución.
- Compilación externa de scripts verificada con bibliotecas de Unity. El Editor no pudo iniciarse por falta de licencia activa; escena, prefabs y materiales aún no se han generado ni inspeccionado.
- Simplificaciones: filas de agentes como texto agrupado (sin AgentRow.prefab); bases/bodega/obstáculos como primitivas creadas al cargar; prefabs generados en `Assets/Prefabs/Harvest`, materiales en `Assets/Materials/Harvest`. Las pruebas de lector y reproductor están agrupadas en `TraceTests.cs`.
- Instrucciones y pendientes actuales en [README.md](README.md). Las secciones siguientes conservan la especificación de diseño; una casilla de aceptación vacía sigue pendiente.

## 1. Objetivo y referencia

Construir un cliente Unity que lea un `trace.json` de harvest_sim y reproduzca fielmente la corrida con cubos, círculos y GameObjects vacíos. Python sigue siendo la única fuente de verdad: Unity representa posiciones, orientaciones, estados, cargas, cosecha, depósitos y comunicación ya registrados.

Referencia revisada: `harvest_sim`, referencia local `origin/main`, commit `7902ba9`. El `main` local (`fd8a22b`) está atrasado. No se hizo fetch: se registra la referencia disponible, no se afirma que sea el último commit remoto. Los módulos de datos y visualización del checkout `v1-merge` coinciden con esa referencia.

Proyecto destino: `harvest-simu-visualization`, rama revisada `mario-branch`, Unity **6000.3.7f1**, URP. Actualmente contiene `SampleScene` y scripts del tutorial, sin scripts propios del visualizador. Ya están declarados UGUI, Input System y Unity Test Framework. Hay modificaciones locales previas en los dos archivos de paquetes: conservarlas al integrar dependencias.

El HTML y el video son referencias de la experiencia de reproducción. En la referencia inspeccionada, `viz/dashboard.py` genera HTML y `viz/viewer.py` reproduce y exporta PNG; no se encontró un exportador MP4. Este MVP entrega reproducción interactiva en Unity Editor/escritorio. Generar HTML o codificar MP4 desde Unity queda para una entrega posterior; no es requisito para reproducir el mismo trace.

## 2. Alcance y resultado demostrable

- Cargar un trace local asignado como `TextAsset` desde el Inspector.
- Construir el mapa a partir de `mundo`, incluyendo servicio, bases, bodega y obstáculos declarados.
- Instanciar agentes desde el catálogo; validar inicialmente los escenarios reales con una cosechadora y un carro. No fijar sus identificadores en código.
- Mostrar posición, orientación, estado y carga/capacidad de cada agente.
- Distinguir celdas pendientes y cosechadas; mostrar el grano entregado en bodega.
- Mostrar los eventos del tick y los mensajes en tránsito, sin ejecutar decisiones de agentes.
- Reproducir/pausar, avanzar/retroceder un tick, reiniciar, ir al final y cambiar velocidad.
- Mostrar tick actual/total, escenario, velocidad, progreso de cosecha y resumen de métricas finales si existen.
- Mantener cámara cenital ortográfica y una leyenda legible con texto; no depender únicamente de colores.

No implementar BFS, NavMesh, físicas de movimiento, máquinas de estados de negocio, conexión en vivo a Python, editor de escenarios, modelos definitivos, partículas, audio, dashboard experimental ni nuevas métricas. Los nombres de estado son datos, no órdenes para que Unity simule acciones.

## 3. Contrato de datos que debe respetarse

Formato aceptado: `schema_version == "2.1"`.

| Sección | Uso en Unity |
|---|---|
| `meta.escenario`, `meta.total_ticks` | Título y límites del reproductor. `total_ticks` es el último tick, no el número de entradas guardadas. |
| `mundo.ancho`, `alto`, `bodega`, `bases`, `grano_por_celda`, `obstaculos` | Construcción del escenario. `bases` es un objeto indexado por id; coordenadas son pares `[x,y]`. Obstáculos del trace ya son celdas expandidas. |
| `agentes[]` | Catálogo: `id`, `tipo`, `capacidad`, `base`. Tipos iniciales: `cosechadora` y `carro`. |
| `ticks[]` | Entradas ordenadas con `tick`, `cambios[]`, `checksum`. Puede haber huecos entre ticks. |
| `config`, campos `clase` | Metadatos; no recalcular la corrida a partir de ellos. |
| `metricas` | Resultados finales ya calculados; puede estar vacío. No presentarlos como valores del tick actual. |

### Aplicación de eventos

| `tipo` | Efecto sobre el estado reproducido |
|---|---|
| `AGENTE` | Actualizar exclusivamente las claves presentes en `set`: `pose`, `estado`, `carga`. Una carga explícita de `0` sí reemplaza la anterior. Si aparece `pose`, contiene `x`, `y`, `orientacion`. |
| `CELDA_COSECHADA` | Agregar `celda` al conjunto de cosechadas y cambiar su material. No cosechar por el mero paso de un agente. |
| `DEPOSITO` | Asignar `entregado_total` a la bodega. No sumar ese total como si fuera un incremento. |
| `MENSAJE_ENVIADO` | Registrar por id numérico: `tipo_mensaje`, `performativa`, `de`, `a`, `payload`, `entrega_en`. Mostrar envío y agregar al conjunto en tránsito. |
| `MENSAJE_ENTREGADO` | Quitar el id de mensajes en tránsito y mostrar la entrega. El evento decide cuándo ocurre; Unity no la agenda por su cuenta. |
| `BLOQUEO` | Mostrar el agente afectado en eventos del tick; no cambiar su pose ni crear un obstáculo permanente. |

Aplicar los eventos en el orden del archivo. `AGENTE.id` es texto; el id de mensaje es numérico: no usar un único campo `int id` para todos los eventos.

El tick 0 contiene el estado inicial completo de cada agente. Los ticks ausentes conservan agentes, cosecha, depósitos y mensajes en tránsito; la lista de eventos del tick se vacía. La reproducción abarca **0..total_ticks inclusive**, aunque la última entrada guardada sea anterior al final.

Al retroceder: limpiar el estado y reaplicar desde 0 al destino. Es suficiente para las corridas actuales; no construir snapshots de todo el mundo por cada tick. Al avanzar varios ticks, aplicar todos sus eventos, aunque solo se renderice el destino.

Estados visibles de cosechadora: `EN_BASE`, `YENDO_A_CAMPO`, `COSECHANDO`, `LLENA_ESPERANDO`, `TRANSFIRIENDO`, `REGRESANDO_A_BASE`, `FIN`. Del carro: `EN_BASE`, `YENDO_A_COSECHADORA`, `ESPERANDO_TRANSFERENCIA`, `YENDO_A_BODEGA`, `DESCARGANDO`, `REGRESANDO_A_BASE`.

### Lectura y validación

Propuesta: agregar mediante Package Manager el paquete oficial `com.unity.nuget.newtonsoft-json` al implementar el lector, registrando la versión resuelta en manifest y lock. No está instalado actualmente. Usar `JObject` para comprobar presencia de claves y eventos heterogéneos; clases sencillas para catálogo y estado reconstruido. Evitar un parser casero o modificar el trace para acomodarlo al cliente. Mantener deshabilitada la resolución de tipos indicada por datos externos (`TypeNameHandling.None`).

Validar antes de crear objetos: JSON legible, versión, secciones obligatorias, dimensiones y capacidades positivas, total no negativo, ids únicos, tick 0 completo, ticks enteros únicos y crecientes dentro del rango, referencias a agentes conocidos, coordenadas válidas, orientaciones `N/E/S/O`, tipos y campos de eventos, cargas entre 0 y capacidad. Rechazar un tipo de evento no soportado con un error claro; aceptar metadatos adicionales. Informar archivo, tick/campo y motivo. Ante error, mantener detenido el reproductor y mostrar el error en UI, sin una escena parcialmente cargada.

La validación integral y del checksum se conserva en `tools/validar_trace.py`, que se debe ejecutar antes de importar cada fixture. El MVP Unity valida su contrato de lectura pero **no recalcula el SHA-256 canónico de Python**; documentar ese límite en el lector. Un checksum presente no significa que Unity lo haya verificado.

## 4. Coordenadas y representación inicial

Una celda mide 1 unidad Unity. El plano del mapa es **XZ**; Y es altura. El origen de `WorldRoot` es el centro de la celda `(0,0)`.

- `CeldaAPosicion(x,y,altura) = new Vector3(x, altura, -y)` en coordenadas locales de `WorldRoot`.
- El norte del trace disminuye y: `N -> +Z`, `E -> +X`, `S -> -Z`, `O -> -X`.
- El frente local del agente es `+Z`: rotaciones Y respectivas de `0°, 90°, 180°, 270°`.
- Mantener `WorldRoot` con transform identidad para evitar conversiones implícitas.
- Fila de servicio: `y == 0`. Campo: `0 <= x < ancho`, `1 <= y < alto`, excluyendo obstáculos.
- Progreso: `cosechadas / celdas_de_campo`; mostrar 0 si no hay campo, sin dividir entre cero. Grano esperado: cantidad de celdas de campo por `grano_por_celda`.
- Cámara centrada en `((ancho-1)/2, alturaCamara, -(alto-1)/2)`, mirando hacia abajo, con +Z hacia arriba en pantalla. Ajustar tamaño ortográfico al ancho, alto y aspecto del área visible, reservando sitio para el HUD.

Celda: cubo aplanado de escala `(0.95,0.1,0.95)`, centro Y=-0.05. Agentes: cubo cosechadora `(0.6,0.5,0.6)` y cilindro corto para el carro, cuyo círculo se ve desde arriba. Ambos tienen un pequeño cubo hijo en su frente para indicar orientación. Bases: cilindros aplanados a nivel del suelo. Bodega: cubo bajo con etiqueta B, sin tapar el agente que la visite. Obstáculos: cubos grises. Separar alturas de superficies para evitar parpadeo por superposición.

Movimiento MVP: saltos exactos entre celdas por tick. La interpolación suave es estética posterior; nunca debe cambiar cargas o cosecha antes del tick correspondiente.

## 5. Scripts a generar y funciones base

Las firmas siguientes son contratos propuestos, no implementaciones. Nombres de archivo y clase coinciden; namespace común `HarvestSim.Visualization`.

| Archivo propuesto | Tipo y responsabilidad | Funciones base |
|---|---|---|
| `Scripts/Data/TraceData.cs` | Clases C# sin MonoBehaviour: `TraceData`, catálogo `AgentData`, `AgentState`, `PlaybackState`. Agrupa los pocos modelos necesarios. | Datos, sin ciclo de vida Unity. `PlaybackState`: tick, diccionario de agentes por id, conjunto de cosechadas, total entregado, mensajes en tránsito y eventos actuales. Guardar cambios originales con presencia de claves. |
| `Scripts/Data/TraceLoader.cs` | Clase estática: parsear y validar. No toca escena. | `TraceData Parse(string json)`; `void Validate(TraceData trace)`. Lanzar error descriptivo; el controlador lo presenta al usuario. |
| `Scripts/Playback/TracePlayback.cs` | Clase C# pura: única fuente del estado reproducido. | Constructor `(TraceData trace)`; `void Reset()` (limpia y aplica tick 0); `void Seek(int tick)`; `void Step(int delta)`; privado `ApplyEvent(JObject change)`; propiedad de lectura `State`. Limitar destinos a `[0,total]`. |
| `Scripts/Playback/PlaybackController.cs` | MonoBehaviour coordinador: carga, reloj y comandos. Único dueño de Update. | `void LoadTrace(TextAsset asset)`; `void PlayPause()`; `void StepForward()`; `void StepBackward()`; `void Restart()`; `void GoToEnd()`; `void SetSpeed(float ticksPerSecond)`; privados `Update()` y `RefreshView()`. |
| `Scripts/View/WorldView.cs` | MonoBehaviour: crear y actualizar mapa y catálogo visual. Mantiene diccionarios celda->Renderer e id->AgentView. | `void Build(TraceData trace)`; `void Render(PlaybackState state)`; `void Clear()`; `Vector3 CellToWorld(int x,int y,float height)`; `void FrameCamera(Camera camera)`. |
| `Scripts/View/AgentView.cs` | MonoBehaviour del prefab: representar un agente. No lee JSON ni decide rutas. | `void Initialize(AgentData agent)`; `void Render(AgentState state, Vector3 position)`; privado `ApplyOrientation(string orientation)`. Id y estado en HUD, forma/color por tipo. |
| `Scripts/UI/SimulationHUD.cs` | MonoBehaviour: textos, indicadores y referencias a controles. | `void Bind(PlaybackController controller)`; `void Render(TraceData trace, PlaybackState state, bool paused, float speed)`; `void ShowError(string message)`; `void ClearError()`. Desregistrar listeners si se vuelve a enlazar. |

Son **7 scripts de ejecución**, sin un Manager por cada objeto ni un script por celda. `WorldView` administra celdas, bases y bodega; `SimulationHUD` administra filas de agentes y eventos. No crear ScriptableObjects ni bus global de eventos para este MVP.

### Flujo de ejecución

```text
TextAsset trace.json
  -> TraceLoader.Parse + Validate
  -> TracePlayback.Reset (tick 0)
  -> WorldView.Build
  -> WorldView.Render + SimulationHUD.Render

Botón / Update del PlaybackController
  -> TracePlayback.Seek / Step
  -> WorldView.Render + SimulationHUD.Render
```

Al iniciar, cargar y quedar pausado en tick 0. `Update` acumula `Time.unscaledDeltaTime * ticksPorSegundo`; por cada unidad completa avanza un tick. No igualar un frame a un tick ni usar `Time.timeScale` como reloj del trace. Procesar todos los pasos pendientes sin perder eventos. Pausar al llegar al último tick. Pasos manuales, reinicio y salto al final dejan el reproductor pausado y limpian el acumulador. Validar velocidad finita positiva; opciones MVP: 1, 4, 8 y 16 ticks/s, predeterminada 8.

Las vistas solo leen `PlaybackState`: no modifican colecciones ni conservan una segunda versión del estado lógico. Al retroceder, `Render` debe restaurar también materiales de celdas que dejaron de estar cosechadas. Al recargar, eliminar instancias anteriores y reiniciar UI y reloj.

## 6. Objetos de escena y componentes

Crear `Assets/Scenes/HarvestPlayback.unity`. Cada objeto tiene Transform; UI usa RectTransform.

```text
HarvestPlayback
├── SimulationRoot                  [PlaybackController]
├── WorldRoot                       [WorldView]
│   ├── CellsRoot                   [vacío; celdas generadas]
│   ├── StructuresRoot              [vacío; bases, bodega y obstáculos]
│   └── AgentsRoot                  [vacío; instancias del catálogo]
├── Main Camera                     [Camera ortográfica, AudioListener, datos URP]
├── Directional Light               [Light]
├── Canvas                          [Canvas, CanvasScaler, GraphicRaycaster, SimulationHUD]
│   ├── Header                      [texto escenario, tick, velocidad, estado]
│   ├── Controls                    [Button: PlayPause, Atrás, Adelante, Reinicio, Fin]
│   ├── Speed                       [TMP_Dropdown: 1, 4, 8, 16]
│   ├── AgentsPanel                 [filas con id, estado, carga/capacidad]
│   ├── WorldPanel                  [cosecha y total entregado]
│   ├── EventsPanel                 [ScrollRect + texto de eventos y mensajes en tránsito]
│   ├── FinalMetricsPanel           [métricas finales del archivo, rotuladas como finales]
│   ├── Legend                      [formas/colores y etiquetas]
│   └── ErrorPanel                  [texto de error; oculto si carga correcta]
└── EventSystem                     [EventSystem, InputSystemUIInputModule]
```

Usar TextMeshProUGUI para texto; importar sus recursos esenciales si Unity lo solicita. Canvas en Screen Space Overlay, CanvasScaler con Scale With Screen Size. Conectar botones al controlador y el dropdown a `SetSpeed` mediante el HUD. No usar simultáneamente dos módulos de entrada UI. El MVP usa botones; atajos del visor Python pueden agregarse después sin crear un segundo reloj.

| Prefab / objeto generado | Geometría y componentes | Configuración |
|---|---|---|
| `Cell.prefab` | Cube: MeshFilter, MeshRenderer | Material según servicio, cultivo o cosecha. Sin script por celda. |
| `Harvester.prefab` | Raíz vacía + AgentView; hijo Body Cube; hijo Front Cube | Body con MeshFilter y MeshRenderer. Tamaño menor que una celda. Frente +Z. |
| `Cart.prefab` | Raíz vacía + AgentView; Body Cylinder y Front Cube | Cilindro corto, círculo visible desde cámara cenital. Mismo contrato que cosechadora. |
| Bases | Cylinder aplanado: MeshFilter, MeshRenderer | Una por entrada de `mundo.bases`; etiqueta/id en leyenda o HUD. |
| Bodega | Cube bajo: MeshFilter, MeshRenderer; etiqueta B | Posición del trace; contador de depósito en HUD. |
| Obstáculos | Cube: MeshFilter, MeshRenderer | Solo representación de `mundo.obstaculos`. |

Quitar los Colliders que las primitivas traen por defecto. No agregar Rigidbody, NavMeshAgent, Animator ni scripts de cosecha/transferencia. Usar materiales URP simples compartidos: cultivo amarillo, cosechado beige, servicio gris azulado, cosechadora verde, carro azul, obstáculos gris oscuro. Conservar diferencias de forma y etiquetas además del color.

### Referencias del Inspector

- `PlaybackController`: TextAsset inicial, WorldView, SimulationHUD y velocidad inicial.
- `WorldView`: CellsRoot, StructuresRoot, AgentsRoot, Camera, prefabs de celda/cosechadora/carro y materiales. Crear estructuras con primitivas, sin prefabs adicionales obligatorios.
- `AgentView`: Body y Front. No necesita referencia al controlador.
- `SimulationHUD`: textos, panel de error, botones, dropdown y contenedor de filas de agentes. Las filas pueden ser UI creada desde un prefab simple de fila.
- Validar referencias faltantes al cargar y reportar cuál falta. No depender de búsquedas por nombre de objeto en Update.

## 7. Archivos previstos e integración

```text
Assets/
  Scenes/HarvestPlayback.unity
  Scripts/Data/{TraceData,TraceLoader}.cs
  Scripts/Playback/{TracePlayback,PlaybackController}.cs
  Scripts/View/{WorldView,AgentView}.cs
  Scripts/UI/SimulationHUD.cs
  Scripts/HarvestSim.Visualization.asmdef
  Prefabs/{Cell,Harvester,Cart,AgentRow}.prefab
  Materials/                       # materiales simples indicados arriba
  Data/Traces/{trace_ejemplo,escenario_a,escenario_b,escenario_c}.json
  Tests/EditMode/{TraceLoaderTests,TracePlaybackTests}.cs
  Tests/EditMode/HarvestSim.Visualization.Tests.asmdef
  Tests/PlayMode/PlaybackSmokeTests.cs
  Tests/PlayMode/HarvestSim.Visualization.PlayModeTests.asmdef
PLAN_MVP_UNITY.md
README.md                          # enlace al plan e instrucciones de ejecución
Packages/{manifest,packages-lock}.json  # solo integración del parser cuando se implemente
```

Los asmdef separan pruebas y runtime, con referencias al ensamblado del parser, UI e Input System según corresponda. Unity genera los `.meta`: versionarlos junto con escenas, scripts, carpetas, materiales, prefabs y fixtures. No versionar Library, Temp, Logs ni binarios de build.

Importar los JSON en `Assets/Data/Traces` permite asignarlos como TextAsset. No duplicarlos también en Resources o StreamingAssets. Elegir otra corrida cambiando el asset en Inspector; un selector de archivos en ejecución no es parte del MVP.

## 8. Secuencia de trabajo y reparto por paquetes

Asignar integrantes en equipo; las filas son responsabilidades, no asignaciones personales impuestas. Integrar por entregables verificables, no por número de scripts.

| Orden | Paquete / responsable por asignar | Entregable | Dependencia y criterio de cierre |
|---|---|---|---|
| 1 | Contrato y datos | TraceData, TraceLoader, JSON de ejemplo validado, prueba de lector, dependencia JSON | Carga 2.1; rechaza errores con contexto; conserva ausencia de claves y carga 0. |
| 2 | Reproducción | TracePlayback y pruebas EditMode | Depende de 1. Coincide con Python al avanzar, retroceder y saltar ticks vacíos. |
| 3 | Mundo y objetos | WorldView, AgentView, materiales y prefabs | Depende del contrato de 1. Puede empezar con estado fijo; luego representa el estado de 2. Coordenadas y orientaciones verificadas. |
| 4 | Controles e información | PlaybackController, SimulationHUD y escena enlazada | Depende de 2 y 3. Todos los controles funcionan y no duplican el avance. |
| 5 | Integración y calidad | Fixtures A/B/C, prueba PlayMode, comparaciones, README de uso | Corridas completas y pruebas de error; una persona integra la escena para evitar conflictos de YAML. |

Cada paquete debe incluir sus comprobaciones y actualizar instrucciones si cambia su contrato. Quien trabaja en scripts no debe editar la escena compartida sin coordinarse con la persona integradora. Primero fijar los modelos y firmas; cualquier cambio del trace se acuerda con el equipo Python.

## 9. Pruebas y aceptación

### Preparar referencias desde harvest_sim

Desde la raíz de harvest_sim, usando su entorno Python ya configurado:

```bash
python -m harvest_sim.cli config/escenario_a.json --sin-plots
python -m harvest_sim.cli config/escenario_b.json --sin-plots
python -m harvest_sim.cli config/escenario_c.json --sin-plots
python tools/validar_trace.py tests/fixtures/trace_ejemplo.json runs/escenario_a/trace.json runs/escenario_b/trace.json runs/escenario_c/trace.json
python viz/viewer.py runs/escenario_a/trace.json --guardar-cuadros runs/escenario_a/cuadros --cada 1
```

Copiar los traces validados a los nombres definidos en `Assets/Data/Traces`. Registrar commit de origen al actualizar fixtures. El fixture de ejemplo es sintético: no reportarlo como resultado experimental real.

Para comparar datos, usar `harvest_sim.datos.reconstruir(trace)` como oráculo: en ticks elegidos, contrastar id, pose, orientación, estado, carga, conjunto de cosechadas, total entregado y mensajes en tránsito. No comparar píxeles entre Python y Unity. Los cuadros PNG sirven para revisar distribución visual y leyenda.

### Pruebas automatizadas propuestas

- `TraceLoaderTests`: fixture 2.1 válido; JSON mal formado; versión distinta; tick 0 ausente; ticks repetidos/desordenados; coordenadas o id inválidos; carga fuera de rango; referencias faltantes. Comprobar errores sin crear GameObjects.
- `TracePlaybackTests`: carga explícita 0 frente a clave ausente; ticks 12 y 13 sin entradas en el fixture; depósito como asignación; entrega de mensaje; bloqueo sin movimiento; seek hacia atrás y reinicio equivalentes a reproducir desde cero; último tick sin cambios; rango con `total_ticks == 0`.
- `PlaybackSmokeTests`: cargar escena/fixture, instancias correctas, avance y pausa, retroceso que restaura celdas, segunda carga sin duplicados y parada al final. Comprobar transform/estado, sin depender de capturas idénticas.

### Lista de aceptación manual

- [ ] Abrir HarvestPlayback con la versión de Unity del repositorio sin errores de compilación.
- [ ] Play inicia pausado en tick 0 con bodega, bases y agentes en sus coordenadas reales.
- [ ] Las cuatro orientaciones coinciden con Python; `N` no apunta al sur por inversión de ejes.
- [ ] Se ve la secuencia de cosecha, espera, transferencia, viaje y descarga del trace, incluso cuando varios ticks no tienen cambios.
- [ ] Carga/capacidad, cosechadas y entregado coinciden con el estado de Python en inicio, primera espera, primera transferencia, primer depósito y final de A/B/C.
- [ ] Atrás y Reinicio restauran mapa, cargas, bodega, mensajes, eventos y tick.
- [ ] A 1 y 16 ticks/s el estado final es idéntico; la pausa no acumula tiempo pendiente.
- [ ] No hay avance más allá de `meta.total_ticks` ni excepciones al pulsar paso en los extremos.
- [ ] Un archivo inválido presenta un mensaje legible y deshabilita reproducción; se puede cargar después uno válido.
- [ ] La UI no tapa el mapa y continúa legible al cambiar el tamaño de Game View.
- [ ] README permite a otro integrante asignar un trace y reproducirlo sin ejecutar Python dentro de Unity.

## 10. Relación con gráficas computacionales y siguiente entrega

El MVP practica transformaciones locales/globales, traslación y rotación de objetos, escalado de primitivas, jerarquías de escena, proyección ortográfica, materiales e iluminación básica, y actualización temporal independiente de los FPS. La separación de Body/Front respecto a AgentView permitirá sustituir geometría sin cambiar el contrato de datos.

Después de aceptar la fidelidad del MVP: modelos agrícolas, interpolación visual, cámara en perspectiva, animaciones e iluminación elaborada. Después, evaluar exportación de video y un build compartible si se requieren. No condicionar la demostración de datos a esos cambios estéticos.

## Referencias

- [Formato y vocabulario 2.1](https://github.com/JoelMEsc/harvest_sim/blob/7902ba9/src/harvest_sim/datos/formato.py).
- [Construcción del trace](https://github.com/JoelMEsc/harvest_sim/blob/7902ba9/src/harvest_sim/datos/trace.py).
- [Reconstrucción de estados](https://github.com/JoelMEsc/harvest_sim/blob/7902ba9/src/harvest_sim/datos/linea_de_tiempo.py).
- [Visor y controles existentes](https://github.com/JoelMEsc/harvest_sim/blob/7902ba9/viz/viewer.py).
- [Dashboard HTML](https://github.com/JoelMEsc/harvest_sim/blob/7902ba9/viz/dashboard.py).
- [Fixture de referencia](https://github.com/JoelMEsc/harvest_sim/blob/7902ba9/tests/fixtures/trace_ejemplo.json).
- [Paquete JSON oficial de Unity](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html): lectura flexible y LINQ to JSON para el lector propuesto.

El estado de implementación y verificación actual está al inicio del documento y en README.md. La ejecución visual y las pruebas dentro de Unity siguen pendientes. La limitación de integridad del cliente y la referencia Git inspeccionada están documentadas arriba.
