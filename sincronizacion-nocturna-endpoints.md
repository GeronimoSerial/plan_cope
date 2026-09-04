## API de Gestión Educativa — Endpoints

Ejemplo: Escuela Pujol

2026-08-13

## API de Gestión Educativa (GE) — Endpoints usados en la sincronización nocturna

Ejemplo usado: Escuela Normal “Dr. Juan Gregorio Pujol” — CUE 1800554-00 (dato público)

Este documento describe cómo se consume el servicio externo de Gestión Educativa (GE) durante una sincronización nocturna de una escuela: qué endpoints se llaman, en qué orden, con qué parámetros y qué devuelve cada uno.

Por confidencialidad, este documento no incluye la URL base del servicio ni las credenciales de acceso (se comparten por otro canal). Se listan únicamente las rutas relativas, que se agregan a esa URL base.

## 1. Cuándo ocurre

La sincronización corre todos los días a las 03:00. A partir de ahí, se realizan las siguientes llamadas al servicio de GE.

## 2. Endpoints de GE usados, en orden de llamada

## 2.1 — POST /token

Autenticación OAuth. Se pide una sola vez por corrida completa (no por escuela) y el token se reutiliza para todas las escuelas, renovándose antes de expirar o al recibir un 401.

- Body (form-urlencoded): credenciales de servicio + grant_type=password (valores no incluidos en este documento)

- Devuelve: access_token , expires_in

## 2.2 — GET /api/externo/asistencias/GetSecciones

Trae todas las secciones de todas las escuelas de una sola vez. Se llama una vez por corrida completa, no por escuela — la lista se filtra después por cueAnexo para quedarse con las secciones de la escuela que se está procesando (para Pujol: cueAnexo === "1800554-00" ).

- Header: Authorization: Bearer {token}

- Devuelve: establecimientoCursoDivisionId , cueAnexo , curso , división , turno , etc.


## 2.3 — GET /api/externo/asistencias/GetAlumnosPorSeccionV2?cue={cue}&cicloLectivo= {cicloLectivo}

Trae el padrón de alumnos por sección de esa escuela puntual. Se llama una vez por escuela.

Ejemplo real para Pujol (ruta relativa, sin URL base):

GET /api/externo/asistencias/GetAlumnosPorSeccionV2?cue=1800554-00&cicloLectivo=2026

- Parámetros: cue (CUE de la escuela, ej. 1800554-00 ), cicloLectivo (año actual como string, ej. 2026 )

- Devuelve: personaId , establecimientoCursoDivisionId , tipoMovimiento , tipoMovimientoEgreso , cohorteAlumno , tieneEgreso

- Si devuelve 0 alumnos, se corta el procesamiento de esa escuela con un aviso (protección contra respuesta vacía de GE).

## 2.4 — GET /api/externo/asistencias/GetPersonasAlumnos?pageSize=100&pageIndex=1&personaId= {id}

Trae el detalle de una persona puntual (nombre, DNI, fecha de nacimiento, etc.). No se pide para todo el padrón — solo para los alumnos nuevos o con cambio de sección detectados en esa corrida.

- Parámetro: personaId

- Devuelve: apellido , nombre , tipoDocumento , nroDocumento , sexo , fechaNacimiento , poseeCUD

- Frecuencia: una llamada por alumno nuevo/cambio de sección, en lotes de 5 concurrentes con una pausa de 2 segundos entre lotes — para no saturar el servicio de GE.

## 3. Reintentos ante fallas de GE

- 401 (token vencido/inválido): se descarta el token, se pide uno nuevo ( /token ) y se reintenta la llamada una vez.

- 5xx / timeout: se espera 3 segundos y se reintenta una vez.

- Cualquier otro error se registra en el resumen de la escuela.

## 4. Endpoints de GE disponibles pero NO usados en la sincronización nocturna

- DNI. GET /api/externo/asistencias/GetPersonasAlumnosPorNroDocumento — búsqueda manual por

- notas, siempre bajo demanda, no en el proceso nocturno. GET /api/externo/AlumnoNotas/ObtenerNotas/{cicloId}/{personaId} — sincronización de


## 5. Secuencia resumida — Ejemplo real: Escuela Normal “Dr. Juan Gregorio Pujol” (CUE 1800554-00 )

- 1. 03:00 — arranca la sincronización. 2. POST /token — una vez para toda la corrida.

- 3. GET /api/externo/asistencias/GetSecciones escuelas). 4. Se filtran las secciones de Pujol ( cueAnexo === "1800554-00" ) — sin llamada extra. — una vez para toda la corrida (todas las

- 5. GET /api/externo/asistencias/GetAlumnosPorSeccionV2?cue=1800554-00&cicloLectivo=2026

- una vez para Pujol. 6. Se compara el padrón recibido contra los datos ya guardados; se identifican altas, cambios de sección y bajas.

- 7. GET /api/externo/asistencias/GetPersonasAlumnos?pageSize=100&pageIndex=1&personaId= {id} — una vez por cada alumno nuevo o con cambio de sección de Pujol, en lotes de 5 con

- pausas de 2s. 8. Fin del procesamiento de Pujol; el token y las secciones ya traídas se reutilizan para la siguiente escuela.

Todas las rutas de este documento son relativas — se agregan a la URL base del servicio de GE, compartida por otro canal.
