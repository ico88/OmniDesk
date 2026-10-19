import { Activity, ClipboardList, Files, Gauge, LifeBuoy, Monitor, Package, ScrollText, ServerCog, Terminal, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

export type DeviceTabId = 'overview' | 'desktop' | 'terminal' | 'files' | 'processes' | 'services' | 'software' | 'updates' | 'events' | 'tickets';

const tabs: { id: DeviceTabId; label: string; icon: LucideIcon }[] = [
  { id: 'overview', label: 'Panoramica', icon: Gauge },
  { id: 'desktop', label: 'Desktop remoto', icon: Monitor },
  { id: 'terminal', label: 'Terminale e PowerShell', icon: Terminal },
  { id: 'files', label: 'Trasferimento file', icon: Files },
  { id: 'processes', label: 'Processi', icon: Activity },
  { id: 'services', label: 'Servizi Windows', icon: ServerCog },
  { id: 'software', label: 'Software installato', icon: Package },
  { id: 'updates', label: 'Aggiornamenti', icon: ClipboardList },
  { id: 'events', label: 'Eventi e log', icon: ScrollText },
  { id: 'tickets', label: 'Ticket e interventi', icon: LifeBuoy },
];

export function DeviceTabs({ active, onChange }: { active: DeviceTabId; onChange: (tab: DeviceTabId) => void }) {
  return <nav className="device-tabs" aria-label="Sezioni dispositivo" role="tablist">
    {tabs.map(({ id, label, icon: Icon }) => <button
      key={id}
      type="button"
      role="tab"
      id={`device-tab-${id}`}
      aria-controls={`device-panel-${id}`}
      aria-selected={active === id}
      tabIndex={active === id ? 0 : -1}
      className={active === id ? 'active' : ''}
      onClick={() => onChange(id)}
      onKeyDown={event => {
        const controls = Array.from(event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]') ?? []);
        const index = controls.indexOf(event.currentTarget);
        const next = event.key === 'ArrowRight' ? (index + 1) % controls.length
          : event.key === 'ArrowLeft' ? (index - 1 + controls.length) % controls.length
          : event.key === 'Home' ? 0 : event.key === 'End' ? controls.length - 1 : -1;
        if (next >= 0) {
          event.preventDefault();
          controls[next].focus();
          const target = tabs.find(tab => `device-tab-${tab.id}` === controls[next].id);
          if (target) onChange(target.id);
        }
      }}
    ><Icon size={16} aria-hidden="true"/><span>{label}</span></button>)}
  </nav>;
}

export function DevicePanel({ id, children }: { id: DeviceTabId; children: ReactNode }) {
  return <section id={`device-panel-${id}`} className="device-tab-panel" role="tabpanel" aria-labelledby={`device-tab-${id}`} tabIndex={0}>{children}</section>;
}
