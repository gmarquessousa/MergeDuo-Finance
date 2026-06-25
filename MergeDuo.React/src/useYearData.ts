import { useMemo } from 'react';
import { useFinance } from './store';
import { CATEGORY_META, type Transaction } from './types';
import {
  getUserRegistrationMonth,
  isTransactionOnOrAfterRegistrationMonth,
} from './userRegistration';
import { isoDate } from './utils';
import { effectiveCashDate } from './cardInvoice';
import { isOwnerAllowed } from './useMonthData';
import {
  fixedRuleOccurrenceKey,
  materializeFixedTransactionsForCashMonth,
} from './fixedTransactions';
import type { Card } from './types';

export interface MonthSummary {
  monthIdx: number;
  entradas: number;
  saidas: number;
  aportes: number;
  net: number;
  accumulated: number;
  investedAccumulated: number;
  patrimonio: number;
  txCount: number;
  transactions: Transaction[];
  byCategory: Record<string, number>;
}

export interface YearData {
  months: MonthSummary[];
  yearTotals: {
    entradas: number;
    saidas: number;
    aportes: number;
    byCategory: Record<string, number>;
  };
  totalAcumulado: number;
  totalInvested: number;
  patrimonioTotal: number;
  baseBeforeYear: number;
  investedBeforeYear: number;
  topTransactions: Transaction[];
}

function remapCreditCardTxs(txs: Transaction[], cards: Card[]): Transaction[] {
  return txs.map((tx) => {
    if (tx.category !== 'credit_card' || !tx.cardId) return tx;
    const cashDate = effectiveCashDate(tx, cards);
    return cashDate === tx.date ? tx : { ...tx, date: cashDate };
  });
}

function startingBalanceFor(
  myStarting: number,
  filter: string,
  mergeActive: boolean,
  partnerStarting: number,
) {
  if (!mergeActive) return myStarting;
  if (filter === 'me') return myStarting;
  if (filter === 'partner') return partnerStarting;
  return myStarting + partnerStarting;
}

/**
 * Local fallback derivation of annual summaries from already-fetched
 * transactions. Same caveats as {@link useMonthData}: future FixedRules are
 * projected locally when Aggregates cannot serve a value.
 */
export function useYearData(year: number): YearData {
  const {
    startingBalance,
    transactions,
    cards,
    partner,
    mergeActive,
    ownerFilter,
    currentUser,
    fixedTransactions,
  } = useFinance();

  return useMemo(() => {
    const registration = getUserRegistrationMonth(currentUser?.registeredAt);

    const ownerFiltered = transactions.filter((tx) =>
      isOwnerAllowed(tx, ownerFilter, mergeActive, partner, currentUser),
    );
    const allowedTransactions = ownerFiltered.filter((tx) =>
      isTransactionOnOrAfterRegistrationMonth(tx.date, registration),
    );
    const allowedRemapped = remapCreditCardTxs(allowedTransactions, cards);

    const firstOfYearDate = new Date(year, 0, 1);
    const firstOfYear = firstOfYearDate.getTime();

    const partnerStarting = partner?.startingBalance ?? 0;
    let base = startingBalanceFor(
      startingBalance,
      ownerFilter,
      mergeActive,
      partnerStarting,
    );
    let invested = 0;
    for (const t of allowedRemapped) {
      const d = new Date(t.date + 'T00:00').getTime();
      if (d < firstOfYear) {
        const k = CATEGORY_META[t.category].kind;
        if (k === 'in') base += t.amount;
        else if (k === 'out') base -= t.amount;
        else if (k === 'invest') {
          base -= t.amount;
          invested += t.amount;
        }
      }
    }
    const baseBeforeYear = base;
    const investedBeforeYear = invested;

    let runningCash = baseBeforeYear;
    let runningInvested = investedBeforeYear;
    const months: MonthSummary[] = [];
    const byCategory: Record<string, number> = {};
    let yearEntradas = 0;
    let yearSaidas = 0;
    let yearAportes = 0;

    const firstMonth =
      year === registration.year
        ? registration.monthIdx
        : year > registration.year
          ? 0
          : 12;

    const today = new Date();
    const todayIso = isoDate(today.getFullYear(), today.getMonth(), today.getDate());

    for (let m = firstMonth; m < 12; m++) {
      const monthYm = `${year}-${String(m + 1).padStart(2, '0')}`;
      const isFutureMonth = year > today.getFullYear() || (year === today.getFullYear() && m > today.getMonth());
      const isCurrentMonthForToday = year === today.getFullYear() && m === today.getMonth();

      let monthTransactionsSource: Transaction[] = allowedRemapped;
      if (ownerFilter !== 'partner' && (isFutureMonth || isCurrentMonthForToday)) {
        const existingFixedOccurrences = new Set(
          allowedRemapped
            .filter((tx) => tx.fixedRuleId && (tx.yearMonth === monthYm || tx.date.slice(0, 7) === monthYm))
            .map((tx) => fixedRuleOccurrenceKey(tx))
            .filter((key): key is string => key != null),
        );
        const projected = materializeFixedTransactionsForCashMonth(fixedTransactions, cards, year, m)
          .filter((tx) => {
            const key = fixedRuleOccurrenceKey(tx);
            return key != null && !existingFixedOccurrences.has(key);
          })
          .filter((tx) => !isCurrentMonthForToday || tx.date > todayIso)
          .map((tx) => ({ ...tx, projected: true as const }));
        if (projected.length > 0) {
          monthTransactionsSource = [...allowedRemapped, ...projected];
        }
      }
      const monthTransactionsSourceForMonth = monthTransactionsSource.filter((t) => t.date.slice(0, 7) === monthYm);
      let entradas = 0;
      let saidas = 0;
      let aportes = 0;
      const monthByCategory: Record<string, number> = {};
      for (const t of monthTransactionsSourceForMonth) {
        const k = CATEGORY_META[t.category].kind;
        if (k === 'in') entradas += t.amount;
        else if (k === 'out') saidas += t.amount;
        else aportes += t.amount;
        byCategory[t.category] = (byCategory[t.category] ?? 0) + t.amount;
        monthByCategory[t.category] = (monthByCategory[t.category] ?? 0) + t.amount;
      }
      const net = entradas - saidas - aportes;
      runningCash += net;
      runningInvested += aportes;
      yearEntradas += entradas;
      yearSaidas += saidas;
      yearAportes += aportes;
      months.push({
        monthIdx: m,
        entradas,
        saidas,
        aportes,
        net,
        accumulated: runningCash,
        investedAccumulated: runningInvested,
        patrimonio: runningCash + runningInvested,
        txCount: monthTransactionsSourceForMonth.length,
        transactions: monthTransactionsSourceForMonth,
        byCategory: monthByCategory,
      });
    }

    const topTransactions = months
      .flatMap((month) => month.transactions)
      .filter((t) => CATEGORY_META[t.category].kind === 'out')
      .sort((a, b) => b.amount - a.amount)
      .slice(0, 5);

    return {
      months,
      yearTotals: {
        entradas: yearEntradas,
        saidas: yearSaidas,
        aportes: yearAportes,
        byCategory,
      },
      totalAcumulado: runningCash,
      totalInvested: runningInvested,
      patrimonioTotal: runningCash + runningInvested,
      baseBeforeYear,
      investedBeforeYear,
      topTransactions,
    };
  }, [
    startingBalance,
    transactions,
    cards,
    year,
    partner,
    mergeActive,
    ownerFilter,
    currentUser,
    fixedTransactions,
  ]);
}
