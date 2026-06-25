import type { TransactionLoadState } from './store';

export function hasReadyTransactions(load?: TransactionLoadState): boolean {
  return load?.status === 'ready';
}

export function hasReadyYearTransactions(
  loads: Array<TransactionLoadState | undefined>,
): boolean {
  return loads.length > 0 && loads.every((load) => load?.status === 'ready');
}

export function shouldTrustMonthAggregate(
  isStale: boolean,
  monthTransactionLoad?: TransactionLoadState,
  locallyInvalidated = false,
): boolean {
  if (locallyInvalidated) return false;
  return !isStale || !hasReadyTransactions(monthTransactionLoad);
}

export function shouldTrustYearAggregate(
  isStale: boolean,
  yearTransactionLoads: Array<TransactionLoadState | undefined>,
  locallyInvalidated = false,
): boolean {
  if (locallyInvalidated) return false;
  return !isStale || !hasReadyYearTransactions(yearTransactionLoads);
}
