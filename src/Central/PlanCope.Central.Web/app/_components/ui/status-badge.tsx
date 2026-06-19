interface StatusBadgeProps {
  status: string;
}

// Mapea el estado del backend (Draft/Review/Approved/Published/Archived) a una etiqueta clara.
const labels: Record<string, { text: string; className: string }> = {
  draft: { text: "Borrador", className: "badge--draft" },
  review: { text: "En revisión", className: "badge--draft" },
  approved: { text: "Aprobado", className: "badge--published" },
  published: { text: "Publicado", className: "badge--published" },
  archived: { text: "Archivado", className: "" }
};

export function StatusBadge({ status }: StatusBadgeProps) {
  const entry = labels[status.toLowerCase()] ?? { text: status, className: "" };
  return <span className={`badge ${entry.className}`}>{entry.text}</span>;
}
