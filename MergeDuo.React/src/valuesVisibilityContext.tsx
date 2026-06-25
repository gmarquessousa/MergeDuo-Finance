/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react';

interface ValuesVisibilityContextType {
  hidden: boolean;
  toggle: () => void;
}

const ValuesVisibilityContext = createContext<ValuesVisibilityContextType>({
  hidden: false,
  toggle: () => {},
});

const STORAGE_KEY = 'valuesHidden';

export function ValuesVisibilityProvider({ children }: { children: ReactNode }) {
  const [hidden, setHidden] = useState<boolean>(
    () => localStorage.getItem(STORAGE_KEY) === 'true',
  );

  const toggle = useCallback(() => {
    setHidden((prev) => {
      const next = !prev;
      localStorage.setItem(STORAGE_KEY, String(next));
      return next;
    });
  }, []);

  return (
    <ValuesVisibilityContext.Provider value={{ hidden, toggle }}>
      {children}
    </ValuesVisibilityContext.Provider>
  );
}

export function useValuesHidden(): boolean {
  return useContext(ValuesVisibilityContext).hidden;
}

export function useValuesVisibility() {
  return useContext(ValuesVisibilityContext);
}
