// Glosario de PlanCope Central: definiciones cortas en español para un administrador
// escolar. Cada acepción está tomada del repositorio; el comentario "Source" indica el
// archivo del que se extrajo. No inventar términos: si una definición no se puede
// rastrear a una fuente, no va acá.

export interface GlossaryEntry {
  /** Nombre visible del término (etiqueta en español). */
  label: string;
  /** Definición corta y llana (máximo ~20 palabras / 140 caracteres). */
  definition: string;
  /** Archivo del repositorio del que se tomó la definición. */
  source: string;
}

// Source: docs/central/exam-publishing-contract.md §6; app/_components/nodes/node-registry-panel.tsx
// Source: docs/reference-profile.md §1 (PlanCope es una app de escritorio que corre en cada escuela).
export const glossary = {
  nodo: {
    label: "Nodo",
    definition:
      "PlanCope instalado en una computadora de la escuela: entrega exámenes sin internet y sincroniza con Central.",
    source: "docs/central/exam-publishing-contract.md"
  },
  "clave-activacion": {
    label: "Clave de activación",
    definition:
      "Código universal que emite Central para enrolar una computadora como nodo; no está atado a una escuela y admite un máximo de activaciones.",
    source: "docs/activation-passphrase.md"
  },
  cue: {
    label: "CUE",
    definition:
      "Número único de 9 dígitos que identifica un establecimiento educativo: 7 del edificio y 2 del anexo.",
    source: "docs/roster-release.md"
  },
  anexo: {
    label: "Anexo",
    definition:
      "Sede o extensión de una escuela con el mismo CUE base. Se distingue con los últimos 2 dígitos del CUE.",
    source: "docs/roster-release.md"
  },
  "padron-provincial": {
    label: "Padrón provincial",
    definition:
      "Listado de todas las escuelas y sus alumnos de la provincia. Viaja cifrado en el instalador.",
    source: "docs/roster-release.md"
  },
  "padron-escolar": {
    label: "Padrón escolar",
    definition:
      "Listado de alumnos y divisiones de una sola escuela. El nodo lo importa al activarse con su CUE.",
    source: "docs/roster-release.md"
  },
  rol: {
    label: "Rol",
    definition: "Permiso que se le asigna a un usuario. Define qué puede ver y hacer en Central.",
    source: "app/_lib/roles.ts; src/Central/PlanCope.Central.Api/Controllers/UsersAdminController.cs"
  },
  "rol-administrador": {
    label: "Administrador",
    definition:
      "Acceso completo: crea usuarios, emite claves, publica exámenes y ve todas las escuelas.",
    source: "src/Central/PlanCope.Central.Api/Data/DevelopmentSeeder.cs"
  },
  "rol-padron-provincial": {
    label: "Padrón provincial",
    definition:
      "Rol de solo lectura sobre el padrón de toda la provincia: ve todos los CUE, sin administrar usuarios.",
    source: "src/Central/PlanCope.Central.Api/Data/DevelopmentSeeder.cs"
  },
  "rol-padron-escolar": {
    label: "Padrón escolar",
    definition:
      "Rol acotado a las escuelas (CUE) asignadas: muestra y administra datos solo dentro de ese alcance.",
    source: "app/_lib/roles.ts; src/Central/PlanCope.Central.Api/Controllers/UsersAdminController.cs"
  },
  "rol-autor-examenes": {
    label: "Autor de exámenes",
    definition: "Rol que crea y edita exámenes y sus versiones en el builder.",
    source: "app/_lib/roles.ts"
  },
  "rol-corrector": {
    label: "Corrector",
    definition: "Rol que revisa y califica las respuestas de los exámenes rendidos.",
    source: "app/_lib/roles.ts"
  },
  "rol-operador": {
    label: "Operador",
    definition:
      "Rol de operación diaria en la escuela: activa el equipo y busca exámenes nuevos en el nodo.",
    source: "app/_lib/roles.ts; docs/local/exam-delivery.md"
  },
  examen: {
    label: "Examen",
    definition:
      "Prueba con un código y un título que agrupa versiones. Cada versión tiene preguntas con sus reglas de puntaje.",
    source: "docs/central/exam-publishing-contract.md"
  },
  version: {
    label: "Versión",
    definition:
      "Copia concreta de un examen con sus preguntas. Se numera desde 1 y arranca como borrador.",
    source: "docs/central/exam-publishing-contract.md"
  },
  "version-instalador": {
    label: "Versión",
    definition: "Número de versión del instalador de PlanCope.",
    source: "app/(app)/descargas/page.tsx; app/_lib/contracts.ts"
  },
  "curso-grado": {
    label: "Curso / grado",
    definition: "Curso o grado al que está destinado el examen; no cambia a quién se entrega.",
    source: "src/Shared/PlanCope.Shared.Contracts/Exams/ExamContracts.cs; src/Central/PlanCope.Central.Api/Controllers/ExamsController.cs"
  },
  bloque: {
    label: "Bloque",
    definition: "Parte de una versión: una pregunta, un texto o una imagen. Se ordenan en el builder.",
    source: "src/Shared/PlanCope.Shared.Contracts/Exams/ExamContracts.cs; docs/local-exam-format.md"
  },
  estado: {
    label: "Estado",
    definition: "Situación del examen o de la versión: borrador, listo para publicar o publicado.",
    source: "app/_lib/exams/exam-state.ts"
  },
  "estado-borrador": {
    label: "Borrador",
    definition: "Estado inicial de un examen (sin contenido) o de una versión que todavía no se publicó.",
    source: "src/Shared/PlanCope.Shared.Contracts/Exams/ExamContracts.cs"
  },
  "estado-listo-para-publicar": {
    label: "Listo para publicar",
    definition: "Estado de un examen con al menos una versión con contenido y ninguna publicada todavía.",
    source: "src/Shared/PlanCope.Shared.Contracts/Exams/ExamContracts.cs"
  },
  "estado-publicado": {
    label: "Publicado",
    definition: "Versión ya publicada: queda inmutable y los nodos la reciben al sincronizar.",
    source: "src/Shared/PlanCope.Shared.Contracts/Exams/ExamContracts.cs"
  },
  "estado-reemplazada": {
    label: "Reemplazada",
    definition:
      "Versión publicada que dejó de ser la actual porque una versión posterior la reemplazó en los nodos.",
    source: "app/_lib/exams/version-state.ts; docs/central/exam-publishing-contract.md"
  },
  "basada-en": {
    label: "Basada en",
    definition: "Versión creada copiando el contenido de otra versión del examen; la versión original no cambia.",
    source: "app/_lib/exams/version-state.ts; docs/central/exam-publishing-contract.md"
  },
  publicar: {
    label: "Publicar",
    definition:
      "Enviar una versión terminada a los nodos como paquete. Una versión publicada ya no se puede editar.",
    source: "docs/central/exam-publishing-contract.md"
  },
  sincronizacion: {
    label: "Sincronización",
    definition:
      "Conexión periódica (unos 30 segundos) en la que el nodo descarga exámenes publicados y sube lo pendiente.",
    source: "docs/central/exam-publishing-contract.md; docs/local/exam-delivery.md"
  },
  "recibido-por-nodos": {
    label: "Recibido por N nodos",
    definition: "Cantidad de nodos que ya descargaron la última versión publicada de este examen.",
    source: "docs/central/exam-publishing-contract.md"
  },
  "regla-puntaje": {
    label: "Regla de puntaje",
    definition: "Forma de calcular los puntos de una pregunta según lo que marcó el alumno.",
    source: "app/_lib/schema/exam.ts; app/_lib/exams/policy-examples.ts"
  },
  "politica-all-or-nothing": {
    label: "Todo o nada",
    definition:
      "Da el puntaje completo solo si el alumno marca exactamente las respuestas correctas; si no, cero.",
    source: "app/_lib/schema/exam.ts; app/_lib/exams/policy-examples.ts"
  },
  "politica-proportional-penalised": {
    label: "Proporcional con penalización",
    definition: "Suma por cada respuesta correcta y resta por cada incorrecta, sin bajar de cero puntos.",
    source: "app/_lib/schema/exam.ts; app/_lib/exams/policy-examples.ts"
  },
  "politica-proportional-plain": {
    label: "Proporcional simple",
    definition:
      "Suma por cada respuesta correcta marcada y no descuenta las incorrectas. Marcar todas da el total.",
    source: "app/_lib/schema/exam.ts; app/_lib/exams/policy-examples.ts"
  },
  puntos: {
    label: "Puntos",
    definition: "Puntaje máximo que aporta una pregunta al total del examen.",
    source: "app/_lib/schema/exam.ts"
  },
  instalador: {
    label: "Instalador",
    definition: "Programa que instala PlanCope en la computadora de la escuela. Se descarga desde esta pantalla.",
    source: "app/(app)/descargas/page.tsx; app/_lib/contracts.ts"
  },
  canal: {
    label: "Canal",
    definition: "Línea de versiones del instalador: stable es producción y beta es para pruebas.",
    source: "src/Central/PlanCope.Central.Api/Services/GitHubReleaseInstallerStorage.cs"
  },
  "stats-intentos": {
    label: "Intentos",
    definition: "Cantidad de exámenes rendidos y entregados por los alumnos en el período filtrado.",
    source: "src/Central/PlanCope.Central.Api/Controllers/StatsController.cs"
  },
  "stats-promedio": {
    label: "Promedio de puntaje (%)",
    definition: "Puntaje promedio obtenido sobre el máximo posible, expresado en porcentaje.",
    source: "src/Central/PlanCope.Central.Api/Controllers/StatsController.cs"
  },
  "stats-correctas": {
    label: "Correctas",
    definition: "Cantidad de respuestas de ese bloque que el alumno respondió bien.",
    source: "src/Central/PlanCope.Central.Api/Controllers/StatsController.cs"
  },
  "stats-parciales": {
    label: "Parciales",
    definition: "Respuestas que obtuvieron puntaje parcial según la regla de puntaje de cada pregunta.",
    source: "src/Central/PlanCope.Central.Api/Controllers/StatsController.cs"
  },
  "stats-incorrectas": {
    label: "Incorrectas",
    definition: "Cantidad de respuestas de ese bloque que el alumno respondió mal.",
    source: "src/Central/PlanCope.Central.Api/Controllers/StatsController.cs"
  },
  "stats-en-blanco": {
    label: "En blanco",
    definition: "Preguntas que el alumno dejó sin responder.",
    source: "src/Central/PlanCope.Central.Api/Controllers/StatsController.cs"
  },
  "stats-no-calificables": {
    label: "No calificables",
    definition: "Bloques que no se califican (como texto o imagen) y que no aportan al puntaje del examen.",
    source:
      "src/Central/PlanCope.Central.Api/Controllers/StatsController.cs; src/Shared/PlanCope.Shared.Grading/BlockOutcome.cs"
  },
  "stats-cohorte-insuficiente": {
    label: "Cohorte insuficiente",
    definition:
      "En la vista provincial se oculta el dato con menos de 5 intentos, para no exponer información de alumnos.",
    source: "src/Shared/PlanCope.Shared.Domain/CohortSuppression.cs"
  }
} as const satisfies Record<string, GlossaryEntry>;

export type GlossaryTerm = keyof typeof glossary;

/** Definición en español de un término del glosario. */
export function glossaryDefinition(term: GlossaryTerm): string {
  return glossary[term].definition;
}

/** Nombre visible de un término del glosario. */
export function glossaryLabel(term: GlossaryTerm): string {
  return glossary[term].label;
}
