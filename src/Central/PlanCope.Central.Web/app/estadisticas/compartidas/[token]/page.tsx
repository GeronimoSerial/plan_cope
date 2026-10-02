import { PublicStatsView } from "./public-stats-view";

export const metadata = { title: "Estadísticas compartidas · PlanCope Central" };

export default async function SharedStatsPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  return <main className="mx-auto grid w-full max-w-5xl gap-6 px-4 py-8 sm:px-6">
    <header className="grid gap-2 border-b pb-5">
      <p className="text-sm font-medium text-muted-foreground">PlanCope Central</p>
      <h1 className="text-2xl font-semibold">Estadísticas compartidas</h1>
      <p className="max-w-prose text-sm text-muted-foreground">Este enlace muestra una copia fija de resultados agregados.</p>
    </header>
    <PublicStatsView token={token} />
  </main>;
}
