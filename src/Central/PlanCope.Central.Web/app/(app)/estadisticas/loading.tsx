import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <div>
      <Skeleton className="mb-6 h-8 w-44" />
      <div className="overflow-hidden rounded-xl ring-1 ring-foreground/10">
        <div className="flex items-center gap-4 border-b p-4">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="ml-auto h-8 w-28" />
        </div>
        <div className="grid gap-4 p-4">
          <div className="flex flex-wrap gap-3">
            <Skeleton className="h-14 w-40" />
            <Skeleton className="h-14 w-40" />
            <Skeleton className="mt-6 h-8 w-32" />
          </div>
          <Skeleton className="h-40 w-full" />
        </div>
      </div>
    </div>
  );
}
