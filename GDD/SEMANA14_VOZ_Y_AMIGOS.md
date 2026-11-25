# Anexo GDD — Voz de proximidad y amigos

El centro de juego presenta al personaje y permite elegir Partida rápida, Con amigos o Jugar solo. En Partida rápida la búsqueda encuentra una sesión pública compatible o crea una para esperar al segundo jugador. Jugar solo usa al aliado IA. El acceso manual vive en Opciones por código.

## Flujo social

1. Desde el centro de juego se pueden agregar amigos por PlayerId, aceptar solicitudes y consultar presencia.
2. El jugador elige un amigo disponible y pulsa Invitar. El juego crea una sala privada automáticamente.
3. La invitación incluye el código de sala, `SessionId`, remitente y vencimiento de 10 minutos.
4. Al aceptar, el invitado entra a esa sesión y vuelve al mismo flujo de LISTO de ambos jugadores.
5. Cuenta regresiva, partida iniciada o invitación vencida bloquean el ingreso tardío.

## Voz durante la partida

- Canal: uno posicional por sesión multijugador.
- Identidad: `PlayerId` autenticado por Unity Authentication.
- Distancia audible: 32 unidades del mundo (1 unidad = 1 metro del nivel).
- Distancia conversacional: 1 m.
- Atenuación: inversa, intensidad 1.0.
- Posición: se reporta desde el avatar local cada 0.3 s.
- Accesibilidad: silenciar micrófono, silenciar participante y ajustar su volumen local.
- Privacidad: la voz se une al entrar a la sesión y sale al abandonarla; nunca se mantiene entre salas distintas.

## Comportamiento ante fallos

Vivox y Friends son complementarios al gameplay. Si un servicio social está deshabilitado, sin red o sin permisos de micrófono, se informa el estado en pantalla y el jugador puede seguir usando el código de sala y jugar sin voz. Ningún error social debe derribar la sesión de Netcode/Relay.

## Evidencia pendiente

La validación final requiere dos dispositivos y cuentas distintas: invitación aceptada, voz a 5/15/35 m, controles de silencio/volumen, abandono del canal y registro de eco. Los valores observados se completan en la tabla de Semana 14 del README después de la prueba real.
