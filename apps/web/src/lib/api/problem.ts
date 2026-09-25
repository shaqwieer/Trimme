/**
 * RFC 7807 problem details as returned by the TRIMME API (docs/architecture.md §Errors).
 * `errorCode` is the stable, machine-readable code clients localize; `errors` maps fields to codes.
 */
export type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
};

export class ApiError extends Error {
  readonly status: number;
  readonly errorCode: string;
  readonly correlationId: string | undefined;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, problem: ProblemDetails | undefined) {
    super(problem?.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.errorCode = problem?.errorCode ?? fallbackCode(status);
    this.correlationId = problem?.correlationId;
    this.fieldErrors = problem?.errors ?? {};
  }

  get isUnauthenticated() {
    return this.status === 401;
  }

  get isForbidden() {
    return this.status === 403;
  }

  get isNotFound() {
    return this.status === 404;
  }

  get isConflict() {
    return this.status === 409;
  }

  get isValidation() {
    return this.errorCode === 'validation.failed';
  }
}

/** Same fallback mapping as the API's ApiErrorCodes.ForStatus, for responses without a body. */
export function fallbackCode(status: number): string {
  switch (status) {
    case 401:
      return 'auth.unauthenticated';
    case 403:
      return 'auth.forbidden';
    case 404:
      return 'resource.not_found';
    case 405:
      return 'http.method_not_allowed';
    case 409:
      return 'resource.conflict';
    case 413:
      return 'request.too_large';
    case 429:
      return 'rate_limit.exceeded';
    default:
      return status >= 500 ? 'server.unexpected' : 'request.invalid';
  }
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('errorCode' in value || 'status' in value || 'title' in value)
  );
}

/** Builds an ApiError from a failed response, tolerating non-JSON bodies. */
export async function toApiError(response: Response): Promise<ApiError> {
  let body: unknown;
  try {
    body = await response.clone().json();
  } catch {
    body = undefined;
  }
  return new ApiError(response.status, isProblemDetails(body) ? body : undefined);
}
