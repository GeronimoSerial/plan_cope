import { useCallback, useMemo, useState } from "react";
import type { LocalExamBlock } from "../../shared/api-types";
import {
  type AnswerMap,
  collectAnswers,
  findMissingRequiredAnswers,
  getInitialSessionCode
} from "../domain/examAnswers";
import { StudentApi } from "../studentApi";
import type { ResolvedStudent } from "../types";

export function useStudentExam() {
  const api = useMemo(() => new StudentApi(), []);
  const [sessionCode, setSessionCode] = useState(getInitialSessionCode);
  const [attemptId, setAttemptId] = useState<string | null>(null);
  const [document, setDocument] = useState("");
  const [resolution, setResolution] = useState<{ token: string; student: ResolvedStudent } | null>(null);
  const [blocks, setBlocks] = useState<LocalExamBlock[]>([]);
  const [answers, setAnswers] = useState<AnswerMap>({});
  const [missingRequired, setMissingRequired] = useState<Set<string>>(new Set());
  const [confirmationCode, setConfirmationCode] = useState<string | null>(null);
  const [submittedAt, setSubmittedAt] = useState<string | null>(null);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [isBusy, setIsBusy] = useState(false);

  const runBusy = useCallback(async (action: () => Promise<void>) => {
    setIsBusy(true);
    setError("");
    try {
      await action();
      return true;
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "No se pudo completar la operacion.");
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

  const setAnswer = useCallback((blockId: string, value: string) => {
    setAnswers(current => ({ ...current, [blockId]: value }));
  }, []);

  const resolveStudent = useCallback(async () => {
    if (!sessionCode.trim()) {
      setError("Completa el codigo de examen.");
      return;
    }

    if (!document.trim()) {
      setError("Completa tu DNI.");
      return;
    }

    await runBusy(async () => {
      const response = await api.resolveStudent(sessionCode.trim(), document);
      setResolution({ token: response.resolutionToken, student: response.student });
    });
  }, [api, document, runBusy, sessionCode]);

  const correctIdentity = useCallback(() => {
    setResolution(null);
    setError("");
  }, []);

  const startAttempt = useCallback(async () => {
    if (!sessionCode.trim() || !resolution) {
      return;
    }

    await runBusy(async () => {
      const response = await api.startAttempt(sessionCode.trim(), resolution.token);
      setAttemptId(response.attempt.id);
      setBlocks(response.blocks);
      setAnswers({});
      setMissingRequired(new Set());
      setConfirmationCode(null);
      setSubmittedAt(null);
      setStatus("");
    });
  }, [api, resolution, runBusy, sessionCode]);

  const saveAnswers = useCallback(async () => {
    if (!attemptId || !validateRequired()) {
      return false;
    }

    return runBusy(async () => {
      await api.saveAnswers(attemptId, collectAnswers(blocks, answers));
      setStatus("Respuestas guardadas.");
    });
  }, [answers, api, attemptId, blocks, runBusy, validateRequired]);

  const submitAttempt = useCallback(async () => {
    if (!attemptId || !(await saveAnswers())) {
      return;
    }

    await runBusy(async () => {
      const response = await api.submitAttempt(attemptId);
      setConfirmationCode(response.confirmationCode);
      setSubmittedAt(response.submittedAt);
      setStatus("");
    });
  }, [api, attemptId, runBusy, saveAnswers]);

  return {
    attemptId,
    answers,
    blocks,
    confirmationCode,
    correctIdentity,
    document,
    error,
    isBusy,
    missingRequired,
    resolution,
    sessionCode,
    status,
    submittedAt,
    attemptStudentName: resolution?.student.displayName ?? null,
    resolveStudent,
    saveAnswers,
    setAnswer,
    setDocument,
    setSessionCode,
    startAttempt,
    submitAttempt
  };
}
