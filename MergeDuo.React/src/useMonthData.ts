import { useMemo } from 'react';
import { useFinance } from './store';
import { CATEGORY_META, type FinanceUser, type OwnerFilter, type Transaction } from './types';
import {
  getUserRegistrationMonth,
  isTransactionOnOrAfterRegistrationMonth,
} from './userRegistration';
import { daysInMonth, isoDate } from './utils';
import { effectiveCashDate } from './cardInvoice';
import type { Card, MergePartnerInfo } from './types';
import {
  fixedRuleOccurrenceKey,
  materializeFixedTransactionsForCashMonth,
} from './fixedTransactions';

export interface DayData {
  day: number;
  net: number;
  accumulated: number;
  investedAccumulated: number;
  txs: Transaction[];
}

export interface MonthData {
  perDay: DayData[];
  baseBeforeMonth: number;
  investedBeforeMonth: number;
  totalAcumulado: number;
  totalInvested: number;
  patrimonioTotal: number;
  saldoHoje: number | null;
  investidoHoje: number | null;
  patrimonioHoje: number | null;
  monthTotals: {
    entradas: number;
    saidas: number;
    aportes: number;
    byCategory: Record<string, number>;
  };
  monthTransactions: Transaction[];
}

export function isOwnerAllowed(
  tx: Pick<Transaction, 'owner' | 'userId'>,
  filter: OwnerFilter,
  mergeActive: boolean,
  partner: MergePartnerInfo | null,
  currentUser: FinanceUser | null,
): boolean {
  const isMine = tx.userId
    ? tx.userId === currentUser?.id
    : !tx.owner || tx.owner === currentUser?.name;
  const isPartner = !!partner && (
    tx.userId
      ? tx.userId === partner.partnerUserId
      : tx.owner === partner.name
  );

  if (!mergeActive || !partner) return isMine;
  if (filter === 'me') return isMine;
  if (filter === 'partner') return isPartner;
  return isMine || isPartner;
}

function startingBalanceFor(
  myStarting: number,
  filter: OwnerFilter,
  mergeActive: boolean,
  partner: MergePartnerInfo | null,
): number {
  if (!mergeActive || !partner) return myStarting;
  if (filter === 'me') return myStarting;
  if (filter === 'partner') return partner.startingBalance;
  return myStarting + partner.startingBalance;
}

function remapCreditCardTxs(txs: Transaction[], cards: Card[]): Transaction[] {
  return txs.map((tx) => {
    if (tx.category !== 'credit_card' || !tx.cardId) return tx;
    const cashDate = effectiveCashDate(tx, cards);
    return cashDate === tx.date ? tx : { ...tx, date: cashDate };
  });
}

export function useMonthData(year: number, monthIdx: number): MonthData {
  const {
    startingBalance,
    transactions,
    fixedTransactions,
    cards,
    partner,
    mergeActive,
    ownerFilter,
    currentUser,
  } = useFinance();
  const totalDays = daysInMonth(year, monthIdx);

  return useMemo(() => {
    const registration = getUserRegistrationMonth(currentUser?.registeredAt);

    const ownerFiltered = transactions.filter((tx) =>
      isOwnerAllowed(tx, ownerFilter, mergeActive, partner, currentUser),
    );

    const allowedTransactions = ownerFiltered.filter((tx) =>
      isTransactionOnOrAfterRegistrationMonth(tx.date, registration),
    );

    const allowedRemapped = remapCreditCardTxs(allowedTransactions, cards);

    const firstOfMonthDate = new Date(year, monthIdx, 1);
    const firstOfMonth = firstOfMonthDate.getTime();

    const today = new Date();
    const isFutureMonth =
      year > today.getFullYear() ||
      (year === today.getFullYear() && monthIdx > today.getMonth());
    const isCurrentMonth =
      year === today.getFullYear() && monthIdx === today.getMonth();

    const shouldInjectProjected = ownerFilter !== 'partner' && (isCurrentMonth || isFutureMonth);

    let monthTransactionsSource: Transaction[];
    if (shouldInjectProjected && fixedTransactions.length > 0) {
      const realFixedOccurrences = new Set(
        allowedRemapped
          .filter((tx) => tx.fixedRuleId && (tx.yearMonth === `${year}-${String(monthIdx + 1).padStart(2, '0')}` || tx.date.slice(0, 7) === `${year}-${String(monthIdx + 1).padStart(2, '0')}`))
          .map((tx) => fixedRuleOccurrenceKey(tx))
          .filter((key): key is string => key != null),
      );
      const projected = materializeFixedTransactionsForCashMonth(fixedTransactions, cards, year, monthIdx)
        .filter((tx) => {
          const key = fixedRuleOccurrenceKey(tx);
          return key != null && !realFixedOccurrences.has(key);
        })
        .map((tx) => ({ ...tx, projected: true as const }));
      monthTransactionsSource = [...allowedRemapped, ...projected];
    } else {
      monthTransactionsSource = allowedRemapped;
    }

    const transactionsByDate = groupTransactionsByDate(monthTransactionsSource);
    const actualTransactionsByDate = groupTransactionsByDate(allowedRemapped);

    let baseBeforeMonth = startingBalanceFor(startingBalance, ownerFilter, mergeActive, partner);
    let investedBeforeMonth = 0;
    for (const t of allowedRemapped) {
      const d = new Date(t.date + 'T00:00').getTime();
      if (d < firstOfMonth) {
        const k = CATEGORY_META[t.category].kind;
        if (k === 'in') baseBeforeMonth += t.amount;
        else if (k === 'out') baseBeforeMonth -= t.amount;
        else if (k === 'invest') {
          baseBeforeMonth -= t.amount;
          investedBeforeMonth += t.amount;
        }
      }
    }

    const isCurrentMonthForToday =
      today.getFullYear() === year && today.getMonth() === monthIdx;
    const todayDay = isCurrentMonthForToday ? today.getDate() : null;

    const perDay: DayData[] = [];
    let runningCash = baseBeforeMonth;
    let runningInvested = investedBeforeMonth;
    let runningCashActual = baseBeforeMonth;
    let runningInvestedActual = investedBeforeMonth;
    let saldoHoje: number | null = null;
    let investidoHoje: number | null = null;

    const summarizeDay = (txs: Transaction[]) => {
      let net = 0;
      let dayInvest = 0;
      for (const t of txs) {
        const k = CATEGORY_META[t.category].kind;
        if (k === 'in') net += t.amount;
        else if (k === 'out') net -= t.amount;
        else if (k === 'invest') {
          net -= t.amount;
          dayInvest += t.amount;
        }
      }

      return { net, dayInvest };
    };

    for (let d = 1; d <= totalDays; d++) {
      const iso = isoDate(year, monthIdx, d);
      const txs = transactionsByDate.get(iso) ?? [];
      const actualTxs = actualTransactionsByDate.get(iso) ?? [];
      const { net, dayInvest } = summarizeDay(txs);
      const { net: actualNet, dayInvest: actualDayInvest } = summarizeDay(actualTxs);
      runningCash += net;
      runningInvested += dayInvest;
      runningCashActual += actualNet;
      runningInvestedActual += actualDayInvest;
      perDay.push({
        day: d,
        net,
        accumulated: runningCash,
        investedAccumulated: runningInvested,
        txs,
      });
      if (todayDay != null && d === todayDay) {
        saldoHoje = runningCashActual;
        investidoHoje = runningInvestedActual;
      }
    }

    let entradas = 0;
    let saidas = 0;
    let aportes = 0;
    const byCategory: Record<string, number> = {};
    const monthTransactions: Transaction[] = [];
    for (const t of monthTransactionsSource) {
      const d = new Date(t.date + 'T00:00');
      if (d.getFullYear() !== year || d.getMonth() !== monthIdx) continue;
      monthTransactions.push(t);
      const k = CATEGORY_META[t.category].kind;
      if (k === 'in') entradas += t.amount;
      else if (k === 'out') saidas += t.amount;
      else aportes += t.amount;
      byCategory[t.category] = (byCategory[t.category] ?? 0) + t.amount;
    }

    const totalAcumulado = perDay[perDay.length - 1]?.accumulated ?? baseBeforeMonth;
    const totalInvested =
      perDay[perDay.length - 1]?.investedAccumulated ?? investedBeforeMonth;
    const patrimonioTotal = totalAcumulado + totalInvested;
    const patrimonioHoje =
      saldoHoje != null && investidoHoje != null ? saldoHoje + investidoHoje : null;

    return {
      perDay,
      baseBeforeMonth,
      investedBeforeMonth,
      totalAcumulado,
      totalInvested,
      patrimonioTotal,
      saldoHoje,
      investidoHoje,
      patrimonioHoje,
      monthTotals: { entradas, saidas, aportes, byCategory },
      monthTransactions,
    };
  }, [
    startingBalance,
    transactions,
    fixedTransactions,
    cards,
    year,
    monthIdx,
    totalDays,
    partner,
    mergeActive,
    ownerFilter,
    currentUser,
  ]);
}

function groupTransactionsByDate(transactions: Transaction[]): Map<string, Transaction[]> {
  const grouped = new Map<string, Transaction[]>();
  for (const tx of transactions) {
    const existing = grouped.get(tx.date);
    if (existing) {
      existing.push(tx);
    } else {
      grouped.set(tx.date, [tx]);
    }
  }
  return grouped;
}
