import type { ReactNode } from "react";
import type { Crumb } from "../ui/breadcrumbs";

interface PageHeaderProps {
  title: string;
  description?: string;
  actions?: ReactNode;
  /**
   * @deprecated The app header renders the breadcrumb trail. Kept so pages still
   * passing `eyebrow` keep compiling; the value is intentionally not rendered.
   */
  eyebrow?: string;
  /**
   * @deprecated Navigation location is shown in the app header. Accepted for
   * backward compatibility, not rendered here.
   */
  breadcrumbs?: Crumb[];
}

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-2xl font-semibold tracking-tight text-foreground">{title}</h1>
        {description && <p className="mt-1 max-w-prose text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
