import { describe, expect, it } from 'vitest';
import { resolveDailyAuditTrail } from '../dailyAuditTrail';
import type { DailyMovementResponse } from '../api/aggregates';
import type { DayData } from '../useMonthData';
import type { Transaction } from '../types';

describe('resolveDailyAuditTrail', () => {
  it('surfaces aggregate-only partner projections in total-mode details', () => {
    const result = resolveDailyAuditTrail({
      year: 2026,
      monthIdx: 5,
      perDay: [{ day: 1, net: 0, accumulated: 0, investedAccumulated: 0, txs: [] }],
      dailyMovements: [partnerProjectedMovement()],
      currentUser: {
        id: 'me',
        name: 'Ana',
        registeredAt: '2026-01-01T00:00:00Z',
        startingBalance: 0,
      },
      partner: {
        id: 'partnership-1',
        partnershipId: 'partnership-1',
        status: 'active',
        partnerUserId: 'partner',
        name: 'Bruno',
        handle: 'bruno',
        initials: 'BR',
        mergedSince: '2026-01-01',
        startingBalance: 0,
        financialDataAvailable: true,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        endedAt: null,
      },
    });

    expect(result.netByDay.get(1)).toBe(-250);
    expect(result.transactionsByDay.get(1)).toEqual([
      expect.objectContaining({
        id: 'projected_partner_rent_20260601',
        owner: 'Bruno',
        projected: true,
        aggregateOnly: true,
      }),
    ]);
  });

  it('dedupes local projected fixed rules against aggregate projected movements', () => {
    const localProjected: Transaction = {
      id: 'fixed:fxr_salary:2026-06-01',
      date: '2026-06-01',
      purchaseDate: '2026-06-01',
      category: 'income',
      description: 'Salary',
      amount: 3000,
      fixedRuleId: 'fxr_salary',
      projected: true,
    };
    const perDay: DayData[] = [
      { day: 1, net: 3000, accumulated: 3000, investedAccumulated: 0, txs: [localProjected] },
    ];

    const result = resolveDailyAuditTrail({
      year: 2026,
      monthIdx: 5,
      perDay,
      dailyMovements: [
        {
          day: 1,
          id: 'projected_fxr_salary_20260601',
          userId: 'me',
          category: 'income',
          kind: 'in',
          description: 'Salary',
          amount: 3000,
          cardId: null,
          fixedRuleId: 'fxr_salary',
          projected: true,
          purchaseDate: '2026-06-01',
        },
      ],
      currentUser: {
        id: 'me',
        name: 'Ana',
        registeredAt: '2026-01-01T00:00:00Z',
        startingBalance: 0,
      },
      partner: null,
    });

    expect(result.transactionsByDay.get(1)).toHaveLength(1);
    expect(result.netByDay.get(1)).toBe(3000);
  });
});

function partnerProjectedMovement(): DailyMovementResponse {
  return {
    day: 1,
    id: 'projected_partner_rent_20260601',
    userId: 'partner',
    category: 'fixed_expense',
    kind: 'out',
    description: 'Aluguel do parceiro',
    amount: 250,
    cardId: null,
    fixedRuleId: 'fxr_partner_rent',
    projected: true,
    purchaseDate: null,
  };
}