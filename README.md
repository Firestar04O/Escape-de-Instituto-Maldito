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

## Semana 14 — Voz de proximidad, amigos e invitaciones

- Paquetes: Unity Vivox `16.13.0` y Friends `1.4.1`, compartiendo el `PlayerId` de Authentication con Multiplayer Services.
- Cada jugador entra automáticamente a un canal posicional privado derivado del `SessionId` al crear o unirse a una sala, actualiza la posición de su avatar cada `0.3 s` y abandona el canal al salir de la sesión.
- Configuración 3D: distancia audible `32 m`, conversación `1 m`, modelo `InverseByDistance` e intensidad `1.0`. El alcance cubre una sala escolar sin permitir escuchar indefinidamente a través del recorrido; el modelo inverso mantiene una caída natural y perceptible.
- Controles: silencio del micrófono, silencio local del participante y volumen individual de `-50` a `+50`.
- El centro de juego ofrece **Partida rápida**, **Con amigos** y **Jugar solo**. Partida rápida busca o crea una sala pública de dos plazas. Con amigos abre la lista de presencia y solicitudes antes de crear una sala.
- Al invitar a un amigo disponible se crea una sala privada. Friends transporta el código y el identificador de sesión internamente. La invitación vence después de 10 minutos. Ambos jugadores marcan **Listo** antes de la cuenta regresiva.
- **Opciones por código** permite crear o unirse a una sala manualmente. **Jugar solo** inicia una partida local con el aliado IA.
- Si Vivox o Friends no están habilitados en Unity Dashboard, el juego conserva el lobby y la partida; la UI muestra el error del servicio sin cerrar la sesión multijugador.

### Prueba de distancia (dos dispositivos)

Esta tabla debe llenarse con una prueba real y grabada. No se colocan valores estimados como evidencia.

| Distancia | Resultado esperado | Resultado observado | Evidencia |
|---:|---|---|---|
| 5 m | Voz clara y cercana | Pendiente de medir | Video / captura |
| 15 m | Voz atenuada pero comprensible | Pendiente de medir | Video / captura |
| 35 m | Fuera del alcance; voz inaudible | Pendiente de medir | Video / captura |

### Guion mínimo de validación

1. Activar Vivox y Friends para el mismo Cloud Project en Unity Dashboard.
2. Abrir dos perfiles/dispositivos, copiar sus PlayerId y aceptar la amistad en ambos clientes.
3. Entrar en **Con amigos**, invitar al amigo disponible y aceptar la notificación desde el centro de juego del segundo jugador.
4. Iniciar la partida y comprobar voz a 5, 15 y 35 m, silencio de micrófono, silencio local y volumen individual.
5. Abandonar la sala y verificar que se cierra el canal. Repetir con una desconexión.
6. Registrar en la tabla los resultados reales, dispositivo, plataforma y si hubo eco.

## Documentación técnica

La arquitectura, las tablas comparativas, los parámetros y las evidencias completas de las Semanas 10–12 están en [NETWORKING.md](NETWORKING.md). El diseño funcional de la Semana 14 también se resume en [GDD/SEMANA14_VOZ_Y_AMIGOS.md](GDD/SEMANA14_VOZ_Y_AMIGOS.md).
