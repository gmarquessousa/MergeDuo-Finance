import type {
  OwnerFilter,
  Transaction,
  TransactionCategory,
  TransactionKind,
} from '../types';
import {
  ApiError,
  NetworkError,
  TimeoutError,
  apiFetch,
  newIdempotencyKey,
  type ProblemDetails,
  type RequestControlOptions,
} from './http';
import { appConfig } from './config';

export const transactionsApiBaseUrl = appConfig.transactionsApiBaseUrl;

export interface InstallmentResponse {
  index: number;
  total: number;
  groupId: string;
}

export interface TransactionResponse {
  id: string;
  userId: string;
  yearMonth: string;
  date: string;
  purchaseDate: string | null;
  category: TransactionCategory;
  kind: TransactionKind;
  description: string;
  amount: number;
  currency: string;
  ownerLabel: string | null;
  cardId: string | null;
  cardTitle: string | null;
  fixedRuleId: string | null;
  installments: InstallmentResponse | null;
  tags: string[];
  notes: string | null;
  source: {
    channel: string;
  };
  createdAt: string;
  updatedAt: string;
  etag: string | null;
}

export interface TransactionsListResponse {
  items: TransactionResponse[];
  continuationToken: string | null;
}

export interface TagSummaryResponse {
  tag: string;
  expensesTotal: number;
  transactionCount: number;
  transactions?: TransactionResponse[];
}

export interface TagAnalyticsResponse {
  tags: string[];
  items: TagSummaryResponse[];
}

export interface TagSuggestion {
  tag: string;
  count: number;
}

export interface TagSuggestionsResponse {
  items: TagSuggestion[];
}

export interface CreateTransactionsResponse {
  groupId: string | null;
  items: TransactionResponse[];
}

export interface TransactionGroupResponse {
  groupId: string;
  items: TransactionResponse[];
}

export interface DeleteTransactionGroupResponse {
  groupId: string;
  deletedCount: number;
  skippedCount: number;
}

export interface ListTransactionsRequest {
  ym: string;
  category?: TransactionCategory | null;
  cardId?: string | null;
  owner?: OwnerFilter;
  pageSize?: number;
  continuationToken?: string | null;
  sort?: 'dateAsc' | 'dateDesc';
}

export interface CreateTransactionRequest {
  date?: string;
  purchaseDate?: string;
  category: TransactionCategory;
  description: string;
  amount: number;
  currency: 'BRL';
  ownerLabel?: string | null;
  cardId?: string | null;
  fixedRuleId?: string | null;
  installments?: { total: number } | null;
  tags?: string[];
  notes?: string | null;
}

export type UpdateTransactionRequest = Partial<CreateTransactionRequest>;

export interface TransactionOwnershipContext {
  currentUserId?: string | null;
  partnerUserId?: string | null;
  partnerName?: string | null;
}

/** @deprecated Prefer the shared {@link ApiError} from `./http`; kept for compatibility. */
export class TransactionsApiError extends ApiError {
  constructor(status: number, code: string, message: string, problem: ProblemDetails | null = null) {
    super(status, code, message, problem);
    this.name = 'TransactionsApiError';
  }
}

function requireIfMatch(ifMatch?: string | null): string {
  if (!ifMatch?.trim()) {
    throw new TransactionsApiError(412, 'if_match_required', 'Transaction requires If-Match header for updates.');
  }
  return ifMatch.trim();
}

export async function listTransactions(
  accessToken: string,
  request_: ListTransactionsRequest,
  options?: RequestControlOptions,
): Promise<TransactionsListResponse> {
  return request<TransactionsListResponse>({
    path: '/transactions',
    method: 'GET',
    accessToken,
    query: {
      ym: request_.ym,
      category: request_.category ?? undefined,
      cardId: request_.cardId ?? undefined,
      owner: request_.owner ?? undefined,
      pageSize: request_.pageSize ?? undefined,
      continuationToken: request_.continuationToken ?? undefined,
      sort: request_.sort ?? undefined,
    },
    ...options,
  });
}

export async function getTransaction(
  accessToken: string,
  id: string,
  yearMonth: string,
  ownerUserId?: string | null,
  options?: RequestControlOptions,
): Promise<TransactionResponse> {
  return request<TransactionResponse>({
    path: `/transactions/${encodeURIComponent(id)}`,
    method: 'GET',
    accessToken,
    query: { ym: yearMonth, ownerUserId: ownerUserId ?? undefined },
    ...options,
  });
}

export async function getTransactionTags(
  accessToken: string,
  includeTransactions = false,
  options?: RequestControlOptions,
): Promise<TagAnalyticsResponse> {
  return request<TagAnalyticsResponse>({
    path: '/transactions/tags',
    method: 'GET',
    accessToken,
    query: { includeTransactions },
    ...options,
  });
}

export async function getTagSuggestions(
  accessToken: string,
  params?: { prefix?: string; limit?: number },
  options?: RequestControlOptions,
): Promise<TagSuggestionsResponse> {
  return request<TagSuggestionsResponse>({
    path: '/transactions/tags/suggestions',
    method: 'GET',
    accessToken,
    query: {
      prefix: params?.prefix ?? undefined,
      limit: params?.limit ?? undefined,
    },
    ...options,
  });
}

export async function createTransaction(
  accessToken: string,
  request_: CreateTransactionRequest,
  idempotencyKey: string = newIdempotencyKey(),
  options?: RequestControlOptions,
): Promise<CreateTransactionsResponse> {
  return request<CreateTransactionsResponse>({
    path: '/transactions',
    method: 'POST',
    accessToken,
    json: request_,
    idempotencyKey,
    ...options,
  });
}

export async function patchTransaction(
  accessToken: string,
  id: string,
  yearMonth: string,
  request_: UpdateTransactionRequest,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<TransactionResponse> {
  const etag = requireIfMatch(ifMatch);
  return request<TransactionResponse>({
    path: `/transactions/${encodeURIComponent(id)}`,
    method: 'PATCH',
    accessToken,
    json: request_,
    query: { ym: yearMonth },
    ifMatch: etag,
    ...options,
  });
}

export async function deleteTransaction(
  accessToken: string,
  id: string,
  yearMonth: string,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<void> {
  const etag = requireIfMatch(ifMatch);
  await request<void>({
    path: `/transactions/${encodeURIComponent(id)}`,
    method: 'DELETE',
    accessToken,
    query: { ym: yearMonth },
    ifMatch: etag,
    ...options,
  });
}

export async function getTransactionGroup(
  accessToken: string,
  groupId: string,
  ownerUserId?: string | null,
  options?: RequestControlOptions,
): Promise<TransactionGroupResponse> {
  return request<TransactionGroupResponse>({
    path: `/transactions/groups/${encodeURIComponent(groupId)}`,
    method: 'GET',
    accessToken,
    query: { ownerUserId: ownerUserId ?? undefined },
    ...options,
  });
}

export async function deleteTransactionGroup(
  accessToken: string,
  groupId: string,
  options?: RequestControlOptions,
): Promise<DeleteTransactionGroupResponse> {
  return request<DeleteTransactionGroupResponse>({
    path: `/transactions/groups/${encodeURIComponent(groupId)}`,
    method: 'DELETE',
    accessToken,
    ...options,
  });
}

export function toTransaction(
  response: TransactionResponse,
  context: TransactionOwnershipContext = {},
): Transaction {
  const isPartner =
    !!context.partnerUserId &&
    response.userId === context.partnerUserId &&
    response.userId !== context.currentUserId;
  const owner = isPartner
    ? response.ownerLabel ?? context.partnerName ?? undefined
    : undefined;

  return {
    id: response.id,
    userId: response.userId,
    yearMonth: response.yearMonth,
    date: response.date,
    purchaseDate: response.purchaseDate ?? undefined,
    category: response.category,
    kind: response.kind,
    description: response.description,
    amount: response.amount,
    currency: response.currency,
    owner,
    ownerLabel: response.ownerLabel ?? undefined,
    cardId: response.cardId ?? undefined,
    cardTitle: response.cardTitle ?? undefined,
    fixedRuleId: response.fixedRuleId ?? undefined,
    installments: response.installments ?? undefined,
    tags: response.tags,
    notes: response.notes ?? undefined,
    source: response.source,
    createdAt: response.createdAt,
    updatedAt: response.updatedAt,
    etag: response.etag,
  };
}

interface TransactionsRequestOptions {
  path: string;
  method: string;
  accessToken?: string;
  json?: unknown;
  query?: Record<string, string | number | boolean | null | undefined>;
  idempotencyKey?: string;
  ifMatch?: string | null;
  signal?: AbortSignal;
  timeoutMs?: number;
}

function request<T>(options: TransactionsRequestOptions): Promise<T> {
  return apiFetch<T>({
    baseUrl: transactionsApiBaseUrl,
    defaultErrorCode: 'transactions_api_error',
    ...options,
  }).catch((err: unknown) => {
    if (err instanceof TransactionsApiError) throw err;
    if (err instanceof TimeoutError || err instanceof NetworkError) throw err;
    if (err instanceof ApiError) {
      throw new TransactionsApiError(err.status, err.code, err.message, err.problem);
    }
    throw err;
  });
}
