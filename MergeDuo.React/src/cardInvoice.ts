import type { Card, Transaction } from './types';
import { daysInMonth, isoDate } from './utils';

/**
 * For a credit_card+cardId transaction WITHOUT installments, the stored `date`
 * is treated as the purchase date. The invoice that closes on/after the
 * purchase date determines the cash-impact (due) date.
 *
 * Convention:
 *   - purchase date <= closing date: invoice closes in the purchase month
 *   - purchase date > closing date: invoice closes in the next month
 *   - dueDay > closingDay uses the invoice month; otherwise the next month
 */
export function computeInvoiceDueDate(
  purchaseISO: string,
  card: Pick<Card, 'closingDay' | 'dueDay'>,
  installmentOffset = 0,
): string {
  const purchase = new Date(purchaseISO + 'T00:00');
  if (Number.isNaN(purchase.getTime())) return purchaseISO;

  const closeDay = Math.min(card.closingDay, daysInMonth(purchase.getFullYear(), purchase.getMonth()));
  const closingDate = new Date(purchase.getFullYear(), purchase.getMonth(), closeDay);
  const invoiceMonth = purchase.getTime() <= closingDate.getTime()
    ? new Date(purchase.getFullYear(), purchase.getMonth(), 1)
    : new Date(purchase.getFullYear(), purchase.getMonth() + 1, 1);

  const dueBaseMonth = card.dueDay > card.closingDay
    ? invoiceMonth
    : new Date(invoiceMonth.getFullYear(), invoiceMonth.getMonth() + 1, 1);
  const dueDate = new Date(dueBaseMonth.getFullYear(), dueBaseMonth.getMonth() + installmentOffset, 1);
  const totalDays = daysInMonth(dueDate.getFullYear(), dueDate.getMonth());
  const day = Math.min(card.dueDay, totalDays);
  return isoDate(dueDate.getFullYear(), dueDate.getMonth(), day);
}

/**
 * Resolves the cash-impact date for any transaction. Stored date is used
 * unless this is a legacy credit_card+cardId tx without an installment marker
 * and without an explicit purchaseDate (in that case we treat date as
 * purchase date and remap to the invoice due date).
 */
export function effectiveCashDate(tx: Transaction, cards: Card[]): string {
  if (tx.category !== 'credit_card' || !tx.cardId) return tx.date;
  // installments and freshly-added txs already store the due date in `date`
  if (tx.installments) return tx.date;
  if (tx.purchaseDate) return tx.date;
  const card = cards.find((c) => c.id === tx.cardId);
  if (!card) return tx.date;
  return computeInvoiceDueDate(tx.date, card);
}

/**
 * Returns the invoice "month" (year+monthIdx) a credit_card transaction belongs to,
 * based on its cash-impact date.
 */
export function invoiceMonthOf(tx: Transaction, cards: Card[]): { year: number; monthIdx: number } | null {
  if (tx.category !== 'credit_card' || !tx.cardId) return null;
  const iso = effectiveCashDate(tx, cards);
  const d = new Date(iso + 'T00:00');
  if (Number.isNaN(d.getTime())) return null;
  return { year: d.getFullYear(), monthIdx: d.getMonth() };
}

/**
 * Returns the due-date year-month (YYYY-MM) that a purchase date maps to,
 * given the card's closingDay and dueDay. Mirrors the backend logic.
 */
export function invoiceMonthForPurchase(
  purchaseISO: string,
  card: Pick<Card, 'closingDay' | 'dueDay'>,
): string {
  const due = computeInvoiceDueDate(purchaseISO, card, 0);
  return due.slice(0, 7);
}

/**
 * Given a target invoice due-year-month (YYYY-MM), returns a synthetic
 * purchaseDate (ISO YYYY-MM-DD) that — when passed to `computeInvoiceDueDate`
 * — produces a due date inside that same year-month.
 *
 * Strategy: work backwards from dueYM to find the invoice's closing month,
 * then place the purchase on the closingDay of that month (the latest day still
 * inside that window). Mirrors CardInvoiceRules.DueDateForPurchase in the backend.
 */
export function synthesizePurchaseDateForInvoice(
  targetDueYM: string,
  card: Pick<Card, 'closingDay' | 'dueDay'>,
): string {
  const [yearStr, monthStr] = targetDueYM.split('-');
  const dueYear = Number(yearStr);
  const dueMonthIdx = Number(monthStr) - 1; // 0-based

  // The due month is either: invoiceMonth (when dueDay > closingDay)
  // or invoiceMonth + 1 (when dueDay <= closingDay).
  // Invert: if dueDay > closingDay → closeMonthIdx = dueMonthIdx
  //         else                   → closeMonthIdx = dueMonthIdx - 1
  const closeMonthIdx =
    card.dueDay > card.closingDay ? dueMonthIdx : dueMonthIdx - 1;

  // Normalise negative month
  const closeDate = new Date(dueYear, closeMonthIdx, 1);
  const closeYear = closeDate.getFullYear();
  const closeMonth = closeDate.getMonth(); // 0-based

  // Place purchase on closingDay (clamped to month length)
  const day = Math.min(card.closingDay, daysInMonth(closeYear, closeMonth));
  return isoDate(closeYear, closeMonth, day);
}

/**
 * Formats a YYYY-MM string as 'Mai/2026' style (short month + year).
 */
export function formatInvoiceYM(ym: string): string {
  const [y, m] = ym.split('-');
  if (!y || !m) return ym;
  const d = new Date(Number(y), Number(m) - 1, 1);
  return d.toLocaleDateString('pt-BR', { month: 'short', year: 'numeric' })
    .replace('.', '')
    .replace(/^./, (c) => c.toUpperCase());
}

/**
 * Generates N installment transactions (without ids — store assigns them)
 * for a credit-card purchase. All installments share a `groupId`.
 */
export function buildInstallmentTransactions(input: {
  purchaseISO: string;
  card: Card;
  category: 'credit_card';
  description: string;
  totalAmount: number; // total purchase price
  installments: number; // 1..N
  owner?: string;
  groupId: string;
}): Omit<Transaction, 'id'>[] {
  const per = +(input.totalAmount / input.installments).toFixed(2);
  const txs: Omit<Transaction, 'id'>[] = [];
  for (let i = 1; i <= input.installments; i++) {
    const due = computeInvoiceDueDate(input.purchaseISO, input.card, i - 1);
    const description =
      input.installments > 1
        ? `${input.description} (${i}/${input.installments})`
        : input.description;
    // Adjust last installment to absorb rounding
    const amount =
      i === input.installments
        ? +(input.totalAmount - per * (input.installments - 1)).toFixed(2)
        : per;
    txs.push({
      date: due,
      category: 'credit_card',
      description,
      amount,
      owner: input.owner,
      cardId: input.card.id,
      purchaseDate: input.purchaseISO,
      installments:
        input.installments > 1
          ? { index: i, total: input.installments, groupId: input.groupId }
          : undefined,
    });
  }
  return txs;
}
