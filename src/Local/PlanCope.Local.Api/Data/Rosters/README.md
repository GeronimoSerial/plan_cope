# Padrones embebidos

Antes de compilar un release, exportar desde Central cada padrón aprobado como
`<cue>-<ciclo>.roster.json` dentro de esta carpeta. Sólo los archivos que terminan
en `.roster.json` se compilan como recursos del ensamblado local.

Al iniciar, la API local valida checksum, CUE, cantidades y relaciones, y luego
importa cada paquete de manera idempotente en SQLite. Un archivo inválido hace
fallar el arranque para evitar distribuir una nómina parcial o corrupta.

El CUE guardado en el JSON debe tener exactamente 9 dígitos (7 de establecimiento
y 2 de anexo), sin guiones ni espacios. El build también debe configurar
`Nominalization:DocumentHmacKey` con al menos 32 bytes; el DNI nunca se guarda en
claro en SQLite.
