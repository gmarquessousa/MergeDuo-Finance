import type { MergePartnerInfo } from '../types';
import { ApiError, apiFetch, newIdempotencyKey, type RequestControlOptions } from './http';
import { appConfig } from './config';

export const partnershipApiBaseUrl = appConfig.partnershipApiBaseUrl;

export interface PartnerSnapshotResponse {
  userId: string;
  name: string;
  handle: string;
  initials: string;
}

export interface CreateInviteResponse {
  token: string;
  status: 'pending' | 'accepted' | 'revoked' | 'expired' | string;
  inviteUrl: string;
  qrPayload: string;
  expiresAt: string;
}

export interface InvitePreviewResponse {
  token: string;
  status: 'pending' | string;
  inviter: PartnerSnapshotResponse;
  expiresAt: string;
}

export interface AcceptInviteResponse {
  partnershipId: string;
  partnershipDocumentId: string;
  status: 'active' | string;
}

export interface PartnershipResponse {
  id: string;
  partnershipId: string;
  status: 'active' | 'paused' | 'ended' | string;
  userId: string;
  partnerUserId: string;
  partner: PartnerSnapshotResponse;
  startingBalance: number;
  createdAt: string;
  updatedAt: string;
  endedAt: string | null;
}

export interface CurrentPartnershipResponse {
  partnership: PartnershipResponse | null;
}

export interface PartnershipStatusResponse {
  id: string;
  partnershipId: string;
  status: 'active' | 'paused' | 'ended' | string;
  updatedAt: string;
  endedAt: string | null;
}

/** @deprecated Prefer the shared {@link ApiError} from `./http`; kept for compatibility. */
export class PartnershipApiError extends ApiError {
  constructor(status: number, code: string, message: string) {
    super(status, code, message);
    this.name = 'PartnershipApiError';
  }
}

export async function createInvite(
  accessToken: string,
  channel: 'link' | 'qr' | 'share' = 'link',
  idempotencyKey: string = newIdempotencyKey(),
  options?: RequestControlOptions,
): Promise<CreateInviteResponse> {
  return request<CreateInviteResponse>({
    path: '/invites',
    method: 'POST',
    accessToken,
    json: { channel },
    idempotencyKey,
    ...options,
  });
}

export async function previewInvite(
  token: string,
  options?: RequestControlOptions,
): Promise<InvitePreviewResponse> {
  return request<InvitePreviewResponse>({
    path: `/invites/${encodeURIComponent(token)}`,
    method: 'GET',
    suppressUnauthorizedHandler: true,
    ...options,
  });
}

export async function acceptInvite(
  accessToken: string,
  token: string,
  options?: RequestControlOptions,
): Promise<AcceptInviteResponse> {
  return request<AcceptInviteResponse>({
    path: `/invites/${encodeURIComponent(token)}/accept`,
    method: 'POST',
    accessToken,
    ...options,
  });
}

export async function revokeInvite(
  accessToken: string,
  token: string,
  options?: RequestControlOptions,
): Promise<PartnershipStatusResponse> {
  return request<PartnershipStatusResponse>({
    path: `/invites/${encodeURIComponent(token)}/revoke`,
    method: 'POST',
    accessToken,
    ...options,
  });
}

export async function getCurrentPartnership(
  accessToken: string,
  options?: RequestControlOptions,
): Promise<CurrentPartnershipResponse> {
  return request<CurrentPartnershipResponse>({
    path: '/partnerships/me',
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function endPartnership(
  accessToken: string,
  partnershipDocumentId: string,
  options?: RequestControlOptions,
): Promise<PartnershipStatusResponse> {
  return request<PartnershipStatusResponse>({
    path: `/partnerships/${encodeURIComponent(partnershipDocumentId)}/end`,
    method: 'POST',
    accessToken,
    ...options,
  });
}

export function toMergePartnerInfo(partnership: PartnershipResponse): MergePartnerInfo {
  return {
    id: partnership.id,
    partnershipId: partnership.partnershipId,
    status: partnership.status,
    partnerUserId: partnership.partnerUserId,
    name: partnership.partner.name,
    handle: partnership.partner.handle,
    initials: partnership.partner.initials,
    mergedSince: partnership.createdAt,
    startingBalance: partnership.startingBalance ?? 0,
    financialDataAvailable: true,
    createdAt: partnership.createdAt,
    updatedAt: partnership.updatedAt,
    endedAt: partnership.endedAt,
  };
}

interface PartnershipRequestOptions {
  path: string;
  method: string;
  accessToken?: string;
  json?: unknown;
  query?: Record<string, string | number | boolean | null | undefined>;
  idempotencyKey?: string;
  suppressUnauthorizedHandler?: boolean;
  signal?: AbortSignal;
  timeoutMs?: number;
}

function request<T>(options: PartnershipRequestOptions): Promise<T> {
  return apiFetch<T>({
    baseUrl: partnershipApiBaseUrl,
    defaultErrorCode: 'partnership_api_error',
    ...options,
  });
}
