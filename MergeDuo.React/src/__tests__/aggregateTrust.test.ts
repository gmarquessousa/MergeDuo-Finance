import { describe, expect, it } from 'vitest';
import {
  hasReadyTransactions,
  hasReadyYearTransactions,
  shouldTrustMonthAggregate,
  shouldTrustYearAggregate,
} from '../aggregateTrust';
import type { TransactionLoadState } from '../store';

const loadingLoad: TransactionLoadState = {
  status: 'loading',
  error: null,
  continuationToken: null,
  itemKeys: [],
};

const readyLoad: TransactionLoadState = {
  status: 'ready',
  error: null,
  continuationToken: null,
  itemKeys: [],
};

describe('aggregateTrust', () => {
  it('detects when a month transaction load is ready', () => {
    expect(hasReadyTransactions(readyLoad)).toBe(true);
    expect(hasReadyTransactions(loadingLoad)).toBe(false);
    expect(hasReadyTransactions(undefined)).toBe(false);
  });

  it('detects when all annual transaction loads are ready', () => {
    expect(hasReadyYearTransactions([readyLoad, readyLoad])).toBe(true);
    expect(hasReadyYearTransactions([readyLoad, loadingLoad])).toBe(false);
    expect(hasReadyYearTransactions([])).toBe(false);
  });

  it('does not trust a stale month aggregate after local transactions are ready', () => {
    expect(shouldTrustMonthAggregate(true, readyLoad)).toBe(false);
    expect(shouldTrustMonthAggregate(true, loadingLoad)).toBe(true);
    expect(shouldTrustMonthAggregate(false, readyLoad)).toBe(true);
  });

  it('never trusts a locally invalidated aggregate', () => {
    expect(shouldTrustMonthAggregate(false, loadingLoad, true)).toBe(false);
    expect(shouldTrustYearAggregate(false, [loadingLoad], true)).toBe(false);
  });

  it('does not trust a stale year aggregate after all local transactions are ready', () => {
    expect(shouldTrustYearAggregate(true, [readyLoad, readyLoad])).toBe(false);
    expect(shouldTrustYearAggregate(true, [readyLoad, loadingLoad])).toBe(true);
    expect(shouldTrustYearAggregate(false, [readyLoad, readyLoad])).toBe(true);
  });
});
