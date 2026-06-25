import { describe, expect, it } from 'vitest';
import {
  buildAggregateLoadPlan,
  shouldLoadYearAggregates,
  shouldLoadYearTransactions,
  transactionMonthsForScope,
} from '../appDataScope';

describe('appDataScope', () => {
  it('loads the full year of transactions when the simulator is open from monthly mode', () => {
    const months = transactionMonthsForScope({
      year: 2026,
      monthIdx: 4,
      period: 'monthly',
      screen: 'simulator',
    });

    expect(months).toHaveLength(12);
    expect(months[0]).toBe('2026-01');
    expect(months[11]).toBe('2026-12');
    expect(shouldLoadYearTransactions({ period: 'monthly', screen: 'simulator' })).toBe(true);
    expect(shouldLoadYearAggregates({ period: 'monthly', screen: 'simulator' })).toBe(true);
  });

  it('keeps monthly scope for regular home screens outside annual mode', () => {
    const months = transactionMonthsForScope({
      year: 2026,
      monthIdx: 4,
      period: 'monthly',
      screen: 'home',
    });

    expect(months).toEqual(['2026-05']);
    expect(shouldLoadYearTransactions({ period: 'monthly', screen: 'home' })).toBe(false);
    expect(shouldLoadYearAggregates({ period: 'monthly', screen: 'home' })).toBe(false);
  });

  it('prioritizes the active month and defers runway years on the monthly home screen', () => {
    const plan = buildAggregateLoadPlan({
      year: 2026,
      monthIdx: 4,
      period: 'monthly',
      screen: 'home',
      runwayMonthRefs: [
        { year: 2026, monthIdx: 3 },
        { year: 2026, monthIdx: 4 },
        { year: 2027, monthIdx: 0 },
      ],
    });

    expect(plan).toEqual({
      criticalMonths: [{ year: 2026, monthIdx: 4 }],
      criticalYears: [],
      backgroundYears: [2026, 2027],
    });
  });

  it('prioritizes the active year and defers only extra runway years on annual home', () => {
    const plan = buildAggregateLoadPlan({
      year: 2026,
      monthIdx: 4,
      period: 'annual',
      screen: 'home',
      runwayMonthRefs: [
        { year: 2026, monthIdx: 3 },
        { year: 2026, monthIdx: 11 },
        { year: 2027, monthIdx: 0 },
      ],
    });

    expect(plan).toEqual({
      criticalMonths: [],
      criticalYears: [2026],
      backgroundYears: [2027],
    });
  });

  it('loads the simulator active year without runway background work', () => {
    const plan = buildAggregateLoadPlan({
      year: 2026,
      monthIdx: 4,
      period: 'monthly',
      screen: 'simulator',
      runwayMonthRefs: [
        { year: 2026, monthIdx: 3 },
        { year: 2027, monthIdx: 0 },
      ],
    });

    expect(plan).toEqual({
      criticalMonths: [],
      criticalYears: [2026],
      backgroundYears: [],
    });
  });
});
