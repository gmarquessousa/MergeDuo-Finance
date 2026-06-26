/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, type ReactNode } from 'react';

export interface RefreshContextValue {
  refreshAll: () => void;
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

export function useRefresh(): RefreshContextValue | null {
  return useContext(RefreshCtx);
}
