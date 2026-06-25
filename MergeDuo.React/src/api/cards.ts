import type { Card } from '../types';
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

export const cardsApiBaseUrl = appConfig.cardsApiBaseUrl;

export type CardResponse = Card;

export interface CardsListResponse {
  items: CardResponse[];
}

export interface CreateCardRequest {
  title: string;
  closingDay: number;
  dueDay: number;
  currency: 'BRL';
}

export type UpdateCardRequest = Partial<CreateCardRequest>;

export interface BillingCycleResponse {
  closingDate: string;
  dueDate: string;
}

export interface CardUsageFreshnessResponse {
  retrievedAt: string;
  source: string;
  status: 'fresh' | 'fallback' | string;
  isFresh: boolean;
  isFallback: boolean;
}

export interface CardUsageResponse {
  cardId: string;
  yearMonth: string;
  currency: string;
  totalAmount: number;
  transactionCount: number;
  installmentCount: number;
  billingCycle: BillingCycleResponse;
  source: string;
  freshness?: CardUsageFreshnessResponse | null;
}

/** @deprecated Prefer the shared {@link ApiError} from `./http`; kept for compatibility. */
export class CardsApiError extends ApiError {
  constructor(status: number, code: string, message: string, problem: ProblemDetails | null = null) {
    super(status, code, message, problem);
    this.name = 'CardsApiError';
  }
}

function requireIfMatch(ifMatch?: string | null): string {
  if (!ifMatch?.trim()) {
    throw new CardsApiError(412, 'if_match_required', 'Card requires If-Match header for updates.');
  }
  return ifMatch.trim();
}

export async function listCards(
  accessToken: string,
  options?: RequestControlOptions,
): Promise<CardsListResponse> {
  return request<CardsListResponse>({
    path: '/cards',
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function createCard(
  accessToken: string,
  request_: CreateCardRequest,
  idempotencyKey: string = newIdempotencyKey(),
  options?: RequestControlOptions,
): Promise<CardResponse> {
  return request<CardResponse>({
    path: '/cards',
    method: 'POST',
    accessToken,
    json: request_,
    idempotencyKey,
    ...options,
  });
}

export async function getCard(
  accessToken: string,
  id: string,
  options?: RequestControlOptions,
): Promise<CardResponse> {
  return request<CardResponse>({
    path: `/cards/${encodeURIComponent(id)}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function deleteCard(
  accessToken: string,
  id: string,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<void> {
  const etag = requireIfMatch(ifMatch);
  await request<void>({
    path: `/cards/${encodeURIComponent(id)}`,
    method: 'DELETE',
    accessToken,
    ifMatch: etag,
    ...options,
  });
}

export async function patchCard(
  accessToken: string,
  id: string,
  request_: UpdateCardRequest,
  ifMatch?: string | null,
  options?: RequestControlOptions,
): Promise<CardResponse> {
  const etag = requireIfMatch(ifMatch);
  return request<CardResponse>({
    path: `/cards/${encodeURIComponent(id)}`,
    method: 'PATCH',
    accessToken,
    json: request_,
    ifMatch: etag,
    ...options,
  });
}

export async function getCardUsage(
  accessToken: string,
  id: string,
  yearMonth: string,
  options?: RequestControlOptions,
): Promise<CardUsageResponse> {
  return request<CardUsageResponse>({
    path: `/cards/${encodeURIComponent(id)}/usage`,
    method: 'GET',
    accessToken,
    query: { ym: yearMonth },
    ...options,
  });
}

interface CardsRequestOptions {
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

function request<T>(options: CardsRequestOptions): Promise<T> {
  return apiFetch<T>({
    baseUrl: cardsApiBaseUrl,
    defaultErrorCode: 'cards_api_error',
    ...options,
  }).catch((err: unknown) => {
    if (err instanceof CardsApiError) throw err;
    if (err instanceof TimeoutError || err instanceof NetworkError) throw err;
    if (err instanceof ApiError) {
      throw new CardsApiError(err.status, err.code, err.message, err.problem);
    }
    throw err;
  });
}
