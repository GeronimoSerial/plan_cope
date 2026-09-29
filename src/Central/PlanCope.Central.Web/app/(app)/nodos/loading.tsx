import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <div>
      <Skeleton className="mb-6 h-8 w-56" />
      <div className="overflow-hidden rounded-xl ring-1 ring-foreground/10">
        <div className="flex items-center gap-4 border-b bg-muted/40 p-3">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-4 w-1/4" />
          <Skeleton className="ml-auto h-4 w-20" />
        </div>
        <div className="divide-y">
          {Array.from({ length: 6 }).map((_, index) => (
            <div key={index} className="flex items-center gap-4 p-3">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-4 w-1/4" />
              <Skeleton className="hidden h-4 w-1/5 sm:block" />
              <Skeleton className="ml-auto h-5 w-20 rounded-4xl" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
