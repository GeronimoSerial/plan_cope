import { Suspense } from "react";
import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getLatestInstaller, isNoInstallerPublishedError, isSessionExpired } from "../../_lib/api/server";
import { buildInstallerDownloadHref } from "../../_lib/installer-download";
import { PageHeader } from "../../_components/layout/page-header";
import { TermLabel } from "../../_components/help/term-hint";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { redirectAfterSessionExpired } from "../../_lib/server/auth-refresh";

export const metadata: Metadata = { title: "Descargas · PlanCope Central" };

async function InstallerCard() {
  let installer;
  try {
    installer = await getLatestInstaller();
  } catch (error) {
    if (isSessionExpired(error)) {
      await redirectAfterSessionExpired("/descargas");
    }
    if (isNoInstallerPublishedError(error)) {
      return <p className="text-sm text-muted-foreground">Todavía no hay un instalador publicado.</p>;
    }
    return (
      <Alert variant="destructive">
        <AlertDescription>No se pudo consultar el instalador. Intentá nuevamente más tarde.</AlertDescription>
      </Alert>
    );
  }

  return (
    <Card className="max-w-md">
      <CardHeader>
        <CardTitle>
          <TermLabel term="instalador">Instalador de escritorio</TermLabel>
        </CardTitle>
      </CardHeader>
      <CardContent className="grid gap-4">
        <p className="inline-flex flex-wrap items-center gap-1 text-sm">
          <TermLabel term="version">
            <span>
              Versión <span className="font-medium">{installer.version}</span>
            </span>
          </TermLabel>
          <span aria-hidden="true">·</span>
          <TermLabel term="canal">
            <span>
              Canal <span className="font-medium">{installer.channel}</span>
            </span>
          </TermLabel>
        </p>
        <div>
          <Button render={<a href={buildInstallerDownloadHref(installer.channel)} download />}>
            Descargar instalador
          </Button>
        </div>
        {installer.sha256 ? (
          <p className="text-xs break-all text-muted-foreground">SHA-256: {installer.sha256}</p>
        ) : null}
      </CardContent>
    </Card>
  );
}

function InstallerCardSkeleton() {
  return (
    <Card className="max-w-md">
      <CardHeader>
        <Skeleton className="h-5 w-44" />
      </CardHeader>
      <CardContent className="grid gap-4">
        <Skeleton className="h-4 w-56" />
        <Skeleton className="h-8 w-44" />
      </CardContent>
    </Card>
  );
}

export default function DescargasPage() {
  return (
    <>
      <PageHeader
        title="Descargas"
        description="Instalador de PlanCope para las escuelas. Descargá el último publicado y activá cada equipo con una clave."
      />
      <Suspense fallback={<InstallerCardSkeleton />}>
        <InstallerCard />
      </Suspense>
    </>
  );
}
