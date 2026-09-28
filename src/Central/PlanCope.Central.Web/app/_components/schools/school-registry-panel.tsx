"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { normalizeCue } from "../../_lib/scope";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { EmptyState } from "../ui/empty-state";
import { StatusBadge } from "../ui/status-badge";
import { TextField } from "../ui/text-field";
import type { SchoolSummary } from "../../_lib/api/server";
import type { UserProfile } from "../../_lib/contracts";
import {
  buildCreateSchoolPayload,
  buildUpdateSchoolPayload,
  canOfferSchoolCreate,
  canOfferSchoolEdit,
  formatAnnex,
  schoolsEmptyStateDescription
} from "./school-mapping";

interface SchoolRegistryPanelProps {
  initialSchools: SchoolSummary[];
  user: UserProfile;
}

const createSchoolSchema = z
  .object({
    cue: z.string().trim().min(1, "El CUE es requerido."),
    code: z.string().trim().min(1, "El código es requerido."),
    name: z.string().trim().min(1, "El nombre es requerido."),
    localityId: z.string().trim().min(1, "La localidad es requerida."),
    annex: z.string().optional()
  })
  .refine(values => normalizeCue(values.cue) !== null, {
    message: "El CUE debe tener exactamente 9 dígitos.",
    path: ["cue"]
  });

type CreateSchoolFormValues = z.input<typeof createSchoolSchema>;
type CreateSchoolValues = z.output<typeof createSchoolSchema>;

const createSchoolDefaults: CreateSchoolFormValues = {
  cue: "",
  code: "",
  name: "",
  localityId: "",
  annex: ""
};

export function SchoolRegistryPanel({ initialSchools, user }: SchoolRegistryPanelProps) {
  const router = useRouter();
  const [schools, setSchools] = useState<SchoolSummary[]>(initialSchools);
  const [formOpen, setFormOpen] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [editTarget, setEditTarget] = useState<SchoolSummary | null>(null);
  const [editName, setEditName] = useState("");
  const [editAnnex, setEditAnnex] = useState("");
  const [editLocalityId, setEditLocalityId] = useState("");
  const [editError, setEditError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const canCreate = canOfferSchoolCreate(user);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting }
  } = useForm<CreateSchoolFormValues, unknown, CreateSchoolValues>({
    resolver: zodResolver(createSchoolSchema),
    defaultValues: createSchoolDefaults
  });

  async function refreshSchools() {
    try {
      const updated = await callCentral<SchoolSummary[]>("admin/schools");
      setSchools(updated);
    } catch {
      router.refresh();
    }
  }

  async function onCreateSubmit(values: CreateSchoolValues) {
    setCreateError(null);
    try {
      await callCentral<SchoolSummary>("admin/schools", {
        method: "POST",
        body: JSON.stringify(buildCreateSchoolPayload(values))
      });
      reset(createSchoolDefaults);
      setFormOpen(false);
      await refreshSchools();
      router.refresh();
    } catch (error) {
      setCreateError(getErrorMessage(error, "No se pudo crear la escuela."));
    }
  }

  function startEdit(school: SchoolSummary) {
    setEditTarget(school);
    setEditName(school.name);
    setEditAnnex(school.annex === null || school.annex === undefined ? "" : String(school.annex));
    setEditLocalityId(school.localityId);
    setEditError(null);
  }

  function cancelEdit() {
    setEditTarget(null);
    setEditName("");
    setEditAnnex("");
    setEditLocalityId("");
    setEditError(null);
  }

  async function submitEdit() {
    if (!editTarget) {
      return;
    }
    setSaving(true);
    setEditError(null);
    try {
      await callCentral<undefined>(`admin/schools/${encodeURIComponent(editTarget.id)}`, {
        method: "PATCH",
        body: JSON.stringify(
          buildUpdateSchoolPayload({ name: editName, localityId: editLocalityId, annex: editAnnex })
        )
      });
      cancelEdit();
      await refreshSchools();
      router.refresh();
    } catch (error) {
      setEditError(getErrorMessage(error, "No se pudo editar la escuela."));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="stack">
      {editTarget && (
        <div className="card">
          <form
            className="card__body stack"
            onSubmit={event => {
              event.preventDefault();
              void submitEdit();
            }}
            noValidate
          >
            <h3>Editar escuela {editTarget.name}</h3>
            <p className="field__hint">El CUE y el código no se pueden modificar.</p>
            <div className="cols-2">
              <div className="field">
                <span className="field-label">CUE</span>
                <span>{editTarget.cue}</span>
              </div>
              <div className="field">
                <span className="field-label">Código</span>
                <span>{editTarget.code}</span>
              </div>
            </div>
            <TextField
              label="Nombre"
              required
              value={editName}
              onChange={event => setEditName(event.target.value)}
            />
            <TextField
              label="Localidad"
              required
              value={editLocalityId}
              onChange={event => setEditLocalityId(event.target.value)}
            />
            <TextField
              type="number"
              label="Anexo (opcional)"
              value={editAnnex}
              onChange={event => setEditAnnex(event.target.value)}
            />
            <div className="row">
              <Button type="submit" disabled={saving}>
                {saving ? "Guardando…" : "Guardar cambios"}
              </Button>
              <Button variant="secondary" onClick={cancelEdit}>
                Cancelar
              </Button>
            </div>
            {editError && <Banner tone="error">{editError}</Banner>}
          </form>
        </div>
      )}

      <div className="card">
        <div className="card__header">
          <h2>Escuelas</h2>
          {canCreate && (
            <Button onClick={() => setFormOpen(open => !open)}>{formOpen ? "Cerrar" : "Nueva escuela"}</Button>
          )}
        </div>
        <div className="card__body stack">
          {formOpen && canCreate && (
            <form className="stack" onSubmit={handleSubmit(onCreateSubmit)} noValidate>
              <div className="cols-2">
                <TextField
                  label="CUE"
                  placeholder="Ej. 180000100"
                  required
                  error={errors.cue?.message}
                  {...register("cue")}
                />
                <TextField
                  label="Código"
                  placeholder="Ej. EESC-0100"
                  required
                  error={errors.code?.message}
                  {...register("code")}
                />
              </div>
              <TextField
                label="Nombre"
                placeholder="Ej. Escuela Nº 12"
                required
                error={errors.name?.message}
                {...register("name")}
              />
              <div className="cols-2">
                <TextField
                  label="Localidad"
                  placeholder="Ej. 02000"
                  required
                  error={errors.localityId?.message}
                  {...register("localityId")}
                />
                <TextField
                  type="number"
                  label="Anexo (opcional)"
                  error={errors.annex?.message}
                  {...register("annex")}
                />
              </div>
              <div className="row">
                <Button type="submit" disabled={isSubmitting}>
                  {isSubmitting ? "Creando…" : "Crear escuela"}
                </Button>
              </div>
            </form>
          )}

          {createError && <Banner tone="error">{createError}</Banner>}

          {schools.length === 0 ? (
            <EmptyState
              title="Aún no hay escuelas"
              description={schoolsEmptyStateDescription(user)}
            />
          ) : (
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr
                  style={{
                    textAlign: "left",
                    color: "var(--text-muted)",
                    fontSize: "12px",
                    textTransform: "uppercase",
                    letterSpacing: "0.05em"
                  }}
                >
                  <th style={{ padding: "0 0 var(--space-2)" }}>CUE</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Código</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Nombre</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Localidad</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Anexo</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Estado</th>
                  <th style={{ padding: "0 0 var(--space-2)" }}>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {schools.map(school => (
                  <tr key={school.id} style={{ borderTop: "1px solid var(--line)" }}>
                    <td
                      style={{
                        padding: "var(--space-3) var(--space-1) var(--space-3) 0",
                        fontFamily: "monospace",
                        fontWeight: 700
                      }}
                    >
                      {school.cue}
                    </td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>{school.code}</td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>{school.name}</td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>{school.localityId}</td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>{formatAnnex(school.annex)}</td>
                    <td style={{ padding: "var(--space-3) var(--space-1)" }}>
                      <StatusBadge status={school.status} />
                    </td>
                    <td style={{ padding: "var(--space-3) 0 var(--space-3) var(--space-1)" }}>
                      {canOfferSchoolEdit(user, school.cue) ? (
                        <Button variant="secondary" size="sm" onClick={() => startEdit(school)}>
                          Editar
                        </Button>
                      ) : (
                        <span className="field__hint">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
}
