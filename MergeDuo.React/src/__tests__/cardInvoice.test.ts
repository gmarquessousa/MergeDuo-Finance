import { describe, expect, it } from 'vitest';
import { computeInvoiceDueDate } from '../cardInvoice';

describe('card invoice due date', () => {
  it('uses the invoice month when due day is after closing day', () => {
    const card = { closingDay: 5, dueDay: 25 };

    expect(computeInvoiceDueDate('2026-04-03', card)).toBe('2026-04-25');
    expect(computeInvoiceDueDate('2026-04-06', card)).toBe('2026-05-25');
  });

  it('uses the next month when due day is before closing day', () => {
    const card = { closingDay: 30, dueDay: 5 };

    expect(computeInvoiceDueDate('2026-01-31', card)).toBe('2026-03-05');
    expect(computeInvoiceDueDate('2026-02-28', card)).toBe('2026-03-05');
  });
});
