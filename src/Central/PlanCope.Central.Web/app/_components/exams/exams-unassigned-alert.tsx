import Link from "next/link";
import { TriangleAlert } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { unassignedVersionsAlert } from "../../_lib/exams/exam-state";

interface ExamsUnassignedAlertProps {
  count: number;
}

export function ExamsUnassignedAlert({ count }: ExamsUnassignedAlertProps) {
  if (count <= 0) {
    return null;
  }

  return (
    <Alert className="mb-4">
      <TriangleAlert />
      <AlertDescription>
        {unassignedVersionsAlert(count)} <Link href="/politicas-legado">Asignar</Link>
      </AlertDescription>
    </Alert>
  );
}
