import { OtpFlowProvider } from '@/components/auth/OtpFlow';

/** Shared by the phone and verify screens so the pending challenge survives the navigation between them. */
export default function OtpFlowLayout({ children }: { children: React.ReactNode }) {
  return <OtpFlowProvider>{children}</OtpFlowProvider>;
}
