# Datos de prueba

Generados/validados el 19 de noviembre de 2026 con harvest_sim, checkout `a899fa0` (`v1-merge`). Sus carpetas `src`, `config` y el fixture coinciden con la referencia local `origin/main` `7902ba9` inspeccionada. No se hizo fetch.

- `trace_ejemplo.json`: copia del fixture sintético `tests/fixtures/trace_ejemplo.json`. 50 estados (ticks 0..49). No es un resultado experimental real.
- `escenario_a.json`: `ejecutar(cargar_config("config/escenario_a.json"))`, serializado con `escribir_trace`. 135 estados.
- `escenario_b.json`: misma generación para B. 194 estados.
- `escenario_c.json`: misma generación para C. 263 estados.

Todos pasaron `validar_trace`, incluidos checksums. `Assets/Tests/EditMode/Fixtures/expected_states.json` contiene la salida de `reconstruir(trace)` por cada tick de estos mismos cuatro archivos. No editar por separado los traces y ese oráculo.

Para regenerar desde la raíz de harvest_sim, con su entorno activo, reemplaza DESTINO por la ruta del proyecto Unity:

```python
import json
import shutil
from pathlib import Path
from harvest_sim.config import cargar_config
from harvest_sim.simulacion import ejecutar
from harvest_sim.datos import escribir_trace, validar_trace, reconstruir

unity = Path("DESTINO")
traces = unity / "Assets/Data/Traces"
shutil.copyfile("tests/fixtures/trace_ejemplo.json", traces / "trace_ejemplo.json")
expected = {}
for name in ["trace_ejemplo", "escenario_a", "escenario_b", "escenario_c"]:
    path = traces / (name + ".json")
    if name != "trace_ejemplo":
        escribir_trace(ejecutar(cargar_config("config/" + name + ".json")), path)
    trace = json.loads(path.read_text())
    assert not validar_trace(trace)
    expected[name] = list(reconstruir(trace))
(unity / "Assets/Tests/EditMode/Fixtures/expected_states.json").write_text(
    json.dumps(expected, ensure_ascii=False) + "\n", encoding="utf-8"
)
```

Actualizar también el commit de origen en este documento y ejecutar las pruebas al regenerar.
