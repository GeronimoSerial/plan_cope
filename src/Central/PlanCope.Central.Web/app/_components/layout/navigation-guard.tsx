"use client";

import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { shouldBlockNavigation } from "../../_lib/navigation-guard";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle
} from "@/components/ui/alert-dialog";

interface NavigationGuardValue {
  /** Registers whether the mounted view has unsaved changes (and whether it is read-only). */
  setDirty: (dirty: boolean, readOnly?: boolean) => void;
  /**
   * Intercepts in-app navigation while dirty and opens the shared "¿Salir sin guardar?" dialog.
   * Returns true when navigation was intercepted, so the caller must not navigate in that case;
   * `onConfirm` runs only when the user chooses "Salir sin guardar".
   */
  intercept: (onConfirm: () => void) => boolean;
}

const NavigationGuardContext = createContext<NavigationGuardValue | null>(null);

// Single dirty-guard for the whole app layout: the builder registers its unsaved state here and
// every in-app link (sidebar, header, breadcrumbs) consults it before navigating. The dialog lives
// here so there is exactly one implementation of the confirmation flow.
export function NavigationGuardProvider({ children }: { children: ReactNode }) {
  const stateRef = useRef({ dirty: false, readOnly: false });
  const [pending, setPending] = useState<(() => void) | null>(null);

  const setDirty = useCallback((dirty: boolean, readOnly = false) => {
    stateRef.current = { dirty, readOnly };
  }, []);

  const intercept = useCallback((onConfirm: () => void) => {
    const { dirty, readOnly } = stateRef.current;
    if (!shouldBlockNavigation(dirty, readOnly)) {
      return false;
    }
    setPending(() => onConfirm);
    return true;
  }, []);

  const value = useMemo<NavigationGuardValue>(() => ({ setDirty, intercept }), [setDirty, intercept]);

  return (
    <NavigationGuardContext.Provider value={value}>
      {children}
      <AlertDialog open={pending !== null} onOpenChange={next => !next && setPending(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Salir sin guardar?</AlertDialogTitle>
            <AlertDialogDescription>Tenés cambios sin guardar. Si salís, se pierden.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Volver a editar</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                const action = pending;
                setPending(null);
                action?.();
              }}
            >
              Salir sin guardar
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </NavigationGuardContext.Provider>
  );
}

export function useNavigationGuard(): NavigationGuardValue {
  const context = useContext(NavigationGuardContext);
  if (!context) {
    throw new Error("useNavigationGuard must be used within a NavigationGuardProvider.");
  }
  return context;
}
