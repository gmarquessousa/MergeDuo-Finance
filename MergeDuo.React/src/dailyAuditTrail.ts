import type { DailyMovementResponse } from './api/aggregates';
import { isoDate } from './utils';
import {
  signedAmount,
  type FinanceUser,
  type MergePartnerInfo,
  type Transaction,
  type TransactionCategory,
  type TransactionKind,
} from './types';
import type { DayData } from './useMonthData';

interface ResolveDailyAuditTrailInput {
  year: number;
  monthIdx: number;
  perDay: DayData[];
  dailyMovements: DailyMovementResponse[] | null | undefined;
  currentUser: FinanceUser | null;
  partner: MergePartnerInfo | null;
}

export interface DailyAuditTrailState {
  transactionsByDay: Map<number, Transaction[]>;
  netByDay: Map<number, number>;
}

export function resolveDailyAuditTrail({
  year,
  monthIdx,
  perDay,
  dailyMovements,
  currentUser,
  partner,
}: ResolveDailyAuditTrailInput): DailyAuditTrailState {
  const aggregateByDay = new Map<number, Transaction[]>();

  for (const movement of dailyMovements ?? []) {
    const transaction = toAggregateTransaction(movement, year, monthIdx, currentUser, partner);
    const bucket = aggregateByDay.get(movement.day);
    if (bucket) bucket.push(transaction);
    else aggregateByDay.set(movement.day, [transaction]);
  }

  const transactionsByDay = new Map<number, Transaction[]>();
  const netByDay = new Map<number, number>();

  for (const row of perDay) {
    const aggregateTransactions = aggregateByDay.get(row.day) ?? [];
    if (aggregateTransactions.length === 0) {
      transactionsByDay.set(row.day, row.txs);
      netByDay.set(row.day, row.net);
      continue;
    }

    const merged = mergeTransactions(row.txs, aggregateTransactions);
    transactionsByDay.set(row.day, merged);
    netByDay.set(
      row.day,
      merged.reduce((sum, tx) => sum + signedAmount(tx), 0),
    );
  }

  return { transactionsByDay, netByDay };
}

function mergeTransactions(localTransactions: Transaction[], aggregateTransactions: Transaction[]): Transaction[] {
  const merged: Transaction[] = [];
  const seen = new Set<string>();

  for (const transaction of localTransactions) {
    const key = transactionKey(transaction);
    seen.add(key);
    merged.push(transaction);
  }

  for (const transaction of aggregateTransactions) {
    const key = transactionKey(transaction);
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    merged.push(transaction);
  }

  return merged;
}

function transactionKey(transaction: Pick<Transaction, 'id' | 'userId' | 'fixedRuleId' | 'date' | 'purchaseDate' | 'category' | 'amount'>): string {
  if (transaction.fixedRuleId) {
    return `fixed:${transaction.fixedRuleId}:${transaction.purchaseDate ?? transaction.date}:${transaction.category}:${transaction.amount}`;
  }

  return `id:${transaction.userId ?? ''}:${transaction.id}`;
}

function toAggregateTransaction(
  movement: DailyMovementResponse,
  year: number,
  monthIdx: number,
  currentUser: FinanceUser | null,
  partner: MergePartnerInfo | null,
): Transaction {
  return {
    id: movement.id,
    userId: movement.userId,
    yearMonth: `${year}-${String(monthIdx + 1).padStart(2, '0')}`,
    kind: movement.kind as TransactionKind,
    date: isoDate(year, monthIdx, movement.day),
    purchaseDate: movement.purchaseDate ?? undefined,
    category: movement.category as TransactionCategory,
    description: movement.description,
    amount: movement.amount,
    cardId: movement.cardId ?? undefined,
    fixedRuleId: movement.fixedRuleId ?? undefined,
    projected: movement.projected,
    owner: ownerName(movement.userId, currentUser, partner),
    aggregateOnly: true,
    source: { channel: 'aggregate_daily_movement' },
  };
}

function ownerName(
  userId: string,
  currentUser: FinanceUser | null,
  partner: MergePartnerInfo | null,
): string | undefined {
  if (userId === currentUser?.id) return currentUser.name;
  if (userId === partner?.partnerUserId) return partner.name;
  return undefined;
}