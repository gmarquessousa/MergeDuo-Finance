import { useMemo } from 'react';
import { combineMonthAggregates } from './api/aggregates';
import { shouldTrustMonthAggregate } from './aggregateTrust';
import {
  aggregateMonthKey,
  aggregateOwnersFor,
  aggregateYearKey,
  useAggregates,
} from './aggregatesStore';
import { transactionCacheKey, useFinance } from './store';

export interface YearAggregateBalance {
  saldo: number;
  investido: number;
  patrimonio: number;
}

export function useYearAggregateBalances(year: number): Map<number, YearAggregateBalance> | null {
  const { currentUser, partner, mergeActive, ownerFilter, transactionLoads } = useFinance();
  const { monthByKey, yearByKey } = useAggregates();

  return useMemo(() => {
    if (!currentUser) return null;

    const owners = aggregateOwnersFor(
      mergeActive ? ownerFilter : 'me',
      currentUser.id,
      partner?.partnerUserId ?? null,
    );
    const primaryYearState = yearByKey[aggregateYearKey(owners.primary, year)];
    const primary = primaryYearState?.data ?? null;
    if (!primary) return null;

    const secondaryYearState = owners.secondary
      ? yearByKey[aggregateYearKey(owners.secondary, year)]
      : null;
    const secondary = secondaryYearState?.data ?? null;
    if (owners.secondary && !secondary) return null;

    const out = new Map<number, YearAggregateBalance>();
    for (const monthA of primary.months) {
      const primaryMonthState = monthByKey[aggregateMonthKey(
        owners.primary,
        monthA.year,
        monthA.month,
      )];
      const secondaryMonthState = owners.secondary
        ? monthByKey[aggregateMonthKey(owners.secondary, monthA.year, monthA.month)]
        : null;
      const monthLoad = transactionLoads[transactionCacheKey({
        yearMonth: monthA.yearMonth,
        owner: ownerFilter,
      })];
      let saldo = monthA.totals.saldo;
      let investido = monthA.totals.investido;
      let isStale = monthA.isStale;

      if (secondary) {
        const monthB = secondary.months.find((month) => month.month === monthA.month);
        if (monthB) {
          const combined = combineMonthAggregates(monthA, monthB);
          saldo = combined.totals.saldo;
          investido = combined.totals.investido;
          isStale = combined.isStale;
        }
      }

      const locallyInvalidated = Boolean(
        primaryYearState?.locallyInvalidated
        || secondaryYearState?.locallyInvalidated
        || primaryMonthState?.locallyInvalidated
        || secondaryMonthState?.locallyInvalidated,
      );

      if (!shouldTrustMonthAggregate(isStale, monthLoad, locallyInvalidated)) {
        continue;
      }

      out.set(monthA.monthIdx, {
        saldo,
        investido,
        patrimonio: saldo + investido,
      });
    }

    return out;
  }, [
    currentUser,
    mergeActive,
    monthByKey,
    ownerFilter,
    partner?.partnerUserId,
    transactionLoads,
    year,
    yearByKey,
  ]);
}
