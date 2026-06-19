"use client";

import { useMemo, useState } from "react";
import { CentralApi } from "./centralApi";
import type { BlockType, ExamSummary, ExamVersion } from "./types";

const blockTypes: BlockType[] = ["Text", "Image", "MultipleChoice", "TrueFalse", "ShortAnswer"];

export default function CentralHome() {
  const [apiBaseUrl, setApiBaseUrl] = useState("https://localhost:7058");
  const [token, setToken] = useState("");
  const [username, setUsername] = useState("admin");
  const [password, setPassword] = useState("");
  const [message, setMessage] = useState("Conecta con Central API para comenzar.");
  const [exams, setExams] = useState<ExamSummary[]>([]);
  const [selectedExam, setSelectedExam] = useState<ExamSummary | null>(null);
  const [versions, setVersions] = useState<ExamVersion[]>([]);
  const [version, setVersion] = useState<ExamVersion | null>(null);
  const [newExam, setNewExam] = useState({ code: "", title: "", subject: "", level: "", area: "" });
  const [metadataJson, setMetadataJson] = useState("{}");
  const [blockJson, setBlockJson] = useState(defaultBlockJson());
  const [publish, setPublish] = useState({ subject: "", grade: "", division: "" });

  const api = useMemo(() => new CentralApi(apiBaseUrl, token), [apiBaseUrl, token]);

  async function login() {
    try {
      const accessToken = await api.login(username, password);
      setToken(accessToken);
      setMessage("Sesion iniciada.");
      await loadExams(accessToken);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudo iniciar sesion.");
    }
  }

  async function loadExams(nextToken = token) {
    try {
      const client = new CentralApi(apiBaseUrl, nextToken);
      const items = await client.listExams();
      setExams(items);
      setMessage(`${items.length} examenes cargados.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudieron cargar examenes.");
    }
  }

  async function createExam() {
    try {
      const created = await api.createExam({
        code: newExam.code,
        title: newExam.title,
        subject: newExam.subject || undefined,
        level: newExam.level || undefined,
        area: newExam.area || undefined
      });
      setSelectedExam(created);
      await loadExams();
      setMessage(`Examen ${created.code} creado.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudo crear el examen.");
    }
  }

  async function selectExam(exam: ExamSummary) {
    setSelectedExam(exam);
    setVersion(null);
    setPublish(current => ({ ...current, subject: exam.subject ?? "" }));

    try {
      const items = await api.listVersions(exam.id);
      setVersions(items);
      setMessage(`${items.length} versiones cargadas para ${exam.code}.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudieron cargar versiones.");
    }
  }

  async function createVersion() {
    if (!selectedExam) {
      return;
    }

    try {
      const created = await api.createVersion(selectedExam.id, parseJsonObject(metadataJson));
      setVersion(created);
      await selectExam(selectedExam);
      setMessage(`Version ${created.versionNumber} creada.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudo crear la version.");
    }
  }

  async function openVersion(versionId: string) {
    try {
      const loaded = await api.getVersion(versionId);
      setVersion(loaded);
      setMessage(`Version ${loaded.versionNumber} abierta.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudo abrir la version.");
    }
  }

  async function saveBlock() {
    if (!version) {
      return;
    }

    try {
      const block = parseJsonObject(blockJson) as {
        orderIndex: number;
        blockType: BlockType;
        title?: string;
        description?: string;
        config: Record<string, unknown>;
        validation?: Record<string, unknown>;
      };
      await api.upsertBlock(version.id, block);
      await openVersion(version.id);
      setMessage("Bloque guardado.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudo guardar el bloque.");
    }
  }

  async function publishVersion() {
    if (!version) {
      return;
    }

    try {
      await api.publishVersion(version.id, {
        subject: publish.subject || selectedExam?.subject,
        grade: publish.grade,
        division: publish.division || null
      });
      await openVersion(version.id);
      setMessage("Version publicada y disponible para sync.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "No se pudo publicar la version.");
    }
  }

  return (
    <main className="page">
      <header className="topbar">
        <div>
          <p className="eyebrow">PlanCope Central</p>
          <h1>Builder online de examenes</h1>
        </div>
        <button className="button secondary" type="button" onClick={() => void loadExams()}>
          Actualizar
        </button>
      </header>

      <section className="panel auth-panel">
        <label>
          Central API
          <input value={apiBaseUrl} onChange={event => setApiBaseUrl(event.target.value)} />
        </label>
        <label>
          Usuario
          <input value={username} onChange={event => setUsername(event.target.value)} />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={event => setPassword(event.target.value)} />
        </label>
        <button className="button" type="button" onClick={() => void login()}>
          Ingresar
        </button>
      </section>

      <p className="status">{message}</p>

      <section className="workspace">
        <aside className="panel">
          <h2>Examenes</h2>
          <div className="stack">
            <input placeholder="Codigo" value={newExam.code} onChange={event => setNewExam({ ...newExam, code: event.target.value })} />
            <input placeholder="Titulo" value={newExam.title} onChange={event => setNewExam({ ...newExam, title: event.target.value })} />
            <input placeholder="Materia" value={newExam.subject} onChange={event => setNewExam({ ...newExam, subject: event.target.value })} />
            <input placeholder="Curso/grado" value={newExam.level} onChange={event => setNewExam({ ...newExam, level: event.target.value })} />
            <input placeholder="Area" value={newExam.area} onChange={event => setNewExam({ ...newExam, area: event.target.value })} />
            <button className="button" type="button" onClick={() => void createExam()}>
              Crear examen
            </button>
          </div>
          <div className="list">
            {exams.map(exam => (
              <button className={selectedExam?.id === exam.id ? "list-item active" : "list-item"} key={exam.id} type="button" onClick={() => void selectExam(exam)}>
                <strong>{exam.code}</strong>
                <span>{exam.title}</span>
                <small>{exam.versionCount} versiones</small>
              </button>
            ))}
          </div>
        </aside>

        <section className="panel">
          <h2>Versiones</h2>
          <textarea value={metadataJson} onChange={event => setMetadataJson(event.target.value)} />
          <button className="button" disabled={!selectedExam} type="button" onClick={() => void createVersion()}>
            Crear version
          </button>
          <div className="list horizontal">
            {versions.map(item => (
              <button className={version?.id === item.id ? "list-item active" : "list-item"} key={item.id} type="button" onClick={() => void openVersion(item.id)}>
                v{item.versionNumber} · {item.status}
              </button>
            ))}
          </div>

          {version && (
            <>
              <div className="section-title">
                <h3>Editor de bloques</h3>
                <span>{blockTypes.join(" / ")}</span>
              </div>
              <textarea className="code" value={blockJson} onChange={event => setBlockJson(event.target.value)} />
              <button className="button" type="button" onClick={() => void saveBlock()}>
                Guardar bloque
              </button>

              <div className="section-title">
                <h3>Publicar</h3>
                <span>Targets: materia, curso y division opcional</span>
              </div>
              <div className="publish-grid">
                <input placeholder="Materia" value={publish.subject} onChange={event => setPublish({ ...publish, subject: event.target.value })} />
                <input placeholder="Curso/grado" value={publish.grade} onChange={event => setPublish({ ...publish, grade: event.target.value })} />
                <input placeholder="Division opcional" value={publish.division} onChange={event => setPublish({ ...publish, division: event.target.value })} />
                <button className="button" type="button" onClick={() => void publishVersion()}>
                  Publicar version
                </button>
              </div>

              <h3>Preview JSON</h3>
              <pre>{JSON.stringify(version, null, 2)}</pre>
            </>
          )}
        </section>
      </section>
    </main>
  );
}

function parseJsonObject(value: string): Record<string, unknown> {
  const parsed = JSON.parse(value) as unknown;
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new Error("El JSON debe ser un objeto.");
  }

  return parsed as Record<string, unknown>;
}

function defaultBlockJson() {
  return JSON.stringify(
    {
      orderIndex: 0,
      blockType: "MultipleChoice",
      title: "Pregunta 1",
      config: {
        question: "Cuanto es 2 + 2?",
        options: [
          { value: "4", label: "4" },
          { value: "5", label: "5" }
        ]
      },
      validation: { required: true }
    },
    null,
    2
  );
}
