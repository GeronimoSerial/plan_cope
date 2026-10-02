# Conexión LAN sin internet

El host escucha en todas las interfaces (`0.0.0.0`) y publica la IPv4 del
adaptador activo para que los alumnos puedan acceder desde la misma LAN.
No necesita salida a internet ni un gateway configurado para detectar esa IP.
La dirección publicada se revisa cada 5 segundos; si cambia, el host actualiza
el enlace de la sesión sin cerrar la aplicación.

La selección prioriza Wi-Fi/Ethernet sobre otras interfaces; entre adaptadores
físicos prefiere uno con gateway y luego Wi-Fi. Excluye loopback, túneles,
IPv6 y direcciones automáticas `169.254.*`. Si no hay una IPv4 LAN utilizable,
la dirección queda en `127.0.0.1` hasta que aparezca una.

## Verificación en Windows

1. Abrir la app sin conexión y crear una sesión.
2. Conectarse al Wi-Fi del router con la WAN desconectada.
3. Esperar hasta 5 segundos: el enlace debe pasar de `127.0.0.1` a la IPv4
   del equipo que muestra `ipconfig` para ese adaptador.
4. Desde otro dispositivo en esa misma red, abrir el enlace del examen.
5. Desconectar/reconectar o cambiar de red y comprobar que el enlace se actualiza.

Si el enlace muestra la IP correcta pero no abre desde otro dispositivo,
verificar el acceso entrante al puerto publicado en el firewall de Windows y
el aislamiento de clientes del router. Eso es independiente de detectar la IP.
