# Escape de Instituto Maldito

Proyecto Integrador multijugador en Unity con Netcode for GameObjects y arquitectura listen server autoritativa para dos jugadores.

## Semana 10 — Predicción y reconciliación

- Inputs numerados a 30 Hz, predicción local e historial circular de 256 entradas.
- Estado oficial y último tick procesado escritos únicamente por el servidor.
- Reconciliación, re-simulación de inputs pendientes y suavizado visual.
- Prueba real a 250 ms RTT: movimiento inmediato, 9 inputs pendientes y error final de `0.0000 m`.

## Semana 11 — Compensación de lag

- Historial circular autoritativo de jugadores y enemigos con interpolación temporal.
- Rewind del flash usando el instante del snapshot observado y cap de 750 ms.
- Prueba real a 267 ms RTT: `1/1` impacto aceptado con rewind frente a `0/1` usando el estado actual.

## Semana 12 — Seguridad y anti-cheat

- Resultados centralizados en el servidor; los clientes solo envían intenciones.
- Validación de tick, magnitud, velocidad, estado, inventario, cooldown, rango y tiempo observado.
- Registro `[Security]` y expulsión tras 6 infracciones maliciosas en 10 segundos.
- Prueba con cliente modificado: movimiento imposible rechazado sin cambiar posición; 6 solicitudes inválidas causaron desconexión autoritativa.
- Matriz de autoridad y tabla antes/después en [NETWORKING.md](NETWORKING.md#seguridad-y-validación-autoritativa--semana-12).

## Semana 13 — Área de Interés

- AoI autoritativa por distancia aplicada a los snapshots de enemigos: entrada a 18 m y salida a 22 m.
- El host, el propietario y los dos objetos de jugador permanecen siempre visibles mediante la replicación normal de NGO.
- Contador en HUD: objetos relevantes conocidos y enemigos visibles frente al total del servidor.
- Prueba de histéresis: visible a 20 m, oculto a 23 m, aún oculto al volver a 20 m y visible nuevamente a 17 m.

| Network Profiler, 60 s | Antes | Con AoI | Cambio |
|---|---:|---:|---:|
| Objetos relevantes conocidos | 17 | 7 | -58.82% |
| Total bytes enviados | 457,344 B | 232,488 B | -49.17% |
| Named Message bytes enviados | 361,746 B | 135,044 B | -62.67% |
| Total bytes recibidos | 71,888 B | 71,872 B | estable |
| Spawn / destroy bytes enviados | 0 / 0 B | 0 / 0 B | estable |

## Documentación técnica

La arquitectura, las tablas comparativas, los parámetros y las evidencias completas de las Semanas 10–12 están en [NETWORKING.md](NETWORKING.md).
