import type {
  Card,
  FixedTransactionRule,
  FixedTransactionSchedule,
  Transaction,
} from './types';
import { daysInMonth, isoDate } from './utils';
import { computeInvoiceDueDate } from './cardInvoice';

function monthStart(year: number, monthIdx: number) {
  return new Date(year, monthIdx, 1);
}

function dateOnly(value: Date) {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate());
}

function parseIsoDate(value?: string | null) {
  if (!value) return null;

  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;

  const year = Number(match[1]);
  const monthIdx = Number(match[2]) - 1;
  const day = Number(match[3]);
  const date = new Date(year, monthIdx, day);

  if (
    date.getFullYear() !== year ||
    date.getMonth() !== monthIdx ||
    date.getDate() !== day
  ) {
    return null;
  }

  return date;
}

function isBusinessDay(date: Date) {
  const day = date.getDay();
  return day !== 0 && day !== 6;
}

function nthBusinessDay(year: number, monthIdx: number, ordinal: number) {
  const totalDays = daysInMonth(year, monthIdx);
  let count = 0;
  let lastBusinessDay = 1;

  for (let day = 1; day <= totalDays; day++) {
    const date = new Date(year, monthIdx, day);
    if (!isBusinessDay(date)) continue;
    count++;
    lastBusinessDay = day;
    if (count === ordinal) return day;
  }

  return lastBusinessDay;
}

export function resolveFixedTransactionDay(
  schedule: FixedTransactionSchedule,
  year: number,
  monthIdx: number,
) {
  if (schedule.type === 'calendar_day') {
    return Math.min(schedule.day, daysInMonth(year, monthIdx));
  }

  if (schedule.type === 'business_day') {
    return nthBusinessDay(year, monthIdx, schedule.ordinal);
  }

  if (schedule.period === 'start') return 1;
  if (schedule.period === 'middle') return Math.min(15, daysInMonth(year, monthIdx));
  return daysInMonth(year, monthIdx);
}

function ruleAppliesToOccurrence(rule: FixedTransactionRule, occurrence: Date) {
  const startsAt = parseIsoDate(rule.startsAt);
  if (startsAt && occurrence.getTime() < startsAt.getTime()) {
    return false;
  }

  const endsAt = parseIsoDate(rule.endsAt);
  if (endsAt && occurrence.getTime() > endsAt.getTime()) {
    return false;
  }

  return true;
}

export function materializeFixedTransactionsForMonth(
  rules: FixedTransactionRule[],
  year: number,
  monthIdx: number,
): Transaction[] {
  return rules.flatMap((rule) => {
    if (!rule.active) return [];

    const day = resolveFixedTransactionDay(rule.schedule, year, monthIdx);
    const occurrence = new Date(year, monthIdx, day);
    if (!ruleAppliesToOccurrence(rule, occurrence)) return [];

    const date = isoDate(year, monthIdx, day);
    return [{
      id: `fixed:${rule.id}:${date}`,
      date,
      category: rule.category,
      description: rule.description,
      amount: rule.amount,
      fixedRuleId: rule.id,
      cardId: rule.cardId ?? undefined,
      tags: rule.tags ?? [],
    }];
  });
}

export function fixedRuleOccurrenceKey(tx: Pick<Transaction, 'fixedRuleId' | 'date' | 'purchaseDate'>) {
  if (!tx.fixedRuleId) return null;
  return `${tx.fixedRuleId}|${tx.purchaseDate ?? tx.date}`;
}

export function materializeFixedTransactionsForCashMonth(
  rules: FixedTransactionRule[],
  cards: Card[],
  year: number,
  monthIdx: number,
): Transaction[] {
  const projected: Transaction[] = [];
  const targetStart = new Date(year, monthIdx, 1);

  for (let offset = -2; offset <= 0; offset++) {
    const candidate = new Date(targetStart.getFullYear(), targetStart.getMonth() + offset, 1);
    const occurrences = materializeFixedTransactionsForMonth(
      rules,
      candidate.getFullYear(),
      candidate.getMonth(),
    );

    for (const tx of occurrences) {
      if (tx.category !== 'credit_card') {
        if (candidate.getFullYear() === year && candidate.getMonth() === monthIdx) {
          projected.push(tx);
        }
        continue;
      }

      if (!tx.cardId) continue;
      const card = cards.find((item) => item.id === tx.cardId);
      if (!card) continue;

      const dueDate = computeInvoiceDueDate(tx.date, card);
      const due = new Date(`${dueDate}T00:00`);
      if (due.getFullYear() !== year || due.getMonth() !== monthIdx) {
        continue;
      }

      projected.push({
        ...tx,
        date: dueDate,
        purchaseDate: tx.date,
      });
    }
  }

  return projected;
}

export function materializeFixedTransactionsBefore(
  rules: FixedTransactionRule[],
  cutoff: Date,
): Transaction[] {
  const cutoffMonth = monthStart(cutoff.getFullYear(), cutoff.getMonth()).getTime();
  const txs: Transaction[] = [];

  for (const rule of rules) {
    if (!rule.active) continue;

    const startsAt = new Date(`${rule.startsAt}T00:00`);
    if (Number.isNaN(startsAt.getTime())) continue;

    let cursorYear = startsAt.getFullYear();
    let cursorMonth = startsAt.getMonth();
    const endsAt = parseIsoDate(rule.endsAt);

    while (monthStart(cursorYear, cursorMonth).getTime() < cutoffMonth) {
      if (endsAt && monthStart(cursorYear, cursorMonth).getTime() > monthStart(endsAt.getFullYear(), endsAt.getMonth()).getTime()) {
        break;
      }

      txs.push(...materializeFixedTransactionsForMonth([rule], cursorYear, cursorMonth));
      cursorMonth++;
      if (cursorMonth > 11) {
        cursorMonth = 0;
        cursorYear++;
      }
    }
  }

  return txs;
}

export function describeFixedTransactionSchedule(schedule: FixedTransactionSchedule) {
  if (schedule.type === 'calendar_day') {
    return `Todo dia ${schedule.day}`;
  }

  if (schedule.type === 'business_day') {
    return `${schedule.ordinal}º dia útil`;
  }

  const periodLabels = {
    start: 'Início do mês',
    middle: 'Meio do mês',
    end: 'Fim do mês',
  };

  return periodLabels[schedule.period];
}

export function nextFixedTransactionDate(rule: FixedTransactionRule, from = new Date()) {
  const start = new Date(`${rule.startsAt}T00:00`);
  const base = dateOnly(from);
  let year = Number.isNaN(start.getTime()) ? base.getFullYear() : start.getFullYear();
  let monthIdx = Number.isNaN(start.getTime()) ? base.getMonth() : start.getMonth();

  if (monthStart(year, monthIdx).getTime() < monthStart(base.getFullYear(), base.getMonth()).getTime()) {
    year = base.getFullYear();
    monthIdx = base.getMonth();
  }

  for (let i = 0; i < 36; i++) {
    const [tx] = materializeFixedTransactionsForMonth([{ ...rule, active: true }], year, monthIdx);
    if (tx) {
      const txDate = new Date(`${tx.date}T00:00`);
      if (txDate.getTime() >= base.getTime()) return tx.date;
    }

    const endsAt = parseIsoDate(rule.endsAt);
    if (endsAt && monthStart(year, monthIdx).getTime() > monthStart(endsAt.getFullYear(), endsAt.getMonth()).getTime()) {
      return null;
    }

    monthIdx++;
    if (monthIdx > 11) {
      monthIdx = 0;
      year++;
    }
  }

  return null;
}
