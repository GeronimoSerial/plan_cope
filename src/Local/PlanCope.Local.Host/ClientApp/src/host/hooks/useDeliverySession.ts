import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiClient } from "../api/apiClient";
import { ensureSelectedExamId, toExamOption } from "../domain/exams";
import {
  buildCreateSessionRequest,
  initialSessionForm,
  type SessionForm,
  validateSessionForm
} from "../domain/sessionForm";
import type {
  ExamOption,
  FormErrors,
  HostContext,
  LocalSession,
  RosterSection,
  RosterSnapshot,
  SessionProgress
} from "../types";
import { isValidCue } from "../domain/cue";

const PROGRESS_POLL_MS = 3000;
const PROGRESS_POLL_MAX_MS = 30000;

type ProgressPollerOptions = {
  accessCode: string;
  fetchProgress: (signal?: AbortSignal) => Promise<SessionProgress>;
  onProgress: (progress: SessionProgress) => void;
  onError: () => void;
};

function progressChanged(previous: SessionProgress, next: SessionProgress): boolean {
  return (
    previous.submittedCount !== next.submittedCount ||
    previous.inProgressCount !== next.inProgressCount ||
    previous.completionPercentage !== next.completionPercentage
  );
}

export function createProgressPoller(options: ProgressPollerOptions): () => void {
  const { accessCode, fetchProgress, onProgress, onError } = options;

  if (!accessCode) {
    return () => {};
  }

  const controller = new AbortController();
  let cancelled = false;
  let generation = 0;
  let intervalMs = PROGRESS_POLL_MS;
  let previous: SessionProgress | null = null;

  const schedule = (ms: number) => {
    if (cancelled) {
      return;
    }

    const gen = ++generation;
    setTimeout(() => {
      if (cancelled || gen !== generation) {
        return;
      }
      void poll(gen);
    }, ms);
  };

  const poll = async (gen: number) => {
    if (cancelled || gen !== generation) {
      return;
    }
    if (document.visibilityState !== "visible") {
      return;
    }

    let next: SessionProgress;
    try {
      next = await fetchProgress(controller.signal);
    } catch {
      if (cancelled || gen !== generation) {
        return;
      }
      onError();
      intervalMs = Math.min(intervalMs * 2, PROGRESS_POLL_MAX_MS);
      schedule(intervalMs);
      return;
    }

    if (cancelled || gen !== generation) {
      return;
    }

    const changed = previous === null || progressChanged(previous, next);
    previous = next;
    onProgress(next);
    intervalMs = changed ? PROGRESS_POLL_MS : Math.min(intervalMs * 2, PROGRESS_POLL_MAX_MS);
    schedule(intervalMs);
  };

  const handleVisibilityChange = () => {
    if (cancelled || document.visibilityState !== "visible") {
      return;
    }
    intervalMs = PROGRESS_POLL_MS;
    schedule(intervalMs);
  };

  document.addEventListener("visibilitychange", handleVisibilityChange);
  schedule(0);

  return () => {
    cancelled = true;
    generation += 1;
    controller.abort();
    document.removeEventListener("visibilitychange", handleVisibilityChange);
  };
}

export type DeliverySessionState = ReturnType<typeof useDeliverySession>;

export function useDeliverySession(hostContext: HostContext) {
  const api = useMemo(() => new ApiClient(hostContext.apiBaseUrl), [hostContext.apiBaseUrl]);
  const [exams, setExams] = useState<ExamOption[]>([]);
  const [selectedExamId, setSelectedExamId] = useState("");
  const [form, setForm] = useState<SessionForm>(() => initialSessionForm(hostContext.operatorName));
  const [session, setSession] = useState<LocalSession | null>(null);
  const [activeSessions, setActiveSessions] = useState<LocalSession[]>([]);
  const [progress, setProgress] = useState<SessionProgress | null>(null);
  const [status, setStatus] = useState("Iniciando API local...");
  const [isBusy, setIsBusy] = useState(false);
  const [isLoadingExams, setIsLoadingExams] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formErrors, setFormErrors] = useState<FormErrors>({});
  const [resumeAccessCode, setResumeAccessCode] = useState("");
  const [rosterSnapshot, setRosterSnapshot] = useState<RosterSnapshot | null>(null);
  const [rosterSections, setRosterSections] = useState<RosterSection[]>([]);
  const [selectedRosterSectionId, setSelectedRosterSectionId] = useState("");
  const [isLoadingRoster, setIsLoadingRoster] = useState(false);
  const [rosterError, setRosterError] = useState<string | null>(null);

  useEffect(() => {
    setForm(current => ({ ...current, operatorName: current.operatorName || hostContext.operatorName }));
    setStatus(`API local activa en ${hostContext.lanBaseUrl}`);
  }, [hostContext.lanBaseUrl, hostContext.operatorName]);

  const loadExams = useCallback(async (signal?: AbortSignal) => {
    setIsLoadingExams(true);
    setError(null);

    try {
      const items = await api.getExams(signal);
      const options = items.map(toExamOption);
      setExams(options);
      setSelectedExamId(current => ensureSelectedExamId(options, current));
      setStatus(options.length > 0 ? `API local activa en ${hostContext.lanBaseUrl}` : "No hay examenes locales publicados en este equipo.");
    } catch (exception) {
      if (!signal?.aborted) {
        setError(exception instanceof Error ? exception.message : "No se pudieron cargar los examenes locales.");
        setStatus("No se pudieron cargar los examenes locales.");
      }
    } finally {
      if (!signal?.aborted) {
        setIsLoadingExams(false);
      }
    }
  }, [api, hostContext.lanBaseUrl]);

  const refreshExams = useCallback(async (signal?: AbortSignal) => {
    setIsLoadingExams(true);
    setError(null);
    setStatus("Sincronizando examenes...");

    try {
      await api.pullExams(signal);
    } catch (exception) {
      if (!signal?.aborted) {
        setError(exception instanceof Error ? exception.message : "No se pudo sincronizar con Central; se mostrara el catalogo local.");
      }
    }

    await loadExams(signal);
  }, [api, loadExams]);

  const loadRoster = useCallback(async (signal?: AbortSignal) => {
    const cue = form.cue.trim();
    if (!isValidCue(cue)) {
      setRosterSnapshot(null);
      setRosterSections([]);
      setSelectedRosterSectionId("");
      setRosterError(null);
      return;
    }

    setIsLoadingRoster(true);
    setRosterError(null);
    try {
      const response = await api.getLatestRoster(cue, signal);
      setRosterSnapshot(response.snapshot);
      setRosterSections(response.sections);
      setSelectedRosterSectionId(current => response.sections.some(section => section.id === current)
        ? current
        : "");
    } catch (exception) {
      if (!signal?.aborted) {
        setRosterSnapshot(null);
        setRosterSections([]);
        setSelectedRosterSectionId("");
        setRosterError(exception instanceof Error ? exception.message : "No se pudo consultar el padrón local.");
      }
    } finally {
      if (!signal?.aborted) {
        setIsLoadingRoster(false);
      }
    }
  }, [api, form.cue]);

  useEffect(() => {
    const controller = new AbortController();
    void loadRoster(controller.signal);
    return () => controller.abort();
  }, [loadRoster]);

  const selectedRosterSection = useMemo(
    () => rosterSections.find(section => section.id === selectedRosterSectionId) ?? null,
    [rosterSections, selectedRosterSectionId]
  );

  useEffect(() => {
    if (selectedRosterSection && rosterSnapshot) {
      setForm(current => ({
        ...current,
        classroomCode: [selectedRosterSection.course, selectedRosterSection.division].filter(Boolean).join(" "),
        expectedStudentCount: selectedRosterSection.studentCount
      }));
      return;
    }

    setForm(current => ({ ...current, classroomCode: "", expectedStudentCount: 0 }));
  }, [rosterSnapshot, selectedRosterSection]);

  const loadActiveSessions = useCallback(async (signal?: AbortSignal) => {
    try {
      const sessions = await api.getActiveSessions(signal);
      setActiveSessions(sessions);
      setSession(current => current ?? sessions[0] ?? null);
      setResumeAccessCode(current => current || sessions[0]?.accessCode || "");
    } catch (exception) {
      if (!signal?.aborted) {
        setError(exception instanceof Error ? exception.message : "No se pudieron recuperar las sesiones activas.");
      }
    }
  }, [api]);

  useEffect(() => {
    const controller = new AbortController();

    void refreshExams(controller.signal);
    void loadActiveSessions(controller.signal);

    return () => controller.abort();
  }, [loadActiveSessions, refreshExams]);

  useEffect(() => {
    setSelectedExamId(current => ensureSelectedExamId(exams, current));
  }, [exams]);

  const selectedExam = useMemo(
    () => exams.find(exam => exam.id === selectedExamId) ?? null,
    [exams, selectedExamId]
  );

  const schoolName = rosterSnapshot?.schoolName?.trim() ?? "";
  const sessionLink = session ? `${hostContext.lanBaseUrl}/examen/${session.accessCode}` : "";

  const updateForm = useCallback(<TKey extends keyof SessionForm>(key: TKey, value: SessionForm[TKey]) => {
    setForm(current => ({ ...current, [key]: value }));
    setFormErrors(current => ({ ...current, [key]: undefined }));
  }, []);

  const createSession = useCallback(async () => {
    const nextErrors = validateSessionForm(form, selectedExam?.id ?? "");
    if (!rosterSnapshot || !rosterSnapshot.status || rosterSnapshot.status.toLowerCase() !== "ready") {
      nextErrors.rosterSectionId = "Actualiza el padrón GE antes de crear una sesión.";
    } else if (!selectedRosterSection) {
      nextErrors.rosterSectionId = "Selecciona una sección del padrón.";
    }
    setFormErrors(nextErrors);

    if (Object.keys(nextErrors).length > 0) {
      setError("Revisa los datos marcados para crear la sesion.");
      return;
    }

    if (!selectedExam) {
      return;
    }

    setIsBusy(true);
    setError(null);
    setStatus("Creando sesion...");

    try {
      const created = await api.createSession(buildCreateSessionRequest(form, selectedExam, rosterSnapshot, selectedRosterSection));
      setSession(created);
      setActiveSessions(current => [created, ...current.filter(item => item.id !== created.id)]);
      setResumeAccessCode(created.accessCode);
      setStatus("Sesion creada. Comparte el codigo con los alumnos.");
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "No se pudo crear la sesion.");
      setStatus("No se pudo crear la sesion.");
    } finally {
      setIsBusy(false);
    }
  }, [api, form, selectedExam, rosterSnapshot, selectedRosterSection]);

  const resumeSession = useCallback(async (accessCode?: string) => {
    const code = (accessCode ?? resumeAccessCode).trim().toUpperCase();
    if (!code) {
      setFormErrors(current => ({ ...current, accessCode: "Ingresa un codigo de sesion." }));
      return;
    }

    setIsBusy(true);
    setError(null);
    setFormErrors(current => ({ ...current, accessCode: undefined }));
    setStatus("Recuperando sesion...");

    try {
      const restored = await api.getSession(code);
      setSession(restored);
      setResumeAccessCode(restored.accessCode);
      setStatus("Sesion recuperada.");
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "No se pudo recuperar la sesion.");
      setStatus("No se pudo recuperar la sesion.");
    } finally {
      setIsBusy(false);
    }
  }, [api, resumeAccessCode]);

  useEffect(() => {
    if (!session?.accessCode) {
      return;
    }

    return createProgressPoller({
      accessCode: session.accessCode,
      fetchProgress: signal => api.getSessionProgress(session.accessCode, signal),
      onProgress: setProgress,
      onError: () => setStatus("No se pudo actualizar el progreso.")
    });
  }, [api, session?.accessCode]);

  return {
    examCatalog: {
      exams,
      isLoadingExams,
      selectedExamId,
      setSelectedExamId,
      loadExams: refreshExams
    },
    roster: {
      snapshot: rosterSnapshot,
      sections: rosterSections,
      selectedSectionId: selectedRosterSectionId,
      setSelectedSectionId: (value: string) => {
        setSelectedRosterSectionId(value);
        setFormErrors(current => ({ ...current, rosterSectionId: undefined }));
      },
      isLoading: isLoadingRoster,
      error: rosterError
    },
    sessionForm: {
      form,
      formErrors,
      schoolName,
      updateForm
    },
    activeSession: {
      session,
      progress,
      sessionLink,
      activeSessions,
      resumeAccessCode,
      setResumeAccessCode,
      resumeSession
    },
    status,
    error,
    isBusy,
    createSession
  };
}
