import { Skeleton } from "@/components/ui/skeleton";

export default function Loading() {
  return (
    <div>
      <Skeleton className="mb-6 h-8 w-40" />
      <div className="mb-4 flex max-w-md items-end gap-2">
        <div className="grid flex-1 gap-1.5">
          <Skeleton className="h-4 w-16" />
          <Skeleton className="h-8 w-full" />
        </div>
        <Skeleton className="h-8 w-20" />
      </div>
      <div className="overflow-hidden rounded-xl ring-1 ring-foreground/10">
        <div className="flex items-center gap-4 border-b bg-muted/40 p-3">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-4 w-1/3" />
          <Skeleton className="ml-auto h-4 w-16" />
        </div>
        <div className="divide-y">
          {Array.from({ length: 10 }).map((_, index) => (
            <div key={index} className="flex items-center gap-4 p-3">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-4 w-1/3" />
              <Skeleton className="ml-auto h-5 w-16 rounded-4xl" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
