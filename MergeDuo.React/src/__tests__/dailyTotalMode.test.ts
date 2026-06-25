import { describe, expect, it } from 'vitest';
import { resolveDailyTotalMode } from '../dailyTotalMode';
import type { AggregateSummary } from '../useAggregateSummary';
import type { DayData } from '../useMonthData';

const perDay: DayData[] = [
  { day: 1, net: 0, accumulated: 1000, investedAccumulated: 0, txs: [] },
  { day: 2, net: 200, accumulated: 1200, investedAccumulated: 0, txs: [] },
  { day: 3, net: -100, accumulated: 1100, investedAccumulated: 0, txs: [] },
];

describe('resolveDailyTotalMode', () => {
  it('uses aggregate daily balances for future months', () => {
    const now = new Date();
    const futureMonth = now.getMonth() === 11 ? 0 : now.getMonth() + 1;
    const futureYear = now.getMonth() === 11 ? now.getFullYear() + 1 : now.getFullYear();

    const result = resolveDailyTotalMode({
      year: futureYear,
      monthIdx: futureMonth,
      perDay,
      totalAcumulado: 1100,
      aggregateSummary: summary({
        dailyBalances: [
          { day: 1, saldo: 1500 },
          { day: 2, saldo: 1700 },
          { day: 3, saldo: 1600 },
        ],
      }),
      canUseAggregateCorrection: true,
    });

    expect(result.ready).toBe(true);
    expect(result.balancesByDay.get(1)).toBe(1500);
    expect(result.balancesByDay.get(3)).toBe(1600);
  });

  it('falls back to legacy correction for past months when no daily balances are available', () => {
    const now = new Date();
    const pastMonth = now.getMonth() === 0 ? 11 : now.getMonth() - 1;
    const pastYear = now.getMonth() === 0 ? now.getFullYear() - 1 : now.getFullYear();

    const result = resolveDailyTotalMode({
      year: pastYear,
      monthIdx: pastMonth,
      perDay,
      totalAcumulado: 1100,
      aggregateSummary: summary({
        totals: {
          entradas: 0,
          saidas: 0,
          aportes: 0,
          saldo: 2100,
          investido: 0,
        },
      }),
      canUseAggregateCorrection: true,
    });

    expect(result.ready).toBe(true);
    expect(result.balancesByDay.get(1)).toBe(2000);
    expect(result.balancesByDay.get(3)).toBe(2100);
  });

  it('keeps future months unavailable when daily balances are missing', () => {
    const now = new Date();
    const futureMonth = now.getMonth() === 11 ? 0 : now.getMonth() + 1;
    const futureYear = now.getMonth() === 11 ? now.getFullYear() + 1 : now.getFullYear();

    const result = resolveDailyTotalMode({
      year: futureYear,
      monthIdx: futureMonth,
      perDay,
      totalAcumulado: 1100,
      aggregateSummary: summary(),
      canUseAggregateCorrection: true,
    });

    expect(result.ready).toBe(false);
    expect(result.balancesByDay.size).toBe(0);
  });
});

function summary(overrides: Partial<AggregateSummary> = {}): AggregateSummary {
  return {
    status: 'ready',
    error: null,
    source: 'stored',
    isStale: false,
    computedAt: '2026-04-01T00:00:00Z',
    totals: null,
    snapshotToday: null,
    dailyBalances: [],
    dailyMovements: [],
    isProjected: false,
    locallyInvalidated: false,
    ...overrides,
  };
}
