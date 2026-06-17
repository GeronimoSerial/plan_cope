import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiClient } from "../api/apiClient";
import { toExamOption, uniqueSorted } from "../domain/exams";
import type { CreateSessionRequest, ExamOption, HostContext, LocalSession, SessionProgress } from "../types";

type SessionForm = {
  cue: string;
  classroomCode: string;
  operatorName: string;
  expectedStudentCount: number;
};

const initialForm: SessionForm = {
  cue: "",
  classroomCode: "6A",
  operatorName: "",
  expectedStudentCount: 30
};

export function useDeliverySession(hostContext: HostContext) {
  const api = useMemo(() => new ApiClient(hostContext.apiBaseUrl), [hostContext.apiBaseUrl]);
  const [exams, setExams] = useState<ExamOption[]>([]);
  const [selectedCourse, setSelectedCourse] = useState("");
  const [selectedDivision, setSelectedDivision] = useState("");
  const [selectedExamId, setSelectedExamId] = useState("");
  const [form, setForm] = useState<SessionForm>({ ...initialForm, operatorName: hostContext.operatorName });
  const [session, setSession] = useState<LocalSession | null>(null);
  const [progress, setProgress] = useState<SessionProgress | null>(null);
  const [status, setStatus] = useState("Iniciando API local...");
  const [isBusy, setIsBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setForm(current => ({ ...current, operatorName: current.operatorName || hostContext.operatorName }));
    setStatus(`API local activa en ${hostContext.lanBaseUrl}`);
  }, [hostContext.lanBaseUrl, hostContext.operatorName]);

  useEffect(() => {
    const controller = new AbortController();

    api.getExams(controller.signal)
      .then(items => {
        const options = items.map(toExamOption);
        setExams(options);
        setSelectedExamId(options[0]?.id ?? "");
        setError(null);
      })
      .catch((exception: Error) => {
        if (!controller.signal.aborted) {
          setError(exception.message);
          setStatus("No se pudieron cargar los examenes locales.");
        }
      });

    return () => controller.abort();
  }, [api]);

  const filteredExams = useMemo(() => {
    return exams
      .filter(exam => !selectedCourse || exam.course === selectedCourse)
      .filter(exam => !selectedDivision || exam.division === selectedDivision);
  }, [exams, selectedCourse, selectedDivision]);

  useEffect(() => {
    if (!filteredExams.some(exam => exam.id === selectedExamId)) {
      setSelectedExamId(filteredExams[0]?.id ?? "");
    }
  }, [filteredExams, selectedExamId]);

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
  }, []);

  const createSession = useCallback(async () => {
    if (!selectedExam) {
      return;
    }

    if (!form.cue.trim()) {
      setError("Completa el CUE para continuar.");
      return;
    }

    const request: CreateSessionRequest = {
      examVersionId: selectedExam.id,
      schoolCode: form.cue.trim().toUpperCase(),
      classroomCode: form.classroomCode.trim(),
      commissionCode: null,
      startedBy: form.operatorName.trim(),
      expectedStudentCount: form.expectedStudentCount,
      config: null
    };

    setIsBusy(true);
    setError(null);
    setStatus("Creando sesion...");

    try {
      const created = await api.createSession(request);
      setSession(created);
      setStatus("Sesion creada. Comparte el codigo con los alumnos.");
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "No se pudo crear la sesion.");
      setStatus("No se pudo crear la sesion.");
    } finally {
      setIsBusy(false);
    }
  }, [api, form, selectedExam]);

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
    courses,
    divisions,
    error,
    filteredExams,
    form,
    isBusy,
    progress,
    schoolName,
    selectedCourse,
    selectedDivision,
    selectedExamId,
    session,
    sessionLink,
    status,
    createSession,
    setSelectedCourse,
    setSelectedDivision,
    setSelectedExamId,
    updateForm
  };
}

function resolveSchoolName(cue: string): string {
  const normalizedCue = cue.trim().toUpperCase();
  if (!normalizedCue) {
    return "";
  }

  if (["ESCUELA-DEMO", "CUE-DEMO", "123456789"].includes(normalizedCue)) {
    return "Escuela Demo";
  }

  return `Escuela CUE ${normalizedCue}`;
}
