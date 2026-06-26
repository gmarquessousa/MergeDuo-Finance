import type { Card, Transaction } from './types';
import { daysInMonth, isoDate } from './utils';

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

export function effectiveCashDate(tx: Transaction, cards: Card[]): string {
  if (tx.category !== 'credit_card' || !tx.cardId) return tx.date;
  if (tx.installments) return tx.date;
  if (tx.purchaseDate) return tx.date;
  const card = cards.find((c) => c.id === tx.cardId);
  if (!card) return tx.date;
  return computeInvoiceDueDate(tx.date, card);
}

export function invoiceMonthOf(tx: Transaction, cards: Card[]): { year: number; monthIdx: number } | null {
  if (tx.category !== 'credit_card' || !tx.cardId) return null;
  const iso = effectiveCashDate(tx, cards);
  const d = new Date(iso + 'T00:00');
  if (Number.isNaN(d.getTime())) return null;
  return { year: d.getFullYear(), monthIdx: d.getMonth() };
}

export function invoiceMonthForPurchase(
  purchaseISO: string,
  card: Pick<Card, 'closingDay' | 'dueDay'>,
): string {
  const due = computeInvoiceDueDate(purchaseISO, card, 0);
  return due.slice(0, 7);
}

export function synthesizePurchaseDateForInvoice(
  targetDueYM: string,
  card: Pick<Card, 'closingDay' | 'dueDay'>,
): string {
  const [yearStr, monthStr] = targetDueYM.split('-');
  const dueYear = Number(yearStr);
  const dueMonthIdx = Number(monthStr) - 1;

  const closeMonthIdx =
    card.dueDay > card.closingDay ? dueMonthIdx : dueMonthIdx - 1;

  const closeDate = new Date(dueYear, closeMonthIdx, 1);
  const closeYear = closeDate.getFullYear();
  const closeMonth = closeDate.getMonth();

  const day = Math.min(card.closingDay, daysInMonth(closeYear, closeMonth));
  return isoDate(closeYear, closeMonth, day);
}

export function formatInvoiceYM(ym: string): string {
  const [y, m] = ym.split('-');
  if (!y || !m) return ym;
  const d = new Date(Number(y), Number(m) - 1, 1);
  return d.toLocaleDateString('pt-BR', { month: 'short', year: 'numeric' })
    .replace('.', '')
    .replace(/^./, (c) => c.toUpperCase());
}

export function buildInstallmentTransactions(input: {
  purchaseISO: string;
  card: Card;
  category: 'credit_card';
  description: string;
  totalAmount: number;
  installments: number;
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
