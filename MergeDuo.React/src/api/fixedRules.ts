import type {
  FixedTransactionRule,
  FixedTransactionSchedule,
  TransactionCategory,
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

export const fixedRulesApiBaseUrl = appConfig.fixedRulesApiBaseUrl;

export type FixedRuleResponse = FixedTransactionRule;

export interface FixedRulesListResponse {
  items: FixedRuleResponse[];
}

export interface CreateFixedRuleRequest {
  category: TransactionCategory;
  description: string;
  amount: number;
  cardId?: string | null;
  tags?: string[];
  schedule: FixedTransactionSchedule;
  startsAt: string;
  endsAt?: string | null;
  active?: boolean;
}

export interface FixedRuleWarningResponse {
  code: string;
  message: string;
  severity: 'info' | 'warning' | 'error' | string;
}

export interface FixedRuleOccurrenceResponse {
  occurrenceDate: string;
  yearMonth: string;
  category: TransactionCategory;
  description: string;
  amount: number;
  cardId: string | null;
  tags: string[];
  warnings?: FixedRuleWarningResponse[] | null;
}

export interface FixedRulePreviewResponse {
  ruleId: string;
  active: boolean;
  from: string;
  to: string;
  items: FixedRuleOccurrenceResponse[];
  warnings?: FixedRuleWarningResponse[] | null;
}

export type FixedRuleActiveFilter = 'true' | 'false' | 'all';

/** @deprecated Prefer the shared {@link ApiError} from `./http`; kept for compatibility. */
export class FixedRulesApiError extends ApiError {
  constructor(status: number, code: string, message: string, problem: ProblemDetails | null = null) {
    super(status, code, message, problem);
    this.name = 'FixedRulesApiError';
  }
}

function requireIfMatch(ifMatch?: string | null): string {
  if (!ifMatch?.trim()) {
    throw new FixedRulesApiError(412, 'if_match_required', 'Fixed rule requires If-Match header for updates.');
  }
  return ifMatch.trim();
}

export async function listFixedRules(
  accessToken: string,
  active: FixedRuleActiveFilter = 'all',
  options?: RequestControlOptions,
): Promise<FixedRulesListResponse> {
  return request<FixedRulesListResponse>({
    path: '/fixed-rules',
    method: 'GET',
    accessToken,
    query: { active },
    ...options,
  });
}

export async function createFixedRule(
  accessToken: string,
  request_: CreateFixedRuleRequest,
  idempotencyKey: string = newIdempotencyKey(),
  options?: RequestControlOptions,
): Promise<FixedRuleResponse> {
  return request<FixedRuleResponse>({
    path: '/fixed-rules',
    method: 'POST',
    accessToken,
    json: request_,
    idempotencyKey,
    ...options,
  });
}

export async function getFixedRule(
  accessToken: string,
  id: string,
  options?: RequestControlOptions,
): Promise<FixedRuleResponse> {
  return request<FixedRuleResponse>({
    path: `/fixed-rules/${encodeURIComponent(id)}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function deleteFixedRule(
  accessToken: string,
  id: string,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<void> {
  const etag = requireIfMatch(ifMatch);
  await request<void>({
    path: `/fixed-rules/${encodeURIComponent(id)}`,
    method: 'DELETE',
    accessToken,
    ifMatch: etag,
    ...options,
  });
}

export async function pauseFixedRule(
  accessToken: string,
  id: string,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<FixedRuleResponse> {
  const etag = requireIfMatch(ifMatch);
  return request<FixedRuleResponse>({
    path: `/fixed-rules/${encodeURIComponent(id)}/pause`,
    method: 'POST',
    accessToken,
    ifMatch: etag,
    ...options,
  });
}

export async function resumeFixedRule(
  accessToken: string,
  id: string,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<FixedRuleResponse> {
  const etag = requireIfMatch(ifMatch);
  return request<FixedRuleResponse>({
    path: `/fixed-rules/${encodeURIComponent(id)}/resume`,
    method: 'POST',
    accessToken,
    ifMatch: etag,
    ...options,
  });
}

export interface UpdateFixedRuleRequest {
  category?: string | null;
  description?: string | null;
  amount?: number | null;
  cardId?: string | null;
  tags?: string[] | null;
  schedule?: FixedTransactionSchedule | null;
  startsAt?: string | null;
  endsAt?: string | null;
}

export async function patchFixedRule(
  accessToken: string,
  id: string,
  request_: UpdateFixedRuleRequest,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<FixedRuleResponse> {
  const etag = requireIfMatch(ifMatch);
  return request<FixedRuleResponse>({
    path: `/fixed-rules/${encodeURIComponent(id)}`,
    method: 'PATCH',
    accessToken,
    json: request_,
    ifMatch: etag,
    ...options,
  });
}

export async function getFixedRulePreview(
  accessToken: string,
  id: string,
  from: string,
  to: string,
  options?: RequestControlOptions,
): Promise<FixedRulePreviewResponse> {
  return request<FixedRulePreviewResponse>({
    path: `/fixed-rules/${encodeURIComponent(id)}/preview`,
    method: 'GET',
    accessToken,
    query: { from, to },
    ...options,
  });
}

interface FixedRulesRequestOptions {
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

function request<T>(options: FixedRulesRequestOptions): Promise<T> {
  return apiFetch<T>({
    baseUrl: fixedRulesApiBaseUrl,
    defaultErrorCode: 'fixed_rules_api_error',
    ...options,
  }).catch((err: unknown) => {
    if (err instanceof FixedRulesApiError) throw err;
    if (err instanceof TimeoutError || err instanceof NetworkError) throw err;
    if (err instanceof ApiError) {
      throw new FixedRulesApiError(err.status, err.code, err.message, err.problem);
    }
    throw err;
  });
}
