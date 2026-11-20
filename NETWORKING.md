# Decisión de arquitectura multijugador

## Versiones verificadas

- Unity `6000.3.9f1`, de `ProjectSettings/ProjectVersion.txt`: `m_EditorVersion: 6000.3.9f1`.
- Netcode for GameObjects `2.14.0`, de `Packages/manifest.json`: `"com.unity.netcode.gameobjects": "2.14.0"`.
- Unity Transport `2.6.0`, resuelto en `Packages/packages-lock.json`: `"com.unity.transport": "2.6.0"`.
- Multiplayer Play Mode `2.0.2`, de `Packages/manifest.json`: `"com.unity.multiplayer.playmode": "2.0.2"`.
- Multiplayer Tools `2.2.12`, de `Packages/manifest.json`: `"com.unity.multiplayer.tools": "2.2.12"`.

## Decisión

- **Stack:** Netcode for GameObjects, exigido por la rúbrica que requiere `NetworkShow`, `NetworkHide` y `CheckObjectVisibility`.
- **Topología:** listen server de dos jugadores. El host jugador es la autoridad aceptada por la rúbrica.
- **Autoridad:** el cliente solo envía intenciones; el host valida y escribe movimiento, vida, inventario, interacción y resultados.
- **Presupuesto:** 2 jugadores, hasta 23 enemigos activos durante frenesí, 30 ticks/s, inputs a 30 Hz y snapshots visuales de enemigos a 15 Hz.
- **Latencia objetivo:** 250 ms RTT, interpretada como aproximadamente 125 ms por trayecto.

La configuración está implementada en `SchoolEscapeNetworkBootstrap.ConfigureNetworkManager`, que asigna `TickRate = 30`, y en `NetworkClientAllyBridge`, cuyos RPC se dirigen al servidor y exigen propiedad del objeto.

## Replicación de movimiento — Semana 10

| Dato | Escritor | Transporte | Predicción/presentación | Relevancia |
|---|---|---|---|---|
| Input de movimiento | Cliente propietario | RPC no fiable por tick | Se simula inmediatamente y se guarda en historial circular | Solo servidor |
| Posición oficial | Servidor | `NetworkVariable<Vector3>` | El propietario reconcilia y re-simula inputs posteriores al ACK | Propietario y servidor |
| Último tick procesado | Servidor | `NetworkVariable<uint>` | Elimina historial confirmado | Solo propietario |
| Movimiento remoto | Servidor | `NetworkTransform` | Interpolación para observadores; el propietario usa reconciliación propia | Observadores del objeto |
| Corrección visual | Cliente propietario | Local | Offset visual suavizado hasta cero, separado de la posición autoritativa | Solo cliente propietario |

El historial del propietario es almacenamiento local deliberado de predicción. NGO no proporciona rollback automático; la posición autoritativa y el ACK son las fuentes de verdad persistentes de red.

## Ciclo de vida

- El servidor asigna los slots: host como jugador 1 y cliente remoto como jugador 2.
- Ambos jugadores se crean desde prefabs registrados con `NetworkManager.AddNetworkPrefab`.
- Una desconexión del jugador 2 restaura el aliado controlado por IA.
- No existe migración de host; si el host termina la sesión, termina la autoridad.

## Probe NGO previo a Semana 10

Ejecutado el 20 de noviembre de 2026 con el escenario `Assets/Settings/PlayMode/Week10Prediction.asset`, usando el Editor principal como host y `Player 2` como clon.

Salida leída mediante Unity CLI después de conectar ambos procesos:

```text
Host: listening=True; remote=True; allySpawned=True; allyHealth=3
Cliente: listening=True; ownerSpawned=True; health=3; room=Sala 1/6
```

Esto verifica inicio de sesión NGO, conexión entre procesos, spawn de un prefab `NetworkObject` registrado y lectura de `NetworkVariable` en el cliente.

## Verificación de predicción y reconciliación

Ejecutada el 20 de noviembre de 2026 con Host + `Player 2`. La prueba aplicó 30 inputs durante un segundo a 30 Hz y leyó las métricas públicas de `NetworkClientAllyBridge`.

| Condición | Resultado observado |
|---|---|
| Red local, sin latencia artificial | El cliente avanzó `5.8 m`; `ack=2776`, `tick=2777`, `pending=1`, error final `0.0000 m`. |
| `250 ms RTT` con Network Simulator oficial (`125 ms` de ida y `125 ms` de vuelta) | El cliente avanzó inmediatamente `5.8 m`; al asentarse: `ack=3336`, `tick=3345`, `pending=9`, error final `0.0000 m`; no se añadió ninguna corrección durante el movimiento normal. |
| Ráfaga artificial de 30 inputs en un solo frame | El servidor limitó el avance por paquete, el cliente corrigió el exceso y suavizó la presentación; esta prueba elevó el contador de correcciones de `1` a `2`. |

La corrección inicial grande registrada al aparecer el objeto corresponde a alinear el spawn local con la posición oficial del servidor. Las pruebas normales posteriores terminaron sin divergencia.

### Antes y después — Semana 10

| Aspecto | Antes | Después |
|---|---|---|
| Envío | Vector de movimiento a intervalos, sin identificador | Intención numerada por tick a 30 Hz |
| Respuesta local | Movimiento local sin historial | Predicción inmediata con historial circular de 256 inputs |
| Autoridad | El servidor movía con el último vector recibido, sin ACK | El servidor simula ticks validados, publica posición oficial y último tick procesado |
| Divergencia | Sin mecanismo para detectar o reparar diferencias | Umbral de `0.08 m`, rewind al estado oficial y re-simulación de inputs pendientes |
| Presentación | `NetworkTransform` podía competir con el movimiento del propietario | Raíz autoritativa separada de una representación visual que suaviza correcciones |
| Evidencia a 250 ms RTT | No había telemetría verificable | `5.8 m` en 30 inputs, `9` inputs pendientes y `0.0000 m` de error final |

## Verificación pendiente

- El Área de Interés y sus métricas con Network Profiler pertenecen a la Semana 13.

## Compensación de lag del flash — Semana 11

### Diseño implementado

- `ServerRewindHistory.RecordFrame` guarda a 30 Hz las posiciones autoritativas de los dos jugadores y hasta 32 enemigos en un buffer circular de 64 frames.
- Cada snapshot visual de enemigos incluye `NetworkManager.ServerTime.Time` y `ServerTime.Tick`. El cliente devuelve ese tiempo en `UseFlashRpc`, por lo que no decide el resultado: solo informa qué estado del servidor estaba viendo.
- `ServerRewindHistory.TryGetFrames` encuentra los frames anterior y posterior e interpola las posiciones para el instante solicitado.
- `SchoolEscapePrototype.FireCompensatedFlashFor` evalúa el cono histórico sin mover ni alterar temporalmente los objetos actuales. El servidor aplica después el resultado sobre los enemigos que siguen siendo válidos.
- El cap es `750 ms`. Cubre el objetivo de 250 ms RTT, el snapshot de enemigos a 15 Hz, buffers e interpolación, pero rechaza explícitamente solicitudes de un segundo o más para limitar la ventaja del atacante.
- La telemetría conserva solicitudes aceptadas/rechazadas, impactos con rewind, impactos que habría dado el estado actual y antigüedad de la última acción.

### Evidencia a latencia

Prueba MPPM del 20 de noviembre de 2026: Editor principal como host y `Player 2` como cliente, con Network Simulator oficial. Se colocó un enemigo a 4 m frente al cliente, se programó su esquiva fuera del cono y el cliente disparó justo antes de verla.

| Medición real | Resultado |
|---|---|
| RTT informado por Unity Transport | `267 ms` |
| Antigüedad del estado visto al validar | `267 ms` |
| Impactos aceptados con rewind | `1/1` |
| Impactos que habría aceptado el estado actual | `0/1` |
| Resultado autoritativo | `Aceptado · rewind 1 / actual 0 · 267 ms` |
| Solicitud artificial de `1000 ms` | Rechazada por superar el cap de `750 ms` |

Primer intento registrado: con `300 ms RTT` y cap inicial de `500 ms`, la acción llegó con `534 ms` de antigüedad y fue rechazada. Esa evidencia motivó el cap final de `750 ms`; no se ocultó ni se sustituyó por un valor estimado.

Punto de retorno anterior a Semana 11: commit `b47b779`.

Punto de retorno anterior a Semana 10: commit `69bf3754cdb2627489893d778890795c50404b49`.

## Seguridad y validación autoritativa — Semana 12

### Reglas implementadas

- Los RPC del cliente expresan intenciones y requieren ser el propietario del objeto (`RpcInvokePermission.Owner`). El cliente nunca envía vida, inventario, loot, impactos ni resultado de partida.
- El servidor rechaza componentes `NaN`/infinitos, inputs con magnitud mayor a `1.05`, ticks repetidos o atrasados y saltos futuros mayores a 12 ticks.
- Cada tick aceptado aplica como máximo `5.8 m/s / 30 Hz`; después de simular, el servidor vuelve a comprobar que el desplazamiento no exceda ese presupuesto más `0.05 m` de tolerancia.
- Vendas y flash validan en el servidor estado del actor, inventario, vida, cooldown y, para el flash, dirección y antigüedad del estado observado.
- Los ticks repetidos y acciones imposibles por estado se registran pero no expulsan por sí solos, porque pueden provenir de red no fiable o de una pulsación legítima. Datos no finitos, magnitudes imposibles, ticks futuros y spam antes del cooldown sí suman una infracción.
- Seis infracciones dentro de una ventana de 10 segundos desconectan al cliente remoto mediante `NetworkManager.DisconnectClient`. El host es el servidor confiable del modelo listen server.

### Matriz de autoridad auditada

| Sistema | Intención permitida al cliente | Validación/resultado del servidor | Réplica al cliente |
|---|---|---|---|
| Movimiento | Tick, vector 2D y botón de interacción | Orden y salto de tick, finitud, magnitud, velocidad y colisiones | Posición oficial y último tick procesado |
| Interacción | Botón mantenido | Busca el objeto más cercano a `2.8 m`, acumula duración y ejecuta `Complete` | Progreso y mensaje personal |
| Venda | Solicitar uso | Estado, vida, inventario y cooldown; descuenta y cura | Vida e inventario oficiales |
| Flash | Dirección y tiempo del snapshot observado | Propiedad, estado, cooldown, cap temporal, rewind, alcance y cono; descuenta pila y aplica impactos | Inventario, mensajes y estado de enemigos |
| Daño/captura/rescate | Ningún resultado | IA y lógica de partida modifican vida y cautiverio | Estado oficial del actor |
| Loot | Interactuar con el contenedor | El servidor decide y modifica el inventario | Inventario y anuncio personal |
| Victoria/derrota | Llegar físicamente a una salida | El servidor cuenta participantes resueltos y fija el resultado | Resultado, tiempo y panel final |
| Director/enemigos | Ninguno | El host crea, mueve, limita y elimina enemigos | Snapshots visuales a 15 Hz |

### Prueba con cliente modificado

Prueba MPPM del 20 de noviembre de 2026 con Editor principal como host y `Player 2` como cliente. Desde el cliente se invocaron los mismos RPC que usaría un binario alterado.

| Intento | Antes de Semana 12 | Después de Semana 12 (medido) |
|---|---|---|
| `tick=100000`, input `(99,99)` | El vector se normalizaba y se simulaban hasta 8 ticks; no había auditoría ni sanción | Rechazado antes de simular: magnitud `140.01`; posición oficial permaneció `(30.033, 1.000, -36.602)` |
| Tick repetido/atrasado | Se ignoraba silenciosamente | Se rechaza y registra como evento de red no sancionable |
| Dirección de flash con `NaN` | No existía validación explícita de finitud | Rechazada antes de consumir pila o evaluar impactos |
| Ráfaga de solicitudes inválidas | No había contador ni expulsión | `1` movimiento + `5` acciones = `6/6` infracciones; `kicked=True`, `remote=False` y el `NetworkObject` del cliente fue despawneado |

Contadores observados al finalizar: `RejectedMovementCount=1`, `RejectedActionCount=5`, `SecurityStrikeCount=6`. La consola del host emitió entradas `[Security]` por cada rechazo y por la expulsión.

Punto de retorno anterior a Semana 12: commit `6795b3e`.

## Área de Interés — Semana 13

### Regla de visibilidad

Los jugadores son `NetworkObject` y conservan la visibilidad predeterminada de NGO: el host y el propietario nunca se ocultan. Los enemigos ya usaban snapshots dirigidos mediante `CustomMessagingManager`, por lo que la AoI se implementó en ese canal equivalente en vez de convertirlos artificialmente en objetos de red duplicados.

- El servidor evalúa la distancia horizontal entre cada enemigo y el jugador remoto antes de construir cada snapshot de 15 Hz.
- Un enemigo desconocido entra al conjunto relevante a `18 m` o menos.
- Un enemigo ya conocido permanece hasta superar `22 m`.
- La separación de 4 m es la histéresis que evita crear/destruir la representación al oscilar cerca del borde.
- Los enemigos fuera del conjunto no se serializan ni consumen ancho de banda hacia ese cliente.
- Cada snapshot incluye el total autoritativo y el subconjunto visible. El HUD muestra `Red: N conocidos · AoI visibles/total`; `N` suma los `NetworkObject` de NGO y las representaciones relevantes de enemigos.
- El diseño actual admite un único cliente remoto, coherente con el límite de dos jugadores. Los conjuntos pueden separarse por `clientId` si el proyecto aumenta ese límite.

Documentación instalada consultada para NGO 2.14 y Multiplayer Tools 2.2: `basics/object-visibility.md`, `advanced-topics/message-system/custom-messages.md`, `runtime-stats-monitor.md` y `profiler.md`.

### Verificación de histéresis

Prueba MPPM con 15 enemigos congelados en posiciones reproducibles: cinco cerca del cliente y diez a más de 35 m.

| Posición del enemigo de prueba | Estado anterior | Resultado observado |
|---:|---|---|
| 20 m | visible | Sigue visible porque no superó el radio de salida de 22 m |
| 23 m | visible | Se oculta; el cliente pasa de 5 a 4 enemigos conocidos |
| 20 m | oculto | Sigue oculto porque no cruzó el radio de entrada de 18 m |
| 17 m | oculto | Reaparece; el cliente vuelve a 5 enemigos conocidos |

El contador del servidor registró seis entradas —las cinco iniciales más el reingreso— y una salida. El cliente mostró `Red: 7 conocidos · AoI 5/15`: dos jugadores de NGO y cinco enemigos relevantes.

### Comparación con Network Profiler

Medición real del 20 de noviembre de 2026 con los módulos `NGO Messages` y `NGO Objects`. Cada captura solicitó 60 segundos, conservó 1793 frames y analizó los mismos 1792 frames útiles, excluyendo el primero por ser parcial. La carga fue idéntica: 15 enemigos, cinco dentro del AoI y diez fuera.

| Métrica medida | Semana 12, sin AoI | Semana 13, con AoI | Variación |
|---|---:|---:|---:|
| Objetos relevantes conocidos por el cliente | 17 | 7 | -58.82% |
| Total Bytes Sent | 457,344 B | 232,488 B | -49.17% |
| Named Message Bytes Sent | 361,746 B | 135,044 B | -62.67% |
| Total Bytes Received | 71,888 B | 71,872 B | -16 B, estable |
| Object Spawned Bytes Sent | 0 B | 0 B | Sin cambios durante la ventana estable |
| Object Destroyed Bytes Sent | 0 B | 0 B | Sin cambios durante la ventana estable |

Los eventos de entrada/salida de enemigos son creaciones locales alimentadas por mensajes personalizados, por eso no aparecen como bytes de `NetworkObject` Spawn/Destroy. El ahorro relevante se observa en `Named Message Bytes Sent` y en el total enviado. Los cambios de visibilidad se probaron antes de iniciar la ventana estable para no contaminar la comparación de tráfico continuo.

Punto de retorno anterior a Semana 13: commit `27d5b64`.
