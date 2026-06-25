import { CATEGORY_META, type Transaction } from './types';

export interface DayMarkerCandidate {
  day: number;
  transactions: Transaction[];
}

export function dayOutflowTotal(transactions: Transaction[]): number {
  return transactions.reduce(
    (sum, tx) => (CATEGORY_META[tx.category].kind === 'out' ? sum + tx.amount : sum),
    0,
  );
}

export function findLargestExpenseDays(days: DayMarkerCandidate[]): Set<number> {
  const markedDays = new Set<number>();
  let largestExpense: number | null = null;

  for (const day of days) {
    const outflowTotal = dayOutflowTotal(day.transactions);
    if (outflowTotal <= 0) continue;

    if (largestExpense == null || outflowTotal > largestExpense) {
      largestExpense = outflowTotal;
      markedDays.clear();
      markedDays.add(day.day);
      continue;
    }

    if (outflowTotal === largestExpense) {
      markedDays.add(day.day);
    }
  }

  return markedDays;
}