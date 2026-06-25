// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render } from '@testing-library/react';
import { FinanceProvider, transactionCacheKey, useFinance } from '../store';
import { useMonthData, type MonthData } from '../useMonthData';
import type { FixedTransactionRule } from '../types';

afterEach(() => {
  cleanup();
  localStorage.clear();
  vi.useRealTimers();
});

describe('useMonthData fixed rules', () => {
  it('keeps the current-month overdue fixed rule in month-end projection without changing saldoHoje', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-07T12:00:00'));

    let finance: ReturnType<typeof useFinance> | null = null;
    let month: MonthData | null = null;

    function Probe() {
      finance = useFinance();
      month = useMonthData(2026, 4);
      return null;
    }

    render(
      <FinanceProvider>
        <Probe />
      </FinanceProvider>,
    );

    act(() => {
      finance!.setCurrentUserFinance({
        id: 'usr_ana',
        name: 'Ana',
        registeredAt: '2026-01-01T00:00:00Z',
        startingBalance: 1000,
      });
      finance!.setTransactions(
        transactionCacheKey({ yearMonth: '2026-05', owner: 'me' }),
        [],
      );
      finance!.setFixedRules([
        rule({
          amount: 200,
          startsAt: '2026-05-01',
          endsAt: '2026-08-31',
          schedule: { type: 'business_day', ordinal: 1 },
        }),
      ]);
    });

    expect(month).not.toBeNull();
    expect(
      month!.monthTransactions.filter((tx) => tx.projected).map((tx) => tx.date),
    ).toContain('2026-05-01');
    expect(month!.saldoHoje).toBe(1000);
    expect(month!.totalAcumulado).toBe(800);
  });
});

function rule(overrides: Partial<FixedTransactionRule> = {}): FixedTransactionRule {
  return {
    id: 'fxr_test',
    category: 'fixed_expense',
    description: 'Aluguel',
    amount: 2200,
    schedule: { type: 'calendar_day', day: 15 },
    startsAt: '2026-01-01',
    endsAt: null,
    active: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}