import { Suspense } from "react";
import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getLatestInstaller, isNoInstallerPublishedError, isSessionExpired } from "../../_lib/api/server";
import { buildInstallerDownloadHref } from "../../_lib/installer-download";
import { PageHeader } from "../../_components/layout/page-header";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

export const metadata: Metadata = { title: "Descargas · PlanCope Central" };

async function InstallerCard() {
  let installer;
  try {
    installer = await getLatestInstaller();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
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
        <CardTitle>Instalador de escritorio</CardTitle>
      </CardHeader>
      <CardContent className="grid gap-4">
        <p className="text-sm">
          Versión <span className="font-medium">{installer.version}</span> · Canal{" "}
          <span className="font-medium">{installer.channel}</span>
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
      <PageHeader title="Descargas" />
      <Suspense fallback={<InstallerCardSkeleton />}>
        <InstallerCard />
      </Suspense>
    </>
  );
}
