# Escape de Instituto Maldito

Proyecto Integrador multijugador en Unity con Netcode for GameObjects y arquitectura listen server autoritativa para dos jugadores.

## Semana 10 — Predicción y reconciliación

- Inputs numerados a 30 Hz, predicción local e historial circular de 256 entradas.
- Estado oficial y último tick procesado escritos únicamente por el servidor.
- Reconciliación, re-simulación de inputs pendientes y suavizado visual.
- Prueba real a 250 ms RTT: movimiento inmediato, 9 inputs pendientes y error final de `0.0000 m`.

### Antes y después

| Aspecto | Antes | Implementación de Semana 10 |
|---|---|---|
| Autoridad | Movimiento sin ACK ni historial de reconciliación | El servidor simula entradas numeradas y publica posición oficial y último tick procesado |
| Respuesta local | Sin historial para corregir divergencias | Predicción a 30 Hz e historial circular de 256 entradas |
| Corrección | Sin reparación de diferencias | Umbral de 0.08 m, retorno al estado oficial y re-simulación de entradas pendientes |
| Presentación | Sin suavizado independiente de la corrección | Representación visual separada de la raíz, con suavizado |
| Evidencia con latencia | No hay medición comparable sin predicción registrada | A 250 ms RTT: 5.8 m en 30 inputs, 9 inputs pendientes y error final de 0.0000 m |

Medición registrada el 20 de noviembre de 2026 con host y cliente en Multiplayer Play Mode, usando Network Simulator oficial: 125 ms por trayecto (250 ms RTT). Son resultados históricos documentados, no una nueva ejecución. Una ráfaga artificial de 30 inputs en un frame provocó una corrección adicional. La prueba sin predicción bajo la misma latencia debe mostrarse en el video; no se dispone aquí de un resultado medido para esa condición.

Implementación: [NetworkClientAllyBridge.cs](Assets/Scripts/Multiplayer/NetworkClientAllyBridge.cs). Procedimiento y resultados completos: [NETWORKING.md](NETWORKING.md#verificación-de-predicción-y-reconciliación).

### Repositorio y entrega

- Implementación de Semana 10: [b47b779 — feat: week 10 prediction and reconciliation](https://github.com/Firestar04O/Escape-de-Instituto-Maldito/commit/b47b77935a8204cf507cfcb5af283c10f96fa182).
- Rama de trabajo de John: [Mi-parte-John](https://github.com/Firestar04O/Escape-de-Instituto-Maldito/tree/Mi-parte-John).
- Video de 3 a 5 minutos: pendiente de grabación y enlace por John. Debe mostrar el cliente remoto sin y con predicción a 250 ms, el RTT medido, una corrección provocada y la explicación y participación del equipo.

### Integrantes y roles

Nombres tomados de los créditos del juego. Los roles individuales de implementación requieren confirmación del equipo: el historial registra los commits bajo la cuenta Firestar04O y no permite distinguir todos los aportes personales.

| Integrante | Rol confirmado para la entrega |
|---|---|
| Ayudante John | Preparación y grabación del video demostrativo |
| Coronel Oscar | Pendiente de confirmación del equipo |
| Espino Ricardo | Pendiente de confirmación del equipo |
| Gutierrez Ismael | Pendiente de confirmación del equipo |

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
