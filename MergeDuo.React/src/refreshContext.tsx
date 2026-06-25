/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, type ReactNode } from 'react';

export interface RefreshContextValue {
  /**
   * Re-fetches all data that drives the current view: aggregates for the
   * active period (current user + partner when merged), transactions for
   * the active period, cards, fixed rules, partnership and profile stats.
   * Safe to call multiple times in quick succession; in-flight requests
   * are superseded by sequence counters.
   */
  refreshAll: () => void;
  /** True while at least one refresh is in flight. */
  refreshing: boolean;
}

const RefreshCtx = createContext<RefreshContextValue | null>(null);

export function RefreshProvider({
  value,
  children,
}: {
  value: RefreshContextValue;
  children: ReactNode;
}) {
  return <RefreshCtx.Provider value={value}>{children}</RefreshCtx.Provider>;
}

/**
 * Returns the global refresh hook. Components MAY call `refreshAll()` after
 * any successful mutation to keep the UI in sync without a full reload.
 * Returns `null` when used outside the provider (e.g. tests, auth screen).
 */
export function useRefresh(): RefreshContextValue | null {
  return useContext(RefreshCtx);
}
