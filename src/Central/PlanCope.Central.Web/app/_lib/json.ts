import { ZodError } from "zod";

export function getErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ZodError) {
    return error.issues[0]?.message ?? fallback;
  }

  return error instanceof Error ? error.message : fallback;
}

export function optionalText(value: string): string | undefined {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

export function nullableText(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

// Extrae el mensaje mas util de una respuesta de error del Central API
// (ProblemDetails / ValidationProblemDetails de ASP.NET Core o texto plano).
export function extractApiError(text: string, status: number): string {
  if (!text) {
    return `El servicio respondio con estado ${status}.`;
  }

  try {
    const payload = JSON.parse(text) as {
      title?: string;
      detail?: string;
      error?: string;
      errors?: Record<string, string[]>;
    };
    const validationErrors = payload.errors ? Object.values(payload.errors).flat() : [];
    return validationErrors[0] ?? payload.detail ?? payload.error ?? payload.title ?? text;
  } catch {
    return text;
  }
}
