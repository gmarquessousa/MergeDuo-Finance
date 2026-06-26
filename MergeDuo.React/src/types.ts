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
  date: string;
  category: TransactionCategory;
  description: string;
  amount: number;
  currency?: string;
  owner?: string;
  ownerLabel?: string;
  fixedRuleId?: string;
  projected?: boolean;
  cardId?: string;
  cardTitle?: string;
  purchaseDate?: string;
  installments?: {
    index: number;
    total: number;
    groupId: string;
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
  closingDay: number;
  dueDay: number;
  currency: string;
  limit?: number;
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
  startsAt: string;
  endsAt?: string | null;
  active: boolean;
  cardId?: string | null;
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
  return -t.amount;
}
