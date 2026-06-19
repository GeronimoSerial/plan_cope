import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiClient } from "../api/apiClient";
import { ensureSelectedExamId, filterExams, toExamOption, uniqueSorted } from "../domain/exams";
import {
  buildCreateSessionRequest,
  initialSessionForm,
  resolveSchoolName,
  type SessionForm,
  validateSessionForm
} from "../domain/sessionForm";
import type { ExamOption, FormErrors, HostContext, LocalSession, SessionProgress } from "../types";

export type DeliverySessionState = ReturnType<typeof useDeliverySession>;

export function useDeliverySession(hostContext: HostContext) {
  const api = useMemo(() => new ApiClient(hostContext.apiBaseUrl), [hostContext.apiBaseUrl]);
  const [exams, setExams] = useState<ExamOption[]>([]);
  const [selectedCourse, setSelectedCourse] = useState("");
  const [selectedDivision, setSelectedDivision] = useState("");
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

    void loadExams(controller.signal);
    void loadActiveSessions(controller.signal);

    return () => controller.abort();
  }, [loadActiveSessions, loadExams]);

  const filteredExams = useMemo(
    () => filterExams(exams, selectedCourse, selectedDivision),
    [exams, selectedCourse, selectedDivision]
  );

  useEffect(() => {
    setSelectedExamId(current => ensureSelectedExamId(filteredExams, current));
  }, [filteredExams]);

  const courses = useMemo(() => uniqueSorted(exams.map(exam => exam.course)), [exams]);
  const divisions = useMemo(() => {
    const scopedExams = selectedCourse ? exams.filter(exam => exam.course === selectedCourse) : exams;
    return uniqueSorted(scopedExams.map(exam => exam.division));
  }, [exams, selectedCourse]);

  const selectedExam = useMemo(
    () => filteredExams.find(exam => exam.id === selectedExamId) ?? null,
    [filteredExams, selectedExamId]
  );

  const schoolName = useMemo(() => resolveSchoolName(form.cue), [form.cue]);
  const sessionLink = session ? `${hostContext.lanBaseUrl}/examen/${session.accessCode}` : "";

  const updateForm = useCallback(<TKey extends keyof SessionForm>(key: TKey, value: SessionForm[TKey]) => {
    setForm(current => ({ ...current, [key]: value }));
    setFormErrors(current => ({ ...current, [key]: undefined }));
  }, []);

  const createSession = useCallback(async () => {
    const nextErrors = validateSessionForm(form, selectedExam?.id ?? "");
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
      const created = await api.createSession(buildCreateSessionRequest(form, selectedExam));
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
  }, [api, form, selectedExam]);

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

  const refreshProgress = useCallback(async () => {
    if (!session?.accessCode) {
      return;
    }

    try {
      setProgress(await api.getSessionProgress(session.accessCode));
    } catch {
      setStatus("No se pudo actualizar el progreso.");
    }
  }, [api, session?.accessCode]);

  useEffect(() => {
    if (!session?.accessCode) {
      return;
    }

    void refreshProgress();
    const intervalId = window.setInterval(refreshProgress, 3000);
    return () => window.clearInterval(intervalId);
  }, [refreshProgress, session?.accessCode]);

  return {
    examCatalog: {
      courses,
      divisions,
      filteredExams,
      isLoadingExams,
      selectedCourse,
      selectedDivision,
      selectedExamId,
      setSelectedCourse,
      setSelectedDivision,
      setSelectedExamId,
      loadExams
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
