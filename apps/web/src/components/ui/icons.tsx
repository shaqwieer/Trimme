import {
  Ban,
  BarChart3,
  Bell,
  BookOpen,
  CalendarDays,
  Camera,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Clock,
  Coffee,
  CreditCard,
  Download,
  EllipsisVertical,
  EyeOff,
  Heart,
  Home,
  Info,
  LayoutGrid,
  Layers,
  List,
  LogOut,
  type LucideIcon,
  type LucideProps,
  MapPin,
  MessageCircle,
  Pause,
  Pencil,
  Phone,
  Plane,
  Play,
  Plus,
  QrCode,
  RefreshCw,
  Scissors,
  Search,
  Settings,
  Shield,
  SlidersHorizontal,
  Star,
  Store,
  Tag,
  Trash2,
  TriangleAlert,
  User,
  Users,
  X,
} from 'lucide-react';
import { cn } from '@/lib/cn';

/**
 * The design's 46 outline icons (TRIMME.dc.html ICONS, 3319–3374) mapped to Lucide equivalents.
 * All icons use stroke 1.75 like the design. Keys are the design's names so screens can be built
 * directly from the prototype.
 */
export const designIcons = {
  home: Home,
  search: Search,
  calendar: CalendarDays,
  heart: Heart,
  bell: Bell,
  user: User,
  pin: MapPin,
  star: Star,
  clock: Clock,
  scissors: Scissors,
  chevR: ChevronRight,
  chevL: ChevronLeft,
  chevD: ChevronDown,
  x: X,
  check: Check,
  plus: Plus,
  filter: SlidersHorizontal,
  qr: QrCode,
  msg: MessageCircle,
  settings: Settings,
  users: Users,
  store: Store,
  grid: LayoutGrid,
  alert: TriangleAlert,
  trash: Trash2,
  edit: Pencil,
  phone: Phone,
  more: EllipsisVertical,
  logout: LogOut,
  pause: Pause,
  info: Info,
  camera: Camera,
  download: Download,
  shield: Shield,
  chart: BarChart3,
  ban: Ban,
  coffee: Coffee,
  plane: Plane,
  card: CreditCard,
  list: List,
  eyeOff: EyeOff,
  layers: Layers,
  refresh: RefreshCw,
  book: BookOpen,
  play: Play,
  tag: Tag,
} satisfies Record<string, LucideIcon>;

export type DesignIconName = keyof typeof designIcons;

/**
 * Icons that express direction ("next", "back", "forward") and must mirror in RTL.
 * In Arabic the design uses chevL for forward/next and chevR for back (design analysis §3).
 */
export const DIRECTIONAL_ICONS: ReadonlySet<DesignIconName> = new Set(['chevR', 'chevL', 'logout', 'play']);

type IconProps = Omit<LucideProps, 'ref'> & {
  name: DesignIconName;
  /** Accessible label. Omit for decorative icons (they are hidden from assistive technology). */
  label?: string;
};

/**
 * Renders a design icon. Directional icons are written for LTR and flipped in RTL, so
 * `<Icon name="chevR" />` always means "forward" in the reading direction.
 */
export function Icon({ name, label, className, strokeWidth = 1.75, ...props }: IconProps) {
  const Component = designIcons[name];
  return (
    <Component
      aria-hidden={label ? undefined : true}
      aria-label={label}
      role={label ? 'img' : undefined}
      strokeWidth={strokeWidth}
      className={cn('shrink-0', DIRECTIONAL_ICONS.has(name) && 'rtl:-scale-x-100', className)}
      {...props}
    />
  );
}
