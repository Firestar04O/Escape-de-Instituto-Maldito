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

Pendiente: regla de visibilidad con histéresis, contador de objetos conocidos y medición real durante 60 segundos mediante Network Profiler o Runtime Network Stats Monitor.

## Documentación técnica

La arquitectura, las tablas comparativas, los parámetros y las evidencias completas de las Semanas 10–12 están en [NETWORKING.md](NETWORKING.md).
