import { describe, expect, it } from 'vitest';
import {
  buildDailyRunwayMonthRefs,
  resolveDailyRunway,
  resolveDailyRunwayReferenceDate,
} from '../dailyRunway';
import type { AggregateSummary } from '../useAggregateSummary';
import { daysInMonth } from '../utils';

describe('resolveDailyRunway', () => {
  it('anchors the runway to the selected month while clamping the day', () => {
    const result = resolveDailyRunwayReferenceDate(2026, 1, new Date(2026, 4, 31));

    expect(result).toEqual(new Date(2026, 1, 28));
  });

  it('uses the worst projected day so the daily value never drives the user negative', () => {
    const referenceDate = new Date(2026, 4, 7);
    const [previous, current, next1, next2, next3] = buildDailyRunwayMonthRefs(referenceDate, 3);

    const result = resolveDailyRunway({
      referenceDate,
      horizonMonths: 3,
      months: [
        month(previous.year, previous.monthIdx, { investido: 100, saldoByDay: 900 }),
        month(current.year, current.monthIdx, { investido: 100, saldoByDay: 850 }),
        month(next1.year, next1.monthIdx, { investido: 100, saldoByDay: 700 }),
        month(next2.year, next2.monthIdx, { investido: 100, saldoByDay: 650 }),
        month(next3.year, next3.monthIdx, {
          investido: 100,
          saldoByDay: (day) => (day <= 3 ? -450 : 700),
        }),
      ],
    });

    expect(result.ready).toBe(true);
    expect(result.minProjectedTotal).toBe(-350);
    expect(result.minProjectedDate).toBe('2026-08-01');
    expect(result.remainingTotal).toBe(800);
    expect(result.horizonEndDate).toBe('2026-08-06');
    expect(result.horizonDays).toBe(92);
    expect(result.value).toBeCloseTo(-350 / 92);
    expect(result.averagePerDay).toBeCloseTo(800 / 92);
  });

  it('falls back to the last day when no day is worse than the final balance', () => {
    const referenceDate = new Date(2026, 4, 7);
    const [previous, current, next1, next2, next3] = buildDailyRunwayMonthRefs(referenceDate, 3);

    const result = resolveDailyRunway({
      referenceDate,
      horizonMonths: 3,
      months: [
        month(previous.year, previous.monthIdx, { investido: 100, saldoByDay: 900 }),
        month(current.year, current.monthIdx, { investido: 100, saldoByDay: 1000 }),
        month(next1.year, next1.monthIdx, { investido: 100, saldoByDay: 950 }),
        month(next2.year, next2.monthIdx, { investido: 100, saldoByDay: 900 }),
        month(next3.year, next3.monthIdx, { investido: 100, saldoByDay: 850 }),
      ],
    });

    expect(result.ready).toBe(true);
    expect(result.minProjectedTotal).toBe(950);
    expect(result.remainingTotal).toBe(950);
    expect(result.minProjectedDate).toBe('2026-08-01');
    expect(result.horizonEndDate).toBe('2026-08-06');
    expect(result.value).toBeCloseTo(950 / 92);
    expect(result.averagePerDay).toBeCloseTo(950 / 92);
  });

  it('waits until all horizon months have daily balances', () => {
    const referenceDate = new Date(2026, 4, 7);
    const [previous, current, next1, next2, next3] = buildDailyRunwayMonthRefs(referenceDate, 3);

    const result = resolveDailyRunway({
      referenceDate,
      horizonMonths: 3,
      months: [
        month(previous.year, previous.monthIdx, { investido: 100, saldoByDay: 900 }),
        month(current.year, current.monthIdx, { investido: 100, saldoByDay: 850 }),
        month(next1.year, next1.monthIdx, { investido: 100, saldoByDay: 700, includeDailyBalances: false }),
        month(next2.year, next2.monthIdx, { investido: 100, saldoByDay: 650 }),
        month(next3.year, next3.monthIdx, { investido: 100, saldoByDay: 600 }),
      ],
    });

    expect(result.ready).toBe(false);
    expect(result.value).toBeNull();
    expect(result.horizonDays).toBe(92);
  });
});

function month(
  year: number,
  monthIdx: number,
  options: {
    investido: number;
    saldoByDay: number | ((day: number) => number);
    includeDailyBalances?: boolean;
  },
): { year: number; monthIdx: number; summary: AggregateSummary } {
  const totalDays = daysInMonth(year, monthIdx);
  const includeDailyBalances = options.includeDailyBalances ?? true;

  return {
    year,
    monthIdx,
    summary: {
      status: 'ready',
      error: null,
      source: 'stored',
      isStale: false,
      computedAt: '2026-05-01T00:00:00Z',
      totals: {
        entradas: 0,
        saidas: 0,
        aportes: 0,
        saldo: valueForDay(options.saldoByDay, totalDays),
        investido: options.investido,
      },
      snapshotToday: null,
      dailyBalances: includeDailyBalances
        ? Array.from({ length: totalDays }, (_, index) => ({
            day: index + 1,
            saldo: valueForDay(options.saldoByDay, index + 1),
          }))
        : [],
      dailyMovements: [],
      isProjected: false,
      locallyInvalidated: false,
    },
  };
}

function valueForDay(source: number | ((day: number) => number), day: number): number {
  return typeof source === 'function' ? source(day) : source;
}
