// Pure navigation-guard logic shared by the React provider (components/layout/navigation-guard.tsx)
// and its unit tests.

/**
 * In-app navigation must be intercepted only when the current view has unsaved changes and is
 * still editable. A read-only view (a published exam version) never blocks navigation.
 */
export function shouldBlockNavigation(dirty: boolean, readOnly: boolean): boolean {
  return dirty && !readOnly;
}
