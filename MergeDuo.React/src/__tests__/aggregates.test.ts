import { describe, expect, it } from 'vitest';
import {
  combineMonthAggregates,
  combineYearAggregates,
  type DailyBalanceResponse,
  type DailyMovementResponse,
  type MonthlyAggregateResponse,
  type YearAggregatesResponse,
} from '../api/aggregates';

function month(year: number, m: number, totals: Partial<MonthlyAggregateResponse['totals']> = {}) {
  return {
    id: `${year}-${m}`,
    userId: 'u',
    year,
    month: m,
    monthIdx: m - 1,
    yearMonth: `${year}-${String(m).padStart(2, '0')}`,
    totals: { entradas: 0, saidas: 0, aportes: 0, saldo: 0, investido: 0, ...totals },
    snapshotToday: null,
    dailyBalances: makeDailyBalances(totals.saldo ?? 0),
    dailyMovements: makeDailyMovements(totals.saldo ?? 0),
    projection: { includesProjected: false, projectedCount: 0, asOfDate: '2026-04-01' },
    byCategory: {},
    byCard: {},
    byOwner: {},
    transactionsCount: 0,
    computedAt: '2026-04-01T00:00:00Z',
    sourceVersion: 2,
    isStale: false,
    source: 'live',
  } as MonthlyAggregateResponse;
}

describe('combineMonthAggregates', () => {
  it('sums totals and snapshotToday across two users', () => {
    const a = month(2026, 4, { entradas: 100, saidas: 30, aportes: 10, saldo: 60, investido: 10 });
    const b = month(2026, 4, { entradas: 50, saidas: 5, aportes: 5, saldo: 40, investido: 5 });
    a.snapshotToday = { saldoHoje: 60, investidoHoje: 10, patrimonioHoje: 70, asOfDate: '2026-04-15' };
    b.snapshotToday = { saldoHoje: 40, investidoHoje: 5, patrimonioHoje: 45, asOfDate: '2026-04-15' };
    const sum = combineMonthAggregates(a, b);
    expect(sum.totals.entradas).toBe(150);
    expect(sum.totals.saidas).toBe(35);
    expect(sum.totals.aportes).toBe(15);
    expect(sum.totals.saldo).toBe(100);
    expect(sum.totals.investido).toBe(15);
    expect(sum.snapshotToday).toEqual({
      saldoHoje: 100,
      investidoHoje: 15,
      patrimonioHoje: 115,
      asOfDate: '2026-04-15',
    });
    expect(sum.dailyBalances).toEqual(makeDailyBalances(100));
    expect(sum.dailyMovements).toEqual([
      ...makeDailyMovements(40),
      ...makeDailyMovements(60),
    ]);
    expect(sum.projection).toEqual({
      includesProjected: false,
      projectedCount: 0,
      asOfDate: '2026-04-01',
    });
    expect(sum.isStale).toBe(false);
    expect(sum.source).toBe('live');
  });

  it('propagates projection metadata across users', () => {
    const a = month(2026, 4);
    const b = month(2026, 4);
    b.projection = { includesProjected: true, projectedCount: 2, asOfDate: '2026-04-15' };

    const sum = combineMonthAggregates(a, b);

    expect(sum.projection).toEqual({
      includesProjected: true,
      projectedCount: 2,
      asOfDate: '2026-04-01',
    });
  });

  it('marks the result stale when either input is stale', () => {
    const a = month(2026, 4);
    const b = { ...month(2026, 4), isStale: true };
    expect(combineMonthAggregates(a, b).isStale).toBe(true);
  });
});

describe('combineYearAggregates', () => {
  it('aggregates totals across both users when secondary is provided', () => {
    const yearA: YearAggregatesResponse = {
      userId: 'a',
      year: 2026,
      months: [
        month(2026, 1, { entradas: 100, saldo: 100 }),
        month(2026, 2, { entradas: 50, saldo: 150 }),
      ],
    };
    const yearB: YearAggregatesResponse = {
      userId: 'b',
      year: 2026,
      months: [
        month(2026, 1, { entradas: 30, saldo: 30 }),
        month(2026, 2, { entradas: 20, saldo: 50 }),
      ],
    };
    const combined = combineYearAggregates(yearA, yearB);
    expect(combined.totals.entradas).toBe(200);
    expect(combined.endOfYearSaldo).toBe(200);
    expect(combined.includesProjected).toBe(false);
  });

  it('propagates annual projected metadata', () => {
    const projected = month(2026, 2, { saldo: 150 });
    projected.projection = { includesProjected: true, projectedCount: 1, asOfDate: '2026-01-15' };
    const yearA: YearAggregatesResponse = {
      userId: 'a',
      year: 2026,
      months: [month(2026, 1, { saldo: 100 }), projected],
    };

    const combined = combineYearAggregates(yearA, null);

    expect(combined.includesProjected).toBe(true);
  });

  it('returns the primary year unchanged when no secondary is provided', () => {
    const yearA: YearAggregatesResponse = {
      userId: 'a',
      year: 2026,
      months: [month(2026, 1, { entradas: 100, saldo: 100 })],
    };
    const combined = combineYearAggregates(yearA, null);
    expect(combined.totals.entradas).toBe(100);
    expect(combined.endOfYearSaldo).toBe(100);
  });
});

function makeDailyBalances(saldo: number): DailyBalanceResponse[] {
  return [1, 2, 3].map((day) => ({ day, saldo }));
}

function makeDailyMovements(amount: number): DailyMovementResponse[] {
  return [
    {
      day: 1,
      id: `tx-${amount}`,
      userId: 'u',
      category: 'income',
      kind: 'in',
      description: `Movement ${amount}`,
      amount,
      cardId: null,
      fixedRuleId: null,
      projected: false,
      purchaseDate: null,
    },
  ];
}
