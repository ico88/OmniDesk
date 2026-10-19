import {
  Activity, ArrowRight, BookOpen, CalendarClock, Check, Clock3, Cpu, Download, Files, LayoutDashboard,
  LifeBuoy, LogOut, MessageSquare, Monitor, Palette, Plus, RefreshCw, Search, Settings2,
  ShieldCheck, Users, X, Link2, Workflow, type LucideIcon,
} from 'lucide-react';

const icons: Record<string, LucideIcon> = {
  overview: LayoutDashboard,
  devices: Monitor,
  security: ShieldCheck,
  support: LifeBuoy,
  myTickets: MessageSquare,
  knowledge: BookOpen,
  branding: Palette,
  users: Users,
  audit: Activity,
  logout: LogOut,
  refresh: RefreshCw,
  terminal: Activity,
  files: Files,
  download: Download,
  plus: Plus,
  search: Search,
  arrow: ArrowRight,
  settings: Settings2,
  cpu: Cpu,
  clock: Clock3,
  close: X,
  check: Check,
  link: Link2,
  workflows: Workflow,
  slaPolicies: CalendarClock,
};

export function Icon({ name, size = 19 }: { name: string; size?: number }) {
  const Glyph = icons[name] ?? Monitor;
  return <Glyph className="icon" size={size} strokeWidth={1.8} aria-hidden="true" focusable="false" />;
}
