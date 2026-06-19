import Link from "next/link";
import { StatusBadge } from "../ui/status-badge";
import type { ExamSummary } from "../../_lib/contracts";

interface ExamListProps {
  exams: ExamSummary[];
}

export function ExamList({ exams }: ExamListProps) {
  return (
    <ul className="resource-list" style={{ listStyle: "none", padding: 0, margin: 0 }}>
      {exams.map(exam => (
        <li key={exam.id}>
          <Link href={`/exams/${exam.id}`} className="resource-card">
            <div className="resource-card__title">
              <span>{exam.title}</span>
              <StatusBadge status={exam.status} />
            </div>
            <div className="resource-card__meta">
              {exam.code}
              {exam.subject ? ` · ${exam.subject}` : ""}
              {exam.level ? ` · ${exam.level}` : ""} · {exam.versionCount} versión(es)
            </div>
          </Link>
        </li>
      ))}
    </ul>
  );
}
