export type TransactionCategory =
  | 'income'
  | 'credit_card'
  | 'loan'
  | 'fixed_expense'
  | 'variable_expense'
  | 'investment';

export type TransactionKind = 'in' | 'out' | 'invest';

export interface Transaction {
  id: string;
  userId?: string;
  yearMonth?: string;
  kind?: TransactionKind;
  date: string; // ISO yyyy-mm-dd — for credit_card+cardId, this is the cash-impact (due) date
  category: TransactionCategory;
  description: string;
  amount: number; // always positive; kind determines sign
  currency?: string;
  owner?: string; // display name; undefined = current user
  ownerLabel?: string;
  fixedRuleId?: string;
  projected?: boolean; // true when materialized from a FixedRule, not yet emitted by Scheduler
  cardId?: string; // linked registered card (credit_card category only)
  cardTitle?: string; // title of the linked card (populated for partner cards not in local store)
  purchaseDate?: string; // ISO yyyy-mm-dd — original purchase date for credit_card txs
  installments?: {
    index: number; // 1-based
    total: number;
    groupId: string; // shared by all installments of a purchase
  };
  tags?: string[];
  notes?: string;
  source?: {
    channel: string;
  };
  pendingSync?: boolean;
  syncError?: string | null;
  localOnly?: boolean;
  aggregateOnly?: boolean;
  createdAt?: string;
  updatedAt?: string;
  etag?: string | null;
}

export interface FinanceUser {
  id: string;
  name: string;
  registeredAt: string;
  startingBalance: number;
}

export interface Card {
  id: string;
  title: string;
  closingDay: number; // 1-31 — invoice closing day
  dueDay: number; // 1-31 — invoice due day
  currency: string;
  limit?: number; // optional credit limit (R$)
  createdAt: string;
  updatedAt: string;
  etag?: string | null;
}

export type OwnerFilter = 'me' | 'partner' | 'both';

export type MergePartnershipStatus = 'active' | 'paused' | 'ended' | string;

export interface MergePartnerInfo {
  id: string;
  partnershipId: string;
  status: MergePartnershipStatus;
  partnerUserId: string;
  name: string;
  handle: string;
  initials: string;
  mergedSince: string;
  startingBalance: number;
  financialDataAvailable: boolean;
  createdAt: string;
  updatedAt: string;
  endedAt: string | null;
}

export type FixedTransactionSchedule =
  | { type: 'calendar_day'; day: number }
  | { type: 'business_day'; ordinal: number }
  | { type: 'period'; period: 'start' | 'middle' | 'end' };

export interface FixedTransactionRule {
  id: string;
  category: TransactionCategory;
  description: string;
  amount: number;
  schedule: FixedTransactionSchedule;
  startsAt: string; // ISO yyyy-mm-dd, usually first day of the start month
  endsAt?: string | null;
  active: boolean;
  cardId?: string | null; // required when category === 'credit_card'
  tags?: string[];
  lastRunAt?: string | null;
  nextRunAt?: string | null;
  createdAt: string;
  updatedAt?: string;
  etag?: string | null;
  warnings?: FixedRuleWarning[] | null;
}

export interface FixedRuleWarning {
  code: string;
  message: string;
  severity: 'info' | 'warning' | 'error' | string;
}

export const CATEGORY_META: Record<
  TransactionCategory,
  { label: string; kind: TransactionKind; emoji: string; color: string }
> = {
  income: { label: 'Entrada', kind: 'in', emoji: '↑', color: 'text-accent-pos' },
  credit_card: { label: 'Cartão de crédito', kind: 'out', emoji: '▦', color: 'text-accent-neg' },
  loan: { label: 'Empréstimo', kind: 'out', emoji: '◈', color: 'text-accent-neg' },
  fixed_expense: { label: 'Gasto fixo', kind: 'out', emoji: '◼', color: 'text-accent-neg' },
  variable_expense: { label: 'Saída', kind: 'out', emoji: '◇', color: 'text-accent-neg' },
  investment: { label: 'Aporte', kind: 'invest', emoji: '◎', color: 'text-accent-invest' },
};

export function signedAmount(t: Transaction): number {
  const k = CATEGORY_META[t.category].kind;
  if (k === 'in') return t.amount;
  if (k === 'out') return -t.amount;
  // investment: leaves cash, becomes invested patrimony
  return -t.amount;
}
