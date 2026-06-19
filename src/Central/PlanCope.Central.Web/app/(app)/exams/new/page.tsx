import type { Metadata } from "next";
import { PageHeader } from "../../../_components/layout/page-header";
import { CreateExamForm } from "../../../_components/exams/create-exam-form";

export const metadata: Metadata = { title: "Nuevo examen · PlanCope Central" };

export default function NewExamPage() {
  return (
    <>
      <PageHeader
        eyebrow="Exámenes"
        title="Nuevo examen"
        description="Definí los datos generales. Después vas a poder crear versiones y construir las preguntas."
        breadcrumbs={[
          { label: "Inicio", href: "/dashboard" },
          { label: "Exámenes", href: "/exams" },
          { label: "Nuevo" }
        ]}
      />
      <CreateExamForm />
    </>
  );
}
