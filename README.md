# harvest-simu-visualization

Cliente Unity de `trace.json` 2.1 generado por [harvest_sim](https://github.com/JoelMEsc/harvest_sim). Reproduce la corrida; no ejecuta rutas, decisiones de agentes ni física de cosecha.

## Abrir el MVP

1. En Unity Hub, inicia sesión y activa una licencia de Editor válida. Abre este proyecto con **Unity 6000.3.7f1**.
2. Espera la importación de scripts y paquetes. El manifest incluye `com.unity.nuget.newtonsoft-json` **3.2.2**; Package Manager debe poder descargarlo. No copies DLL de Newtonsoft a Assets.
3. Fuera de Play Mode, selecciona **Harvest Sim > Crear o abrir escena MVP** en el menú superior. La primera ejecución importa los recursos esenciales de TextMeshPro y crea `Assets/Scenes/HarvestPlayback.unity`, materiales y prefabs. Si esa escena ya existe, la abre sin sobrescribirla.
4. Pulsa **Play**. La corrida A inicia pausada en tick 0. Usa Reproducir/Pausar, Atrás, Adelante, Reinicio, Fin y el selector de velocidad. El panel derecho tiene desplazamiento vertical con la rueda del mouse.
5. Para otra corrida, sal de Play, selecciona **SimulationRoot** y arrastra `Assets/Data/Traces/escenario_b.json`, `escenario_c.json` o tu trace validado al campo **Initial Trace** del componente **PlaybackController**. Vuelve a pulsar Play.

**No es necesario crear prefabs, posicionar objetos ni asignar scripts manualmente.** El generador conecta cámara, HUD, controles, materiales, prefabs y trace. El mapa, las estructuras y los agentes se instancian al entrar en Play; es normal que no aparezcan en la escena antes de ejecutarla.

Si falta la fuente, usa **Window > TextMeshPro > Import TMP Essential Resources** y repite el paso 3. Si el menú Harvest Sim no aparece, revisa primero los errores de compilación y la descarga del paquete en Console / Package Manager.

## Qué está implementado

- Lectura y validación del contrato 2.1, incluidos ids, tipos, coordenadas, capacidades y ticks.
- Aplicación de cambios parciales sin perder valores anteriores; carga explícita 0, ticks vacíos y retroceso desde el inicio.
- Cosecha, depósito acumulado, eventos del tick y mensajes enviados/entregados.
- Reloj independiente de FPS, pausa, pasos, reinicio, final y velocidades 1/4/8/16 ticks/s.
- Mapa XZ con norte +Z, cámara ortográfica y primitivas sin colliders ni simulación física.
- HUD con agentes, cargas, cosecha, bodega, eventos, mensajes y métricas finales identificadas como tales.
- Errores de carga visibles y reproducción detenida sin conservar un mapa parcial.

| Carpeta | Responsabilidad |
|---|---|
| `Assets/Scripts/Data` | `TraceData.cs`, `TraceLoader.cs`: modelos y validación |
| `Assets/Scripts/Playback` | `TracePlayback.cs`: estado; `PlaybackController.cs`: reloj y comandos |
| `Assets/Scripts/View` | `WorldView.cs`, `AgentView.cs`: objetos y coordenadas |
| `Assets/Scripts/UI` | `SimulationHUD.cs`: presentación y enlaces de controles |
| `Assets/Editor` | `HarvestSceneBuilder.cs`: montaje inicial de escena y prefabs |
| `Assets/Data/Traces` | Fixture sintético y escenarios reales A/B/C |
| `Assets/Tests` | Pruebas de contrato, comparación con Python y smoke test de escena |

Las colecciones de `PlaybackState` pertenecen al reproductor: las vistas deben tratarlas como solo lectura. El trace cargado tampoco se modifica. El movimiento salta exactamente de celda a celda por tick; la interpolación estética queda pendiente.

## Verificación y límites

Verificado el 19 de noviembre de 2026:

- Los cuatro traces pasan el validador Python, incluidos sus checksums.
- **20 casos NUnit de datos pasaron ejecutados con Mono fuera del Editor**. Comparan todos los campos de **642 estados** reconstruidos por Python, además de retrocesos, reinicio y entradas inválidas.
- Los scripts de runtime, Editor y pruebas compilan con Roslyn y las bibliotecas de Unity 6000.3.7f1; se comprobó con la DLL del paquete oficial Newtonsoft 3.2.2. La compilación externa emitió un aviso de compatibilidad de referencias netstandard 2.0/2.1; no es una ejecución dentro de Unity.
- **Pendiente:** importación real, generación de escena, Test Runner dentro de Unity y revisión visual. El intento de abrir Unity en batch terminó con `No valid Unity Editor license found. Please activate your license.` No se generaron todavía los archivos `.unity`, `.prefab` y `.mat`; los genera el comando del paso 3.

Para comprobar desde Unity, primero genera la escena y después abre **Window > General > Test Runner**. Ejecuta **EditMode > Run All** y **PlayMode > Run All**. La prueba PlayMode abre HarvestPlayback y comprueba controles, reloj, reinicio visual, carga inválida y recarga sin duplicados.

El cliente valida la estructura del checksum, pero no recalcula su SHA-256. Antes de incorporar un trace nuevo, ejecútalo por `tools/validar_trace.py` de harvest_sim. No confundir un archivo válido para dibujar con una simulación íntegramente validada por Python.

## Pendientes manuales para el equipo

- Activar la licencia, abrir el proyecto y ejecutar el generador de escena una vez.
- Ejecutar ambos grupos del Test Runner. Revisar Console y la interfaz a distintos tamaños de Game View; comprobar frente blanco, norte, texto B, leyenda y desplazamiento del panel.
- Reproducir A/B/C y revisar visualmente primera espera, transferencia y depósito. Las cifras ya se compararon automáticamente; el render y los clics necesitan verificación en Editor.
- Guardar y versionar los assets que genere Unity, junto con sus `.meta`. Incluye `Assets/Scenes/HarvestPlayback.unity`, `Assets/Prefabs/Harvest`, `Assets/Materials/Harvest` y los recursos TMP importados.
- Si se requiere un ejecutable: añadir HarvestPlayback a **File > Build Profiles > Scene List**, seleccionarla como escena inicial y realizar un build. El generador no altera las escenas de build existentes. No se ha comprobado un build de escritorio.
- Sustituir después los hijos Body/Front de los prefabs Harvester y Cart por modelos, conservando su raíz con AgentView, frente local +Z y escala de una celda. Los cambios de materiales y geometría no requieren modificar el lector.

No se incluye conexión en vivo, exportación MP4/HTML, selector de archivos en runtime ni modelos definitivos. No se hizo commit ni push.

Consulta [PLAN_MVP_UNITY.md](PLAN_MVP_UNITY.md) para el contrato y las etapas del proyecto. La implementación usa texto agrupado para los agentes en lugar de un prefab AgentRow: no requiere asignaciones por agente.
