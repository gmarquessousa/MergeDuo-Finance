/* eslint-disable react-refresh/only-export-components */
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  type ReactNode,
} from 'react';
import type { MonthlyAggregateResponse, YearAggregatesResponse } from './api/aggregates';
import { useFinance } from './store';
import type { OwnerFilter } from './types';

export type AggregateLoadStatus = 'idle' | 'loading' | 'ready' | 'error';

export interface AggregateLoadState<T> {
  status: AggregateLoadStatus;
  data: T | null;
  error: string | null;
  locallyInvalidated?: boolean;
}

interface AggregatesState {
  monthByKey: Record<string, AggregateLoadState<MonthlyAggregateResponse>>;
  yearByKey: Record<string, AggregateLoadState<YearAggregatesResponse>>;
}

interface PersistedAggregatesState {
  version: number;
  monthByKey: Record<string, AggregateLoadState<MonthlyAggregateResponse>>;
  yearByKey: Record<string, AggregateLoadState<YearAggregatesResponse>>;
}

const AGGREGATES_CACHE_VERSION = 3;
const AGGREGATES_CACHE_PREFIX = 'mergeduo:aggregates';
const AGGREGATES_CACHE_WRITE_DELAY_MS = 150;

function emptyAggregatesState(): AggregatesState {
  return { monthByKey: {}, yearByKey: {} };
}

type Action =
  | { type: 'hydrate'; state: AggregatesState }
  | { type: 'set_month_loading'; key: string }
  | { type: 'set_month'; key: string; data: MonthlyAggregateResponse }
  | { type: 'set_month_error'; key: string; error: string }
  | { type: 'set_year_loading'; key: string }
  | { type: 'set_year'; key: string; data: YearAggregatesResponse }
  | { type: 'set_year_error'; key: string; error: string }
  | { type: 'set_months_from_year'; entries: Array<{ key: string; data: MonthlyAggregateResponse }> }
  | { type: 'invalidate_months'; keys: string[] }
  | { type: 'invalidate_years'; keys: string[] }
  | { type: 'clear' };

function reducer(state: AggregatesState, action: Action): AggregatesState {
  switch (action.type) {
    case 'hydrate':
      return action.state;
    case 'set_month_loading': {
      const previous = state.monthByKey[action.key];
      return {
        ...state,
        monthByKey: {
          ...state.monthByKey,
          [action.key]: {
            status: 'loading',
            data: previous?.data ?? null,
            error: null,
            locallyInvalidated: previous?.locallyInvalidated,
          },
        },
      };
    }
    case 'set_month':
      return {
        ...state,
        monthByKey: {
          ...state.monthByKey,
          [action.key]: { status: 'ready', data: action.data, error: null },
        },
      };
    case 'set_month_error': {
      const previous = state.monthByKey[action.key];
      return {
        ...state,
        monthByKey: {
          ...state.monthByKey,
          [action.key]: {
            status: 'error',
            data: previous?.data ?? null,
            error: action.error,
            locallyInvalidated: previous?.locallyInvalidated,
          },
        },
      };
    }
    case 'set_year_loading': {
      const previous = state.yearByKey[action.key];
      return {
        ...state,
        yearByKey: {
          ...state.yearByKey,
          [action.key]: {
            status: 'loading',
            data: previous?.data ?? null,
            error: null,
            locallyInvalidated: previous?.locallyInvalidated,
          },
        },
      };
    }
    case 'set_year':
      return {
        ...state,
        yearByKey: {
          ...state.yearByKey,
          [action.key]: { status: 'ready', data: action.data, error: null },
        },
      };
    case 'set_year_error': {
      const previous = state.yearByKey[action.key];
      return {
        ...state,
        yearByKey: {
          ...state.yearByKey,
          [action.key]: {
            status: 'error',
            data: previous?.data ?? null,
            error: action.error,
            locallyInvalidated: previous?.locallyInvalidated,
          },
        },
      };
    }
    case 'set_months_from_year': {
      if (action.entries.length === 0) return state;
      const next = { ...state.monthByKey };
      for (const entry of action.entries) {
        const previous = next[entry.key];
        if (shouldKeepExistingMonth(previous, entry.data)) {
          continue;
        }

        next[entry.key] = {
          status: 'ready',
          data: entry.data,
          error: null,
          locallyInvalidated: previous?.locallyInvalidated && entry.data.isStale
            ? true
            : undefined,
        };
      }
      return { ...state, monthByKey: next };
    }
    case 'invalidate_months': {
      if (action.keys.length === 0) return state;
      const next = { ...state.monthByKey };
      for (const key of action.keys) {
        const previous = next[key];
        next[key] = {
          status: previous?.status ?? 'idle',
          data: previous?.data ?? null,
          error: null,
          locallyInvalidated: true,
        };
      }
      return { ...state, monthByKey: next };
    }
    case 'invalidate_years': {
      if (action.keys.length === 0) return state;
      const next = { ...state.yearByKey };
      for (const key of action.keys) {
        const previous = next[key];
        next[key] = {
          status: previous?.status ?? 'idle',
          data: previous?.data ?? null,
          error: null,
          locallyInvalidated: true,
        };
      }
      return { ...state, yearByKey: next };
    }
    case 'clear':
      return { monthByKey: {}, yearByKey: {} };
  }
}

interface Ctx {
  monthByKey: Record<string, AggregateLoadState<MonthlyAggregateResponse>>;
  yearByKey: Record<string, AggregateLoadState<YearAggregatesResponse>>;
  setMonthLoading: (key: string) => void;
  setMonth: (key: string, data: MonthlyAggregateResponse) => void;
  setMonthError: (key: string, error: string) => void;
  setYearLoading: (key: string) => void;
  setYear: (key: string, data: YearAggregatesResponse) => void;
  setYearError: (key: string, error: string) => void;
  setMonthsFromYear: (entries: Array<{ key: string; data: MonthlyAggregateResponse }>) => void;
  invalidateMonths: (keys: string[]) => void;
  invalidateYears: (keys: string[]) => void;
  clearAggregates: () => void;
}

const AggregatesCtx = createContext<Ctx | null>(null);

export function AggregatesProvider({ children }: { children: ReactNode }) {
  const { currentUser } = useFinance();
  const currentUserId = currentUser?.id ?? null;
  const [state, dispatch] = useReducer(reducer, undefined, emptyAggregatesState);
  const pendingPersistRef = useRef<{ userId: string; state: AggregatesState } | null>(null);
  const persistTimeoutRef = useRef<number | null>(null);

  useEffect(() => {
    if (!currentUserId) {
      dispatch({ type: 'hydrate', state: emptyAggregatesState() });
      return;
    }

    dispatch({ type: 'hydrate', state: loadPersistedAggregatesState(currentUserId) });
  }, [currentUserId]);

  const setMonthLoading = useCallback((key: string) =>
    dispatch({ type: 'set_month_loading', key }), []);
  const setMonth = useCallback((key: string, data: MonthlyAggregateResponse) =>
    dispatch({ type: 'set_month', key, data }), []);
  const setMonthError = useCallback((key: string, error: string) =>
    dispatch({ type: 'set_month_error', key, error }), []);
  const setYearLoading = useCallback((key: string) =>
    dispatch({ type: 'set_year_loading', key }), []);
  const setYear = useCallback((key: string, data: YearAggregatesResponse) =>
    dispatch({ type: 'set_year', key, data }), []);
  const setYearError = useCallback((key: string, error: string) =>
    dispatch({ type: 'set_year_error', key, error }), []);
  const setMonthsFromYear = useCallback(
    (entries: Array<{ key: string; data: MonthlyAggregateResponse }>) =>
      dispatch({ type: 'set_months_from_year', entries }),
    [],
  );
  const invalidateMonths = useCallback(
    (keys: string[]) => dispatch({ type: 'invalidate_months', keys: unique(keys) }),
    [],
  );
  const invalidateYears = useCallback(
    (keys: string[]) => dispatch({ type: 'invalidate_years', keys: unique(keys) }),
    [],
  );
  const clearAggregates = useCallback(() => dispatch({ type: 'clear' }), []);

  const flushPendingPersist = useCallback(() => {
    if (persistTimeoutRef.current != null && typeof window !== 'undefined') {
      window.clearTimeout(persistTimeoutRef.current);
      persistTimeoutRef.current = null;
    }

    const pending = pendingPersistRef.current;
    if (!pending || typeof localStorage === 'undefined') return;
    pendingPersistRef.current = null;

    try {
      localStorage.setItem(
        aggregatesCacheKey(pending.userId),
        JSON.stringify(createPersistedAggregatesState(pending.state)),
      );
    } catch {
      // Ignore quota/privacy mode failures and keep the in-memory state.
    }
  }, []);

  useEffect(() => {
    if (!currentUserId || typeof localStorage === 'undefined' || typeof window === 'undefined') {
      flushPendingPersist();
      return;
    }

    pendingPersistRef.current = { userId: currentUserId, state };
    if (persistTimeoutRef.current != null) {
      window.clearTimeout(persistTimeoutRef.current);
    }
    persistTimeoutRef.current = window.setTimeout(() => {
      persistTimeoutRef.current = null;
      flushPendingPersist();
    }, AGGREGATES_CACHE_WRITE_DELAY_MS);
  }, [currentUserId, flushPendingPersist, state]);

  useEffect(() => () => {
    flushPendingPersist();
  }, [flushPendingPersist]);

  const value = useMemo<Ctx>(() => ({
    monthByKey: state.monthByKey,
    yearByKey: state.yearByKey,
    setMonthLoading,
    setMonth,
    setMonthError,
    setYearLoading,
    setYear,
    setYearError,
    setMonthsFromYear,
    invalidateMonths,
    invalidateYears,
    clearAggregates,
  }), [
    state.monthByKey,
    state.yearByKey,
    setMonthLoading,
    setMonth,
    setMonthError,
    setYearLoading,
    setYear,
    setYearError,
    setMonthsFromYear,
    invalidateMonths,
    invalidateYears,
    clearAggregates,
  ]);

  return <AggregatesCtx.Provider value={value}>{children}</AggregatesCtx.Provider>;
}

export function useAggregates() {
  const ctx = useContext(AggregatesCtx);
  if (!ctx) throw new Error('useAggregates must be used within AggregatesProvider');
  return ctx;
}

export function aggregateMonthKey(userId: string, year: number, month: number) {
  return `${userId}|${year}-${String(month).padStart(2, '0')}`;
}

export function aggregateYearKey(userId: string, year: number) {
  return `${userId}|${year}`;
}

export function aggregateOwnersFor(
  ownerFilter: OwnerFilter,
  currentUserId: string,
  partnerUserId: string | null | undefined,
): { primary: string; secondary: string | null } {
  if (ownerFilter === 'partner' && partnerUserId) {
    return { primary: partnerUserId, secondary: null };
  }
  if (ownerFilter === 'both' && partnerUserId) {
    return { primary: currentUserId, secondary: partnerUserId };
  }
  return { primary: currentUserId, secondary: null };
}

function aggregatesCacheKey(userId: string) {
  return `${AGGREGATES_CACHE_PREFIX}:${userId}`;
}

function createPersistedAggregatesState(state: AggregatesState): PersistedAggregatesState {
  return {
    version: AGGREGATES_CACHE_VERSION,
    monthByKey: normalizePersistedMap(state.monthByKey),
    yearByKey: normalizePersistedMap(state.yearByKey),
  };
}

function loadPersistedAggregatesState(userId: string): AggregatesState {
  if (typeof localStorage === 'undefined') return emptyAggregatesState();

  try {
    const raw = localStorage.getItem(aggregatesCacheKey(userId));
    if (!raw) return emptyAggregatesState();

    const parsed = JSON.parse(raw) as unknown;
    return parsePersistedAggregatesState(parsed);
  } catch {
    return emptyAggregatesState();
  }
}

function parsePersistedAggregatesState(value: unknown): AggregatesState {
  if (!value || typeof value !== 'object') return emptyAggregatesState();

  const candidate = value as Partial<PersistedAggregatesState>;
  if (candidate.version !== AGGREGATES_CACHE_VERSION) return emptyAggregatesState();

  return {
    monthByKey: normalizePersistedMap(asRecord<AggregateLoadState<MonthlyAggregateResponse>>(candidate.monthByKey)),
    yearByKey: normalizePersistedMap(asRecord<AggregateLoadState<YearAggregatesResponse>>(candidate.yearByKey)),
  };
}

function normalizePersistedMap<T>(
  entries: Record<string, AggregateLoadState<T>>,
): Record<string, AggregateLoadState<T>> {
  return Object.fromEntries(
    Object.entries(entries)
      .filter(([, entry]) => Boolean(entry?.data))
      .map(([key, entry]) => [key, { status: 'ready', data: entry.data, error: null } satisfies AggregateLoadState<T>]),
  );
}

function asRecord<T>(value: unknown): Record<string, T> {
  return value && typeof value === 'object' ? value as Record<string, T> : {};
}

function unique(values: string[]): string[] {
  return [...new Set(values.filter(Boolean))];
}

function shouldKeepExistingMonth(
  previous: AggregateLoadState<MonthlyAggregateResponse> | undefined,
  incoming: MonthlyAggregateResponse,
): boolean {
  if (!previous?.data) return false;
  if (!incoming.isStale) return false;
  if (previous.locallyInvalidated) return true;
  if (!previous.data.isStale) return true;
  if (incoming.source === 'carried' && previous.data.source !== 'carried') return true;

  const previousComputedAt = Date.parse(previous.data.computedAt ?? '');
  const incomingComputedAt = Date.parse(incoming.computedAt ?? '');
  return Number.isFinite(previousComputedAt)
    && Number.isFinite(incomingComputedAt)
    && previousComputedAt > incomingComputedAt;
}
