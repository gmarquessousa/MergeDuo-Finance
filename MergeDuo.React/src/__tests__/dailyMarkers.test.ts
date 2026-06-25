import { describe, expect, it } from 'vitest';
import { findLargestExpenseDays } from '../dailyMarkers';
import type { Transaction } from '../types';

function makeTransaction(overrides: Partial<Transaction> = {}): Transaction {
  return {
    id: overrides.id ?? 'tx',
    date: overrides.date ?? '2026-05-01',
    category: overrides.category ?? 'variable_expense',
    description: overrides.description ?? 'Teste',
    amount: overrides.amount ?? 10,
    ...overrides,
  };
}

describe('findLargestExpenseDays', () => {
  it('marca o dia com a maior saída positiva do mês', () => {
    const markedDays = findLargestExpenseDays([
      {
        day: 2,
        transactions: [makeTransaction({ id: 'income-1', category: 'income', amount: 200 })],
      },
      {
        day: 7,
        transactions: [makeTransaction({ id: 'out-1', amount: 40 })],
      },
      {
        day: 11,
        transactions: [makeTransaction({ id: 'out-2', category: 'fixed_expense', amount: 15 })],
      },
      {
        day: 18,
        transactions: [makeTransaction({ id: 'invest-1', category: 'investment', amount: 5 })],
      },
    ]);

    expect([...markedDays]).toEqual([7]);
  });

  it('marca todos os dias empatados na maior saída', () => {
    const markedDays = findLargestExpenseDays([
      {
        day: 4,
        transactions: [makeTransaction({ id: 'out-1', amount: 12 })],
      },
      {
        day: 9,
        transactions: [makeTransaction({ id: 'out-2', category: 'loan', amount: 35 })],
      },
      {
        day: 22,
        transactions: [makeTransaction({ id: 'out-3', amount: 35 })],
      },
    ]);

    expect([...markedDays]).toEqual([9, 22]);
  });
});