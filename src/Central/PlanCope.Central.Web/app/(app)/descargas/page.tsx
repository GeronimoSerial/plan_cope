import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getLatestInstaller, isNoInstallerPublishedError, isSessionExpired } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { Banner } from "../../_components/ui/banner";
import { EmptyState } from "../../_components/ui/empty-state";

export const metadata: Metadata = { title: "Descargas · PlanCope Central" };

export default async function DescargasPage() {
  let installer;
  try {
    installer = await getLatestInstaller();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    if (isNoInstallerPublishedError(error)) {
      return (
        <>
          <PageHeader
            eyebrow="Descargas"
            title="Descargá el instalador de escritorio"
            description="El instalador de PlanCope para las computadoras de las escuelas."
          />
          <div className="card">
            <div className="card__body">
              <EmptyState
                title="Todavía no hay un instalador publicado"
                description="Aún no se publicó un instalador para este canal. Intentá nuevamente más tarde."
              />
            </div>
          </div>
        </>
      );
    }
    return (
      <>
        <PageHeader eyebrow="Descargas" title="Descargá el instalador de escritorio" />
        <Banner tone="error">No se pudo consultar el instalador disponible. Intentá nuevamente más tarde.</Banner>
      </>
    );
  }

  return (
    <>
      <PageHeader
        eyebrow="Descargas"
        title="Descargá el instalador de escritorio"
        description="El instalador incluye el padrón cifrado de la provincia. Instalalo solo en la computadora de la escuela."
      />
      <div className="card">
        <div className="card__body">
          <p>
            Versión <strong>{installer.version}</strong> · Canal <strong>{installer.channel}</strong>
          </p>
          <a href={installer.downloadUrl} download className="button">
            Descargar instalador
          </a>
          {installer.sha256 ? (
            <p className="resource-card__meta">
              SHA-256: {installer.sha256}
            </p>
          ) : null}
        </div>
      </div>
    </>
  );
}