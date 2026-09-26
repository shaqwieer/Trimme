'use client';

import { createContext, type ReactNode, useContext, useMemo, useState } from 'react';

export type OtpChallenge = {
  challengeId: string;
  /** E.164, shown back to the user on the verify screen; kept in memory only (never in the URL or storage). */
  phoneE164: string;
  codeLength: number;
  expiresAt: string;
  resendAvailableAt: string;
  termsAccepted: boolean;
};

type OtpFlowValue = {
  challenge: OtpChallenge | null;
  setChallenge: (challenge: OtpChallenge | null) => void;
};

const OtpFlowContext = createContext<OtpFlowValue | null>(null);

/**
 * Holds the pending OTP challenge while the user moves from the phone screen to the verify screen. It lives in the
 * shared layout of those routes, so it survives client navigation but not a reload (the user then starts again).
 */
export function OtpFlowProvider({ children }: { children: ReactNode }) {
  const [challenge, setChallenge] = useState<OtpChallenge | null>(null);
  const value = useMemo(() => ({ challenge, setChallenge }), [challenge]);
  return <OtpFlowContext.Provider value={value}>{children}</OtpFlowContext.Provider>;
}

export function useOtpFlow(): OtpFlowValue {
  const value = useContext(OtpFlowContext);
  if (!value) throw new Error('useOtpFlow must be used inside <OtpFlowProvider>.');
  return value;
}
