import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { LocalExamBlock } from "../../shared/api-types";
import { type AnswerMap, findMissingRequiredAnswers, getInitialSessionCode } from "../domain/examAnswers";
import { ResumeCredentialError, StudentApi, StudentNotFoundError } from "../studentApi";
import type { ResolvedStudent, StudentAttempt } from "../types";

const STORAGE_KEY = "plancope.student.exam.v1";
const AUTOSAVE_DELAY_MS = 500;
type PendingAnswer = { blockId: string; answer: string | null; revision: number };
type StoredExam = {
  attemptId: string;
  deliverySessionId: string;
  sessionCode: string;
  credential: string;
  expiresAt: string;
  nextRevision: number;
  pending: PendingAnswer[];
};

function readStoredExam(): StoredExam | null {
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const value = JSON.parse(raw) as StoredExam;
    if (!value.sessionCode || !value.credential || !Array.isArray(value.pending)) return null;
    return value;
  } catch {
    return null;
  }
}

function writeStoredExam(value: StoredExam | null): void {
  try {
    if (value) window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    else window.sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // The visible status remains pending; the next edit/save will retry the small session buffer.
  }
}

export function useStudentExam() {
  const api = useMemo(() => new StudentApi(), []);
  const initialStored = useMemo(readStoredExam, []);
  const storedRef = useRef<StoredExam | null>(initialStored);
  const flushRef = useRef<() => Promise<boolean>>(async () => false);
  const serialRef = useRef<Promise<unknown>>(Promise.resolve());
  const [sessionCode, setSessionCode] = useState(initialStored?.sessionCode ?? getInitialSessionCode);
  const [attemptId, setAttemptId] = useState<string | null>(null);
  const [attempt, setAttempt] = useState<StudentAttempt | null>(null);
  const [document, setDocument] = useState("");
  const [resolution, setResolution] = useState<{ token: string; student: ResolvedStudent } | null>(null);
  const [blocks, setBlocks] = useState<LocalExamBlock[]>([]);
  const [answers, setAnswers] = useState<AnswerMap>({});
  const [missingRequired, setMissingRequired] = useState<Set<string>>(new Set());
  const [confirmationCode, setConfirmationCode] = useState<string | null>(null);
  const [submittedAt, setSubmittedAt] = useState<string | null>(null);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [notFoundPrompt, setNotFoundPrompt] = useState<{ message: string; hint: string } | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const [sessionStatus, setSessionStatus] = useState("active");
  const [isRestoring, setIsRestoring] = useState(Boolean(initialStored));
  const [restoreRetry, setRestoreRetry] = useState(0);

  useEffect(() => {
    const stored = storedRef.current;
    if (!stored) return;
    let cancelled = false;
    let retrying = false;
    void (async () => {
      try {
        const restored = await api.restoreAttempt(stored.attemptId, stored.sessionCode, stored.credential);
        if (cancelled) return;
        stored.attemptId = restored.attempt.id;
        stored.deliverySessionId = restored.attempt.deliverySessionId;
        writeStoredExam(stored);
        const restoredAnswers: AnswerMap = {};
        for (const item of restored.answers) restoredAnswers[item.blockId] = typeof item.answer === "string" ? item.answer : JSON.stringify(item.answer);
        for (const pending of stored.pending) restoredAnswers[pending.blockId] = pending.answer ?? "";
        setAttemptId(restored.attempt.id);
        setAttempt(restored.attempt);
        setBlocks(restored.blocks);
        setSessionStatus(restored.sessionStatus);
        setResolution(restored.attempt.studentFirstName || restored.attempt.studentLastName ? {
          token: "",
          student: {
            firstName: restored.attempt.studentFirstName ?? "",
            lastName: restored.attempt.studentLastName ?? "",
            displayName: [restored.attempt.studentLastName, restored.attempt.studentFirstName].filter(Boolean).join(", "),
            maskedDocument: restored.attempt.documentLast4 ? `**.***.${restored.attempt.documentLast4}` : ""
          }
        } : null);
        setAnswers(restoredAnswers);
        setStatus(stored.pending.length ? "Pendiente por conexión." : "");
      } catch (exception) {
        if (!cancelled) {
          if (exception instanceof ResumeCredentialError && (exception.status === 401 || exception.status === 410)) {
            writeStoredExam(null);
            storedRef.current = null;
            setAttemptId(null);
            setAttempt(null);
            setBlocks([]);
            setAnswers({});
            setResolution(null);
            setStatus("");
            setError("La sesión de examen venció. Volvé a ingresar tu DNI para continuar.");
            return;
          }
          retrying = true;
          setError("");
          setStatus("Sin conexión. Tus cambios siguen en esta pestaña; reintentando recuperación…");
          window.setTimeout(() => { if (!cancelled) setRestoreRetry(value => value + 1); }, 3000);
        }
      } finally {
        if (!cancelled && !retrying) setIsRestoring(false);
      }
    })();
    return () => { cancelled = true; };
  }, [api, restoreRetry]);

  useEffect(() => {
    if (!attemptId || confirmationCode || isRestoring || !sessionCode.trim()) return;
    let cancelled = false;
    let timeout: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const session = await api.getSessionStatus(sessionCode.trim());
        if (!cancelled) setSessionStatus(session.status);
        if (session.status === "closed") {
          writeStoredExam(null);
          storedRef.current = null;
          return;
        }
      } catch { /* Retry while the student is taking the exam. */ }
      if (!cancelled) timeout = setTimeout(poll, 2500);
    };
    void poll();
    return () => { cancelled = true; clearTimeout(timeout); };
  }, [api, attemptId, confirmationCode, isRestoring, sessionCode]);

  const runBusy = useCallback(async (action: () => Promise<void>) => {
    setIsBusy(true);
    setError("");
    try {
      await action();
      return true;
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "No se pudo completar la operación.");
      return false;
    } finally {
      setIsBusy(false);
    }
  }, []);

  const validateRequired = useCallback(() => {
    const missing = findMissingRequiredAnswers(blocks, answers);
    setMissingRequired(missing);
    if (missing.size > 0) {
      setError("Completa las respuestas obligatorias antes de continuar.");
      return false;
    }
    setError("");
    return true;
  }, [answers, blocks]);

  const flushPending = useCallback(async () => {
    const stored = storedRef.current;
    if (!stored) return false;
    const serial = serialRef.current.then(async () => {
      const pending = [...stored.pending].sort((a, b) => a.revision - b.revision);
      for (const entry of pending) {
        await api.saveAnswers(stored.attemptId, stored.credential, entry.revision, [{ blockId: entry.blockId, answer: entry.answer }]);
        stored.pending = stored.pending.filter(item => item.revision !== entry.revision);
        writeStoredExam(stored);
      }
    });
    serialRef.current = serial.catch(() => undefined);
    try {
      await serial;
      setStatus("Respuestas guardadas.");
      return true;
    } catch {
      setStatus("Pendiente por conexión.");
      return false;
    }
  }, [api]);
  flushRef.current = flushPending;

  useEffect(() => {
    if (!attemptId || isRestoring || !storedRef.current?.pending.length) return;
    const timeout = setTimeout(() => { void flushRef.current(); }, AUTOSAVE_DELAY_MS);
    return () => clearTimeout(timeout);
  }, [answers, attemptId, isRestoring]);

  useEffect(() => {
    if (!attemptId) return;
    const retryPending = () => { void flushRef.current(); };
    window.addEventListener("online", retryPending);
    return () => window.removeEventListener("online", retryPending);
  }, [attemptId]);

  const setAnswer = useCallback((blockId: string, value: string) => {
    setAnswers(current => ({ ...current, [blockId]: value }));
    const stored = storedRef.current;
    if (!stored) return;
    const revision = ++stored.nextRevision;
    stored.pending = [...stored.pending.filter(entry => entry.blockId !== blockId), { blockId, answer: value, revision }];
    writeStoredExam(stored);
    setStatus("Guardando…");
  }, []);

  const beginAttempt = useCallback(async (code: string, resolutionToken?: string) => {
    let stored = storedRef.current;
    let credential: string;
    if (!stored || stored.sessionCode !== code || stored.attemptId) {
      const bytes = crypto.getRandomValues(new Uint8Array(32));
      credential = btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
      stored = {
        attemptId: "",
        deliverySessionId: "",
        sessionCode: code,
        credential,
        expiresAt: "",
        nextRevision: 0,
        pending: []
      };
    } else {
      credential = stored.credential;
    }
    storedRef.current = stored;
    writeStoredExam(stored);
    const response = await api.startAttempt(code, resolutionToken, credential);
    stored.attemptId = response.attempt.id;
    stored.deliverySessionId = response.attempt.deliverySessionId;
    stored.expiresAt = response.credentialExpiresAt;
    writeStoredExam(stored);
    setAttemptId(response.attempt.id);
    setAttempt(response.attempt);
    setBlocks(response.blocks);
    setAnswers({});
    setMissingRequired(new Set());
    setConfirmationCode(null);
    setSubmittedAt(null);
    setStatus("");
    setSessionStatus("active");
  }, [api]);

  const resolveStudent = useCallback(async () => {
    setNotFoundPrompt(null);
    if (!sessionCode.trim()) { setError("Completá el código de sesión."); return; }
    await runBusy(async () => {
      const session = await api.getSessionStatus(sessionCode.trim());
      if (!session.rosterSnapshotId || !session.rosterSectionId) {
        await beginAttempt(sessionCode.trim());
        return;
      }
      if (!document.trim()) throw new Error("Completa tu DNI.");
      try {
        const response = await api.resolveStudent(sessionCode.trim(), document);
        setResolution({ token: response.resolutionToken, student: response.student });
      } catch (exception) {
        if (exception instanceof StudentNotFoundError) {
          setNotFoundPrompt({ message: exception.message, hint: exception.hint });
          return;
        }
        throw exception;
      }
    });
  }, [api, beginAttempt, document, runBusy, sessionCode]);

  const correctIdentity = useCallback(() => {
    writeStoredExam(null);
    storedRef.current = null;
    setResolution(null);
    setError("");
    setNotFoundPrompt(null);
  }, []);

  const startAttempt = useCallback(async () => {
    if (!sessionCode.trim() || !resolution) return;
    await runBusy(() => beginAttempt(sessionCode.trim(), resolution.token));
  }, [beginAttempt, resolution, runBusy, sessionCode]);

  const saveAnswers = useCallback(async () => {
    if (!attemptId || !validateRequired()) return false;
    return runBusy(async () => {
      if (!await flushPending()) throw new Error("No se pudo confirmar el guardado. Las respuestas siguen pendientes por conexión.");
    });
  }, [attemptId, flushPending, runBusy, validateRequired]);

  const submitAttempt = useCallback(async () => {
    if (!attemptId || !validateRequired()) return;
    await runBusy(async () => {
      const stored = storedRef.current;
      if (!stored) throw new Error("La sesión de examen no está disponible.");
      if (!await flushPending()) throw new Error("Conectate para guardar las respuestas pendientes antes de entregar.");
      await serialRef.current;
      const response = await api.submitAttempt(attemptId, stored.credential);
      setConfirmationCode(response.confirmationCode);
      setSubmittedAt(response.submittedAt);
      setStatus("");
      writeStoredExam(null);
      storedRef.current = null;
    });
  }, [api, attemptId, flushPending, runBusy, validateRequired]);

  return {
    attemptId,
    answers,
    blocks,
    confirmationCode,
    correctIdentity,
    document,
    error: isRestoring ? "" : error,
    isBusy: isBusy || isRestoring,
    missingRequired,
    notFoundPrompt,
    resolution,
    sessionCode,
    status: isRestoring ? "Recuperando tu examen…" : status,
    sessionStatus,
    submittedAt,
    attemptStudentName: resolution?.student.displayName ?? (attempt?.studentLastName || attempt?.studentFirstName ? [attempt.studentLastName, attempt.studentFirstName].filter(Boolean).join(", ") : null),
    resolveStudent,
    saveAnswers,
    setAnswer,
    setDocument,
    setSessionCode,
    startAttempt,
    submitAttempt
  };
}
