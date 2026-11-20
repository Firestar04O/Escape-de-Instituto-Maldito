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

- Validación anti-cheat y AoI pertenecen a las semanas 12–13 y se implementarán por separado.

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
