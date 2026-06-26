import { useMemo } from 'react';
import {
  aggregateMonthKey,
  aggregateOwnersFor,
  aggregateYearKey,
  useAggregates,
  type AggregateLoadStatus,
} from './aggregatesStore';
import { useFinance } from './store';
import {
  combineMonthAggregates,
  combineYearAggregates,
  type MonthlyAggregateResponse,
  type YearAggregatesResponse,
} from './api/aggregates';

export type AggregateSummaryStatus = AggregateLoadStatus | 'updating';

export interface AggregateSummary {
  status: AggregateSummaryStatus;
  error: string | null;
  source: string | null;
  isStale: boolean;
  computedAt: string | null;
  totals: {
    entradas: number;
    saidas: number;
    aportes: number;
    saldo: number;
    investido: number;
  } | null;
  snapshotToday: {
    saldoHoje: number;
    investidoHoje: number;
    patrimonioHoje: number;
  } | null;
  dailyBalances: Array<{
    day: number;
    saldo: number;
  }> | null;
  dailyMovements: Array<{
    day: number;
    id: string;
    userId: string;
    category: string;
    kind: string;
    description: string;
    amount: number;
    cardId: string | null;
    fixedRuleId: string | null;
    projected: boolean;
    purchaseDate: string | null;
  }> | null;
  isProjected: boolean;
  locallyInvalidated: boolean;
}

export function useAggregateMonthSummary(year: number, month: number): AggregateSummary {
  const { currentUser, partner, mergeActive, ownerFilter } = useFinance();
  const { monthByKey } = useAggregates();

  return useMemo(() => {
    if (!currentUser) {
      return emptySummary('idle');
    }

    const owners = aggregateOwnersFor(
      mergeActive ? ownerFilter : 'me',
      currentUser.id,
      partner?.partnerUserId ?? null,
    );
    const primary = monthByKey[aggregateMonthKey(owners.primary, year, month)];
    const secondary = owners.secondary
      ? monthByKey[aggregateMonthKey(owners.secondary, year, month)]
      : null;

    if (!primary) return emptySummary('idle');

    if (primary.status === 'loading' && !primary.data) {
      return emptySummary('loading', primary.locallyInvalidated);
    }
    if (primary.status === 'error' && primary.data) {
      return { ...monthlyToSummary(primary.data, 'error', primary.locallyInvalidated), error: primary.error };
    }

    if (!primary.data) {
      return { ...emptySummary('error', primary.locallyInvalidated), error: primary.error };
    }

    if (owners.secondary) {
      if (!secondary) return emptySummary('loading', primary.locallyInvalidated);
      if (!secondary.data && secondary.status === 'loading') {
        return emptySummary('loading', Boolean(primary.locallyInvalidated || secondary.locallyInvalidated));
      }
      if (secondary.status === 'error' && secondary.data) {
        const combined = combineMonthAggregates(primary.data, secondary.data);
        return {
          ...combinedMonthToSummary(combined, 'error', Boolean(primary.locallyInvalidated || secondary.locallyInvalidated)),
          error: secondary.error ?? primary.error,
        };
      }
      if (!secondary.data) {
        return {
          ...emptySummary('error', Boolean(primary.locallyInvalidated || secondary.locallyInvalidated)),
          error: secondary.error ?? primary.error,
        };
      }
      const combined = combineMonthAggregates(primary.data, secondary.data);
      return combinedMonthToSummary(
        combined,
        primary.status === 'loading' || secondary.status === 'loading' ? 'updating' : 'ready',
        Boolean(primary.locallyInvalidated || secondary.locallyInvalidated),
      );
    }

    return monthlyToSummary(
      primary.data,
      primary.status === 'loading' ? 'updating' : 'ready',
      primary.locallyInvalidated,
    );
  }, [currentUser, partner?.partnerUserId, mergeActive, ownerFilter, monthByKey, year, month]);
}

export interface AggregateYearSummary {
  status: AggregateSummaryStatus;
  error: string | null;
  isStale: boolean;
  source: string | null;
  totals: { entradas: number; saidas: number; aportes: number } | null;
  endOfYear: { saldo: number; investido: number } | null;
  isProjected: boolean;
  locallyInvalidated: boolean;
}

export function useAggregateYearSummary(year: number): AggregateYearSummary {
  const { currentUser, partner, mergeActive, ownerFilter } = useFinance();
  const { yearByKey } = useAggregates();

  return useMemo(() => {
    if (!currentUser) return emptyYear('idle');

    const owners = aggregateOwnersFor(
      mergeActive ? ownerFilter : 'me',
      currentUser.id,
      partner?.partnerUserId ?? null,
    );
    const primary = yearByKey[aggregateYearKey(owners.primary, year)];
    const secondary = owners.secondary
      ? yearByKey[aggregateYearKey(owners.secondary, year)]
      : null;

    if (!primary) return emptyYear('idle');
    if (primary.status === 'loading' && !primary.data) {
      return emptyYear('loading', primary.locallyInvalidated);
    }
    if (primary.status === 'error' && primary.data) {
      const combined = combineYearAggregates(primary.data, null);
      return {
        ...yearToSummary(combined, 'error'),
        error: primary.error,
        locallyInvalidated: Boolean(primary.locallyInvalidated),
      };
    }
    if (!primary.data) return { ...emptyYear('error', primary.locallyInvalidated), error: primary.error };

    let combinedSecondary: YearAggregatesResponse | null = null;
    let status: AggregateSummaryStatus = primary.status === 'loading' ? 'updating' : 'ready';
    let locallyInvalidated = Boolean(primary.locallyInvalidated);
    if (owners.secondary) {
      if (!secondary) return emptyYear('loading', primary.locallyInvalidated);
      if (!secondary.data && secondary.status === 'loading') {
        return emptyYear('loading', Boolean(primary.locallyInvalidated || secondary.locallyInvalidated));
      }
      if (secondary.status === 'error' && secondary.data) {
        combinedSecondary = secondary.data;
        status = 'error';
      } else if (secondary.status === 'loading') {
        status = 'updating';
      }
      if (!secondary.data) {
        return {
          ...emptyYear('error', Boolean(primary.locallyInvalidated || secondary.locallyInvalidated)),
          error: secondary.error ?? primary.error,
        };
      }
      combinedSecondary = secondary.data;
      locallyInvalidated = locallyInvalidated || Boolean(secondary.locallyInvalidated);
    }

    const combined = combineYearAggregates(primary.data, combinedSecondary);
    return {
      ...yearToSummary(combined, status),
      error: status === 'error' ? (secondary?.error ?? primary.error) : null,
      locallyInvalidated,
    };
  }, [currentUser, partner?.partnerUserId, mergeActive, ownerFilter, yearByKey, year]);
}

function monthlyToSummary(
  data: MonthlyAggregateResponse,
  status: AggregateSummaryStatus,
  locallyInvalidated = false,
): AggregateSummary {
  return {
    status,
    error: null,
    source: data.source,
    isStale: data.isStale,
    computedAt: data.computedAt,
    totals: data.totals,
    dailyBalances: data.dailyBalances,
    dailyMovements: data.dailyMovements,
    isProjected: data.projection.includesProjected,
    locallyInvalidated,
    snapshotToday: data.snapshotToday
      ? {
          saldoHoje: data.snapshotToday.saldoHoje,
          investidoHoje: data.snapshotToday.investidoHoje,
          patrimonioHoje: data.snapshotToday.patrimonioHoje,
        }
      : null,
  };
}

function combinedMonthToSummary(
  data: ReturnType<typeof combineMonthAggregates>,
  status: AggregateSummaryStatus,
  locallyInvalidated = false,
): AggregateSummary {
  return {
    status,
    error: null,
    source: data.source,
    isStale: data.isStale,
    computedAt: data.computedAt,
    totals: data.totals,
    dailyBalances: data.dailyBalances,
    dailyMovements: data.dailyMovements,
    isProjected: data.projection.includesProjected,
    locallyInvalidated,
    snapshotToday: data.snapshotToday
      ? {
          saldoHoje: data.snapshotToday.saldoHoje,
          investidoHoje: data.snapshotToday.investidoHoje,
          patrimonioHoje: data.snapshotToday.patrimonioHoje,
        }
      : null,
  };
}

function yearToSummary(
  data: ReturnType<typeof combineYearAggregates>,
  status: AggregateSummaryStatus,
): AggregateYearSummary {
  return {
    status,
    error: null,
    isStale: data.isStale,
    source: data.source,
    totals: data.totals,
    endOfYear: { saldo: data.endOfYearSaldo, investido: data.endOfYearInvestido },
    isProjected: data.includesProjected,
    locallyInvalidated: false,
  };
}

function emptySummary(status: AggregateSummaryStatus, locallyInvalidated = false): AggregateSummary {
  return {
    status,
    error: null,
    source: null,
    isStale: false,
    computedAt: null,
    totals: null,
    snapshotToday: null,
    dailyBalances: null,
    dailyMovements: null,
    isProjected: false,
    locallyInvalidated,
  };
}

function emptyYear(status: AggregateSummaryStatus, locallyInvalidated = false): AggregateYearSummary {
  return {
    status,
    error: null,
    isStale: false,
    source: null,
    totals: null,
    endOfYear: null,
    isProjected: false,
    locallyInvalidated,
  };
}
