import { Skeleton } from "@/components/ui/skeleton";

export default function BuilderLoading() {
  return (
    <div className="grid gap-4">
      <div className="grid gap-3">
        <Skeleton className="h-4 w-56" />
        <div className="flex items-center justify-between gap-3">
          <Skeleton className="h-7 w-64" />
          <div className="flex gap-2">
            <Skeleton className="h-8 w-24" />
            <Skeleton className="h-8 w-24" />
          </div>
        </div>
      </div>

      <Skeleton className="h-8 w-52" />

      <div className="grid gap-4 rounded-xl border p-4">
        <Skeleton className="h-5 w-40" />
        <div className="grid gap-4 sm:grid-cols-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
        <Skeleton className="h-20 w-full" />
      </div>

      <div className="grid gap-4 rounded-xl border p-4">
        <Skeleton className="h-5 w-40" />
        <Skeleton className="h-24 w-full" />
      </div>
    </div>
  );
}
