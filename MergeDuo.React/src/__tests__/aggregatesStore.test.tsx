// @vitest-environment jsdom
import { act, cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { type MonthlyAggregateResponse, type YearAggregatesResponse } from '../api/aggregates';
import {
  AggregatesProvider,
  aggregateMonthKey,
  aggregateYearKey,
  useAggregates,
} from '../aggregatesStore';
import { FinanceProvider, useFinance } from '../store';
import { useYearAggregateBalances } from '../useYearAggregateBalances';

afterEach(() => {
  cleanup();
  localStorage.clear();
});

describe('AggregatesProvider persistence', () => {
  it('hydrates the last cached aggregate snapshot for the same user', () => {
    let finance: ReturnType<typeof useFinance> | null = null;
    let aggregates: ReturnType<typeof useAggregates> | null = null;

    function Probe() {
      finance = useFinance();
      aggregates = useAggregates();
      return null;
    }

    const user = {
      id: 'user-1',
      name: 'Ana',
      registeredAt: '2026-01-01T00:00:00Z',
      startingBalance: 500,
    };
    const monthKey = aggregateMonthKey(user.id, 2026, 4);
    const yearKey = aggregateYearKey(user.id, 2026);

    const firstRender = render(
      <FinanceProvider>
        <AggregatesProvider>
          <Probe />
        </AggregatesProvider>
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance(user);
    });

    act(() => {
      aggregates!.setMonth(monthKey, monthAggregate(user.id, 2026, 4));
      aggregates!.setYear(yearKey, yearAggregate(user.id, 2026));
    });

    firstRender.unmount();
    finance = null;
    aggregates = null;

    render(
      <FinanceProvider>
        <AggregatesProvider>
          <Probe />
        </AggregatesProvider>
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance(user);
    });

    expect(aggregates!.monthByKey[monthKey]?.status).toBe('ready');
    expect(aggregates!.monthByKey[monthKey]?.data?.totals.saldo).toBe(1200);
    expect(aggregates!.yearByKey[yearKey]?.status).toBe('ready');
    expect(aggregates!.yearByKey[yearKey]?.data?.months).toHaveLength(1);
  });

  it('marks selected cached entries as locally invalidated', () => {
    let finance: ReturnType<typeof useFinance> | null = null;
    let aggregates: ReturnType<typeof useAggregates> | null = null;

    function Probe() {
      finance = useFinance();
      aggregates = useAggregates();
      return null;
    }

    const user = {
      id: 'user-1',
      name: 'Ana',
      registeredAt: '2026-01-01T00:00:00Z',
      startingBalance: 500,
    };
    const monthKey = aggregateMonthKey(user.id, 2026, 4);
    const yearKey = aggregateYearKey(user.id, 2026);

    render(
      <FinanceProvider>
        <AggregatesProvider>
          <Probe />
        </AggregatesProvider>
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance(user);
      aggregates!.setMonth(monthKey, monthAggregate(user.id, 2026, 4));
      aggregates!.setYear(yearKey, yearAggregate(user.id, 2026));
    });

    act(() => {
      aggregates!.invalidateMonths([monthKey]);
      aggregates!.invalidateYears([yearKey]);
    });

    expect(aggregates!.monthByKey[monthKey]?.locallyInvalidated).toBe(true);
    expect(aggregates!.yearByKey[yearKey]?.locallyInvalidated).toBe(true);

    act(() => {
      aggregates!.setMonth(monthKey, monthAggregate(user.id, 2026, 4));
    });

    expect(aggregates!.monthByKey[monthKey]?.locallyInvalidated).toBeUndefined();
  });

  it('does not replace a fresh monthly aggregate with a stale carried month from a year response', async () => {
    let finance: ReturnType<typeof useFinance> | null = null;
    let aggregates: ReturnType<typeof useAggregates> | null = null;

    function Probe() {
      finance = useFinance();
      aggregates = useAggregates();
      return null;
    }

    const user = {
      id: 'user-1',
      name: 'Ana',
      registeredAt: '2026-01-01T00:00:00Z',
      startingBalance: 500,
    };
    const monthKey = aggregateMonthKey(user.id, 2026, 5);

    render(
      <FinanceProvider>
        <AggregatesProvider>
          <Probe />
        </AggregatesProvider>
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance(user);
    });

    await waitFor(() => expect(finance!.currentUser?.id).toBe(user.id));

    act(() => {
      aggregates!.setMonth(monthKey, monthAggregate(user.id, 2026, 5, {
        totals: { entradas: 3000, saidas: 1000, aportes: 200, saldo: 2300, investido: 400 },
        computedAt: '2026-05-07T10:00:00Z',
        isStale: false,
        source: 'recomputed',
      }));
    });

    act(() => {
      aggregates!.setMonthsFromYear([{
        key: monthKey,
        data: monthAggregate(user.id, 2026, 5, {
          totals: { entradas: 0, saidas: 0, aportes: 0, saldo: 500, investido: 0 },
          computedAt: null,
          isStale: true,
          source: 'carried',
        }),
      }]);
    });

    expect(aggregates!.monthByKey[monthKey]?.data?.source).toBe('recomputed');
    expect(aggregates!.monthByKey[monthKey]?.data?.totals.saldo).toBe(2300);
  });

  it('does not expose annual month balances from a locally invalidated month aggregate', async () => {
    let finance: ReturnType<typeof useFinance> | null = null;
    let aggregates: ReturnType<typeof useAggregates> | null = null;
    let balances: ReturnType<typeof useYearAggregateBalances> = null;

    function Probe() {
      finance = useFinance();
      aggregates = useAggregates();
      balances = useYearAggregateBalances(2026);
      return null;
    }

    const user = {
      id: 'user-1',
      name: 'Ana',
      registeredAt: '2026-01-01T00:00:00Z',
      startingBalance: 500,
    };
    const monthKey = aggregateMonthKey(user.id, 2026, 4);
    const yearKey = aggregateYearKey(user.id, 2026);

    render(
      <FinanceProvider>
        <AggregatesProvider>
          <Probe />
        </AggregatesProvider>
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance(user);
    });

    await waitFor(() => expect(finance!.currentUser?.id).toBe(user.id));

    act(() => {
      aggregates!.setMonth(monthKey, monthAggregate(user.id, 2026, 4));
      aggregates!.setYear(yearKey, yearAggregate(user.id, 2026));
    });

    await waitFor(() => expect(balances?.get(3)?.patrimonio).toBe(1500));

    act(() => {
      aggregates!.invalidateMonths([monthKey]);
    });

    expect((balances as Map<number, unknown> | null)?.has(3)).toBe(false);
  });
});

function monthAggregate(
  userId: string,
  year: number,
  month: number,
  overrides: Partial<MonthlyAggregateResponse> = {},
): MonthlyAggregateResponse {
  const base: MonthlyAggregateResponse = {
    id: `${userId}-${year}-${month}`,
    userId,
    year,
    month,
    monthIdx: month - 1,
    yearMonth: `${year}-${String(month).padStart(2, '0')}`,
    totals: { entradas: 2000, saidas: 500, aportes: 300, saldo: 1200, investido: 300 },
    snapshotToday: {
      saldoHoje: 1200,
      investidoHoje: 300,
      patrimonioHoje: 1500,
      asOfDate: `${year}-${String(month).padStart(2, '0')}-20`,
    },
    dailyBalances: [1, 2, 3].map((day) => ({ day, saldo: 1200 })),
    dailyMovements: [],
    projection: {
      includesProjected: false,
      projectedCount: 0,
      asOfDate: `${year}-${String(month).padStart(2, '0')}-01`,
    },
    byCategory: {},
    byCard: {},
    byOwner: {},
    transactionsCount: 12,
    computedAt: `${year}-${String(month).padStart(2, '0')}-20T12:00:00Z`,
    sourceVersion: 1,
    isStale: false,
    source: 'cache',
  };
  return { ...base, ...overrides };
}

function yearAggregate(userId: string, year: number): YearAggregatesResponse {
  return {
    userId,
    year,
    months: [monthAggregate(userId, year, 4)],
  };
}
